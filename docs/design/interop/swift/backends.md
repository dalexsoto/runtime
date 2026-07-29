# ARM64 execution-mode plan

## Objective

Generated bindings must have one semantic model across the supported CoreCLR
execution modes:

- CoreCLR JIT on macOS ARM64;
- CoreCLR ReadyToRun;
- NativeAOT;
- CoreCLR interpreter.

The implementation scope is managed ARM64 targets plus ARM64e-capable
device/system-ABI validation. Mono, AMD64/x64/x86_64, 32-bit architectures,
`arm64_32`, and `armv7k` are explicit non-goals.

## Cross-mode contract

All supported modes consume:

- the same managed raw signatures;
- the same `CallConvSwift` marker;
- the same special marker types;
- the same generated metadata/witness argument order;
- the same managed support library;
- the same generated Swift/native thunks;
- the same manifest capability classification.

Mode-specific fallback is allowed only when selected at build time and reported
in the coverage manifest.

## First-release platform matrix

The matrix is a product contract. The detailed required RID-by-mode cells live
in [validation.md](validation.md); this summary must remain synchronized with
that canonical table.

| Platform | CoreCLR execution | NativeAOT |
|---|---|---|
| macOS ARM64 | JIT, R2R, and interpreter as supported | Required |
| iOS ARM64 device | R2R plus interpreter fallback; no JIT fallback | Candidate until signed fixture passes |
| iOS ARM64 simulator | R2R plus interpreter fallback; no JIT fallback | Candidate until signed fixture passes |
| Mac Catalyst ARM64 | R2R plus interpreter fallback; no JIT fallback | Candidate until signed fixture passes |
| tvOS ARM64 device/simulator | Candidate until signed CoreCLR fixture passes; no JIT fallback | Candidate until signed fixture passes |

> **An interpreter cell is only meaningful against a runtime that has an
> interpreter, and Release does not by default.**
> `src/coreclr/clrfeatures.cmake` sets
> `FEATURE_INTERPRETER $<IF:$<CONFIG:Debug,Checked>,1,0>`, so a **Release**
> build carries no interpreter unless it is explicitly enabled — while
> `DOTNET_Interpreter` is accepted silently regardless, producing no error,
> no warning, and no behavioural change. An interpreter leg run against such
> a build is a second JIT run that passes.
>
> Measured over this tree's own artifacts (`nm`/`strings` over each
> `libcoreclr.dylib`): `osx.arm64.Release` has **no** interpreter (0 symbols,
> no config strings) while `osx.arm64.Checked` and `.Debug` do (8 and 25);
> the `ios`, `iossimulator`, `maccatalyst` and `tvossimulator` **Release**
> builds do carry it, as those targets require.
>
> Consequently the osx-arm64 "interpreter" cells asserted here and in
> [validation.md](validation.md), [storekit2.md](storekit2.md) and
> [roadmap.md](roadmap.md) should **name the build configuration they were
> run against**. If they were Checked/Debug runs the claim stands as written;
> if they were Release runs the cell did not exercise the interpreter.
>
> The check is one line and exact, because the config entries are
> `#ifdef FEATURE_INTERPRETER`-guarded in `src/coreclr/inc/clrconfigvalues.h`:
>
> ```sh
> strings "$libcoreclr" | grep -qx Interpreter
> ```
>
> Recommending it as a precondition in any lane that sets
> `DOTNET_Interpreter`, so the silent-no-op case fails loudly instead.

ARM64e matters even when managed application code is emitted as ARM64 because
Apple system frameworks and dyld-cache slices may expose authenticated indirect
pointers. Real ARM64e-capable device validation remains mandatory.
ARM64e is an ABI/device profile here, not a new managed
`TargetArchitecture`.

Unsupported combinations fail deterministically; they do not silently
disappear from coverage.

The iOS, Mac Catalyst, and tvOS rows depend on the CoreCLR-on-Apple-mobile
workstream and its workload integration. They are not a claim that every
current .NET Apple application configuration already uses CoreCLR.

Before a mobile RID becomes required, prove:

- CoreCLR pack selection with Mono runtime disabled;
- publish and launch of a signed ARM64 fixture;
- R2R and interpreter-mode selection;
- signing/provisioning and Helix/CI execution;
- an equivalent signed NativeAOT fixture before that row is shipping-ready.

