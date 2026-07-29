# Prior art and empirical findings

## Sources reviewed

The plan incorporates four principal lines of prior art:

1. The existing `dotnet/runtime` Swift ABI implementation.
2. `dotnet/runtimelab` branch `feature/swift-bindings`.
3. `chkn/Xamarin.SwiftUI`, including a fresh Hopper analysis of its generated
   ARM64 glue library.
4. The current `dotnet/macios` Objective-C binding and packaging stack.

## Existing .NET Swift design

The proposed design in
[dotnet/designs](https://github.com/dotnet/designs/blob/1b507bda1cff8f86cfe44af3842a54911205ac3d/proposed/swift-interop.md)
established several decisions that remain sound:

- direct C# to Swift calls are a goal;
- the runtime owns calling-convention and register mechanics;
- high-level projection tooling is separate from the JIT;
- `SwiftSelf`, `SwiftError`, and `SwiftIndirectResult` represent special ABI
  positions;
- frozen values use Swift physical lowering;
- resilient values are handled through opaque storage and metadata;
- error-to-exception policy belongs in generated code;
- trimming and AOT compatibility are design requirements.

The current plan extends that proposal to cover R2R, NativeAOT, the CoreCLR
interpreter, metadata/VWT ownership, generated ABI thunks, StoreKit 2, and
current Swift concurrency.

## `dotnet/runtimelab` `feature/swift-bindings`

Snapshot:

- commit `4c3431344dafa611a9a2ba82a603a0250fb80046`
- branch HEAD dated 2025-04-25

The branch is a standalone binding tool and managed support library. It is not
a runtime fork. Its pipeline is:

```text
.swiftinterface
    |
    v
swift-frontend -emit-abi-descriptor-path
    |
    +--> ABI JSON
    +--> framework dylib / system framework path
    +--> per-platform .tbd exports
             |
             v
      parser + demangler + type database
             |
             v
      marshalling handlers and emitters
             |
             +--> C# bindings
             +--> thin Swift wrappers when required
```

Important reusable work:

- compiler-generated ABI JSON ingestion;
- `.tbd` parsing for dyld shared-cache frameworks;
- a Swift 5 demangler;
- type-database and graph-classification designs;
- direct generic method calls using explicit metadata and witness-table
  arguments;
- metadata, VWT, ARC, and protocol-witness prototypes;
- `SwiftString`, `SwiftArray`, `SwiftSet`, and `SwiftOptional` projections;
- CryptoKit and StoreKit end-to-end tests;
- generated async wrappers;
- large generated struct/call stress suites.

Representative direct generated call:

```csharp
[UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
[DllImport(
    "/System/Library/Frameworks/CryptoKit.framework/CryptoKit",
    EntryPoint = "$s...")]
private static extern void Initialize(
    SwiftIndirectResult result,
    void* value,
    TypeMetadata metadata,
    ProtocolWitnessTable witness,
    out SwiftError error);
```

This is an important result: generic Swift methods do not require the JIT to
understand Swift generics when the generator emits the exact metadata and
witness parameters as ordinary pointer-sized arguments.

Important limitations and lessons:

- generic types were skipped by the parser;
- tuples, closures, and existentials were incomplete;
- the managed support library used reflection in paths intended to be AOT
  compatible;
- native value allocation did not fully model alignment and initialization
  state;
- async wrappers used `try!`, lacked cancellation and robust error handling,
  and could leak callback state;
- generated code used runtime symbol lookup for data symbols, which is weak for
  NativeAOT and static linking;
- a string emitter accumulated complexity and already had a proposed redesign;
- generated scripts patched output with `sed`, demonstrating that the build
  contract was not yet stable.

The branch's StoreKit tests reached product lookup, basic property access, and
purchase calls. Transaction streams and complete `VerificationResult` handling
were still incomplete or commented out.

High-value documents in that branch include:

- `docs/binding-overview.md`
- `docs/runtime-metadata.md`
- `docs/binding-value-witness-table.md`
- `docs/memory-management.md`
- `docs/binding-generics.md`
- `docs/binding-protocols.md`
- `docs/runtime-existential-containers.md`
- `docs/swift-code-generation.md`
- `docs/retrieving-symbols-outside-abi-json.md`
- `src/docs/emitter-redesign-proposal.md`

## `Xamarin.SwiftUI`

Snapshot:

- commit `7e9de93abe1ec19405f03b927282d052d0eeb752`
- original experiment began in 2019

The project predates `CallConvSwift`. It manually reconstructed enough of the
Swift ABI to call SwiftUI and to make managed types satisfy selected SwiftUI
protocol requirements.

It implemented:

- direct P/Invokes to hand-discovered mangled SwiftUI symbols;
- a hand-written Swift glue module for non-C-compatible shapes;
- metadata, nominal descriptors, field descriptors, relative pointers, VWTs,
  protocol descriptors, and conformance descriptors;
- packed and unpacked `Optional<T>`;
- tuple metadata;
- generic metadata and witness arguments;
- escaping closure lifetime via a Swift `DelegateBox` and managed `GCHandle`;
- dynamic registration of a managed `View` conformance;
- a source generator that inferred the concrete `Body` type for C# views.

It did not implement Swift `throws`, Swift async, actors, or StoreKit.

### What the source says

[`Hacking.md`](https://github.com/chkn/Xamarin.SwiftUI/blob/7e9de93abe1ec19405f03b927282d052d0eeb752/Hacking.md)
documents the manual workflow:

1. Write the smallest Swift caller for an API.
2. Compile it.
3. Open the binary in Hopper.
4. Read the callee symbol, hidden arguments, registers, and result convention.
5. Hand-author a P/Invoke or a Swift glue function.

[`Glue.swift`](https://github.com/chkn/Xamarin.SwiftUI/blob/7e9de93abe1ec19405f03b927282d052d0eeb752/src/SwiftUIGlue/Glue.swift)
contains comments for each workaround: extra result registers, indirect
results, closure contexts, non-static-size self, metadata self, protocol
witnesses, and the `Optional<Double>` calling shape.

### Fresh Hopper reproduction

The glue library was rebuilt from the pinned source with Xcode 27 beta 2 and
Swift 6.4:

```bash
cd src/SwiftUIGlue
swift build -c release --disable-sandbox
hopper -a -f -e .build/out/Products/Release/libSwiftUIGlue.dylib
```

The following findings were reproduced in Hopper on the ARM64 output.

#### `_swiftui_View_background`

Hopper identified seven logical arguments. They correspond to:

1. destination buffer;
2. view value;
3. background value;
4. view metadata;
5. background metadata;
6. view conformance;
7. background conformance.

The procedure:

- calls metadata accessors for `_BackgroundModifier` and `ModifiedContent`;
- reads the VWT from one pointer before metadata;
- reads the runtime value size from the VWT;
- dynamically allocates aligned stack storage;
- places the temporary result in X8;
- places the view in X20;
- passes metadata and witness arguments in ordinary argument registers;
- invokes `SwiftUI.View.background`;
- moves the result to the caller's explicit destination using a VWT operation;
- destroys the temporary through the VWT.

This one function demonstrates why the final design needs both direct
`CallConvSwift` support and a reusable metadata/VWT support layer.

#### `_swiftui_Button_action_label`

Hopper showed the explicit destination, the three-word delegate
`(invoke, dispose, context)`, the label value, metadata, and conformance.
The function:

- obtains specialized `Button<T>` metadata;
- sizes temporary storage from the VWT;
- allocates a Swift `DelegateBox`;
- stores the managed callbacks and context;
- retains and later releases the box;
- supplies compiler-generated Swift closure entrypoints;
- returns the `Button<T>` value through VWT take initialization.

The glue is doing closure construction and ARC ownership that a plain managed
function pointer cannot safely synthesize.

#### `ThunkView.body`

Hopper showed:

- X20 carrying `self`;
- X8 carrying the indirect result;
- type metadata and VWT pointers loaded from the generic context;
- VWT copy/destroy around the callback;
- a call through the process-global managed body callback.

This validates the source's claim that exporting managed SwiftUI views required
both generated Swift code and carefully synchronized GC/VWT lifetime handling.

#### `_swiftui_VStack_align_spacing_content`

The source accepts `(Bool hasSpacing, CGFloat spacing)`. Hopper showed those
values being transformed into Swift's payload and discriminator representation
for `Optional<CGFloat>` before calling `VStack.init`.

Modern `CallConvSwift` plus a correct Swift enum/optional layout can remove this
specific hand-written glue.

### Lessons

Keep:

- metadata/VWT-driven resilient storage;
- explicit metadata and witness arguments;
- generated Swift boxes for escaping closures;
- compiler-generated proxy types for Swift protocols;
- source generation for opaque associated types.

Replace:

- Hopper-driven production signature discovery;
- literal hand-maintained mangled symbols;
- runtime reflection and dynamic metadata synthesis;
- hand-built protocol descriptors and witness tables;
- platform assumptions copied from macOS to other Apple targets;
- per-API hand-written Swift glue.

## Current `dotnet/macios` approach

The .NET Apple SDK still binds Swift-only APIs through Objective-C-compatible
Swift shims. The StoreKit 2 `AppStore.requestReview` path is representative:

```text
Swift @objc shim
    -> internal Objective-C binding
    -> public C# wrapper
    -> linker/trimmer preservation
    -> native runtime library packaging
```

This is reliable but O(number of APIs). It is appropriate as a compatibility
fallback, not as the primary path for StoreKit 2 or SwiftUI.

The Apple workload remains the correct owner for:

- native framework and XCFramework packaging;
- MSBuild `NativeReference` integration;
- Objective-C registrar reuse;
- trimming and exported-symbol preservation;
- app bundle and App Store validation;
- platform availability mapping.

The runtime should not duplicate this infrastructure.

## Swift 6.4 empirical ABI probes

A separate read-only probe corpus compiled minimized Swift 6.4 ARM64 examples,
emitting SILGen, canonical SIL, LLVM IR, assembly, objects, symbols, and
selected dylibs. Hopper was used to verify async functions, thick closures,
actors/witnesses, and Objective-C thunks.

Key results:

- X20 self/context, X21 error, X22 async context, and X8 indirect result match
  the runtime's existing ARM64 register model.
- Swift async functions tail-branch through a resume pointer; callers allocate
  context using the `Tu` async-function-pointer record and
  `swift_task_alloc`.
- Thick closure values point at compiler-generated partial-apply forwarders
  that move the context register into the raw closure body's ordinary
  argument.
- actor self uses the ordinary Swift self register; isolated behavior also
  involves witness dispatch, relative pointers, coroutines, and executors.
- `Optional<Int>` has no free spare-bit representation, while class and
  `Bool` optionals can reuse extra inhabitants.
- the same mangled function symbol can have incompatible direct versus
  indirect result ABI depending on whether the defining library was compiled
  with library evolution.

The last finding is especially important: symbol resolution alone cannot prove
the generated managed signature is compatible with the binary. The manifest
must record and validate compilation mode.

## Consolidated lessons

| Lesson | Consequence |
|---|---|
| Swift ABI calls are viable from .NET. | Continue extending `CallConvSwift`; do not default everything to C. |
| Source interfaces do not contain the full lowered ABI. | Require compiler-produced ABI/API data. |
| Generic metadata and witnesses can be ordinary generated arguments. | Keep them out of JIT semantics unless a physical ABI rule requires otherwise. |
| Resilient values need VWT ownership. | Add a reusable AOT-safe support library. |
| Async, actors, and reabstraction are compiler-owned. | Generate Swift/native thunks first; research direct SwiftAsync separately. |
| Hand-built Swift metadata is possible but fragile. | Prefer generated Swift proxy/types over runtime synthesis. |
| Objective-C remains valuable. | Reuse existing bindings for imported and `@objc` surfaces. |
| Every target differs. | Generate and validate per target triple; never infer iOS from macOS. |
| Mangling does not encode resilience mode. | Bindings must validate library-evolution mode, not only symbol names. |
| NativeAOT cannot recover missing runtime thunks. | Pre-generate and statically root every required shape. |
| Silent filtering hides product gaps. | Emit stable unsupported-reason diagnostics and coverage reports. |
