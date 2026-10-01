# Experimental watchOS runtime bring-up

The watchOS port currently targets LP64 ARM64 with a watchOS 26.0 minimum deployment version. Device and simulator targets are distinct:

| Target OS | RID | Xcode SDK |
| --- | --- | --- |
| `watchos` | `watchos-arm64` | `watchos` |
| `watchossimulator` | `watchossimulator-arm64` | `watchsimulator` |

This is runtime bring-up, not a released watchOS workload. Signed macios applications have executed on an ARM64 watch running watchOS 27.0.1 with CoreCLR/interpreter, composite ReadyToRun, and NativeAOT. This does not establish App Store eligibility, support for `arm64_32`, support for every watch running watchOS 26, or full runtime acceptance.

## Build

Use a Mac with Xcode's watchOS SDKs and the normal [macOS prerequisites](../../requirements/macos-requirements.md).

From the runtime repository root:

```sh
./build.sh clr.runtime+clr.corelib+clr.nativeaotruntime+clr.nativeaotlibs \
    -os watchossimulator -arch arm64 -cross -c Release

./build.sh clr.tools+clr.crossarchtools -os watchossimulator -arch arm64 -cross -c Release
./build.sh clr.nativecorelib -os watchossimulator -arch arm64 -cross -c Release
./build.sh libs -os watchossimulator -arch arm64 -cross -c Release
```

For device artifacts, use `-os watchos`. Build the host cross-components before crossgenning CoreLib, or include both subsets:

```sh
./build.sh clr.crossarchtools+clr.nativecorelib -os watchos -arch arm64 -cross -c Release
```

`-c Checked` enables runtime assertions, including checks against executable anonymous allocations/protection changes. Device and simulator runtime/CoreLib native builds support this configuration.

Host-running crossgen2 and ILC are under the target's `arm64/` artifact directory. They are macOS tools, not binaries to deploy to a watch. Do not copy their compiler JIT libraries into application runtime assets.

When updating CoreLib, include `clr.nativecorelib` and then `libs` before building
`packs`. The `clr.corelib` subset alone updates `IL/System.Private.CoreLib.dll`,
not all package staging copies. Rebuild `clr.tools` when changing compiler code
or JIT binaries so published ILC contains the matching frontend and backend.
Verify the candidate package payload, not only the build exit code. Installed
fixed-version development packs require explicit repair/reinstallation, followed
by an application clean rebuild.

## Execution modes

CoreCLR is built with `FeatureDynamicCodeCompiled=false` and `FeatureInterpreter=true`. These settings apply to the simulator too. Direct MSBuild and compiler invocations reject unsupported watch architectures and JIT-dependent code-generation configurations.

CoreCLR's native code must come from signed Mach-O images. PE files provide IL and metadata without executable mappings. IL-only images retain writable RVA-data semantics; AOT compilation can move RVA data to read-only native sections. Portable PE composites are rejected before native mapping. The interpreter executes retained IL that has no applicable precompiled implementation.

Use composite Mach-O ReadyToRun for precompiled code. The host must register/load the owner composite with the matching R2R header, image base and size through `host_runtime_contract`. Do not assume an emitted object file alone is runnable, or that a successful application launch proves R2R activation.

When `System.Private.CoreLib.dll` is not beside the CoreCLR binary, expose `SYSTEM_CORELIB_DIRECTORY` through `host_runtime_contract.get_runtime_property`. Passing it only as an ordinary `coreclr_initialize` application property does not implement that host contract.

NativeAOT has a separate native runtime and compiler path. Match managed feature switches and runtime knobs to native libraries: for example, linking `libeventpipe-disabled.a` requires the corresponding EventSource-disabled configuration. Prefer the standard NativeAOT MSBuild integration over hand-built compiler/linker commands.

**There is no new NativeAOT IL interpreter in this port.** `FeatureInterpreter` configures CoreCLR, not NativeAOT.

## Exceptions and GC suspension

Physical watchOS prohibits fatal-signal handlers. Activation signals also arrive
without a usable `ucontext`; Mach thread-state and exception-port APIs are not a
supported alternative. The device and simulator use the same signal-free policy.