Shared Apple workload infrastructure may live under `src/mono` paths. Editing
that shared infrastructure for CoreCLR or NativeAOT does not add Mono runtime
scope.

## Target triples and deployment targets

Every generated manifest, native asset, and managed binding is produced for
exactly one Swift target triple. The triple and deployment target are part of
the binding identity and the capability handshake; a mismatch fails before
native invocation.

### In-scope triples

| RID | Swift target triple | Environment |
|---|---|---|
| `osx-arm64` | `arm64-apple-macosx12.0` | macOS |
| `ios-arm64` | `arm64-apple-ios15.0` | iOS device |
| `iossimulator-arm64` | `arm64-apple-ios15.0-simulator` | iOS simulator on Apple Silicon |
| `maccatalyst-arm64` | `arm64-apple-ios15.0-macabi` | Mac Catalyst |
| `tvos-arm64` | `arm64-apple-tvos15.0` | tvOS device |
| `tvossimulator-arm64` | `arm64-apple-tvos15.0-simulator` | tvOS simulator on Apple Silicon |

Notes:

- Swift triples spell macOS as `macosx`.
- Mac Catalyst triples carry the iOS version number and the `macabi`
  environment; Mac Catalyst 15.0 corresponds to macOS 12.
- Simulator triples use the `-simulator` environment suffix and share the
  device deployment target.
- The version embedded in the triple is the Swift-interop minimum deployment
  target defined below. Raising it is a binding-identity change.

### ARM64e system-ABI validation triples

ARM64e is a validation profile, not a managed `TargetArchitecture`
(see README.md "Initial scope"). Validation inputs and native probe builds use:

| Validation profile | Swift target triple |
|---|---|
| iOS device system ABI | `arm64e-apple-ios15.0` |
| tvOS device system ABI | `arm64e-apple-tvos15.0` |
| macOS dyld-cache/system ABI | `arm64e-apple-macosx12.0` |
| Mac Catalyst system ABI | `arm64e-apple-ios15.0-macabi` |

These triples exist to compile probes and inspect Apple system-framework
slices (`.tbd` `arm64e` entries, dyld-cache pointer authentication). Managed
application code remains `arm64`.

### Minimum deployment targets

Proposed Swift-interop minimums:

| Platform | Swift-interop minimum |
|---|---|
| macOS | 12.0 |
| iOS | 15.0 |
| tvOS | 15.0 |
| Mac Catalyst | 15.0 |

Rationale:

- **StoreKit 2 availability.** The first product target requires iOS 15.0,
  macOS 12.0, tvOS 15.0, and Mac Catalyst 15.0. A lower interop minimum buys
  nothing for P0 and forces availability guards on every StoreKit member.
- **Swift ABI stability.** Stable Swift ABI begins at iOS 12.2 / tvOS 12.2 /
  macOS 10.14.4; all proposed minimums satisfy it with margin.
- **Native Swift concurrency runtime.** Swift concurrency ships in the OS
  starting at iOS 15.0 / macOS 12.0 / tvOS 15.0. Below that (back to
  iOS 13.0), apps need the `libswift_Concurrency` back-deployment library.
  The proposed minimums remove that dependency from every generated async
  thunk and from validation scope. Swift async requires at least iOS 13, so
  no minimum below 13.0 is possible for the async profile regardless.

The effective minimum for an application is the maximum of:

1. the runtime-pack native minimum (this repository currently builds with
   iOS 13.0, tvOS 13.0, macOS 14.0, and Mac Catalyst 17.0 — see
   `SetOSTargetMinVersions` in `Directory.Build.props`);
2. the .NET Apple workload minimum for the target framework;
3. the Swift-interop minimum above.

In the current snapshot the runtime pack already dominates on macOS (14.0)
and Mac Catalyst (17.0). The Swift-interop minimums are still stated
independently so the interop contract does not silently move if the runtime
or workload floors change.

Rules:

- The generator records triple and deployment target in every manifest
  (see architecture.md "Compiler-produced binding manifest") and refuses to
  merge models generated for different triples.
- Direct symbol imports are permitted only when the minimum deployment target
  guarantees the symbol; newer APIs carry availability annotations and either
  guard, use a compiler availability thunk, or are omitted with a diagnostic
  (see architecture.md "Compatibility policy" and validation.md
  "Availability and back deployment").
