# Swift ABI and managed type model

## Support profiles

This document uses three ABI classifications:

- **Direct:** stable enough for manifest-driven direct implementation.
- **Thunk:** use compiler-generated Swift code by default.
- **Unsupported/private:** do not reproduce or synthesize.

| Mechanism | Profile | Plan |
|---|---|---|
| Public Swift symbols and Swift calling convention | Direct | Direct call from generated raw signatures |
| Self, error, indirect result | Direct | Existing runtime marker types |
| Frozen trivial values | Direct | Managed value type with verified layout |
| Resilient values | Direct | Opaque native storage plus metadata/VWT |
| Metadata accessor invocation | Direct | Generated direct signature |
| Generic metadata and witness arguments | Direct | Explicit generated pointer arguments |
| Required VWT prefix and operations | Direct | Managed support or shared native helper |
| Classic existentials | Direct, guarded | Native storage with manifest-known witness count |
| Synchronous untyped `throws` | Direct | Existing error register plus generated translation |
| Native Swift object ARC | Direct, guarded | SafeHandle and provenance-specific retain/release |
| Existing thin/thick closure invocation | Direct, narrow | Manifest-described function/context |
| Resilient/conditional witness dispatch | Thunk | Compiler-generated thunk |
| Closure construction and reabstraction | Thunk | Compiler-generated closure box/thunk |
| Swift async and task creation | Thunk initially | Generated async thunk |
| Actor/executor transitions | Thunk | Generated Swift code |
| Opaque results and associated-type substitutions | Thunk | Generated thunk unless manifest proves direct shape |
| Typed throws on unknown/downlevel runtime | Thunk | Normalize in thunk |
| Runtime-private/extended existential layout | Unsupported/private | Reject or thunk |
| Constructing metadata, VWTs, witness tables, task frames | Unsupported/private | Never synthesize in initial import design |
| Hand-mangling or discovering calls by demangling | Unsupported/private | Build-time compiler manifest only |

### Named ABI profiles

The Direct/Thunk/Unsupported classifications above describe mechanisms. The
capability handshake (architecture.md) consumes a coarser, named, versioned
profile enumeration. Doc-facing name: `SwiftAbiProfile`. Wire form: exact
strings with the `SwiftInterop.` prefix.

| Profile | String | Scope |
|---|---|---|
| `Sync1` | `SwiftInterop.Sync1` | Synchronous `CallConvSwift` calling convention: `SwiftSelf`, `SwiftSelf<T>` (including reverse by-reference), `SwiftError`, `SwiftIndirectResult`, frozen-struct physical lowering forward and reverse, `delegate* unmanaged[Swift]` invocation, deterministic rejection of unsupported shapes |
| `Values1` | `SwiftInterop.Values1` | Dynamic value and metadata ownership: metadata accessor invocation, required VWT prefix operations, aligned native value storage and lifecycle, ARC object handles, error handles, native ABI helper on ptrauth targets |
| `Generics1` | `SwiftInterop.Generics1` | Generic metadata and witness-table trailing arguments, generic static caching, classic existentials with manifest-known witness counts |
| `Proxies1` | `SwiftInterop.Proxies1` | Managed callbacks into Swift: generated closure boxes, protocol proxies, rooted `UnmanagedCallersOnly` stubs, exactly-once context release, per-signature native thunks on ptrauth targets |
| `AsyncThunk1` | `SwiftInterop.AsyncThunk1` | Generated async-thunk ABI: start/cancel/release operation handles, exactly-once terminal callback, `Task` bridge, bounded async-sequence state machine, eager-listener variant |
| `AsyncDirect1` | `SwiftInterop.AsyncDirect1` | Reserved. Direct Swift async (`CallConvSwiftAsync`, X22, call plans). Undefined until the direct-async research track exits with an explicit go |

Prerequisites:

- `Values1` requires `Sync1`.
- `Generics1` requires `Values1`.
- `Proxies1` requires `Values1`.
- `AsyncThunk1` requires `Values1`. It needs only ordinary C callbacks and
  synchronous `CallConvSwift` entry (see backends.md "CoreCLR interpreter");
  it does not require `Proxies1`. Bindings that use both declare both.
- `AsyncDirect1` must not be reported by any runtime before its contract is
  specified and approved.

What each profile requires:

| Profile | Runtime | Support library | Generated assets |
|---|---|---|---|
| `Sync1` | Full synchronous ABI in the executing mode per the backends.md gates | None beyond CoreLib markers | None |
| `Values1` | `Sync1` | Metadata/VWT/storage/handle surface | Native ABI helper, version-matched |
| `Generics1` | `Sync1` (metadata/witness pointers are ordinary arguments) | Generic static contracts, existential storage | None beyond `Values1` |
| `Proxies1` | Rooted UCO reverse entry, no dynamic code | Callback context lifetime helpers | Closure boxes, proxies, per-signature ptrauth thunks |
| `AsyncThunk1` | `Sync1` plus ordinary C callbacks; no X22, no runtime async | Operation-handle lifecycle, completion bridge, sequence state machine | Generated async thunks exporting the generated native ABI version |
| `AsyncDirect1` | To be defined with the approved call-plan design | To be defined | To be defined |

Rules:

1. A profile name plus number is immutable once shipped. Its requirements
   never change.
2. Any incompatible change to a profile's observable contract — marker
   semantics, ownership actions, handle ABI, callback ordering, lowering —
   defines a new number (`Sync2`). The old string keeps its old meaning; a
   runtime may report both when it implements both.
3. A new capability area gets a new family name, not a bump of an existing
   one.
4. The handshake compares exact strings by set containment. There is no
   numeric ordering, wildcard, or ">= version" semantics.
5. A runtime reports a profile for a RID/execution-mode cell only after the
   applicable validation battery in validation.md passes for that cell.
   Partial implementations must not report. Marker-type presence never
   implies any profile (see current-state.md "Scope boundary").
6. The generator computes each binding's required profile set from the
   classifications it actually emitted and records it in the manifest.

The backends.md "Execution-mode gates" rows map onto the profiles as follows:
scalar/frozen ABI and direct function pointer are `Sync1`; the dynamic value
support library is `Values1`; generic metadata/witness arguments are
`Generics1`; generated synchronous and async thunks add `Proxies1` and
`AsyncThunk1` for their callback and lifecycle surfaces; direct Swift async is
`AsyncDirect1`.

## Managed representation rules

### Scalars and imported C values

Use the corresponding unmanaged C# type when the Swift ABI representation is
identical and ownership-free.

Examples:

- fixed-width integers;
- pointer-sized `Int`/`UInt`;
- floating-point values;
- raw pointers;
- imported C enums and structs;
- C function pointers.

Swift `Bool` is not automatically equivalent to managed `bool` for enum spare
bits and layout. A dedicated representation may be required for frozen Swift
layout.

### Frozen trivial values

A frozen Swift struct or enum may be projected as a C# value type only when the
manifest proves all of the following:

- the layout is ABI-public for the target;
- the value is copyable;
- it is POD/bitwise-takable for the required operations;
- all nested fields have a representable managed layout;
- C# implicit copies do not require VWT or ARC work.

`@frozen` alone is insufficient. It fixes Swift ABI layout; it does not promise
C layout or trivial copy semantics.

Raw calls use the managed type directly and let the existing Swift physical
lowering classify it.

### Frozen nontrivial values

A frozen value that contains reference-counted or otherwise nontrivial fields
must not be exposed as an ordinary mutable C# struct. C# assignment would copy
bits without invoking Swift copy/destroy operations.

Project it as a class owning native storage:

```text
SwiftValueHandle<T>
  metadata
  aligned native storage
  initialized/moved/disposed state
  VWT operation plan
```

Expose explicit `Clone`, equality, and conversion APIs where useful. Managed
reference assignment shares the wrapper; it does not silently create a Swift
value copy.

### Resilient values

Resilient structs and enums are always opaque to managed layout. The wrapper:

1. calls a compiler-generated metadata accessor;
2. reads size, stride, alignment, and flags from the VWT;
3. allocates correctly aligned native storage;
4. invokes a constructor through `SwiftIndirectResult`;
5. tracks initialized state;
6. destroys through the VWT;
7. frees aligned storage.

The handle must not call `destroy` on uninitialized storage when a throwing
initializer fails. Move/take operations must invalidate the source.

### Native Swift classes

Project native Swift classes as managed reference types holding a retained
native object pointer.

The wrapper must distinguish:

- native Swift object;
- Objective-C object;
- unknown bridge object;
- actor;
- weak/unowned reference storage.

Retain/release operations are chosen from manifest provenance. Do not apply
`swift_retain` blindly to Objective-C or foreign objects.

Identity caching is optional for import-only bindings. If implemented, use
weak managed references and make disposal/retention rules explicit.

### Objective-C classes

Reuse existing .NET Apple `NSObject` wrappers and registrars. Swift-only
extensions may call Swift symbols while using the existing native handle as
`SwiftSelf`.

### Actors

Actors use object-like identity but may only be accessed through generated
isolation-aware async paths. A managed actor wrapper does not grant direct
synchronous field access.

## Extended layout

The approved `ExtendedLayout` design is the correct runtime hook for Swift
frozen generic layout and selected frozen enums.

### `SwiftStruct`

The intended algorithm must model Swift field placement, including:

- nested unpadded size;
- field alignment;
- top-level stride;
- generic instantiation layout;
- no GC references in raw ABI storage;
- spare-bit contribution from special primitive representations.
- zero-sized Swift values.

The unresolved issue is that Swift exposes both size and stride, while many
.NET APIs expose one `sizeof(T)`. A prototype must define behavior for:

- nested fields;
- arrays and spans;
- boxing;
- `Unsafe.SizeOf<T>()`;
- `Marshal.SizeOf<T>()`;
- R2R layout checks;
- generic sharing;
- reflection.

Array/span element pitch must match Swift stride. The core open question is how
the runtime simultaneously represents that stride and the smaller unpadded copy
size needed when the value is nested or contributes spare bits, including what
`Unsafe.SizeOf<T>()` and reflection report.

`ExtendedLayout` is therefore only a candidate hook. Shipping Swift kinds
requires a CoreCLR VM and managed AOT dual-size contract covering arrays,
nesting, tail-padding reuse, boxing, reflection, generic sharing, zero-sized
values, and call ABI. Opaque native storage remains the permanent fallback.
Swift-specific kinds remain internal prototypes unless API review explicitly
approves their limited runtime and architecture contract.

StoreKit 2 does not need to wait for this feature because resilient values can
use opaque storage. `SwiftStruct` is nevertheless important for performant
standard-library values, optionals, frozen generic types, and SwiftUI.

### `SwiftEnum`

The proposed layout must support:

- no-payload enums;
- single-payload enums and extra inhabitants;
- multi-payload enums and spare bits;
- generic payloads;
- required discriminator bits;
- `SwiftBool` and Swift object pointer spare bits.

Raw tag interpretation should use VWT enum witnesses whenever possible.
`SwiftEnum` layout is needed only when direct frozen layout materially improves
the ABI. Resilient and nontrivial enums remain opaque wrappers.

## Metadata

`TypeMetadata` is an opaque pointer. Generated code may:

- invoke a manifest-listed metadata accessor;
- request complete metadata;
- cache the result in a generic static;
- access the required VWT pointer preceding complete metadata;
- pass metadata as an ordinary hidden argument.

On ptrauth targets, managed code treats the VWT pointer and its function slots
as opaque. The shared native helper retrieves, authenticates, and invokes them
at the original storage address.

Generated code must not:

- copy or construct metadata;
- inspect runtime-private metadata kinds;
- assume generic argument offsets;
- infer completion by peeking at private fields;
- keep a pointer after unloading its defining library.

Candidate low-level types:

```csharp
readonly struct SwiftTypeMetadata;
readonly struct SwiftMetadataRequest;
readonly struct SwiftMetadataResponse;
readonly struct SwiftNominalTypeDescriptor;
readonly struct SwiftProtocolDescriptor;
readonly struct SwiftProtocolConformanceDescriptor;
readonly struct SwiftProtocolWitnessTable;
```

The actual public surface should be smaller than the implementation surface.
Generated bindings can use internal or friend APIs until the model is proven.

## Value witness tables

The support-layer API exposes only the ABI-required VWT prefix:

- initialize buffer with copy;
- destroy;
- initialize/assign with copy;
- initialize/assign with take;
- single-payload enum tag operations;
- size;
- stride;
- flags;
- extra inhabitants;
- enum witnesses when present.

Rules:

1. Metadata must be complete.
2. Function-pointer calling convention and pointer-authentication schema must
   come from the manifest/runtime profile.
3. VWT pointers are consumed, never synthesized or modified.
4. Noncopyable values reject copy operations.
5. Native storage uses alignment and stride, not `NativeMemory.Alloc(size)`
   without alignment.
6. Destruction is exactly once.
7. A move invalidates the source handle.

## Ownership model

Every generated parameter and result has an ownership annotation in the
manifest and an explicit generated action plan.

