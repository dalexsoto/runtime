# Research references

> [!NOTE]
> Historical sources may discuss excluded runtimes and architectures. When
> cited, they are evidence only and create no implementation, compatibility, or
> validation obligation outside the ARM64 in-scope profiles.

## Snapshot

| Source | Revision or environment |
|---|---|
| `dotnet/runtime` | `6c5849144dc8a23aa0760080d6f7784fed60da37` |
| `dotnet/runtimelab` `feature/swift-bindings` | `4c3431344dafa611a9a2ba82a603a0250fb80046` |
| `chkn/Xamarin.SwiftUI` | `7e9de93abe1ec19405f03b927282d052d0eeb752` |
| `swiftlang/swift` source audit | `swift-6.3.3-RELEASE`, commit `064859e41d68596f486c5d724401cb370f260409` |
| Local Apple toolchain | Xcode 27 beta 2, Swift 6.4, SDK 27.0 |

The public Swift source snapshot audited here predates the exact Apple Swift
6.4 toolchain build. Before implementation is approved, repeat the source audit
against the matching Swift 6.4 source revision when available and record any
ABI/runtime differences.

## .NET design and tracking

- [Swift interop proposal](https://github.com/dotnet/designs/blob/1b507bda1cff8f86cfe44af3842a54911205ac3d/proposed/swift-interop.md)
- [dotnet/runtime#95638 - Implement .NET Swift interop support targeting Apple platforms](https://github.com/dotnet/runtime/issues/95638)
- [dotnet/runtime#93631 - Runtime support for Swift Interop in .NET 9](https://github.com/dotnet/runtime/issues/93631)
- [dotnet/runtime#108662 - Runtime support for Swift Interop in .NET 10](https://github.com/dotnet/runtime/issues/108662)
- [dotnet/runtime#100896 - Extended layout API proposal](https://github.com/dotnet/runtime/issues/100896)
- [dotnet/runtimelab#3070 - Future use cases for Swift interop](https://github.com/dotnet/runtimelab/issues/3070)
- [dotnet/runtimelab#3071 - Projection tooling features](https://github.com/dotnet/runtimelab/issues/3071)

## Current runtime implementation

- [CallConvSwift](https://github.com/dotnet/runtime/blob/6c5849144dc8a23aa0760080d6f7784fed60da37/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/CallingConventions.cs)
- [Swift marker types](https://github.com/dotnet/runtime/blob/6c5849144dc8a23aa0760080d6f7784fed60da37/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/Swift/SwiftTypes.cs)
- [Managed Swift physical lowering](https://github.com/dotnet/runtime/blob/6c5849144dc8a23aa0760080d6f7784fed60da37/src/coreclr/tools/Common/JitInterface/SwiftPhysicalLowering.cs)
- [RyuJIT Swift call import](https://github.com/dotnet/runtime/blob/6c5849144dc8a23aa0760080d6f7784fed60da37/src/coreclr/jit/importercalls.cpp)
- [RyuJIT Swift ABI classifier](https://github.com/dotnet/runtime/blob/6c5849144dc8a23aa0760080d6f7784fed60da37/src/coreclr/jit/abi.cpp)
- [CoreCLR interpreter call-stub generator](https://github.com/dotnet/runtime/blob/6c5849144dc8a23aa0760080d6f7784fed60da37/src/coreclr/vm/callstubgenerator.cpp)
- [Swift runtime tests](https://github.com/dotnet/runtime/tree/6c5849144dc8a23aa0760080d6f7784fed60da37/src/tests/Interop/Swift)
- [Apple cryptography Swift shim](https://github.com/dotnet/runtime/blob/6c5849144dc8a23aa0760080d6f7784fed60da37/src/native/libs/System.Security.Cryptography.Native.Apple/pal_swiftbindings.swift)
- [ExtendedLayout implementation](https://github.com/dotnet/runtime/tree/6c5849144dc8a23aa0760080d6f7784fed60da37/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices)
- [Runtime async specification](../../specs/runtime-async.md)
- [Runtime async code-generation responsibilities](../../coreclr/botr/runtime-async-codegen.md)
- [CLR ABI](../../coreclr/botr/clr-abi.md)
- [ReadyToRun format](../../coreclr/botr/readytorun-format.md)

## Key runtime pull requests

- [#95065 - Introduce CallConvSwift and special register types](https://github.com/dotnet/runtime/pull/95065)
- [#96707 - Parse CallConvSwift in CoreCLR/NativeAOT](https://github.com/dotnet/runtime/pull/96707)
- [#98831 - Implement Swift physical lowering in the managed type system](https://github.com/dotnet/runtime/pull/98831)
- [#99294 - Frozen struct arguments](https://github.com/dotnet/runtime/pull/99294)
- [#99704 - Frozen struct returns](https://github.com/dotnet/runtime/pull/99704)
- [#100018 - Basic reverse P/Invoke](https://github.com/dotnet/runtime/pull/100018)
- [#100344 - Frozen structs in reverse P/Invoke](https://github.com/dotnet/runtime/pull/100344)
- [#102717 - SwiftSelf<T> and SwiftIndirectResult](https://github.com/dotnet/runtime/pull/102717)
- [#103570 - SwiftIndirectResult in RyuJIT](https://github.com/dotnet/runtime/pull/103570)
- [#103576 - SwiftSelf<T> in RyuJIT](https://github.com/dotnet/runtime/pull/103576)
- [#123142 - CoreCLR interpreter forward Swift support](https://github.com/dotnet/runtime/pull/123142)
- [#124927 - CoreCLR interpreter reverse Swift support](https://github.com/dotnet/runtime/pull/124927)
- [#125282 - Runtime-async interpreter DiagnosticIP fix](https://github.com/dotnet/runtime/pull/125282)
- [#116082 - Extended layout runtime support](https://github.com/dotnet/runtime/pull/116082)
- [dotnet/roslyn#78741 - ExtendedLayoutAttribute compiler support](https://github.com/dotnet/roslyn/pull/78741)

## `dotnet/runtimelab` prior art

- [Branch root](https://github.com/dotnet/runtimelab/tree/4c3431344dafa611a9a2ba82a603a0250fb80046)
- [Binding overview](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/binding-overview.md)
- [Runtime metadata](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/runtime-metadata.md)
- [Value witness tables](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/binding-value-witness-table.md)
- [Memory management](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/memory-management.md)
- [Generics](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/binding-generics.md)
- [Protocols](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/binding-protocols.md)
- [Existential containers](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/runtime-existential-containers.md)
- [Swift code generation](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/swift-code-generation.md)
- [Symbol retrieval outside ABI JSON](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/docs/retrieving-symbols-outside-abi-json.md)
- [Emitter redesign](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/src/docs/emitter-redesign-proposal.md)
- [Generated CryptoKit projection](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/src/Swift.Bindings/tests/IntegrationTests/FunctionalTests/CryptoKit/CryptoKit.cs)
- [StoreKit framework tests](https://github.com/dotnet/runtimelab/blob/4c3431344dafa611a9a2ba82a603a0250fb80046/src/Swift.Bindings/tests/FrameworkTests/StoreKit/StoreKitTests.cs)

## Xamarin and .NET Apple prior art

- [Xamarin.SwiftUI repository](https://github.com/chkn/Xamarin.SwiftUI/tree/7e9de93abe1ec19405f03b927282d052d0eeb752)
- [Xamarin.SwiftUI Hacking guide](https://github.com/chkn/Xamarin.SwiftUI/blob/7e9de93abe1ec19405f03b927282d052d0eeb752/Hacking.md)
- [Xamarin.SwiftUI glue](https://github.com/chkn/Xamarin.SwiftUI/blob/7e9de93abe1ec19405f03b927282d052d0eeb752/src/SwiftUIGlue/Glue.swift)
- [Xamarin.SwiftUI managed Swift metadata](https://github.com/chkn/Xamarin.SwiftUI/tree/7e9de93abe1ec19405f03b927282d052d0eeb752/src/SwiftUI/Swift/Interop)
- [dotnet/macios Swift prototype discussion](https://github.com/dotnet/macios/issues/15315)
- [dotnet/macios managed static registrar](https://github.com/dotnet/macios/blob/8111cb6c59d7c832543bd094f317247422ae8afc/docs/managed-static-registrar.md)
- [dotnet/macios native library interop](https://github.com/dotnet/macios/blob/8111cb6c59d7c832543bd094f317247422ae8afc/docs/native-library-interop.md)

## Swift ABI primary sources

The following source references are implementation ABI documents, not a
general-purpose supported C API:

- [Calling convention](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/docs/ABI/CallingConvention.rst)
- [Calling-convention summary](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/docs/ABI/CallingConventionSummary.rst)
- [Type layout](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/docs/ABI/TypeLayout.rst)
- [Type metadata](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/docs/ABI/TypeMetadata.rst)
- [Mangling](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/docs/ABI/Mangling.rst)
- [Library evolution](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/docs/LibraryEvolution.rst)
- [ValueWitnessTable.h](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/include/swift/ABI/ValueWitnessTable.h)
- [GenericContext.h](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/include/swift/ABI/GenericContext.h)
- [Metadata.h](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/include/swift/ABI/Metadata.h)
- [MetadataValues.h](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/include/swift/ABI/MetadataValues.h)
- [HeapObject.h](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/include/swift/Runtime/HeapObject.h)
- [Error.h](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/include/swift/Runtime/Error.h)
- [Task.h](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/include/swift/ABI/Task.h)
- [Executor.h](https://github.com/swiftlang/swift/blob/064859e41d68596f486c5d724401cb370f260409/include/swift/ABI/Executor.h)

## StoreKit 2 evidence

The Xcode 27 beta 2 macOS StoreKit interface used during the investigation was:

```text
/Applications/Xcode_27.0.0-beta2.app/Contents/Developer/
  Platforms/MacOSX.platform/Developer/SDKs/MacOSX27.0.sdk/
  System/Library/Frameworks/StoreKit.framework/Versions/A/
  Modules/StoreKit.swiftmodule/arm64e-apple-macos.swiftinterface
```

The corresponding iOS device text-based stub used to establish the ARM64e
system-framework profile was:

```text
/Applications/Xcode_27.0.0-beta2.app/Contents/Developer/
  Platforms/iPhoneOS.platform/Developer/SDKs/iPhoneOS27.0.sdk/
  System/Library/Frameworks/StoreKit.framework/StoreKit.tbd

targets: [ arm64e-ios ]
swift-abi-version: 7
```

Tools used:

```bash
xcrun swift-symbolgraph-extract
xcrun swift-api-digester -dump-sdk -abi
xcrun swift-frontend -emit-abi-descriptor-path
xcrun swiftc -emit-silgen
xcrun swiftc -emit-ir
xcrun swiftc -emit-assembly
nm
swift-demangle
hopper
```

Relevant Apple documentation:

- [Product](https://developer.apple.com/documentation/storekit/product)
- [Transaction](https://developer.apple.com/documentation/storekit/transaction)
- [Transaction updates](https://developer.apple.com/documentation/storekit/transaction/updates)
- [Choosing a StoreKit API](https://developer.apple.com/documentation/storekit/choosing-a-storekit-api-for-in-app-purchases)

## Manifest extraction prototype (2026-07-12)

A working Phase 3 extraction prototype lives outside this repository at
`swiftruntime-ant/manifest-prototype` (extraction scripts, merge tool,
generated StoreKit macOS/iOS and CryptoKit macOS manifests, coverage
reports, and FINDINGS.md). Key verified facts:

- Manifests are reproducible byte for byte from compiler outputs alone
  (symbol graphs + `swift-api-digester` ABI/API descriptors +
  `llvm-readtapi` JSON `.tbd`); no `.swiftinterface` parsing.
- The ABI descriptor provides mangled names, per-accessor symbols, explicit
  ownership, frozen field order, and generic signatures, but NOT async
  flags, typed-throws error types, or actor isolation — those must come
  from symbol-graph fragments.
- StoreKit macOS member classification: 8.1% direct, 52.7% direct with
  managed marshalling, 14.7% generated thunk, 19.4% Objective-C, 5.1%
  unsupported; matches the expectations in [storekit2.md](storekit2.md).
- Timing: ~42 s cold (module cache build), ~0.2 s warm per module.
- macOS SDK `.tbd` files carry only `arm64e`/`x86_64` slices (no plain
  `arm64`), reinforcing the ARM64e system-ABI validation profile.