- Unsupported triples fail generation deterministically; they never fall back
  to a nearby triple.

## CoreCLR JIT

### Preserve and harden

- Keep `CallConvSwift` as the synchronous ABI convention.
- Keep `SwiftSelf`, `SwiftError`, and `SwiftIndirectResult` as explicit marker
  types.
- Preserve current frozen-value lowering and reverse-call reassembly.
- Keep generic metadata and witnesses as ordinary generated arguments.

### Required work

- Complete reverse `SwiftSelf<T>`, including indirect/by-reference self.
- Add direct unmanaged Swift function-pointer invocation tests.
- Implement supported Swift SIMD/HFA/HVA shapes for ARM64.
- Add deterministic diagnostics for unsupported target/shape.
- Add JIT dumps that show:
  - formal Swift argument;
  - physical pieces;
  - fixed special register;
  - ownership-neutral temporary storage;
  - result reconstruction.
- Route authenticated indirect code pointers through the shared native helper
  or generated per-signature thunks.
- Reserve X22 only as part of an approved direct Swift async design.

### Avoid

- JIT knowledge of protocol names, metadata accessors, type databases, or
  high-level Swift ownership policy.
- JIT-generated VWT or witness calls when ordinary managed wrapper code can
  express them.
- Architecture-general abstractions that add unsupported non-ARM64 targets to
  the product contract.

## ReadyToRun

### Existing model

R2R already compiles Swift calls through RyuJIT and resolves the target with
ordinary P/Invoke fixups. No format change is required for the initial
synchronous direct profile.

### Required tests

- non-composite R2R direct Swift P/Invoke;
- composite R2R;
- lazy and eager P/Invoke target cells;
- first-call prestub paths on ARM64;
- X8 `SwiftIndirectResult`;
- X20 self and X21 error across precompiled call sites;
- reverse `UnmanagedCallersOnly` entrypoints;
- generic metadata/witness arguments;
- generated native thunk imports;
- R2R with IL retained for Apple-mobile interpreter fallback;
- fully precompiled/IL-stripped configurations only where no interpreter
  fallback is required;
- JIT fallback and tiered recompilation equivalence on macOS only.

### Format changes

Do not add a Swift-specific R2R fixup for:

- ordinary direct symbols;
- metadata accessors exposed as functions;
- witness pointers passed by generated code;
- generated native thunk functions.

Consider new versioned R2R data only if direct Swift async or
pointer-authenticated indirect calls require a runtime-consumed call plan.
Any new record must:

- be versioned independently from the Swift SDK;
- fail closed;
- permit build-time selection of a generated thunk instead;
- avoid embedding resilient Swift layouts.

### Mach-O R2R envelope

Current main contains Mach-O composite R2R object emission and SDK linking
support. Swift work must validate native-asset integration, force loading,
dead stripping, and runtime discovery for the container format selected by
each ARM64 RID. It does not require both PE and Mach-O validation for every
target.

## NativeAOT

NativeAOT is a first-class target.

### Required properties

- no runtime code generation;
- no reflection-based generic helper discovery;
- no dynamic construction of callback stubs;
- all raw signatures statically visible;
- all `UnmanagedCallersOnly` callbacks rooted;
- generated native thunks compiled for every supported ARM64 slice;
- metadata accessors, conformance symbols, and native helper functions rooted;
- availability and fallback selected at build time;
- deterministic failure for a missing manifest capability.

### Required work

- Add dedicated NativeAOT Swift tests rather than relying only on common runtime
  tests.
- Validate lazy and direct P/Invoke paths.
- Add generated accessor and native data-symbol rooting for generated bindings.
- Integrate generated Swift static or mergeable libraries.
- Verify dead stripping removes unused thunks but retains referenced metadata
  and conformances.
- Validate native-helper VWT/witness invocation on real ARM64e-capable devices.
- Ensure SafeHandle release paths are callable during shutdown.
- Test callback lifetime and exception containment under aggressive trimming.

### Linking model

System framework imports can remain direct. Generated thunks and support code
should be packaged as:

- static or mergeable libraries for Apple-mobile NativeAOT;
- dynamic or static libraries for macOS as appropriate;
- XCFrameworks containing the required ARM64 device/simulator slices.

