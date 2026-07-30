# Validation and compatibility plan

## Principles

Validation must prove the ABI, not merely that generated code compiles.

1. Use the Swift compiler as the primary oracle.
2. Compare every applicable in-scope execution mode against the same generated
   Swift fixtures.
3. Validate ownership and failure paths as aggressively as success paths.
4. Run real-device arm64e tests for indirect code pointers.
5. Treat every Xcode beta as a compatibility event.
6. Never accept "zero tests discovered" or a skipped backend as success.

## Deterministic ABI probe corpus

Create a checked-in generator that emits matched Swift and C# sources from a
single model.

Probe categories:

- scalars and pointers;
- frozen structs;
- nested structs;
- explicit and extended layouts;
- inline arrays;
- unaligned fields;
- direct and indirect results;
- self/context;
- errors;
- callbacks;
- generic metadata and witnesses;
- optionals and enums;
- resilient values;
- existentials;
- closures;
- SIMD;
- async thunk signatures.

For every probe, emit:

- Swift source;
- C# source;
- expected values;
- ABI JSON/API descriptor;
- SILGen;
- canonical SIL;
- LLVM IR;
- assembly;
- symbol list;
- generated manifest;
- expected support classification.

For resilient-value probes, compile otherwise-identical declarations with and
without library evolution. Assert that the generator rejects a managed
signature built for the wrong mode even when the mangled symbol is identical.

The test generator must be deterministic by seed and checked into the runtime
test infrastructure.

## Differential lowering tests

The CoreCLR VM and managed AOT lowering implementations must agree with each
other and with Swift.

For each generated frozen value:

1. Ask the managed type system for `CORINFO_SWIFT_LOWERING`.
2. Ask the CoreCLR VM implementation.
3. Compile the Swift equivalent and inspect SIL/IR/assembly.
4. Execute round-trip argument, return, and callback tests.

Compare:

- direct versus indirect classification;
- primitive piece type;
- piece offset and size;
- register class;
- stack offset and packing;
- return register order;
- size, stride, and alignment.

CI should retain a minimized reproducer for any mismatch.

Extended-layout validation additionally covers:

- direct `System.ValueType` inheritance and enum rejection parity;
- generic instantiations;
- `Marshal.SizeOf`, `StructureToPtr`, and marshal descriptors;
- argument and return classification;
- ARM64 device/simulator, R2R, NativeAOT, and CoreCLR interpreter modes;
- metadata packing fields being ignored where the extended kind requires it.

## Hopper and disassembly

Hopper is a human investigation tool, not a required CI dependency. Use it to
confirm compiler output when:

- pseudo-code hides hidden parameters;
- metadata or witness access is unclear;
- pointer authentication changes code shape;
- a direct call differs from SIL/IR expectations;
- Apple framework behavior is only observable in a produced binary.

Automated CI uses `llvm-objdump`, `otool`, `nm`, `swift-demangle`, and FileCheck
or equivalent textual checks.

The `Xamarin.SwiftUI` reproduction in [prior-art.md](prior-art.md) is the
baseline example.

## Execution-mode matrix

| Target | Required execution modes |
|---|---|
| `osx-arm64` | JIT, R2R with JIT fallback/tiering, NativeAOT, and supported interpreter test configurations |
| `ios-arm64` | R2R with CoreCLR interpreter fallback; NativeAOT after signed fixture qualification |
| `iossimulator-arm64` | R2R with CoreCLR interpreter fallback; NativeAOT after signed fixture qualification |
| `maccatalyst-arm64` | R2R with CoreCLR interpreter fallback; NativeAOT after signed fixture qualification |
| `tvos-arm64` | Candidate: R2R/interpreter and NativeAOT after signed physical-device fixture qualification |
| `tvossimulator-arm64` | Candidate: R2R/interpreter and NativeAOT after signed simulator fixture qualification |

Unsupported target/mode cells produce deterministic diagnostics and do not
count toward completion.

### Historical coverage policy

