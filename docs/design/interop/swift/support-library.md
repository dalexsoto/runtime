# Swift projection support layer

**Status:** PROPOSED — prototype validated; API shape pends review.

This document specifies the low-level managed support layer for projecting
resilient Swift values (roadmap Phase 4). The walking-skeleton implementation
is test-hosted in
`src/tests/Interop/Swift/SwiftValueLifecycle/SwiftValueLifecycle.cs`
(namespace `Swift.Runtime.Support`) and is green under CoreCLR JIT, the
CoreCLR interpreter, R2R, and NativeAOT on ARM64 macOS. Numeric constants
(slot offsets, flag bits, tag order) were probe-verified against Swift 6.4
(Xcode 27) in `swift-abi-probe/vwt-enum-nc`.

Scope matches the repository-wide contract: ARM64 Apple platforms only.
Direct managed witness calls are additionally restricted to non-ptrauth
ARM64; see [Calling-convention audit](#calling-convention-audit).

## Type inventory

| Type | Purpose |
|---|---|
| `SwiftTypeMetadata` | Validated pointer to complete type metadata, plus an optional module lease. |
| `SwiftValueWitnessTable` | View over the required VWT prefix and, for enums, the enum witnesses. |
| `ISwiftTypeToken` | Static-abstract token supplying a metadata accessor (and optionally a module lease) without reflection. |
| `SwiftMetadataCache<TToken>` | Generic-static, reflection-free metadata cache; pins the lease for process lifetime. |
| `SwiftValueStorage` | Aligned native storage for one Swift value with an explicit lifecycle and scoped borrows. |
| `SwiftProtocolDescriptor` | Validated pointer to a protocol descriptor (`…Mp` symbol). |
| `SwiftWitnessTable` | Conformance witness table from `swift_conformsToProtocol`; requirement witnesses from slot 1. |
| `SwiftString` | 16-byte frozen `String` mirror; VWT-backed value semantics, stdlib-init creation, mutation via the method self register. |
| `SwiftObjectHandle` | Owned native object reference balanced with `swift_retain`/`swift_release`. |
| `SwiftErrorHandle` | Owned error-box reference balanced with `swift_errorRetain`/`swift_errorRelease`. |
| `SwiftModuleLease` | Refcounted `NativeLibrary` load keeping a defining module alive. |

## Metadata and VWT access

Metadata is obtained only through generated `…Ma` accessors called with the
blocking complete request (`0`). The two-word response is validated
(`state == 0`, non-null metadata) before any witness use. The VWT pointer
lives one word before the metadata address.

Probe-verified VWT slot layout (pointer-sized slots):

| Slot | Contents |
|---|---|
| 0 | `initializeBufferWithCopyOfBuffer` |
| 1 | `destroy` |
| 2 | `initializeWithCopy` |
| 3 | `assignWithCopy` |
| 4 | `initializeWithTake` |
| 5 | `assignWithTake` |
| 6 | `getEnumTagSinglePayload` |
| 7 | `storeEnumTagSinglePayload` |
| 8 | `size` (`nuint`) |
| 9 | `stride` (`nuint`) |
| 10 | `flags` (`uint32`) + `extraInhabitantCount` (`uint32`) |
| 11 | `getEnumTag` (enum types only) |
| 12 | `destructiveProjectEnumData` (enum types only) |
| 13 | `destructiveInjectEnumTag` (enum types only) |

Flag bits used by the layer (low byte is the alignment mask):

| Bit | Meaning | Probe evidence |
|---|---|---|
| `0x0000_00FF` | alignment mask (`alignment - 1`) | all fixtures |
| `0x0020_0000` | has enum witnesses | resilient enum `0x02210007` vs struct `0x02010007` |
| `0x0080_0000` | noncopyable | `~Copyable` struct `0x02800007` vs copyable `0x02010007` |

Enum-witness access on a type without `0x0020_0000` fails closed with
`InvalidOperationException` — slots 11+ do not exist for non-enum types.

Enum tag order (probe- and test-verified): payload cases take tags
`0..n-1` in declaration order, then no-payload cases continue the sequence.
`destructiveProjectEnumData` replaces the value in place with its raw
payload; `destructiveInjectEnumTag` re-tags an in-place payload back into a
valid enum value. Between project and inject the storage does not hold a
valid enum and must not be destroyed or copied.

## Value lifecycle

`SwiftValueStorage` is a `SafeHandle` over `NativeMemory.AlignedAlloc(stride,
alignment)` with an interlocked state machine:

```
Uninitialized --AdoptFrom/InitializeByCopyFrom/InitializeByTakeFrom--> Initialized
Initialized   --DestroyValue / take-consumed / ReleaseHandle--> Consumed   (exactly once)
```

- **Adoption is exception-safe:** ownership publishes only after the
  initializer returns without throwing.
- **Take consumes the source exactly once** via an interlocked transition;
  a moved-from source is never destroyed. This is the atomic one-consume
  primitive that permits noncopyable values outside generated thunks.
- **Copy rejects noncopyable values** (flag `0x0080_0000`) before touching
  the invalid copy witnesses.
- **Destroy runs exactly once** across explicit `DestroyValue`, `Dispose`,
  and the finalizer.

### Scoped borrows and disposal safety

`WithBorrowed` (shared read) and `WithMutable` (inout-style exclusive
access) expose the value address only inside a callback. Each borrow holds a
`SafeHandle` reference, so a racing `Dispose` defers destroy-and-free until
the last borrow ends — validated by a test that disposes mid-borrow and
still observes a leak-free, torn-free read. `DestroyValue` on a borrowed
value throws. As in Swift's law of exclusivity, full exclusivity
enforcement is the caller's obligation; violations are best-effort
detected.

`inout` parameters of resilient type lower to an ordinary pointer argument;
`WithMutable` passes the borrowed address to such functions and mutation is
observed in place (see `scaleResilientValue` in the fixture).

## Nominal descriptors and protocol conformances

Struct metadata leads with kind `0x200`, enums with `0x201`/`0x202`
(Optional), and the nominal type descriptor sits one word after the kind
(all probe-verified). Class metadata leads with an isa pointer, so the
descriptor accessor fails closed for non-value kinds.

Conformances resolve through `swift_conformsToProtocol(metadata, Mp)`,
which returns the same table as the conformance's exported `…WP` symbol
and null for non-conforming types (fail-closed, test-verified). The
witness table layout is: slot 0 the conformance descriptor, protocol
requirement witnesses from slot 1 in requirement order.

Protocol witnesses use the Swift method convention: the value's address
goes in the self register (`x20`), and `Self` metadata plus the witness
table are appended after the formal arguments (`x0`/`x1` for a
zero-argument requirement). This was probe-verified with a clang
`swiftcall`/`swift_context` caller and is exercised from managed code via
`delegate* unmanaged[Swift]` with a `SwiftSelf` argument. Witness tables
inherit the metadata's module lease.

## ARC handles

- `SwiftObjectHandle` adopts a +1 native object reference and releases it
  exactly once; `Retain()` mints an additional owned handle.
- `SwiftErrorHandle` adopts the +1 error box a Swift `throws` places in the
  error register and balances it with `swift_errorRelease`. Error boxes are
  distinct from plain heap objects and must use the `swift_error*` entry
  points, not `swift_retain`/`swift_release`.
- **Provenance matters:** `swift_retain`/`swift_release` are only valid for
  native Swift heap objects. Using them on `@objc`/NSObject-derived
  instances is undefined behaviour;
  `swift_unknownObjectRetain`/`swift_unknownObjectRelease` handle
  native-Swift, Objective-C, and tagged-pointer provenance and must be
  used whenever provenance is not statically known.

  > **Correction (measured 2026-07-18, Xcode 27 beta 2 / Swift 6.4,
  > `arm64-apple-macos15.0`).** An earlier revision of this bullet said such
  > instances "crash in `swift_release_dealloc` (observed)". They usually do
  > **not**: measured refcount deltas show a plain `NSObject` and a
  > stored-property-free Swift `NSObject` subclass both take a **silent
  > no-op — delta 0, object survives**; only a Swift `NSObject` subclass
  > *with* stored properties segfaults.
  >
  > The conclusion above is unchanged and reinforced, but the risk profile
  > inverts: a mis-provenanced retain frequently takes **no reference at all
  > while appearing to succeed**, so the damage surfaces later as a
  > use-after-free at an unrelated site rather than as an attributable crash.
  > Consequently, any lifetime fixture must assert the reference-count
  > **delta**; an assertion that merely checks the process survived passes on
  > exactly the broken path.
  >
  > Evidence and harness: `swift-abi-probe/sources/objc-identity`
  > (`arc-cross-matrix.txt`), consumed by dotnet/macios
  > `docs/swift/memory-and-lifetime.md`.

## SwiftString

The redesigned `SwiftString` is a 16-byte struct mirroring the frozen
stdlib `String` (two opaque words). All lifetime operations go through the
stdlib value witness table (metadata from the exported `$sSSN` symbol), so
destroy and copy are ARC-correct for small, large, and shared
representations without interpreting the bridge-object bits. Creation uses
the exported `String(cString:)` initializer (owned +1 result in two
registers); by-value calls borrow; mutation uses the probe-verified
mutating-method convention — the argument string in ordinary registers and
the mutable self pointer in the self register (`$sSS6appendyySSF`).
Content verification happens through Swift (UTF-8 sums, equality,
concatenation), so the skeleton needs no managed UTF-8 readback.

## Module lifetime leases

A `SwiftModuleLease` is a refcounted `NativeLibrary.Load` on the defining
module. `ISwiftTypeToken.GetModuleLease()` (static virtual, default `null`)
lets a token attach a lease to the metadata it resolves; the lease then
flows to every `SwiftTypeMetadata`, VWT view, and `SwiftValueStorage`
derived from it, and `ReleaseHandle` keeps it alive until after the final
witness call. `SwiftMetadataCache<TToken>` holds the lease in a static,
pinning the module for process lifetime once any metadata is cached —
cached metadata pointers are never safe across an unload, so unloading a
Swift module with live projections is unsupported by policy. Handles into
`libswiftCore` need no lease: the OS Swift runtime is permanently loaded.

## Calling-convention audit

Every native entry point the layer calls, with its logical convention and
the managed declaration used:

| Entry point | Logical CC | Managed call | ARM64 notes |
|---|---|---|---|
| `<T>Ma` metadata accessor | `swiftcc` | `delegate* unmanaged[Swift]<long, SwiftMetadataResponse>` | request in `x0`, two-word response in `x0`/`x1`; identical to AAPCS64 for this shape |
| `destroy` (slot 1) | `swiftcc` | `unmanaged[Swift]<void*, IntPtr, void>` | value, metadata in `x0`,`x1` |
| `initializeWithCopy`/`WithTake` (2/4) | `swiftcc` | `unmanaged[Swift]<void*, void*, IntPtr, void*>` | dest, src, metadata in `x0..x2` |
| `getEnumTag` (11) | `swiftcc` | `unmanaged[Swift]<void*, IntPtr, uint>` | tag returned in `w0` |
| `destructiveProjectEnumData` (12) | `swiftcc` | `unmanaged[Swift]<void*, IntPtr, void>` | |
| `destructiveInjectEnumTag` (13) | `swiftcc` | `unmanaged[Swift]<void*, uint, IntPtr, void>` | tag in `w1` |
| protocol requirement witness | `swiftcc` method | `unmanaged[Swift]<…, SwiftSelf, T>` | self address in `x20`; `Self` metadata + witness table trail the formal args |
| `swift_conformsToProtocol` | C | `DllImport` | returns witness table or null |
| `swift_retain` | C | `DllImport` | returns the object |
| `swift_release` | C | `DllImport` | |
| `swift_errorRetain`/`swift_errorRelease` | C | `DllImport`, declared `void`-returning | declaring `void` and reusing the known pointer is correct regardless of whether the runtime returns the object |

Audit conclusions:

1. Value witnesses are `SWIFT_CC(swift)` functions, but none of the
   witnesses used here binds the self (`x20`) or error (`x21`) register:
   witnesses receive metadata as an explicit trailing parameter and cannot
   throw. For every witness shape above, swiftcc and AAPCS64 assign
   identical registers, and `unmanaged[Swift]` function pointers are
   contract-correct (they reserve the special registers without using
   them). Direct managed witness calls are therefore valid on ARM64.
2. On **arm64e**, VWT slots contain IA-key, address-discriminated signed
   pointers. Reading a slot into managed memory and calling it is invalid
   there — authenticated pointers must be invoked at their source storage
   address through the mandatory native helper (open roadmap items). The
   direct-call prototype is gated to non-ptrauth ARM64 accordingly.
3. The ARC entry points used here are standard C-convention exports of the
   OS Swift runtime. Any future use of `preserve_most` runtime entry
   points (e.g. specialized retain/release fast paths) must go through
   audited default-CC veneers; none are used by this layer.

## Open items

- Weak/unowned references (blocked on nonmoving storage semantics).
- Objective-C/unknown-object provenance-aware handling.
- ptrauth (arm64e) native-helper path and per-signature thunks.
- Packaging the layer as a shipping library after API review.

## Source-address invocation discipline (ptrauth-forward)

Authenticated indirect pointers (arm64e) are address-diversified:
they are only valid where they are stored. The support surface is
structured so this holds even on arm64 where it is unobservable:
managed state holds only *table and metadata base addresses* (for
example `SwiftValueWitnessTable` wraps the VWT pointer), every witness
function pointer is loaded from its slot at the call site, and no
witness pointer is cached in a managed field. Audited 2026-07-12
across all suites (no cached-witness statics).

The mandatory route for authenticated targets is the shared native
helper (`libSwiftPtrauthHelper`, SwiftValueLifecycle): managed code
passes the witness slot address, the helper loads and invokes at that
address (plain on arm64), and the arm64e build fails closed at compile
time until the discriminator table carries hardware-verified values.
Helper-routed copy/destroy are parity-tested against direct witness
calls in all four execution modes. Shapes the fixed helper cannot
forward (context-register binding: protocol witnesses, thick closures)
get per-signature generated thunks: the BindingGenerator's
emit-thunk-suite mode emits GeneratedPtrauthThunks.c with the same
slot-address/fail-closed contract, validated in the generated pipeline
suite.

## Cross-runtime reference cycles (policy)

A Swift object that owns a strong `GCHandle` to a managed object which
in turn owns the Swift reference forms a cycle NEITHER collector can
reclaim: the GC sees a rooted handle, Swift sees a +1 reference, and
neither can trace through the other (demonstrated live by
`SwiftSoak.CrossRuntimeCycleNeedsExplicitTerminalOwner`).

Policy — every GCHandle that crosses into Swift MUST have an explicit
terminal owner whose lifetime is managed-side deterministic:

1. **Operation terminal**: the exactly-once completion callback frees
   the handle (the async-thunk ABI rule; lifetime bounded by the
   operation).
2. **Stream close**: subscription handles are freed by the enumerator's
   dispose path (lifetime bounded by the stream).
3. **Wrapper dispose**: for object-lifetime associations, the managed
   wrapper (SafeHandle) owns the Swift reference, and its release runs
   the Swift deinit which frees the handle — the managed side always
   holds the cycle-breaking edge, never the Swift side alone.

A Swift-side deinit may free a GCHandle (patterns 1-3 all end there),
but no design may make a Swift deinit the ONLY exit while the handle
strongly roots the managed owner of that same Swift object. Observers
with unbounded lifetimes must use weak GCHandles and fail closed on a
collected target.

## GC memory-pressure accounting (policy)

Native allocations of KNOWN size held behind small managed wrappers
(value storage sized by VWT stride) call `GC.AddMemoryPressure` at
allocation and `RemoveMemoryPressure` on release once they cross a
threshold (4 KiB in `SwiftValueStorage`), so GC pacing sees large
Swift-side values. Swift class instances are deliberately NOT
accounted: their size is unknowable without private runtime
introspection, and their lifetime is governed by the explicit
dispose/terminal-owner rules above, with the soak lane as the leak
backstop.