NativeAOT must not depend on `NativeLibrary.GetExport` for correctness when a
symbol can be emitted as a static import or generated accessor.

The first implementation bans raw Swift data-symbol imports in generated
managed code. Metadata, conformance, and witness data are obtained through
rooted generated/native accessor functions. Add a general data-import facility
only if an accessor cannot represent a required scenario.

The generated asset contract must cover:

- `DirectPInvoke`/direct-import inputs;
- `NativeReference` and `__Internal` selection;
- force-load and dead-strip behavior;
- exported callback/accessor symbols;
- UCO roots;
- asset and manifest hashes;
- a module-lifetime lease held by every metadata, witness, VWT, closure, or
  function-pointer wrapper.

## CoreCLR interpreter

The CoreCLR interpreter is especially important on Apple mobile because it can
execute IL that is not present in R2R images.

### Required work

- Implement reverse indirect/by-reference `SwiftSelf<T>` on ARM64.
- Add direct function-pointer `calli` execution coverage.
- Add SIMD when the JIT ARM64 profile supports it.
- Support generated metadata/witness arguments as ordinary parameters.
- Validate generated native thunk callbacks.
- Add pointer-authenticated indirect call support or route through native
  helper thunks.
- Keep transient-IL and marshalled P/Invoke detection aligned with the JIT.

### Swift async

The initial generated async-thunk model needs only ordinary C callbacks and
synchronous `CallConvSwift` entry support. Direct Swift async should not be
implemented independently in the interpreter before its call-plan contract is
defined for the JIT and NativeAOT.

## Runtime async relationship

.NET runtime async and Swift async are separate ABIs.

| Concern | .NET runtime async | Swift async |
|---|---|---|
| Continuation | Managed `Continuation` object | Swift async context |
| Special register | .NET-specific continuation argument/result | X22 async context |
| Resumption | .NET resumption stubs | Swift compiler continuation functions |
| Task runtime | .NET Task/ValueTask | Swift task/executor runtime |
| R2R fixups | .NET continuation layout/resumption | None currently |

Reusable infrastructure:

- managed Task/ValueTask completion;
- diagnostics and async stack correlation;
- scheduling and `ExecutionContext` integration;
- allocation reduction after the physical bridge completes.

Not reusable as-is:

- continuation object layout;
- special registers;
- tail-call rules;
- task/executor internals;
- exception representation.

Runtime async is currently disabled for Apple-mobile tests. The generated Swift
async-thunk path must work without it. Runtime-async enablement is an external,
non-gating optimization and does not change correctness or direct/thunk
selection.

A method must never combine the managed runtime-async ABI with the unmanaged
Swift async ABI. Their continuation returns also conflict with X2, which Swift
may use for ordinary multi-register results. Any future direct Swift async
convention requires separate metadata and R2R/NativeAOT encoding.

## Apple workload integration

Runtime support alone cannot produce a deployable binding.

Required workload work:

- invoke the selected Xcode toolchain;
- generate per-target ABI manifests;
- compile generated Swift/native thunk modules;
- build ARM64 XCFramework/static/mergeable assets;
- pass framework and Swift runtime link arguments;
- preserve native callback and metadata symbols;
- integrate with managed static registrar output;
- propagate availability annotations;
- package runtime-specific native assets;
- validate device/simulator and App Store bundles.

The Swift binder should share the Apple workload's canonical type and
availability model with Objective-C projections.

## Probable runtime code touchpoints

| Workstream | Primary areas |
|---|---|
| Public marker/capability APIs | `src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices`, `src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/Swift`, `src/libraries/System.Runtime/ref` |
| Extended Swift layout | `src/coreclr/vm/classlayoutinfo.cpp`, `src/coreclr/vm/methodtablebuilder.cpp`, `src/coreclr/tools/Common/TypeSystem`, `src/coreclr/tools/aot`, Reflection.Emit, ILAsm/ILDasm |
| RyuJIT ABI | `src/coreclr/jit/importercalls.cpp`, `abi.cpp`, `lclvars.cpp`, `targetarm64.h`, ARM64 lowering/codegen/LSRA files |
| CoreCLR VM and interpreter | `src/coreclr/vm/callconvbuilder.cpp`, `jitinterface.cpp`, `stubgen.cpp`, `callstubgenerator.cpp`, ARM64 assembly helpers |
| R2R | `src/coreclr/tools/aot/ILCompiler.ReadyToRun`, `src/coreclr/inc/readytorun.h`, VM fixup resolution |
| NativeAOT | `src/coreclr/tools/aot/ILCompiler.Compiler`, `ILCompiler.RyuJit`, NativeAOT build integration and Apple link targets |
| Managed support library | new library under `src/libraries`, plus API ref and unit tests |
| Native Swift ABI helper | new Apple native library under `src/native/libs`, with static and shared build integration |
| Runtime tests | `src/tests/Interop/Swift`, R2R test configuration, NativeAOT runtime tests, Apple extra-platform pipelines |