Tests attributed to excluded runtimes or architectures (the Mono
`ActiveIssue` annotations, the disabled `coreclr`+`x64` combination in
`src/tests/Interop/Swift/Directory.Build.props`, and the historical AMD64
JIT path) are preserved as-is for historical coverage. They are neither
expanded nor counted toward any gate in this plan; only ARM64
in-scope-profile lanes satisfy execution-mode gates.

### Local validation evidence (osx-arm64, 2026-07-11)

All eight existing `src/tests/Interop/Swift` suites (including the new
reverse `SwiftSelf<T>`, direct `calli`, and reverse negative coverage) pass
locally on a Checked build in every locally testable `osx-arm64` mode:

- CoreCLR JIT;
- CoreCLR interpreter (`DOTNET_Interpreter=*`);
- R2R (crossgen2-compiled with R2RDump validation);
- composite R2R (`--composite`);
- R2R image with the interpreter enabled (mixed-mode fallback);
- NativeAOT (ILC-compiled native binaries; `SwiftInvalidCallConv` excluded
  because ILC fails closed at build time for invalid Swift signatures).

This is evidence, not a gate: the dedicated CI lanes with per-method
execution provenance required by this plan still need to be established.

#### Per-method execution provenance (2026-07-12)

`src/tests/Interop/Swift/SwiftR2RProvenance` implements the per-method
provenance requirement in-process: every run attributes each Swift-calling
wrapper to its actual code source through runtime events, so a run in one
mode cannot satisfy another mode's gate. Probe-verified signals:

- `R2RGetEntryPoint` (CompilationDiagnosticKeyword `0x2000000000`) fires
  when precompiled R2R code is bound to a method;
- `MethodLoadVerbose` with the Jitted flag (`0x8`) fires when the JIT
  compiles a method; `MethodFlags` bits 7-9 carry the optimization tier, so
  tier-up recompilations are individually visible;
- the CoreCLR interpreter prepares methods through the same path and
  reports a method-load event (tier 2, never promoted) with no R2R binding;
- `DOTNET_ReadyToRun=0` on an R2R image jits the retained IL (the
  Apple-mobile fallback shape); an R2R image under `DOTNET_Interpreter=*`
  still binds and runs its precompiled code.

Validated legs, all green: JIT, interpreter, R2R, R2R with
`DOTNET_ReadyToRun=0` (fallback integration), R2R with the interpreter,
composite R2R, NativeAOT. R2RDump shows the image carries both P/Invoke
cell kinds against the same Swift entry point: `PINVOKE_TARGET` (direct,
`SuppressGCTransition`) and `INDIRECT_PINVOKE_TARGET` (all ordinary
imports, including the `SwiftError*`-carrying one).

#### Compiler differential evidence (2026-07-12, extended 2026-07-13)

StressGenerator's `--differential` mode compiles each generated fixture
with `swiftc -emit-ir` and compares the swiftcc definitions' parameter
type lists against the normative lowering simulation (lowering.md). IR
parameters map 1:1 to lowered elements — typed intervals, opaque chunk
sizes (i64/i32/i16/i8), and the by-reference `ptr` case are all visible —
so this checks the documented algorithm directly against the compiler,
independent of runtime behavior. The mode covers argument lowerings,
return lowerings (direct scalar/literal-struct IR return types and the
over-cap sret form), and REVERSE-direction lowerings: for callback
fixtures the closure invocation's IR call site (arguments plus swiftself
context) must match the lowering a managed UnmanagedCallersOnly
implementation uses. 820 random functions across thirteen seeds
(including library-evolution batches of all three kinds) match exactly;
a full JIT and interpreter regression sweep across all thirty suites
also passed on 2026-07-13.

#### Manifest emission evidence (2026-07-12)

BindingGenerator emits manifest.md-conforming manifests for real
frameworks: CryptoKit with full sources (symbol graph + ABI descriptor +
tbd) yields 727 USR-keyed declarations, 648 with descriptor symbols and
tbd export checks, explicit-vs-default ownership provenance on every
parameter, and 196 generic contexts recording metadata/witness argument
order. Output is byte-identical across runs (verified with cmp), so
generation caches by the recorded per-source sha256 hashes. Timing on
Apple Silicon: symbol-graph extraction seconds-scale, ABI descriptor
~21s (CryptoKit), emission ~2s.