| Swift ownership | Managed action |
|---|---|
| borrowed/guaranteed | Keep source alive for call; no transfer |
| owned/consuming | Transfer or create owned copy; invalidate source if moved |
| indirect in | Allocate/copy as required; destroy temporary |
| indirect inout | Pin native storage; preserve valid initialized value |
| owned result | Adopt without an extra copy when ABI permits |
| autoreleased ObjC result | Use Objective-C retain/autorelease rules |

The support layer should provide scoped helpers that make cleanup
exception-safe. Generated methods should have one visible ownership plan rather
than scattered retain/release calls.

## Standard library values

### `String`

Expose a Swift-native `SwiftString` wrapper for repeated calls and add explicit
convenience conversions to/from `string`. Do not hide repeated allocation in a
hot-loop API.

### `Array`, `Set`, and `Dictionary`

Use generated generic metadata and witnesses. The wrappers own native Swift
storage and expose idiomatic managed interfaces where semantics are clear.
Trimming must statically root every closed helper shape used by generated code.

### `Optional<T>`

Support two internal strategies:

- direct frozen layout when `ExtendedLayoutKind.SwiftEnum` is available and
  safe;
- opaque/VWT storage otherwise.

Map to nullable/reference-null convenience APIs only when no information or
ownership is lost.

Do not infer spare-bit behavior from the managed type category. Swift 6.4
empirically reports `Optional<Int>` as size 9/stride 16, while class-reference
and `Bool` optionals can reuse extra inhabitants. Query metadata/VWT or use a
generated thunk.

### Raw-representable values

Project future-extensible `RawRepresentable` structs as wrappers that preserve
unknown raw values. Do not emit closed C# enums when Swift permits future
values.

## Extended layout for Swift value projection

**Status: PROPOSED (VM prototype implemented; non-shipping).**

`ExtendedLayoutKind.SwiftStruct` (raw value `2`, deliberately absent from the
public enum) lays a managed value type out with Swift's struct rules so a
frozen Swift value can be projected as a C# struct even where sequential
layout diverges. Prototype: CoreCLR VM only
(`EEClassLayoutInfo::InitializeSwiftStructFieldLayout`), validated by
`src/tests/Loader/classloader/ExtendedLayout/SwiftStructLayout`.

Layout rules (all test-verified):

1. **Declaration order, no reordering.** Fields are placed sequentially at
   their natural alignment; the type's alignment is the maximum field
   alignment. Packing directives and explicit sizes are not honored.
2. **Size versus stride.** Swift distinguishes the unpadded *size* from the
   array-element *stride* (size aligned up). The managed instance size is
   the **stride**, so managed arrays, spans, boxing, `Unsafe.SizeOf`,
   `Marshal.SizeOf`, and reflection all observe the same element step Swift
   arrays use. The unpadded Swift size is recorded in the layout info and
   consumed by rule 3; it is not directly observable from managed code in
   the prototype.
3. **Tail packing.** A nested `SwiftStruct` field occupies only its unpadded
   Swift size, so later fields pack into the nested value's tail padding
   (`{ {Int64; Int8}; Int8 }` places the trailing field at offset 9, stride
   16 — identical to Swift). Nested values of any *other* layout kind keep
   their full managed size: no tail packing across layout models.
4. **Zero-sized values.** A fieldless `SwiftStruct` maps to Swift's empty
   struct: unpadded size 0, managed instance size 1 (the runtime's
   zero-sized flag), matching Swift's stride 1. A nested empty field
   occupies no space, so following fields start at its offset.
5. **Fail-closed constraints.** GC references, byref-like types, base
   classes, auto-layout fields, inline arrays, and explicit sizes are all
   rejected at type load, mirroring `CStruct`. `SwiftStruct` types are
   always blittable.
6. **Other type systems.** The managed AOT type system (ILC/crossgen2)
   implements the same rules through the shared
   `MetadataFieldLayoutAlgorithm`, with the nested unpadded size computed
   recursively; the ABI suite passes precompiled under R2R and as a
   NativeAOT binary. The `SwiftEnum`/`SwiftBool` kinds and spare-bit
   representations remain separate roadmap items.

Direct C# projection through this kind stays restricted to trivial/copy-safe
types (roadmap Phase 5); resilient and nontrivial values continue to use the
support-library storage model.

## Spare bits and discriminator policy

**Status: PROPOSED (witness-backed; test-verified).**

Swift packs enum discriminators into a payload's *extra inhabitants*
(bit patterns the payload never produces): `Bool` has 254
(`Optional<Bool>` and `Optional<Optional<Bool>>` stay one byte;
`true`=1, `false`=0, `nil`=2), and object references have pointer extra
inhabitants (`nil` is the zero word; optionals of references stay one
word). All values above are probe- and witness-verified
(`SwiftGenericMetadata`).

Policy replacing the earlier `RequiredDiscriminatorBits` sketch:

1. **The VWT is the source of truth.** A type's `extraInhabitantCount`
   comes from its value witness table (slot 10, high 32 bits) and is
   recorded per type in the binding manifest; nothing re-derives spare
   bits from layout.
2. **Managed mirrors never claim spare bits.** A projected value type
   reports zero extra inhabitants of its own; single-payload
   discrimination on mirrored storage goes exclusively through the
   `getEnumTagSinglePayload`/`storeEnumTagSinglePayload` witnesses
   (slots 6/7, valid on every type), so the encodings remain
   compiler-owned even when they change across payload types.
3. **`SwiftBool`** is therefore a plain one-byte mirror whose optional
   encodings are witness-managed; no dedicated extended-layout kind is
   required for the synchronous profile.
4. Multi-payload discriminator layouts stay behind the enum witnesses
   (`SwiftEnum` extended-layout projection remains a separate,
   unprototyped item).

## Generic functions and types

### Verified direct generic convention (osx-arm64)

IR- and execution-verified by `src/tests/Interop/Swift/SwiftGenericCalls`
(all four execution modes): a direct generic Swift call from a closed,
nongeneric managed entry point passes

1. opaque generic values by pointer (callee-owned reads, caller storage);
2. a generic return through the indirect-result register (`sret`);
3. `Self` type metadata after the formal arguments;
4. witness tables after the metadata, one per requirement, in the
   requirement's declaration order (`<T: Doubler & Tagger>` passes
   `T.Doubler` then `T.Tagger`).

Witness tables come from exported conformance (`…WP`) symbols or from
`swift_conformsToProtocol` at runtime (validated against the
standard-library `AdditiveArithmetic` conformances of `Int64`/`Double`);
metadata for instantiations comes from `…Ma` accessors, direct metadata
symbols, or `Any.Type`-returning helpers. Resilient values flow through the
same opaque-pointer shape from support-library storage.