Crossgen2 and ILC require software null checks for watch targets, including
compiler-synthesized delegate loads and virtual-function lookup helpers. The
interpreter checks its null guard range explicitly. Native memory violations
and stack overflow remain fatal.

Managed execution cooperates with GC through normal transition-frame-based
polls at method entry, exception entry, and every CFG cycle. Reverse-P/Invoke
attachment and capture of a handler's incoming exception precede polling.
CoreCLR's poll helper and its native-transition stub must not recursively poll.
Asynchronous activation is disabled while frame/layout support remains intact.
Every precompiled watch image must be rebuilt with this compiler policy; mixing
old CoreLib or compiler packages with the new runtime is unsupported.

## Library boundaries

The watch-specific managed variants use Apple crypto, Apple-mobile console/storage/process behavior, and a native HTTP-handler integration point. They must not fall back to desktop Unix/OpenSSL implementations.

BSD socket networking is unavailable to ordinary watch applications. `SocketsHttpHandler` reports unsupported and rejects construction; selecting the managed socket fallback in `HttpClientHandler` also fails explicitly. `System.Net.Sockets` and `System.Net.Ping` currently provide platform-not-supported implementations. `System.Net.NetworkInformation` and `System.Net.Security` remain platform-not-supported until their watch contracts are implemented.

The native HTTP path expects the macios `Microsoft.watchOS` assembly and its
native handler/runtime-options types. The sibling macios integration now supplies
that assembly, an installable development workload, and `dotnet new watch`.
Real macios simulator and signed physical-watch applications have exercised
URLSession HTTPS under CoreCLR and NativeAOT. Phone-independent networking and
distribution acceptance remain separate gates; this runtime repository does
not itself package the macios workload.

The default diagnostics listener is disabled. Simulator networking behavior is not proof that a diagnostic or test-result transport works on a watch.

Objective-C interop's runtime/reference annotations include watchOS. The broader
public watch platform-availability sweep and repository-wide analyzer activation
remain deferred until the full API/caller matrix is reviewed. The runtime
unsupported-operation checks above do not imply complete compile-time platform
diagnostics.

## Validation

The compiler type-system tests cover ARM64 Apple classification/layout. The existing `AppleMobileStripILBodiesUsesFixedInstructionSet` R2R test also targets watch device/simulator configurations. `Loader/classloader/Statics/Misc/WritableRva.ilproj` exercises a PE32+ IL-only assembly containing writable RVA data; `WritableRvaSharedPage.ilproj` covers a PE32 layout whose writable and read-only sections share a native page. These IL-data tests do not run after AOT compilation changes their section ownership.

`GC/Features/CooperativeGC` adds exact-IL self-branch, backward-switch, and
backward-leave collection regressions. These run in isolated processes because
the literal self-loop has no exit. The sibling macios integration app additionally
checks software exception types/order, generic virtual and delegate paths,
concurrent compacting collection with interior roots, native ownership and
finalization, blocks, main-thread callbacks, and HTTPS. Its final interpreter,
R2R, and NativeAOT lanes have emitted `WATCH_INTEGRATION_PASS` on simulator and
physical watchOS 27.0.1.

Keep distinct evidence for compilation, simulator execution, and signed physical-device execution. A release gate must additionally cover native/R2R/interpreter transitions, retained versus stripped IL, native callbacks, GC/EH, complete runtime-pack contents, privacy declarations, and Apple's current uncompressed application-size limit.

`ILCompiler.ReadyToRun.Tests` also includes the compile-only
`WatchOSCodegenTests` suite. It cross-compiles scalar/array, dispatch/delegate,
atomic/vector, struct-copy, loop, and exception-entry fixtures for both watch
targets on a non-watch host. Disassembly assertions require software null-throw
helpers and entry/backedge/handler GC polls, reject hardware null-probe loads,
and use iOS as a control for watch-only policy selection. Building this test
project with `CoreCLRConfiguration=Checked` additionally exercises backend
assertions. Select it with `--filter FullyQualifiedName~WatchOSCodegenTests`;
it does not require Xcode or execute a watch image, and does not replace
NativeAOT-specific or signed-device runtime validation.