## Execution-mode gates

| Capability | JIT | R2R | NativeAOT | CoreCLR interpreter |
|---|---|---|---|---|
| Existing scalar/frozen ABI | Required | Required | Required | Required |
| Direct function pointer | Required | Required | Required | Required |
| Dynamic value support library | Required | Required | Required | Required |
| Generic metadata/witness arguments | Required | Required | Required | Required |
| Generated synchronous thunk | Required | Required | Required | Required |
| Generated async thunk | Required | Required | Required | Required |
| Swift layout kinds | P1, not StoreKit P0 | P1 | P1 | P1 |
| Direct Swift async | Research | Research | Research | Deferred |

No capability is product-complete until the same generated API tests pass in
every applicable mode for the supported ARM64 target matrix and required
ARM64e-capable device ABI validation lanes.

## Simulator and Catalyst lanes (2026-07-13)

Swift interop executes on the iOS simulator:
`src/tests/FunctionalTests/iOS/Simulator/Swift` compiles the fixture for
`arm64-apple-ios15.0-simulator`, publishes it into the app bundle, and
runs `CallConvSwift` P/Invokes (frozen-struct lowering, self register)
on a booted simulator via `simctl` — no signing involved.

Three toolchain blockers were found and fixed to get there; all three
are Xcode-26/27-era regressions that affect ANY iOS build of this repo,
not just Swift interop:

1. **Deployment-target floor.** `eng/native/build-commons.sh` pinned the
   iOS/iOS-simulator CMake deployment target to 13.0; Xcode 27's libc++
   refuses it ("The selected platform is no longer supported by
   libc++") and `-Werror` makes it fatal. Bumped to 15.0.
2. **Mono-flavor default.** `build.sh libs -os iossimulator` defaults to
   the Mono runtime flavor, which .NET 11 no longer supports for mobile
   ("Use the CoreCLR runtime or target .NET 10.0"). The lane must pass
   `-rf CoreCLR`.
3. **App-builder pin.** `src/tasks/AppleAppBuilder/Xcode.cs` hard-coded
   `CMAKE_OSX_DEPLOYMENT_TARGET=13.0` for the generated Xcode project;
   xcodebuild rejects it outright ("supported deployment target
   versions is 15.0 to 27.0.x"). Bumped to 15.0.

Build recipe: `./build.sh clr+clr.aot -os iossimulator -arch arm64 -c
Release` then `./build.sh libs -os iossimulator -arch arm64 -c Release
-rf CoreCLR`, then build the functional-test project with
`/p:TargetOS=iossimulator /p:TargetArchitecture=arm64`.

Execution modes on the simulator (all three run the Swift payload
green):

| mode | how | evidence |
|---|---|---|
| JIT | default launch | SIMLANE PASSED |
| CoreCLR interpreter | `SIMCTL_CHILD_DOTNET_Interpreter='*' xcrun simctl launch ...` (simctl forwards `SIMCTL_CHILD_*` into the app) | SIMLANE PASSED |
| R2R | `iOS.Simulator.Swift.R2R.Test` (`PublishReadyToRun=true`; needs the iossimulator optimization-data package, produced by a full `clr` build for that target) | SIMLANE PASSED; the published image carries the `RTR` signature the IL-only image lacks, so it is genuinely precompiled |

The lane's payload also covers **extended layout**: it references the
same `ExtendedLayoutKind.SwiftStruct` IL mirrors the desktop suite uses
(the kind is not in the public enum, so the types are authored in IL)
and asserts the managed layout against Swift's — size 16 with the
tail-packed field at offset 9 — plus a by-value round trip through a
Swift function. Green in all three simulator modes.

### tvOS simulator

The same recipe runs on the tvOS simulator
(`src/tests/FunctionalTests/tvOS/Simulator/Swift`,
`arm64-apple-tvos15.0-simulator`): Swift interop and extended layout
pass on a booted Apple TV simulator under JIT and the interpreter. The
tvOS build needed the same deployment-target fix as iOS — three MORE
13.0 pins in `eng/native/build-commons.sh` (iOS device, tvOS simulator,
tvOS device) were bumped to 15.0, so all four Apple mobile targets now
build under Xcode 27.

### Mac Catalyst

`src/tests/FunctionalTests/MacCatalyst/Swift` builds the fixture for
`arm64-apple-ios15.0-macabi` (with the SDK's `System/iOSSupport`
framework path) and runs the app **natively on macOS** — ad-hoc signed
by the app builder, no provisioning profile, no device. Swift interop
and extended layout pass under JIT and the interpreter. The Catalyst
runtime needed no deployment-target fix (its floor is already 17.0).

### Mobile-RID contract status

Three of the five mobile RIDs are proven end to end — CoreCLR pack
selection with Mono excluded (.NET 11 rejects the Mono flavor for these
targets outright), publish, launch, and mode selection:

| RID | launch | modes proven |
|---|---|---|
| `iossimulator-arm64` | simctl | JIT, interpreter, R2R |
| `tvossimulator-arm64` | simctl | JIT, interpreter |
| `maccatalyst-arm64` | native macOS, ad-hoc signed | JIT, interpreter |
| `ios-arm64` | **physical device (devicectl)** | **R2R, interpreter, NativeAOT** (device lanes below) |
| `tvos-arm64` | — | **Gate:** device + provisioning |

Four of five RIDs now run Swift interop; only `tvos-arm64` remains
gated on a physical tvOS device. The `ios-arm64` device proof is in the
next section.

**NativeAOT on mobile RIDs is SDK-gated, not device-gated.**
`PublishAot=true` is refused by the SDK for `iossimulator-arm64` and
`maccatalyst-arm64` alike (NETSDK1203, "Ahead-of-time compilation is
not supported for the target runtime identifier"), even with a locally
built ILC and AOT SDK for those targets in the tree; the repo's own
mobile AOT functional tests use Mono AOT rather than ILC. The mobile
NativeAOT rows therefore wait on .NET SDK/workload support, which no
amount of device access changes. The Swift ABI under NativeAOT is
already validated on osx-arm64 across every suite.

The lanes also prove the **@MainActor host requirement** end to end: they
call a `@MainActor async` Swift function from managed code and it
completes, because an app host services the main dispatch queue. The same
call HANGS FOREVER in a console-style host (abi-model.md) — so these lanes
are the evidence that the "app hosts are fine" half of that requirement is
real, on both iOS and Mac Catalyst.

## Physical iOS device lane — `ios-arm64`, arm64e (2026-07-16)

Swift interop runs on a **physical iPad mini (A17 Pro, iPad16,1, iOS
26.5.2)** — an **arm64e** device — under all three CoreCLR-on-Apple
execution modes. Signed with an Apple Development identity and a wildcard
team provisioning profile (team `23BQZX22Y5`, `get-task-allow`), deployed
and launched with `devicectl`. The managed app is emitted as `arm64`; the
Swift and Foundation slices it calls are `arm64e` in the device's dyld
shared cache.

**No existing Swift interop source needed changing to run on device** —
the frozen-struct lowering, self register, extended layout, `@MainActor`
handling, and the interpreter Swift stub all worked as-is on real
hardware. What follows is what had to be learned about *building* for the
device, not about the ABI.

### The two device runtime shapes, and how to select them

`eng/testing/tests.mobile.targets` defaults a **CoreCLR-flavor mobile app
to NativeAOT** (`UseNativeAOTRuntime=true` when `RuntimeFlavor==CoreCLR`).
The two device shapes are therefore:

| shape | how to select | AOT compiler | app assembly |
|---|---|---|---|
| CoreCLR runtime pack (R2R + interpreter fallback) | `UseNativeAOTRuntime=false` (forces `PublishReadyToRun=true`) | crossgen2 | R2R `.r2r.dylib` + IL, interpreter fallback |
| NativeAOT | the mobile default | ILC | fully compiled into a native dylib |

There is **no JIT on device** — matching the platform-matrix row
"R2R plus interpreter fallback; no JIT fallback". "Default launch" here is
R2R-with-interpreter-fallback, not JIT.

### Modes proven (payload `DEVLANE`/`AOTDEV`: addPair, selfSum, extended layout, @MainActor, Foundation UUID)

| mode | launch | result |
|---|---|---|
| CoreCLR R2R + interpreter fallback | `devicectl process launch` | `DEVLANE PASSED` |
| CoreCLR forced interpreter | `DEVICECTL_CHILD_DOTNET_Interpreter='*' DEVICECTL_CHILD_DOTNET_ReadyToRun='0'` | `DEVLANE PASSED` |
| NativeAOT (ILC) | `devicectl process launch` | `AOTDEV PASSED` |

The forced-interpreter run is the strongest interpreter evidence: with
R2R disabled and **no JIT available on device**, the CoreCLR interpreter
necessarily carried the entire Swift-interop path — including the
interpreter Swift stub (the `CallJittedMethodRet*` x20–x22 preservation
and `Load_/Store_` routines) — on real arm64e hardware.

### The arm64e / ptrauth boundary, validated

The arm64 app calls Swift functions that use **Foundation** (a
`UUID()`, whose 16 bytes come back with the RFC 4122 v4 version nibble)
and **Swift concurrency** (`@MainActor async`). Both are `arm64e` slices
in the shared cache, carrying authenticated pointers. All three execution
modes cross that boundary and return correct results — a wrong handling of
authenticated pointers at the interop boundary would fault rather than
return. This is the mandatory real-device arm64e validation, exercised in
R2R, interpreter, and NativeAOT.

`@MainActor` additionally proves the app-host contract on real hardware:
the device app runs a main run loop, so the `@MainActor` call completes
(it would hang forever on a console host).

### NativeAOT device build: a pre-existing, non-Swift local-build friction

Standard `PublishAot=true` is refused by the SDK (`NETSDK1203`) for
**every** iOS RID, device included — so the repo's NativeAOT-iOS path is
the `UseNativeAOTRuntime` library-mode flow, whose local single-shot build
hits a `Publish`↔`PublishTestAsSelfContained` **circular dependency**. The
**stock `iOS.Device.LibraryMode` test reproduces the identical cycle**, so
this is general NativeAOT-iOS-device local-build friction, not Swift.

The supported build is the Helix two-phase flow: a staging `build`
(`BuildTestsOnHelix=true`) that emits `ProxyProjectForAOTOnHelix.props`,
then `ProxyProjectForAOTOnHelix.proj` which runs ILC + bundles. Driven
locally, this reaches ILC success — **ILC compiles the CallConvSwift +
Foundation Swift-interop assembly for `ios-arm64`** (the `.o` exports the
managed entry with the interop compiled in) — and the app builds, signs,
installs, launches, and runs `AOTDEV PASSED` on device. One artifact of
driving the proxy by hand: the extended-layout IL assembly was not
auto-added to `ilc.rsp` (a `ComputeIlcCompileInputs` gap in the manual
flow, not a Swift or ILC defect), so the NativeAOT lane covers core
CallConvSwift + the arm64e boundary while **extended layout is covered on
device by the CoreCLR lane**.

### Reproducibility

- CoreCLR lane (`src/tests/FunctionalTests/iOS/Device/Swift`): a single
  `dotnet build /p:TargetOS=ios /p:TargetArchitecture=arm64` produces the
  signed `.app`; `devicectl install`/`launch` runs it. Fully reproducible.
- NativeAOT lane (`src/tests/FunctionalTests/iOS/Device/SwiftNativeAOT`):
  the lane files mirror the stock `LibraryMode` test and run in CI via the
  proxy; a local device build needs the two-phase proxy invocation plus
  dylib-embedding, as above.

Per-method execution provenance (the desktop SwiftR2RProvenance
mechanism) is NOT asserted on the simulator — the modes are selected by
build/launch configuration and verified by image inspection rather than
by runtime events. Remaining for the lane: CI wiring (**Gate:** AzDO).