### Generic methods

Generic metadata and protocol witness tables are generated as explicit trailing
arguments in the compiler-defined order. This is already demonstrated by the
runtimelab CryptoKit projection.

The runtime does not need to understand Swift generic constraints to pass these
arguments.

### Generic types

Resilient generic values use metadata/VWT-owned storage.

Frozen generic values have two paths:

- `ExtendedLayoutKind.SwiftStruct` for direct, trivial, representable closed
  layouts;
- opaque value wrapper otherwise.

No per-instantiation helper struct should be generated once the extended
layout path is available and validated.

### Generic sharing and NativeAOT

Generated code uses static abstract generic contracts or generated closed helper
types. It must not use `MakeGenericType`, reflection invocation, or runtime
emission to obtain metadata and witnesses.

## Protocols and existentials

### Generic protocol constraints

Generated generic methods pass:

1. value address;
2. type metadata;
3. witness tables in compiler-defined requirement order.

### Classic existential containers

Implement native storage for:

- three-word inline value buffer;
- type metadata;
- manifest-known witness-table vector.

Use VWT copy/destroy. Do not expose raw container layout as a user-facing
struct.

The manifest records the exact representation:

- opaque existential;
- class existential;
- error existential;
- extended/parameterized existential.

It also records witness provenance:

- direct unconditional table;
- accessor-produced table;
- conditional/resilient resolver.

Direct binding initially supports only proven classic opaque/class cases with
unconditional or accessor-produced witnesses. Error, extended, conditional,
associated-type, and resilient cases use generated native resolvers/thunks.

### Protocol method invocation

Use one of:

- a public manifest-listed dispatch thunk;
- a generated Swift thunk;
- a manifest-proven fixed witness entry for a narrow non-resilient case.

Do not infer witness indices from source declarations at runtime.

### Protocols with associated types

Project through generated generic wrappers and Swift proxy/thunk code. C#
interfaces alone cannot represent caller- versus callee-selected opaque types.

Protocol-composition returns have a related language mismatch. A Swift
`P1 & P2` result lets the callee select the concrete type, while a C# generic
return lets the caller select it. Project these results as typed existential
wrappers with explicit unboxing/casting APIs rather than inventing a misleading
generic return.

## Enums and errors

### Enums

Use VWT enum witnesses to inspect and project payloads. Generated APIs should
avoid destructive projection unless they own the value or create a copy first.

### Swift errors

The existing `SwiftError` marker handles only the error register. Add:

- an owning `SwiftErrorHandle`;
- retain/release through an audited ABI path;
- a generic `SwiftException`;
- generated mapping for known framework errors;
- optional `NSError` bridging where Swift supplies it.

Forward calls:

1. clear the error register;
2. invoke Swift;
3. adopt the returned error pointer;
4. translate in generated code;
5. release exactly once.

Reverse calls:

- catch every managed exception before leaving `UnmanagedCallersOnly`;
- translate through generated Swift code;
- never let a managed exception unwind into Swift.

Typed throws initially use a generated thunk that normalizes the error.

### Process-failure policy

Swift error handling covers recoverable `throws`. Everything else is process
failure, and the policy is explicit.

**Swift traps terminate the process.** `fatalError`, precondition and
assertion failures, force-unwrap failures, exclusivity violations, and Swift
runtime traps end the process by design. This is accepted and documented
behavior. Managed code must never catch, intercept, or translate them — no
signal handlers, no vectored recovery, no API contract that promises survival.
Generated thunks must not add trap paths absent from the underlying API: no
`try!`, no force unwraps, no unchecked conversions (see prior-art.md for the
runtimelab failure catalog).

**Managed exceptions never reach Swift.** Generated `UnmanagedCallersOnly`
stubs and thunk callbacks catch every managed exception before the boundary
(see "Swift errors"). If an exception nevertheless escapes — a generated-code
defect — the runtime fails fast. That failfast is documented behavior, not
suppressed; the defect is fixed in the generator, and validation.md reverse
error tests assert no such path exists.

**Shutdown ordering.** After managed shutdown begins, finalizers and
`SafeHandle` releases may race Swift module teardown. Every metadata, witness,
VWT, closure, and function-pointer wrapper holds a module-lifetime lease
(see backends.md "NativeAOT"). A release that cannot acquire the lease becomes
a no-op and leaks. Leaking native memory at process exit is acceptable;
calling into an unloaded or torn-down Swift module is not. Generated async
operation handles keep `release` idempotent and safe after shutdown starts.

**Async at teardown.** Process exit does not wait for in-flight Swift tasks.
Completion callbacks racing shutdown either deliver into still-valid managed
state or are dropped by the lease check; exactly-once release still holds.
Dropped completions must be recoverable by design: for StoreKit, the durable
verify -> deduplicate -> grant -> finish pipeline recovers any lost purchase
result on the next launch through `Transaction.updates` and
`Transaction.currentEntitlements` (see storekit2.md).

**Background, foreground, and watchdog.** iOS suspension is not process
death. The bridge must not run timers or watchdogs that assume wall-clock
progress; suspended operations resume with the process. The system may kill a
suspended process without notice — the same recovery rule as teardown
applies. Blocking the main thread on a Swift executor is prohibited partly
because the platform watchdog kills the process for it.

No policy may convert a process-death case into a silent error return. Fail
closed and fail loud.

## Closures

A Swift thick closure is not just a managed function pointer. It includes a
retained context and may require reabstraction.

### Calling an existing closure

Direct invocation is possible when the manifest supplies:

- ABI-visible entrypoint;
- context pointer;
- lowered signature;
- ownership.

The context is passed through the Swift self/context position.

The ABI-visible entrypoint may be a compiler-generated partial-apply forwarder,
not the raw closure body. Empirical Swift 6.4 code uses `_TA` thunks that move
X20 into the closure body's first ordinary argument before tail-calling it. The
manifest must name the forwarder actually stored in the closure value.

### Passing a managed callback to Swift

Use generated Swift code to construct an escaping closure box:

- C callback function pointer;
- managed `GCHandle` context;
- dispose callback;
- Swift ARC-owned box;
- exactly-once context release.

Nonescaping callbacks may later use a cheaper scoped path after lifetime tests.

`@Sendable` callbacks remain in generated Swift code until the managed capture
and thread-safety policy is explicit. A C# delegate is not assumed Sendable.
For synchronous nonthrowing predicates such as storefront filters, generated
callbacks catch managed exceptions, return a deterministic fail-closed value,
report the exception out of band, and tie `GCHandle` lifetime to Swift
closure-box ARC.