#### Native-asset pipeline evidence (2026-07-12)

The emit-thunk-suite mode closes the pipeline end to end: from the
AssetFixture manifest it generates the Swift thunk (frozen-signature
wrappers, rooted metadata accessor, embedded 128-bit asset identity),
the C# bindings (probe-compiled manglings, identity validated before any
other native call), the build files with DirectPInvoke inputs, the
exported-symbol list, and the workload metadata props. The checked-in
generated suite (SwiftNativeAssetPipeline) passes JIT, interpreter, R2R,
composite R2R, and NativeAOT (eager link visible as an @rpath load
command; UCO callback and accessor survive full trimming), and the same
generated thunk compiles for iOS device and simulator triples. The
emitter (v8) additionally emits PROPERTY ACCESSORS (derived from the
manifest's property-descriptor symbols by the `vp`→`vg` rule; accessors
need no thunk because they are ordinary exported functions taking self in
the self register, and stored and computed properties bind identically),
a derived Apple privacy manifest, pack-erasure thunks discovered from the
manifest's genericSig for every arity in its recorded policy set
(currently 0-5; SWIFTGEN007 keeps out-of-set arities explicitly
unsupported rather than silently missing, and the generated tests prove
every arity emitted) and per-signature ptrauth thunks
(GeneratedPtrauthThunks.c: slot-address entry points for
context-register-binding witness and closure shapes, fail-closed on
arm64e); the regenerated suite proves the generated witness thunk
agrees exactly with the direct generic call in all four modes.

#### IL-stripped R2R finding (2026-07-12)

`crossgen2 --strip-il-bodies` (composite-only) was exercised on the
provenance suite. The stripped composite fails closed on osx-arm64 today:
under default tiering, tier-up reaches a stripped body and throws
`InvalidProgramException`; with tiering disabled (and with
`--inputbubble`), the image fails at load with `BadImageFormatException`.
No configuration silently fell back. IL-stripped R2R is therefore treated
as unsupported for the Swift profile until the toolchain supports it
end-to-end; if that changes, it remains restricted to configurations with
no interpreter or JIT fallback and tiering disabled.

Each test logs:

- runtime and mode;
- target triple;
- architecture;
- OS version;
- Xcode/Swift version used to generate native artifacts;
- manifest version and capability profile;
- whether a direct symbol, generated thunk, Objective-C path, or facade ran.

## Platform matrix

Minimum matrix:

- macOS ARM64;
- iOS ARM64 device;
- iOS ARM64 simulator;
- Mac Catalyst ARM64;
- tvOS ARM64 device and simulator after the candidate rows qualify.

### ARM64 simulator and ordinary ARM64 coverage

Run on macOS ARM64 and Apple-Silicon simulators:

- scalar and frozen-struct forward/reverse stress;
- SIMD/HFA/HVA lowering;
- X20/X21/X8 and X0-X3/V0-V3 register behavior;
- X22 async-context compiler probes;
- generic metadata and witness argument passing;
- ExtendedLayout size/stride/alignment differential tests;
- generated async-thunk lifecycle and ordinary C callbacks.

### ARM64e-capable real-device coverage

Run on real macOS Apple Silicon hardware and physical iOS and tvOS devices
before claiming those device platforms:

- VWT indirect calls;
- witness-method pointers;
- closure entrypoints;
- metadata accessors returning authenticated pointers;
- generated async continuations.

This remains required when the managed application is built as `arm64`.
Apple system framework `.tbd` files can advertise `arm64e` slices, and the
managed process may consume authenticated indirect pointers from those
frameworks on arm64e hardware. Unknown schemas must fail closed.

Add a negative pointer-authentication test that supplies an unknown or
incorrect discriminator/schema and proves deterministic rejection instead of
stripping, re-signing, or crashing.

## R2R tests

Add focused tests that prove:

- `CallConvSwift` survives Crossgen2 signature encoding;
- target-cell fixups resolve the correct symbol;
- X8 indirect results survive first-call thunks;
- error and self registers survive prestubs;
- generated native thunk imports work;
- composite images behave identically;
- macOS tiered recompilation does not change results;
- Apple-mobile R2R retains the IL required by interpreter fallback;
- IL-stripped configurations retain required native metadata and callbacks and
  are not used where interpreter fallback is required.