## Async and actors

Swift async is a distinct ABI from .NET runtime async. It uses:

- Swift async context register;
- compiler-defined context layouts;
- continuation functions;
- task allocation and executor operations;
- `swifttailcc`;
- target- and version-sensitive pointer authentication.

### Initial product path

Generate Swift async thunks that:

- launch or enter the correct actor/executor;
- `await` the original declaration;
- catch Swift errors;
- transfer the result into owned support-library storage;
- invoke a C callback exactly once;
- support cancellation and disposal;
- never synchronously block an executor.

Managed code maps this to `Task`, `ValueTask`, or `IAsyncEnumerable<T>`.

`Task` is the baseline bridge. `ValueTask` requires a separately validated
`IValueTaskSource` pooling design; it is not a free wrapper over
`TaskCompletionSource`. Async sequences require an explicit bounded
backpressure state machine.

For `AsyncSequence`, the baseline is demand driven:

- one Swift `next()` operation per managed `MoveNextAsync`;
- at most one in-flight `next()` per enumerator;
- an explicit native task/enumerator handle;
- one terminal completion/error/cancellation state;
- disposal arbitrates with an in-flight operation and releases its item
  exactly once.

Long-lived event sequences may override the pull lifecycle. In particular,
`Transaction.updates` must start listening during application startup and
buffer or durably process events before a managed enumerator attaches. It
cannot wait for the first `MoveNextAsync`.

### Advanced direct path

Investigate `CallConvSwiftAsync` and `SwiftAsyncContext` only after the thunk
path is production-ready. Direct support requires a compiler-produced async
call plan and must not inspect or construct private Swift `AsyncTask`,
`AsyncContext`, executor, or coroutine-frame layouts.

Any new async marker remains internal/non-shipping until API review explicitly
approves its ARM64 and in-scope-runtime contract.

Empirical Swift 6.4 code shows why this is not a register-only feature:

- the caller reads context size from the `Tu` async-function-pointer record;
- allocates a context with `swift_task_alloc`;
- installs its resume address;
- tail-branches to the callee;
- the callee tail-branches to the resume function rather than returning;
- continuation code deallocates the context and switches executors.

The frame-pointer async marker observed in generated machine code is a
diagnostics/backtrace implementation detail and must not be copied into a
stable .NET contract without matching Swift source guarantees.

Existing .NET runtime-async infrastructure may be reused for managed Task
completion, diagnostics, and scheduling, but not for the physical Swift ABI.
Its managed continuation return also overlaps registers used by Swift
multi-register results, so the two calling conventions cannot be combined on a
single method.

## Generated async-thunk ABI (v1, stable)

**Status: implemented and test-verified (`SwiftAsyncBridge`; the StoreKit
lane uses the same shape).**

1. **Version gate.** `…abiVersion() -> Int64` precedes any use; unknown
   versions fail closed (SWIFT0004 family).
2. **Operation handles.** `begin…` returns a +1 owned operation handle.
   `cancel` and `release` are idempotent and safe in any order relative to
   completion. Completion may fire synchronously before `begin` returns.
3. **Exactly-once terminal state machine.** Exactly one terminal callback
   fires per operation with a terminal state (completed/failed/cancelled);
   post-terminal cancels are no-ops and never re-fire the callback.
4. **Completion shape.** C callback plus caller context. The managed
   context (GCHandle) is owned by the terminal callback and freed there —
   exactly once, never by managed disposal; abandoning the Task is safe
   (late completions are inert).
5. **Result and error ownership.** Scalar results pass by value. Value
   results initialize caller-owned storage sized from the rooted metadata
   (the caller adopts and later destroys via the VWT). Failures pass error
   codes (or boxes) that the managed side adopts as exceptions. Ownership
   is fixed per terminal state: nothing transfers on cancellation.
6. **Managed bridging.** `Task` (not `ValueTask`) via
   `TaskCompletionSource` with `RunContinuationsAsynchronously` —
   completions arrive on Swift executor threads and must never run managed
   continuations inline or block the executor. `CancellationToken`
   registration maps to `cancel` (cooperative — distinct from disposal).
7. **Actor isolation.** MainActor completions deliver on the main queue;
   headless hosts must pump it (`CFRunLoopRunInMode` on the main thread) or
   install a delivering `SynchronizationContext` — verified both ways.
   Actors remain reentrant at await points.
8. **AsyncSequence adapters.** Pull model: exactly one in-flight `next()`
   per `MoveNextAsync` (backpressure is inherent); disposal marks the
   iterator stopped, waits out any in-flight next, then releases. Eager
   startup listeners buffer on the managed side (the lane's durable
   service) so no update drops before a consumer attaches.

## Pointer authentication

Direct symbol calls are the safest arm64e path. Indirect pointers from VWTs,
witness tables, closures, and metadata may be address-discriminated.

The manifest/runtime profile must identify:

- whether authentication is required;
- standard pointer category;
- address discrimination;
- whether a pointer may be moved or cached;
- whether a generated native thunk is mandatory.

Standard Swift ABI structures use fixed discriminator constants defined by the
Swift ABI headers. The runtime/native helper should mirror those constants in a
versioned table rather than rediscovering them per declaration. The manifest
identifies the pointer category and source storage. Per-declaration manifest
data is reserved for generated closure/context schemes or future ABI features
that are not covered by the standard table.

The fixed native helper cannot forward arbitrary protocol-witness or closure
signatures. Those calls require generated, signature-specific native thunks on
ptrauth targets.

Unknown schemas select a generated thunk or reject. Managed code must never strip, copy,
or re-sign an unknown authenticated pointer.

## Opaque results, result builders, and parameter packs (Phase 10, verified 2026-07-12)

Probe corpus `swift-abi-probe/sources/phase10/` (C harness `HARNESS
PASSED`, three legs):

- **`some P` returns are always indirect**: `swiftcc void f(sret out,
  args...)` regardless of underlying size. The caller obtains the
  underlying type through the exported opaque type descriptor
  (`<fn-mangling>QOMQ`): metadata via
  `swift_getOpaqueTypeMetadata2(request, genericArgs, descriptor,
  index 0)` (swiftcc, returns {metadata, state}) and each conformance
  via `swift_getOpaqueTypeConformance2(genericArgs, descriptor,
  ordinal)` (conformances start at ordinal 1). Buffer sized from the
  opaque metadata's VWT; value lifecycle through that VWT; protocol
  calls through the obtained witness table (dispatch thunk or slot).
  `some View` binds exactly this way — the value is opaque, the
  conformance is the View table.
- **Result-builder parameters are ABI-ordinary closures**: the builder
  attribute transforms Swift-side closure *literals* only; the
  parameter lowers as a thick closure (fn, context) pair, and a foreign
  caller passes any closure of the right type (proven by passing a C
  function returning a pre-built value). `@ViewBuilder` parameters
  therefore need no new convention — only closure-returning-opaque
  plumbing.
- **Parameter packs**: `f<each T>(repeat each ts)` lowers with a pack
  buffer pointer, an explicit pack length, and a metadata pack pointer
  (`(ptr pack, i64 count, ptr metadataPack)` observed). This is a
  materially different convention (runtime-built packs); per the
  roadmap's alternative, the generator erases packs behind
  fixed-arity generated thunks or classifies the API unsupported with
  an explicit reason — direct pack construction is not pursued.

### Associated-type witness dispatch (View.body, verified 2026-07-13)

Dispatching a protocol requirement whose type involves an associated
type needs only exported symbols (probe
`swift-abi-probe/sources/phase10/viewbody_harness.c`, generated test in
the pipeline suite):

1. Associated type metadata:
   `swift_getAssociatedTypeWitness(request, wtable, conformingMetadata,
   reqBase, assocDescriptor)` (swiftcc, returns {metadata, state}) —
   for View, both descriptors are exported: `$s7SwiftUI4ViewTL`
   (requirements base) and `$s4Body7SwiftUI4ViewPTl` (Body associated
   type descriptor).
2. Dispatch through the exported thunk
   (`$s7SwiftUI4ViewP4body4BodyQzvgTj`): `(sret bodyBuffer, Self
   metadata, wtable, self in the context register)`; the buffer is
   sized from the associated type metadata's VWT and the result
   lifecycles through it.
3. Primitive views' Body is Never (body traps); headless dispatch uses
   a composed wrapper view, which the generator emits alongside the
   view bindings.

## Isolation must fail closed (2026-07-13)

Actor isolation is ABI-relevant: a `@MainActor`-isolated function needs an
actor hop, and MISSING one is a silent correctness bug — the call simply
runs on the wrong executor. The manifest's isolation field therefore fails
closed:

- **An explicit `nonisolated` is proof; silence is not.** A declaration
  that says `nonisolated` is recorded as such. A declaration with NO
  fragments carries no evidence, so it is `"unknown"` — never
  `"nonisolated"` — and the classifier refuses to bind it (SWIFTGEN008,
  "unprovable isolation"), because binding it would mean calling it
  without an actor hop it may require. Verified by stripping the
  fragments from a real StoreKit initializer: the code fires and the
  declaration becomes unsupported instead of silently bound.
- **Beware the substring.** `"nonisolated"` CONTAINS `"isolated"`, so a
  naive substring test misreports every nonisolated declaration as
  unknown. (It did, briefly, in this very detector.) The match is
  word-bounded.
- **The spelling varies by source.** Symbol graphs render `@MainActor`;
  `.swiftinterface` files render `@_Concurrency::MainActor` (verified
  against the StoreKit interface). The detector matches any
  MainActor-suffixed attribute rather than one literal spelling.
- **Other global actors are `"unknown"`, not guessed.** A binding must not
  invent an executor identity it cannot prove.

Measured against real StoreKit (macOS arm64): **65 `mainActor`, 319
`nonisolated`, 0 `unknown`** — every callable in the SDK carries its
isolation explicitly, so nothing is refused today; the SWIFTGEN008 guard
exists for the case where that stops being true.
`Product.purchase(options:)` is correctly `mainActor`, while the
`confirmIn:`-taking overloads are genuinely nonisolated despite taking an
`NSWindow` — verified against the `.swiftinterface`, not assumed from the
parameter type.

## @MainActor async calls require a main-queue pump (2026-07-13)

**A `@MainActor`-isolated Swift async function never completes in a host
that does not drain the main dispatch queue.** Not slowly — never. The
callback simply does not arrive, with no error and no diagnostic.

Measured three ways (`SwiftMainActor` suite; probe in
`swift-abi-probe/sources/mainactor`):

| call | host does NOT drain the main queue | host drains it |
|---|---|---|
| nonisolated `async` | **completes** | completes |
| `@MainActor async` | **HANGS FOREVER** | **completes** |

The main actor's executor *is* the main dispatch queue. If nothing
services it, work enqueued onto the main actor never runs, so the
continuation never resumes.

### What this means for bindings