Run forced-R2R and forced-interpreter tests separately and record per-method
execution provenance. Count R2R-to-interpreter fallback integration as a third
test; it cannot satisfy either forced-mode gate by itself.

Use `r2rdump` assertions where possible to inspect method signatures and
imports.

## NativeAOT tests

Prove:

- no dynamic code;
- no reflection-based generic discovery;
- no missing UCO callback after trimming;
- static/mergeable Swift libraries link;
- data and conformance symbols are preserved;
- unused thunks are dead-stripped;
- SafeHandle finalization calls native cleanup;
- app startup and shutdown are clean;
- generated bindings work in a signed device app.

Run trim/AOT analyzers on the support library and generated code.

## Ownership tests

### Value witnesses

Instrument Swift fixture types with counters for:

- init;
- copy;
- take/move;
- assign copy;
- assign take;
- destroy.

Test:

- normal return;
- throwing constructor;
- managed exception before call;
- Swift error after partial initialization;
- callback exception;
- cancellation;
- duplicate dispose;
- finalizer-only cleanup;
- move followed by source access;
- nested nontrivial values;
- generic values;
- enum payloads.

Every test asserts the final counter balance.

### ARC

Instrument Swift classes and closure boxes with `deinit` counters. Test:

- object adoption;
- retain/release;
- managed wrapper cloning;
- Objective-C bridge object;
- callback capture;
- concurrent dispose;
- wrapper identity cache;
- app shutdown.

### GC

Run:

- forced compacting collections;
- GC stress where available;
- weak-reference tests;
- callback during collection;
- callback after managed cancellation;
- collectible assembly/load-context policy tests if supported.

Raw Swift pointers must never refer into movable managed storage after an
interop call returns.

## Error tests

Forward:

- no error;
- known framework error;
- unknown Swift error;
- NSError-bridgeable error;
- typed error through normalization thunk;
- error during result initialization.

Reverse:

- managed success;
- managed exception;
- cancellation;
- nested callback exception;
- exception while translating another exception.

No unmanaged boundary may observe a managed exception unwind.

## Async tests

Test state transitions:

- synchronous completion before wrapper returns;
- asynchronous completion;
- error;
- cancellation before start;
- cancellation during suspension;
- cancellation racing completion;
- purchase completes in Swift after the managed waiter is cancelled;
- dispose racing completion;
- callback invoked twice by a faulty fixture;
- callback never invoked;
- completion occurs before `start` returns;
- repeated idempotent cancel/release;
- managed cancellation is distinguished from
  `PurchaseResult.userCancelled`;
- actor reentrancy;
- MainActor call from main and background threads;
- app background/foreground;
- process shutdown;
- MainActor completion while the platform main run loop is pumped;
- MainActor invocation from a headless/non-UI host.

The bridge must:

- complete exactly once;
- release callback context exactly once;
- never block a Swift executor;
- use asynchronous managed continuations;
- release native task and result state;
- route any post-cancellation purchase transaction through the durable
  transaction updates/entitlements pipeline.

## Async sequence tests

Test:

- zero items;
- one item;
- many items;
- producer faster than consumer;
- consumer cancellation;
- producer error;
- normal completion;
- app shutdown;
- restart after cancellation;
- eager transaction listener starts before the first managed consumer;
- cold-start unfinished transaction is delivered exactly once;
- startup buffering/processing never drops a transaction.

Measure maximum queued native and managed memory to verify bounded backpressure.
Assert that each managed `MoveNextAsync` produces at most one native `next()`
and that no second operation is in flight concurrently.

Producer-error cases validate the generic sequence bridge. StoreKit transaction
sequences have `Failure == Never` and are not expected to throw from `next()`.

## StoreKit tests

Use StoreKit configuration/test sessions to cover:

- invalid product identifiers;
- valid products;
- consumable, non-consumable, non-renewing subscription, and auto-renewable
  subscription products;