- **App hosts are fine — VERIFIED, not assumed.** The iOS-simulator and
  Mac Catalyst lanes call a `@MainActor async` Swift function from managed
  code and it completes (`SIMLANE mainActor = 42`), because the app host
  runs a main run loop that drains the main queue. This was originally
  written as an assumption; it is now a measured fact on two real app
  hosts — and on a **physical iPad** (backends.md "Physical iOS device
  lane"), where the `@MainActor` call completes under R2R, the
  interpreter, and NativeAOT alike.
- **Console-style hosts are not.** A .NET console app, a test host, or
  any process whose main thread blocks (`Thread.Sleep`, `Task.Wait`,
  a spin loop) will hang on the first `@MainActor` Swift call. The
  remedy is to service the main run loop — `CFRunLoopRunInMode` on the
  main thread — which the suite demonstrates.
- **The manifest already records which declarations are affected**
  (`isolation.kind == "mainActor"`; 65 of them in StoreKit, including
  `Product.purchase(options:)`). A generated binding for a mainActor
  declaration must therefore either require a pumped host or provide the
  pump; it must not silently hang.

This is why isolation detection has to be correct: getting it wrong does
not produce a wrong answer, it produces a **hang**.

### The generator closes the loop (emitter v9)

The isolation field is not decoration — the emitter ACTS on it. For every
declaration the manifest records as `isolation.kind == "mainActor"`, the
generated binding calls `SwiftMainActorGuard.RequireServicedMainQueue`
first. The guard probes the main queue (enqueue a block, see if it runs —
the only honest test, since the answer depends on what the HOST does) and
raises **SWIFT0007** naming the declaration and the remedy, instead of
hanging forever.

So the chain is complete and machine-checked end to end:

**symbol graph → manifest records isolation → classifier refuses what it
cannot prove (SWIFTGEN008) → emitter guards what it can (SWIFT0007) →
generated test asserts the guard fires on an unpumped host.**

## Reverse interop: managed objects projected into Swift (2026-07-17)

The forward direction (managed calling Swift) is most of this document.
The reverse direction — Swift calling managed, and a managed object
standing in for a Swift closure or protocol conformer — has its own ABI
and, uniquely, a cross-runtime *lifetime* problem.

### The reverse-call ABI (already in place)

Swift invokes a managed callback through an ordinary function pointer. A
managed method marked `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]`
is a Swift-convention entrypoint: it receives `SwiftSelf` (the x20 context
register) wherever the signature places it, `SwiftError` (x21), and an
indirect result (x8) — the mirror of the forward special registers. These
paths are validated in the Phase 7 suites (`SwiftSpecialArgOrdering`,
`SwiftSelfContext`, `SwiftIndirectResult`, `SwiftReentrancy`).

For closures and protocol proxies a **`@convention(c)`** callback is the
right tool rather than the raw x20 self: it lets the Swift compiler
generate the closure/witness plumbing while the foreign body is an
ordinary C-ABI function pointer. The managed context travels explicitly
as a pointer argument (a `GCHandle`), which is clearer and more robust
than smuggling it through the self register.

> **On arm64e a `@convention(c)` value is not an *ordinary* C function
> pointer: the call site authenticates it.** Measured statically from Swift
> codegen (dotnet/macios `swift-abi-probe/sources/ptrauth-convention-c`;
> Xcode 27 beta 2, `swiftc -O -S`, `arm64e-apple-macos15.0` versus `arm64`):
>
> - Calls through a `@convention(c)` value emit `braaz`/`blraaz` — key **IA**,
>   **zero discriminator**, no address diversity. The plain `arm64` build
>   emits no PAC/AUT instructions at all.
> - **Passing the pointer as a parameter is not an exemption.** A value
>   arriving as a `@convention(c)` *parameter* and called immediately emits
>   the same authenticated branch as one loaded from a stored field, so
>   "pass it rather than store it" is not a way to avoid signing.
> - **Storing does not sign.** The stored-property getter is a bare
>   `ldr`/`ret` and the initializer signs nothing: the value is stored
>   *already signed* and authenticated only at the call. Whatever hands the
>   pointer to Swift must therefore produce it already signed.
>
> Because the discriminator is zero and there is no address diversity, a
> single signing helper covers every reverse entry point on this shape — no
> per-site discriminator table is needed, which is a simplification for the
> `Proxies1` profile's "per-signature ptrauth thunks" line above.
>
> **However, whether any of this is enforced depends on the process, not on
> the code.** Measured (dotnet/macios `swift-abi-probe/sources/
> ptrauth-crossarch`): one arm64e-only Swift dylib whose codegen contains
> `braaz`, called three ways —
>
> | Process arch | Pointer passed | Outcome |
> |---|---|---|
> | arm64 | unsigned | **returns normally** |
> | arm64e | properly signed | returns normally |
> | arm64e | `ptrauth_strip`ped | **SIGSEGV** |
>
> The arm64 process genuinely executed the arm64e code (`lipo -archs` reports
> `arm64e` alone; `DYLD_PRINT_LIBRARIES` shows that image loaded). The third
> row is the control: without it, the first row could equally mean the probe
> never exercised authentication.
>
> So the natural follow-on question — does a managed `[UnmanagedCallersOnly]`
> pointer taken with `&Method` arrive signed? — is **moot wherever managed
> code runs in an arm64 process**, which is every configuration .NET ships
> today: there is no arm64e RID. An explicit signing step becomes necessary
> only if managed code is ever hosted in an arm64e process, or .NET gains an
> arm64e RID. Because the discriminator is zero and there is no address
> diversity, keeping one signing helper in reserve costs a helper rather than
> a table.
>
> Still unmeasured: all three rows were taken on macOS, none on an iOS
> device. The mechanism is architectural plus kernel process state, so iOS is
> expected to match, but that is an expectation.

### The GC↔ARC lifetime bridge (the hard part)

A managed object handed to Swift must stay alive as long as Swift refers
to it, and must be released deterministically when Swift lets go — across
two independent memory managers. The mechanism:

1. Managed allocates a **`GCHandle`** rooting the object and passes its
   raw pointer to Swift.
2. Swift wraps that pointer in a small **box** class. The box's `deinit`
   calls a managed `free` callback that frees the `GCHandle`.
3. The box is captured by the Swift closure (or held by the proxy), so
   Swift's ARC governs the box: while Swift holds the closure/proxy, the
   box lives, the `GCHandle` lives, the managed object is rooted; when
   Swift releases it, the box `deinit` runs and the `GCHandle` is freed
   **exactly once**.

This makes the managed object's GC lifetime a function of the Swift
object's ARC lifetime — the "GC/ARC lifetime state machine."

### Closure box vs. protocol proxy

- **Closure box**: the box is captured by an `@escaping (T) -> U` Swift
  closure whose body trampolines to a managed callback. A managed object
  *is* a Swift closure.
- **Protocol proxy**: a generated Swift type conforms to the Swift
  protocol and forwards each requirement to a managed callback; the box
  ties lifetime. Because the proxy genuinely conforms, Swift's generic and
  existential dispatch (the witness table) lands in managed code. A
  managed object *is* a Swift protocol conformer.

Both are validated end to end (`SwiftReverseInterop` + the iOS device
lanes) in all seven supported configurations — desktop JIT/interpreter/
R2R/NativeAOT and physical-device (arm64e) R2R/interpreter/NativeAOT —
with the free-exactly-once invariant asserted. No runtime change was
required; the reverse-call ABI already carried it.

### Open-class subclassing and associated-type proxies

Two further projections reuse the same box/lifetime machinery and are
validated in all seven configurations:

- **Managed subclass of an `open` Swift class**: a generated final Swift
  subclass overrides each `open` method to trampoline to a managed
  callback; a `public final` base method that calls the overrides proves
  Swift's own **vtable** dispatch lands in managed code (`report()` =
  `area()*100 + perimeter()` returns 912 for a managed 3×3 square). A
  managed object *is* a Swift subclass instance.
- **Associated-type proxy**: a generated type conforms to a protocol with
  an `associatedtype`, and a Swift **generic** function resolves
  `P.Output` and calls the requirement, landing in managed code
  (`produce(20)` = 41). This exercises associated-type resolution through
  the witness table, not just method forwarding. Note the device fixture
  stores the *concrete* proxy type rather than a parameterized existential
  (`any Producer<Int64>`), because parameterized-protocol existentials
  need an iOS 16 runtime; the generic function still resolves the
  associated type, so the mechanism is covered without the runtime gate.

### Collectible-context (unloading) policy