- verified and unverified transactions;
- successful purchase;
- pending purchase;
- user cancellation;
- purchase failure;
- current entitlements;
- transaction updates;
- transaction update produced before a managed consumer attaches;
- transaction finishing;
- verify -> deduplicate -> durable entitlement grant -> finish ordering;
- duplicate transaction delivery;
- renewal correlation by original transaction identifier;
- subscription grace period and billing retry;
- expiration, revocation, and Family Sharing status sets;
- JWS/signed data preservation;
- availability on minimum OS versions.

`SKTestSession` should use the existing Objective-C binding stack. The Helix
test app packages the `.storekit` file as a bundle resource and initializes the
session in-process at startup.

Live App Store tests are a separate controlled lane and must not replace
deterministic StoreKit test-session coverage.

Separate controlled environments cover:

- StoreKitTest/Xcode configuration for deterministic local behavior;
- sandbox for Apple-signed JWS and server interaction;
- TestFlight for production-like receipt, Family Sharing, and subscription
  behavior.

## Compiler and SDK compatibility

At minimum, compare:

- last supported stable Xcode;
- current stable Xcode;
- current Xcode beta.

For each:

- regenerate manifests;
- diff canonical API identities, not raw text;
- classify new/removed/changed declarations;
- validate `.tbd` symbols;
- compile generated C# and Swift assets;
- run core ABI and StoreKit smoke tests.

The importer must tolerate interface-format spelling changes without a
hand-written parser update when the compiler descriptors remain compatible.

## Capability-handshake tests

Reject before native invocation when:

- the implementation profile is absent or older than required;
- only the shared low-level marker APIs are present;
- the manifest schema/profile is unsupported;
- the generated native ABI version differs;
- the native asset hash or identity differs from the manifest;
- the target RID or execution-mode cell is not allowlisted.

## Availability and back deployment

Test:

- declaration absent from older OS;
- weak-linked symbol;
- compiler back-deployed thunk;
- API with different target availability;
- Mac Catalyst versus macOS differences;
- cross-import overlay presence/absence.

Generated managed APIs carry matching platform annotations and either:

- fail before symbol invocation;
- call a compiler-generated availability thunk;
- are omitted with an explicit diagnostic.

NativeAOT tests must also prove that no eagerly linked direct import references
a symbol unavailable at the minimum deployment target.

## Performance

Track:

- direct call latency;
- generated native thunk latency;
- Objective-C call latency;
- C facade latency;
- metadata first-use and cached-use cost;
- VWT copy/destroy cost;
- managed string/collection conversion cost;
- async completion overhead;
- code size and native thunk count;
- NativeAOT app size;
- R2R startup impact.

Benchmarks should include both Swift-native wrapper APIs and convenience
managed conversions so allocation cost remains visible.

## Coverage metrics

For every framework and target, publish:

- total public declarations;
- Objective-C reused;
- direct Swift ABI;
- direct with managed marshalling;
- generated native thunk;
- facade-only;
- unsupported by reason;
- generated managed source size;
- generated native source/binary size;
- test count and pass count;
- backend/platform coverage.

Regressions in supported declaration count or backend coverage fail CI unless
reviewed and baselined.

## Release gates

A release candidate must satisfy:

- zero ABI differential mismatches;
- zero ownership counter imbalance;
- zero sanitizer or leak failures;
- all applicable in-scope execution modes passing;
- all required platform slices built;
- arm64e-capable real-device indirect-call validation passing;
- no trim/AOT warnings in generated code;
- every metadata/witness/function-pointer wrapper holds its module-lifetime
  lease until disposal;
- no silent unsupported declarations;
- all unsupported-profile and manifest/native-asset mismatch cases reject
  before native invocation;
- StoreKit end-to-end scenario passing;
- no missed transaction across cold start, delayed consumer attachment, or
  managed purchase cancellation;
- App Store bundle validation passing;
- compatibility report for every supported Xcode version.

#### Cross-import overlay evidence (2026-07-12)