Because a Swift-held projection roots the managed object through a strong
`GCHandle` (proven by `SwiftHeldProjectionRootsTheManagedObjectUntilReleased`),
the unloading policy follows directly: a strong handle to an object whose
type lives in a **collectible `AssemblyLoadContext`** keeps that ALC from
unloading — an ALC cannot unload while any of its objects is strongly
reachable — and freeing the handle (the box's `deinit`) lets it unload.
`SwiftStyleHandleKeepsACollectibleContextLoadedUntilFreed` asserts both
directions on CoreCLR. Under **NativeAOT** there are no unloadable
contexts, so the test is a no-op (`RuntimeFeature.IsDynamicCodeSupported`
is false) and the policy is simply that reverse projections are fully
static — there is nothing to unload. The practical rule for callers: a
projection handed to Swift pins its context for exactly as long as Swift
holds it, so unloading a plugin ALC must be preceded by releasing every
projection Swift still owns.

### Objective-C identity and the static registrar (design)

Everything validated above is **pure Swift**: the closure box, protocol
proxy, open-class subclass, and associated-type proxy dispatch through
Swift witness tables and Swift vtables with zero Objective-C involvement.
That is the common case and it needs no registrar.

Objective-C identity enters only when the *target* Swift type carries it —
an `@objc` protocol, an `@objcMembers` type, or a class deriving from an
Objective-C/`NSObject` base. Then the projected managed object needs a
stable Objective-C `Class`/`isa` and selector dispatch in addition to the
Swift witness/vtable entries, because Swift reaches such a type through its
Objective-C-interop metadata. The .NET Apple SDK already solves exactly
this for managed `NSObject` subclasses via its **static registrar**
(`[Register]`-attributed types mapped to Objective-C classes with a
managed↔native identity table).

Design seam: when the generator detects Objective-C identity on the target,
the emitted reverse proxy must be an `NSObject`-derived managed type routed
through the existing static registrar, so the object has **one** identity
honored by both Objective-C (selectors) and Swift (the Objective-C class
seen through Swift's ObjC-interop metadata). The box/`GCHandle` lifetime
established above composes with the registrar's identity mapping — the
registrar owns identity, the box owns the GC↔ARC lease. The design must
**not** introduce a parallel identity map. This integration is **gated on
the Apple workload**, which ships the static registrar (it does not live in
`dotnet/runtime`); it is specified here and wired in Phase 12 alongside the
"workload-owned Objective-C/Swift native handle identity and registrar/
trimmer root contract" item. The pure-Swift path — the bulk of reverse
interop, fully validated — depends on none of it.

> **Two refinements from workload-side measurement** (dotnet/macios, Xcode 27
> beta 2 / Swift 6.4, `arm64-apple-macos15.0`; harnesses in
> `swift-abi-probe/sources/objc-identity` and `.../swift-proxy-subclass`).
> Both narrow the seam rather than replace it; the "one identity, no parallel
> map" contract is unaffected.
>
> **1. "Route it through the registrar" is sufficient for `@objc` protocol
> conformance, but not for subclassing.** Objective-C cannot override a
> Swift-declared method at all: `@objc` alone still dispatches through the
> Swift vtable, and only `@objc dynamic` reaches `objc_msgSend`. All three
> Objective-C subclassing routes fail — the swiftc-generated header refuses
> (`objc_subclassing_restricted` on every exported class),
> `objc_allocateClassPair` silently loses non-`dynamic` overrides, and a
> hand-declared `@interface` bound by `objc_runtime_name` (**the shape a
> static registrar emits**) compiles, links, is accepted by Swift as the base
> type, and then **crashes on the first Swift vtable call**. So an
> `NSObject`-derived *open class* target still needs a generated **Swift**
> proxy subclass; registration adds identity, it does not add the ability to
> override. Measured confirmation that this works: a Swift subclass populates
> both the Swift vtable and the Objective-C method table, so its overrides are
> reached by *both* callers, including for `@objc` non-`dynamic` members.
> The seam is therefore "registrar **plus** proxy", decided per requirement,
> rather than a type-level switch between them.
>
> **2. For an ObjC-identity exposure the lease and the box do not
> co-exist.** "The box owns the GC↔ARC lease" holds on the pure-Swift path;
> where the object has Objective-C identity, the ObjC retain count *is* the
> lease and no `GCHandle` box is created — the existing reference-tracking
> machinery already keeps the managed peer alive for exactly as long as a
> native reference exists, so a box would be a second lifetime authority over
> one object. Reading "composes with" as "both mechanisms are live" is the
> dual-lifetime hazard this section exists to avoid; the workload-side
> contract states them as mutually exclusive per exposure
> (dotnet/macios `docs/swift/memory-and-lifetime.md`).

### Static Swift metadata / VWT / conformance emission (investigation)

The proxy approach leans on the **Swift compiler** to emit the ABI records
for the proxy type: the nominal type descriptor and type metadata, the
value-witness table, the protocol conformance descriptor, and the protocol
witness table. The alternative — investigated here now that proxy export is
stable — is emitting those records from the generator/managed side so a
managed type is a first-class Swift type with no Swift-compiled proxy.

What static emission would have to produce, correctly, per conformance:

- nominal type descriptor + type metadata carrying the Swift mangled name;
- a value-witness table (size/stride/alignment/flags plus
  initializeWithCopy/assignWithTake/destroy) for value types;
- a protocol conformance descriptor — **relative** pointers to the
  protocol descriptor, the conforming type, and the witness table;
- a protocol witness table with one entry per requirement, plus
  associated-type and associated-conformance entries;
- on arm64e, every function pointer in the witness table / vtable / metadata
  **signed** with the correct ptrauth key and discriminator.

Fragility (consistent with the `Xamarin.SwiftUI` prior art and our probes):
relative-pointer encoding, mangled-name resolution across the dyld shared
cache, ptrauth signing of every witness entry, keeping the GC from moving
or collecting metadata the Swift runtime caches by raw pointer, and version
skew as the Swift ABI records evolve. A single wrong relative offset or
unsigned pointer is an immediate crash on device — precisely the failure
class the proxy approach avoids by delegating to the compiler.

When it could be justified: only where a proxy genuinely cannot express the
requirement — e.g. satisfying a Swift generic `where`-clause that needs a
concrete managed **value** type's metadata pointer, or a hot value-type
conformance where a per-call proxy-class allocation is too costly. For the
reference-type, protocol, subclass, and associated-type cases (the bulk,
validated across all seven configs) proxies are strictly safer.

Conclusion: **keep generated proxies as the default.** Defer static metadata
emission until a concrete case demands it; if it is ever built, restrict it
to a closed, generator-emitted, ptrauth-correct set gated by device tests —
never runtime synthesis. This mirrors the import-direction rule elsewhere in
this document ("never synthesize metadata/VWTs/witness tables in the initial
design"): the export direction inherits the same discipline.