`_StoreKit_SwiftUI` (the StoreKit/SwiftUI cross-import overlay)
processes through the full pipeline. The overlay consists entirely of
extension symbol graphs (no standalone module graph — the loader now
treats the main graph as optional): 431 public declarations, 12,836
synthesized symbols filtered, 60.6% covered with every exclusion
carrying an explicit reason (165 unavailable on the macOS target, 4
operator functions, 1 parameter pack). StoreKit's view types construct
headless from managed (SwiftOpaqueAndViews).

#### SDK-direct environment-action evidence (2026-07-12)

EnvironmentValues binds directly from the SwiftUICore binary with no
fixture in the path: `$s7SwiftUI17EnvironmentValuesVACycfC` (resilient
init, indirect result), the openURL getter (self register + indirect
resilient result), metadata via the exported Ma accessors, lifecycle
via the VWTs — headless in all four modes. BackgroundAssets classifies
to 158 declarations with all 92 exclusions carrying SWIFTGEN004
(target-unavailable); its manager is ObjC-backed and its async shapes
map to the validated async-thunk patterns.

#### Probe corpus provenance (2026-07-13)

The ABI probe corpus cited throughout these documents
(`/Volumes/xam/projects/swiftruntime-ant/swift-abi-probe`) is now
version-controlled; evidence citations correspond to commit
`b282a7fe1cb58e1916324773fd6e9a24b9418983` (sources, docs, and analysis
directories; build outputs excluded).

#### Sanitizer and fuzz matrix (2026-07-13)

Defined and first-run against the release-gate requirements:

| leg | target | status |
|---|---|---|
| ASan | direct-async C harness (7 legs, real cross-thread task machinery) | clean |
| ASan | opaque-result C harness | clean |
| UBSan (`-fno-sanitize-recover=all`) | direct-async C harness | clean |
| TSan | view-body harness | clean |
| fuzz | every BindingGenerator manifest consumer (`--fuzz-manifest`, deterministic seed) | 20,000 mutations, 11,646 explicit rejections, 0 crashes, 0 NREs |
| fuzz | the symbol-graph reader (`--fuzz-symbol-graph`) — the generator's other untrusted toolchain input | 10,000 mutations, 6,497 explicit rejections, 0 crashes, 0 NREs |

Remaining matrix legs: sanitizer-instrumented RUNTIME builds (managed-
side ASan/TSan need a sanitizer-built CoreCLR — CI-matrix work riding
the lane), TSan over the managed concurrent dispose/callback paths
(SwiftSoak under a TSan runtime), and continuous fuzzing beyond the
deterministic pass. Every generator input reader is HARDENED — the `.tbd` export reader (an
empty, truncated, or wrong-format tbd would otherwise yield an empty
export set and silently mark EVERY symbol as not-exported, corrupting
the sourcing provenance the manifest records; it now fails closed, while
a real tbd still confirms exports — 192 for StoreKit), the symbol-graph reader (a
missing or mistyped field names the field and file rather than throwing
NRE or, worse, silently dropping declarations, which would understate
the unsupported surface the coverage baseline gates on), the thunk-suite
manifest consumers, the ABI-descriptor reader (a truncated/failed
`swift-api-digester` run is now named as such rather than silently
yielding a symbol-less manifest), and the coverage-baseline reader (a
malformed contract file exits 3 with the missing field named, verified
in both directions). Malformed input raises an explicit
`InvalidDataException` naming the defect (missing/mistyped
`declarations`, empty `symbols`, absent `symbol` string), and the
fuzzer deliberately does NOT catch `NullReferenceException` — an NRE
escaping a consumer fails the fuzz run. Latest: 20,000 mutations,
11,646 explicit rejections, zero crashes and zero NREs.

#### Generated-suite drift gate (2026-07-13)

The generated suite carries a "GENERATED — do not edit" banner, but a
banner is not a guarantee: a hand-edit would silently make the
"generated" claim false, and the files would no longer be reproducible
from the manifest. `--verify-generated` regenerates the suite into a
temporary directory and compares it with the checked-in files byte for
byte, exiting 3 and naming the file on any drift. Verified in both
directions (clean tree passes; a one-line hand-edit fails), and wired
into the swift-interop lane's build job alongside the classification
baseline gate.

#### Cross-version compatibility (2026-07-13)

The capability handshake is exercised against the LIVE runtime, not only
injected fixtures. `SwiftBindingHandshake` probes whether the Swift
calling convention actually functions — by making a real `CallConvSwift`
call and catching failure — rather than checking whether the marker types
exist. That distinction is the point: an older .NET can ship the
`SwiftSelf`/`SwiftError` marker types without implementing the
convention, so a type-presence or version check would WRONGLY ACCEPT such
a runtime. The marker-only shape is refused with SWIFT0002, and the test
asserts zero native calls occur on the refusal path — the binding is
rejected before it can touch Swift.

#### Second-framework validation — Swift Charts (2026-07-16)

StoreKit is a mostly-nominal API; to prove the binding pipeline
generalizes, it was run against **Swift Charts**, a framework that is
almost entirely the *hard* surface — `some ChartContent`/`some View`
opaque results, `@ChartContentBuilder`/`@AxisContentBuilder` result
builders, and generic `Mark`s over `Plottable`.

**Classification** (module `Charts`, `arm64-apple-macos14.0`, Xcode 27
beta 2 / swiftlang-6.4.0.23.5):

- **853 public declarations, 87.7% covered** (105 unsupported).
- The unsupported set is exactly what it should be: **97 SWIFTGEN004**
  (target-availability — e.g. `Chart3DPose`), **4 SWIFTGEN002**
  (operators), **4 SWIFTGEN007** (variadic-pack `buildBlock`). The
  classifier correctly separates the pack `buildBlock(repeat each C)`
  (unsupported) from the fixed-arity `buildBlock` overloads (bound via
  thunk) on the *same* result builder — no over- or under-claim.
- **173 modifiers return opaque types** (`some ChartContent`, etc.).
  These are NOT rubber-stamped: each is routed to the opaque-descriptor
  binding path and the manifest records the exact `…QOMQ` opaque type
  descriptor. One such recorded descriptor was checked byte-for-byte
  against the Charts `.tbd` and **matches an exported symbol exactly** —
  the generator emits a real binding target, not a guess.

**Headless execution** (SwiftOpaqueAndViews, all four modes — JIT,
interpreter, R2R, NativeAOT): the Mark family (`BarMark`, `LineMark`,
`PointMark`, `AreaMark`, `RuleMark`) metadata resolves through
CallConvSwift metadata accessors; the `ChartContent` and `Plottable`
protocol descriptors resolve; and a recorded opaque descriptor resolves
as a genuine exported binding target.

**No generator or runtime change was needed** for Charts — it binds
through the same opaque/generic/builder paths built for SwiftUI and
StoreKit. That is the generalization evidence: the machinery is not
StoreKit-shaped.

**Rendered, not just bound (2026-07-17).** Charts is taken all the way to
*output*: managed code supplies the bar data, Swift Charts rasterizes a
real `Chart { BarMark }` via SwiftUI `ImageRenderer` headless (no window),
and managed validates the returned bitmap — its dimensions plus a
non-background pixel count that is deterministic and data-dependent (a
different dataset produces a different image; the same dataset reproduces
byte-for-byte). Because rendering is `@MainActor`, the work runs on the
main queue and the result returns through a callback, with the host
pumping the run loop (`CFRunLoop` on a console host; the app run loop for
free on device). Proven on the **desktop** suite `SwiftChartsRender` in
all four modes (JIT, interpreter, R2R, NativeAOT) and on a **physical
iPad mini (A17 Pro, arm64e)** under R2R and the interpreter
(`DEVLANE chart rendered = 240x160, nonBackground = 9879`). This is the
same shape a real charting app would use: hand SwiftUI/Charts the data
from .NET, get pixels back.

#### Reverse interop — managed objects in Swift (2026-07-17)

Phase 11's foundation is validated by `SwiftReverseInterop` (desktop) and
the iOS device lanes:

- **Closure box**: a managed `Accumulator` is projected as an `@escaping`
  Swift closure; Swift-invoked callbacks accumulate into it (Sum 5→15→30
  over three calls), and releasing the Swift closure frees the `GCHandle`
  exactly once. Multiple independent boxes show no cross-talk and each
  frees once.
- **Protocol proxy**: a managed `Handler` conforms to a Swift `Handler`
  protocol via a generated proxy; Swift's generic + existential dispatch
  calls the managed methods (`runHandler(9) = handle(9)+label() = 23+42 =
  65`), and the proxy's release frees the handle once.
- **Open-class subclass**: a managed object overrides an `open` Swift
  class's methods; Swift's `public final report()` calls the overrides via
  the **vtable** and lands in managed code (`report()` = 912 for a 3×3
  square; `areaOfShape` = 9). Release frees the handle once.
- **Associated-type proxy**: a managed conformer binds a protocol's
  `associatedtype Output = Int64`, and a Swift generic function resolves
  `P.Output` and calls the requirement (`produce(20) = 41`). Release frees
  the handle once.
- **Lifetime rooting**: with the only strong reference dropped, the
  managed object survives GC while Swift holds the projection (weak ref
  stays alive, callback still runs) and is collected once Swift releases
  it — the GC↔ARC bridge, asserted directly.
- **Collectible-context policy**: a strong `GCHandle` (the box's exact
  mechanism) to an object owned by a collectible `AssemblyLoadContext`
  keeps that ALC from unloading, and freeing it lets the ALC unload
  (CoreCLR; no-op under NativeAOT, which has no unloadable contexts).

Matrix — closure box + protocol proxy + subclass + associated type +
lifetime rooting + free-exactly-once, all green (collectible-context test
is CoreCLR-only, no-op under NativeAOT):

| host | JIT | interpreter | R2R | NativeAOT |
|---|---|---|---|---|
| desktop (osx-arm64) | ✅ | ✅ | ✅ | ✅ |
| device (iPad A17 Pro, arm64e) | n/a (no JIT) | ✅ | ✅ | ✅ |

The reverse-call ABI it rides (managed callbacks receiving `SwiftSelf` in
any position, reverse indirect result) was already green in the Phase 7
suites; no runtime change was needed for the closure-box / proxy /
subclass / associated-type / lifetime layer.

#### Generated reverse proxy (2026-07-17)

`BindingGenerator --emit-reverse-proxy` turns a protocol contract into a
working reverse binding: the Swift proxy (`HandlerProxy.g.swift`) and the
managed P/Invoke glue (`HandlerReverse.g.cs`). `SwiftReverseGenerated`
validates the EMITTED code end to end — it adds only a managed
`Calculator` and its callbacks, then drives the generated `Store` /
`InvokeHandle` / `InvokeLabel` / `ReleaseAll` surface. Swift dispatches
its protocol requirements through the witness table into the managed
methods (`InvokeHandle(10) = 10*3+4 = 34`, `InvokeLabel() = 7`), and the
box frees the `GCHandle` exactly once on `ReleaseAll`. Green in all four
desktop modes (JIT, interpreter, R2R, NativeAOT). The emitted Swift also
compiles standalone (0 errors) and exports the expected `@_cdecl`
entrypoints. The prototype restricts signatures to `Int64`; a non-`Int64`
parameter fails generation loudly rather than emitting unsound glue.

#### NativeAOT direct exported entrypoints (2026-07-17)

On NativeAOT the managed side can be *entered* by name rather than through
a runtime function pointer: `[UnmanagedCallersOnly(EntryPoint = "…")]`
makes ILC emit a named external symbol. The device NativeAOT lane proves
this end to end — the ILC-compiled image
(`libiOS.Device.SwiftNativeAOT.Test.dylib`, `ios-arm64`) exports
`_SayHello` as an **external** symbol:

```
$ nm …/AppBundle/publish/libiOS.Device.SwiftNativeAOT.Test.dylib | grep SayHello
00000000000a0e40 S _SayHello                                   # external (exported)
00000000000a0e40 s _iOS_Device_SwiftNativeAOT_Test_ClassLibrary__SayHello
```

The native host resolves and calls `_SayHello` by name, and that
entrypoint runs the full reverse-interop suite (closure box + protocol
proxy + subclass + associated type) on the physical device. This is the
direct-exported-entrypoint path: reverse-interop entrypoints exported the
same way are linked/`dlsym`'d by Swift by name, with no per-call pointer
handoff.
