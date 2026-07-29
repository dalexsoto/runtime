# Proposed architecture

## Decision summary

The proposed end state is a hybrid architecture with three execution tiers:

| Classification | Boundary | Default use |
|---|---|---|
| Direct | Direct Swift ABI | Stable, manifest-described synchronous ABI |
| Thunk | Generated Swift/native thunk | Compiler-owned or evolving ABI details |
| Facade | C/Objective-C facade | Qualified-profile semantic APIs |

The thunk classification is the default broad-SDK transport. It is not a hand-written wrapper
model: the binding tool mechanically emits a thin Swift declaration. Its
managed-facing entry ABI uses primitives, callbacks, and versioned opaque
native handles by default, while Swift materializes nontrivial values inside
the thunk.
The facade classification is reserved for deliberately flattened DTO/business
APIs within an already-qualified implementation profile.

The runtime should make the direct classification large enough that generated Swift code is needed
only for real compiler-owned behavior, not for register juggling, ordinary
generic metadata, errors, or resilient storage.

## Why a hybrid is required

The experiments establish three facts:

1. Direct Swift ABI calls work and are already in production in
   `dotnet/runtime`.
2. Metadata, VWTs, and explicit generic witness arguments allow significantly
   broader direct calls than the current runtime tests exercise.
3. Swift async, actor/executor transitions, reabstraction, partial application,
   and some resilient dispatch paths require compiler-generated code unless
   .NET implements a much larger and more version-sensitive Swift compiler ABI.

A facade-only design leaves too much performance and projection fidelity on
the table. A direct-only design would make every in-scope execution mode
reproduce compiler behavior that Swift already knows how to generate. The
hybrid keeps the common ABI substrate in the runtime and uses generated code at
the actual evolution boundaries.

## Architecture layers

```text
+---------------------------------------------------------------+
| User API                                                      |
| Idiomatic C# projections, convenience conversions, analyzers  |
+---------------------------------------------------------------+
| Generated binding layer                                       |
| Raw LibraryImport signatures, metadata/witness arguments,      |
| ownership plans, callback stubs, availability, diagnostics     |
+---------------------------------------------------------------+
| Swift projection support                                      |
| Metadata, VWT, native value handles, ARC/error handles,         |
| standard library wrappers, existential/closure support          |
+---------------------------------------------------------------+
| .NET execution backends                                       |
| Swift calling convention, special registers, struct lowering,  |
| P/Invoke/UCO transitions, R2R/AOT/interpreter parity            |
+---------------------------------------------------------------+
| Generated native assets                                       |
| Swift/native thunks and shared Swift ABI helper library         |
+---------------------------------------------------------------+
| Apple SDK frameworks and Swift runtime                         |
+---------------------------------------------------------------+
```

## Division of responsibility

### Runtime and code generators

The CoreCLR code generators own:

- `CallConvSwift` physical calling convention;
- special self, error, indirect-result, and future async-context registers;
- frozen value argument/result lowering;
- reverse-call entry and exit;
- stack/register homing and GC transition correctness;
- validation that is impossible to express at source-generation time;
- R2R and NativeAOT code-generation parity;
- interpreter/native transition thunks.

They should not own:

- Swift symbol discovery;
- protocol conformance selection;
- source overload resolution;
- metadata and witness lookup policy;
- high-level string/collection conversion;
- automatic Swift error-to-exception policy;
- actor scheduling policy;
- API availability projection.

### Managed Swift projection support

A low-level AOT-safe support library should own:

- opaque metadata and descriptor wrappers;
- metadata requests and responses;
- required VWT prefix and operations;
- aligned native value storage;
- initialization/copy/take/destroy state;
- native Swift object ARC handles;
- Swift error handles;
- protocol conformance and witness handles;
- classic existential storage;
- standard callback context lifetime;
- generated static generic contracts;
- diagnostics and manifest capability checks.

The package and final namespace require API review. The conceptual split is
more important than the name:

- CoreLib retains the calling-convention marker types.
- The larger support layer should not be added to CoreLib.
- Swift standard-library projections may be a separate package layered above
  the low-level support library.

Candidate runtime-repository layout:

```text
src/libraries/System.Runtime.InteropServices.Swift/
  low-level managed metadata, VWT, ownership, and handle support

src/native/libs/System.Runtime.InteropServices.Swift.Native/
  shared native helper for ptrauth-sensitive ABI operations

src/tests/Interop/Swift/
  execution-mode-neutral ABI, ownership, thunk, and projection fixtures

docs/design/interop/swift/
  normative design and support profile
```

The high-level SDK binding generator should remain outside the runtime
repository, with generated artifacts consumed by runtime integration tests.

### Binding generator

The generator owns:

- importing compiler ABI/API data;
- merging per-target declaration models;
- choosing Objective-C, direct ABI, thunk, facade, or unsupported;
- emitting exact raw signatures;
- adding generic metadata and witness arguments;
- selecting metadata accessors, dispatch thunks, and availability paths;
- generating ownership and cleanup code;
- generating Swift/native thunks;
- generating protocol proxies and callback stubs;
- generating native-link and trimming metadata;
- producing coverage and unsupported-reason reports.

### .NET Apple workload

The Apple workload owns:

- invoking the Swift toolchain;
- compiling and packaging generated Swift source;
- XCFramework, static, dynamic, and mergeable-library assets;
- framework and Swift runtime link flags;
- simulator/device slices;
- Objective-C registrar integration;
- app bundle, code-signing, and App Store validation.

## Public versus experimental API policy

The shipped public surface is the existing CoreLib marker set: `CallConvSwift`,
`SwiftSelf`, `SwiftSelf<T>`, `SwiftError`, and `SwiftIndirectResult`. It
follows the normal .NET breaking-change policy.

Everything new starts internal:

- new `ExtendedLayout` Swift kinds (`SwiftStruct`, `SwiftEnum`);
- any async marker (`CallConvSwiftAsync`, `SwiftAsyncContext`);
- the support library's metadata, VWT, storage, handle, existential, and
  callback surfaces;
- the manifest schema types and handshake entrypoints.

Rules:

1. An internal API may change or disappear at any time.
2. An API graduates from internal to experimental only when a consumer outside
   its own component needs it and the applicable validation batteries pass.
3. Experimental APIs are marked `[Experimental]` with a stable diagnostic ID
   from a reserved contiguous `SYSLIB` range (exact range assigned at API
   review). Generated code may suppress the ID inside generated files only;
   user code opts in explicitly.
4. Experimental APIs may change or be removed in any release without the
   breaking-change process. Their diagnostic IDs are never reused.
5. An experimental API becomes public only through API review, with all
   release gates in validation.md passing for the surface it exposes, and
   with an explicit ARM64/in-scope-runtime contract. `ExtendedLayout` Swift
   kinds and async markers additionally require the prototypes named in
   abi-model.md.
6. Generated bindings may use internal or friend APIs of the support library
   until the model is proven (see abi-model.md "Metadata"), but a shipped
   binding pack may depend only on public or experimental surface so that
   support-library servicing does not break deployed apps.
7. No public API may expose Swift runtime-private detail: metadata field
   layouts, witness indices, discriminator constants, or context frame
   layouts.
8. Profile strings and handshake diagnostic codes become contract the release
   they ship, regardless of the API surface that carries them.
9. Presence of the public marker types never implies profile support; the
   handshake is the only capability signal.

## Compiler-produced binding manifest

The production tool must not derive call signatures from `.swiftinterface`
text or a demangled symbol alone. It should combine:

- `swift-frontend` ABI descriptor output;
- API descriptor output;
- symbol graphs;
- `.tbd` exports and target information;
- cross-import descriptors;
- compiler-produced SIL/IR lowering extraction for direct-call candidates;
- the selected Swift compiler, SDK, target triple, and deployment target.

The resulting manifest is versioned and target-specific.

The existing ABI/API descriptor formats are designed primarily for API and ABI
comparison. They do not promise to contain every physical register, ownership,
reabstraction, ptrauth, or hidden-argument detail needed by an independent
caller. SIL/IR lowering extraction is therefore a first-class manifest input,
not an exceptional fallback.

Before direct binding is treated as more than an optimization, a go/no-go prototype
must measure:

- which StoreKit direct-call candidates are completely described without
  per-declaration compilation;
- the cost of compiling and caching lowering probes;
- whether a version-pinned Swift compiler component can emit the required call
  plan directly;
- how the plan changes across supported Xcode versions.

Generated thunks remain the correctness baseline if this extraction is too expensive or
fragile.

Conceptual manifest data:

```text
module
  stable module identity
  target triple and deployment target
  SDK and Swift compiler version
  Swift ABI version
  library-evolution/resilience mode
  framework install name and link mode

declaration
  Swift USR and display name
  source availability and isolation
  direct symbol, accessor, dispatch thunk, generated thunk, or client-emitted body
  direct/thunk/client-emitted/ObjC/facade/unsupported classification
  formal managed projection
  fully lowered native signature
  complete ordered lowered parameter list, including packs and fulfillments
  self/error/indirect-result/async-context positions
  generic metadata and witness argument order
  ownership for every parameter and result
  resilience and value-storage requirements
  closure/reabstraction requirements
  pointer category, source storage, and authentication requirements
  fallback and unsupported reason
```

The manifest is primarily a build-time contract. Generated IL should continue
to use ordinary method signatures whenever possible. This is critical for R2R,
NativeAOT, reflection metadata, and the CoreCLR interpreter.

Direct versus generated-thunk selection is a build-time manifest decision.
"Fallback to a generated thunk" never means patching a direct call to a
different ABI at runtime.

## Direct Swift ABI

Direct binding uses `LibraryImport`, `CallConvSwift`, marker types, and generated
support-library calls.

It should cover:

- scalar and pointer values;
- frozen trivial values;
- resilient values passed indirectly through owned native storage;
- native Swift classes as retained object handles;
- public metadata accessors;
- generic methods with generated metadata and witness arguments;
- classic protocol existentials with manifest-known shape;
- synchronous untyped `throws`;
- direct getters, setters, static methods, and dispatch thunks;
- invoking an existing manifest-described Swift closure;
- Objective-C object pointers used by Swift-only extensions.

Example generated shape:

```csharp
private static ProductHandle GetProduct(
    SwiftIndirectResult result,
    SwiftStringArrayHandle identifiers,
    TypeMetadata identifiersMetadata,
    ProtocolWitnessTable collectionWitness,
    out SwiftError error);
```

The public wrapper allocates the result from metadata, prepares witnesses,
calls the raw method, and translates the error.

### Signature-first rule

Do not introduce a general runtime `SwiftCallDescriptor` in the first
implementation. Current direct scenarios can be represented by:

- `CallConvSwift`;
- managed structs with correct layout;
- marker types for special registers;
- explicit pointer-sized hidden arguments;
- generated wrapper code.

A descriptor should be introduced only if a prototype proves that a physical
ABI requirement cannot be represented in ECMA signatures or generated IL.
Likely candidates are direct Swift async and pointer-authenticated indirect
code pointers, not ordinary metadata or witness arguments.

## Generated Swift/native thunks

A generated thunk is a one-to-one compiler-generated adapter. It preserves
Swift values and semantics internally rather than flattening them to
application DTOs. The managed-facing boundary should initially be a
C-compatible, versioned handle ABI so that managed code does not need to
construct nontrivial Swift standard-library values just to enter the thunk.

Use generated thunks for:

- Swift async entry, suspension, and completion;
- actor and global-actor hops;
- `AsyncSequence` iteration;
- reabstraction and partial application;
- constructing escaping Swift closures;
- resilient or conditional witness dispatch;
- associated type/conformance access that requires compiler substitutions;
- opaque `some` results when no direct metadata accessor plan is available;
- typed throws on downlevel runtimes;
- coroutine accessors;
- extended existential shapes;
- noncopyable and newer ownership features;
- unsupported pointer-authentication schemas;
- availability/back-deployment behavior best handled by the Swift compiler.

Conceptual generated thunk:

```swift
public func dotnet_thunk_Product_products(
    identifiersHandle: UnsafeRawPointer,
    completion: @escaping @convention(c) (
        UnsafeMutableRawPointer?,
        UnsafeMutableRawPointer?,
        UnsafeMutableRawPointer) -> Void,
    context: UnsafeMutableRawPointer) -> UnsafeMutableRawPointer
{
    let task = Task {
        do {
            let identifiers: [String] = materializeIdentifiers(identifiersHandle)
            let result = try await Product.products(for: identifiers)
            completion(resultStorage(result), nil, context)
        } catch {
            completion(nil, retainError(error), context)
        }
    }
    return retainOperation(task)
}
```

The real emitted signature should use the support library's native value and
error handles. It must also support cancellation, exactly-once completion, and
actor isolation. The example only illustrates the tier.

Every asynchronous thunk ABI has:

- `start(...) -> operationHandle`;
- idempotent `cancel(operationHandle)`;
- idempotent `release(operationHandle)`;
- completion-before-`start`-return handling;
- exactly one terminal callback;
- explicit result and error ownership.

Cancellation is cooperative. It is not equivalent to a framework result such
as StoreKit's `userCancelled`.

Generated thunks should:

- live in one generated module per framework/package rather than one hand-built
  project per API;
- use compiler-produced public symbols recorded in the manifest;
- avoid Objective-C classes unless Objective-C interop is intentionally used;
- be emitted only for thunk-classified declarations;
- be dead-strippable and statically rootable;
- expose a stable generated ABI version/capability function.

Generated thunks may use `CallConvSwift` internally or for proven trivial parameters, but
its correctness baseline does not require managed code to call arbitrary
nontrivial Swift values directly.

## C or Objective-C facade

Use a facade when:

- an application wants a stable business API independent of SDK evolution;
- security semantics benefit from a constrained boundary;
- the framework is not built with library evolution and exact-binary coupling
  is unacceptable;
- the declaration cannot be represented safely even with a generated ABI
  thunk.

For StoreKit, a facade may be useful above the generated bindings for
`ProductInfo`, entitlement state, and purchase results. It should not be the
only path to the underlying StoreKit API.

A StoreKit commerce service is a valid facade layer above raw bindings. It owns
launch-time transaction observation, at-least-once delivery, deduplication,
durable entitlement grants, recovery, and the rule that `finish()` occurs only
after durable delivery. Those policies do not belong in the raw
`Transaction.updates` transport.

A facade never qualifies an unsupported runtime or architecture. The same
allowlisted implementation-profile and ARM64 target checks apply before any
facade entrypoint is available.

## Native Swift ABI helper

Ship a single shared native helper owned by the runtime support package on
targets where pointer authentication or compiler-specific indirect calling
conventions apply. It isolates operations that are stable compiler ABI but
unsafe to invoke from managed code:

- VWT size/stride/alignment queries, copy/take/destroy, and enum witnesses;
- Swift error retain/release;
- Swift object retain/release variants;
- metadata-state validation;
- weak/unowned storage operations if later supported.

This helper is constant infrastructure, not per-API glue. On ptrauth targets,
managed code must not invoke, copy, or cache VWT, witness, closure, metadata, or
conformance function pointers directly. The shared helper handles only fixed,
finite runtime operations such as VWT and audited ARC/error calls. Arbitrary
witness and closure signatures use generated per-signature Swift/native thunks.
On targets proven not to authenticate those pointers, the helper may remain the
uniform implementation or an optimized direct path may be evaluated.

ARM64 ARC entrypoints may use nondefault conventions such as
`preserve_most`. Call them through an audited helper/default-CC veneer rather
than assuming an ordinary P/Invoke signature.

## Type and member classification

The generator should classify at both type and member granularity.

Suggested type labels:

```text
FrozenTrivialValue
FrozenNontrivialValue
ResilientValue
NativeSwiftObject
ObjectiveCObject
Actor
ClassicExistential
ExtendedExistential
Unsupported
```

Suggested member labels:

```text
ObjectiveC
DirectSwift
DirectSwiftWithManagedMarshalling
GeneratedSwiftThunk
ClientEmittedSwift
FacadeOnly
Unsupported
```

Unsupported classifications must include a stable diagnostic code and the
dependency that would make the declaration supportable.

## Binding surface policies

### Overloads that differ only by return type

Swift permits overloads distinguished only by return type; C# does not.
The generator applies a deterministic naming scheme so regeneration is
stable and diff-friendly:

1. The overload the classifier ranks first (lexicographically smallest
   mangled name) keeps the natural C# name.
2. Every other return-type-only overload gets the suffix `Returning<T>`,
   where `<T>` is the projected C# name of the return type with generic
   arguments flattened (`makeValue() -> Int64` and `makeValue() -> Double`
   project as `MakeValue` and `MakeValueReturningDouble`).
3. Collisions after suffixing (identical projected return names) append
   the shortest unique prefix of the return type's mangled name; this is
   recorded in the manifest so the name never changes once published.
4. The chosen names are part of the binding's compatibility surface:
   regenerating against a framework that adds overloads never renames an
   existing member (rule 1's ranking considers only members present in the
   previous manifest first).

### Convenience conversions and their documented cost

Projected types expose explicit conversions to and from familiar BCL types
only where the cost is predictable, and every conversion documents its
cost class in XML docs:

| Conversion | Cost class |
|---|---|
| `SwiftString` ⇄ `string` | O(n) copy plus allocation on each direction; small strings avoid native allocation |
| `SwiftArray<T>` ⇄ `T[]`/`Span<T>` | O(n) element copy (bridging is never zero-copy across the ABI); trivial `T` uses a block copy |
| `SwiftDictionary<K,V>`/`SwiftSet<T>` ⇄ BCL collections | O(n) rebuild through per-element conversion; hashing re-run on the managed side |
| `SwiftOptional<T>` ⇄ `T?` | O(1) tag inspection plus payload move |
| Raw pointer/buffer wrappers ⇄ `Span<byte>` | O(1) view creation; lifetime remains the caller's obligation |

Conversions are always explicit methods (`ToManaged…`/`From…`), never
implicit operators: crossing the bridge is visible at every call site and
in code review. Value-witness-backed copies stay inside the support
library; convenience conversions never expose partially initialized
values.

## Objective-C coexistence

The binder should reuse existing Objective-C projections for:

- Clang-imported declarations;
- `@objc` methods and properties;
- UIKit/AppKit/Foundation object types;
- existing .NET Apple delegate and registrar patterns.

Swift-only extensions on Objective-C types may use direct Swift ABI while
sharing the same native object handle. The generated type database must
canonicalize those identities so the same object is not wrapped twice.

## Import direction

The first product goal is Swift-to-.NET import. Exporting .NET implementations
to Swift is staged separately:

1. Generated Swift closure boxes for callbacks.
2. Generated Swift proxy classes/structs for protocol implementations.
3. Function-pointer/vtable initialization that works on JIT and interpreter.
4. NativeAOT direct exported entrypoints as an optimization.
5. Only after those are stable, investigate static generation of Swift
   metadata and witness tables for managed types.

`Xamarin.SwiftUI` proves dynamic metadata synthesis is possible, but it also
shows the fragility of hand-built relative pointers, conformance descriptors,
VWT callbacks, and GC rooting. Generated Swift proxy types are the safer
default.

## Tool integration

The recommended build pipeline has two stages:

1. An MSBuild/CLI tool invokes the Swift compiler and produces the manifest and
   native Swift sources/assets.
2. A Roslyn incremental generator consumes the manifest and emits C#.

Roslyn source generators should not invoke Xcode directly. Separating the
stages makes builds deterministic, permits centrally pre-generated Apple SDK
bindings, and allows third-party framework bindings to run locally.

A minimal version of this pipeline must be implemented early enough to compile,
link, root, and execute one generated native thunk in both CoreCLR and
NativeAOT. Full workload integration can follow later, but native-asset
packaging cannot be deferred until after StoreKit.

## Compatibility policy

### Apple SDK frameworks

- Require module-stable `.swiftinterface` input for portable bindings.
- Generate per target triple and merge only semantically compatible models.
- Preserve declaration-level availability.
- Treat `.tbd` `swift-abi-version` and target slices as validation inputs.
- Never hard-code resilient field offsets or enum tags.
- Include manifest schema, generator version, runtime ABI profile, Swift
  compiler build, SDK/module flags, target triple, deployment target, generated
  native ABI version, and relevant transitive binary hashes in the binding
  identity.
- Treat library-evolution/resilience mode as ABI-significant even when the
  mangled symbol is unchanged. A build-mode mismatch must fail generation or
  loading before the call can execute.
- Compile availability and back-deployment thunks for the selected deployment
  target.
- Permit direct imports only when the minimum deployment target guarantees the
  symbol.

### Third-party frameworks

Two modes are required:

1. **Library-evolution mode:** portable across compatible library updates.
2. **Exact-binary mode:** manifest is tied to the precise module/binary hash
   and Swift compiler compatibility.

### Runtime capability and version handshake

Generated bindings never assume the executing runtime supports them. Every
generated binding module validates compatibility once, before any other
generated code can run, and fails closed.

#### Generated side

Each generated binding assembly contains a module initializer that calls the
support library:

```csharp
// Conceptual shape. AOT-safe, no reflection, all inputs are
// compile-time constants emitted by the generator.
SwiftBindingRuntime.ValidateBindingCompatibility(new SwiftBindingManifest
{
    SchemaVersion = 3,
    RequiredProfiles = new[]
    {
        "SwiftInterop.Sync1",
        "SwiftInterop.Values1",
        "SwiftInterop.AsyncThunk1",
    },
    GeneratedNativeAbiVersion = 2,
    NativeAssetIdentity = "…content hash recorded at generation time…",
    TargetRid = "ios-arm64",
    TargetTriple = "arm64-apple-ios15.0",
    GeneratorVersion = "…",
});
```

The validation result is cached: success sets a flag; failure captures the
exception. Every public generated entrypoint checks the flag and rethrows the
captured failure. Module initializers are the primary gate; the per-entrypoint
check defends against any configuration that suppresses or reorders them.
The validation call and everything it reaches are trim/AOT rooted.

#### Support-library side

`ValidateBindingCompatibility` performs these checks, in this order:

1. **Manifest schema version.** The binding's schema version is one the
   support library supports.
2. **Implementation profile.** Every string in `RequiredProfiles` is reported
   by the runtime through the implementation-profile query
   `RuntimeFeature.IsSupported("SwiftInterop.…")` (implemented; constants
   `RuntimeFeature.SwiftInteropSync1` through `SwiftInteropAsyncThunk1`).
   The feature strings are reported only by CoreCLR JIT/R2R, the CoreCLR
   interpreter, and NativeAOT builds that implement the full profile for the
   executing mode. The strings are never derived from marker-type presence,
   `Type.GetType` probing, or architecture checks. Runtimes with partial
   `CallConvSwift` support (including Mono) report none of them, and the
   reserved `SwiftInterop.AsyncDirect1` string is reported by no runtime.
3. **Target allowlist.** The binding's RID and the current execution mode form
   an allowlisted cell of the first-release matrix (validation.md
   "Execution-mode matrix").
4. **Generated native ABI version.** The loaded generated native module's
   version export equals `GeneratedNativeAbiVersion`.
5. **Native asset identity.** The identity hash returned by the native module
   equals `NativeAssetIdentity`.

Checks 1-3 are managed-only. The only native calls permitted before
validation succeeds are the version and identity exports required by checks
4-5. Every generated native module exports them with plain C convention, no
arguments beyond an out-length, no side effects, and no Swift dependencies:

```c
int32_t        dotnet_swift_binding_abi_version(void);
const uint8_t* dotnet_swift_binding_identity(size_t* length);
```

#### Failure behavior

A failed check throws `PlatformNotSupportedException` with a stable
diagnostic code, before any Swift invocation. The message includes the code,
the expected and actual values, the RID, and the execution mode. Checks run
in a fixed order, so the same environment always produces the same code.

| Code | Rejection (from validation.md "Capability-handshake tests") |
|---|---|
| `SWIFT0001` | Implementation profile absent or older than required |
| `SWIFT0002` | Only the shared low-level marker APIs are present (marker-only support) |
| `SWIFT0003` | Manifest schema/profile version unsupported |
| `SWIFT0004` | Generated native ABI version differs |
| `SWIFT0005` | Native asset hash/identity differs from the manifest |
| `SWIFT0006` | Target RID or execution-mode cell not allowlisted |

Rules:

- Handshake failure never selects a different ABI at runtime. Direct-versus-
  thunk selection is a build-time decision (see "Compiler-produced binding
  manifest").
- There is no supported override switch. A diagnostics-only trace switch may
  log the evaluation; it cannot change the outcome.
- Diagnostic codes are contract: never reused, only retired.
- The build should additionally fail early — the workload validates the
  runtime pack against the required profiles at publish time — but the
  runtime handshake remains authoritative because packs, assets, and hosts
  can be recombined after build.

## Security model for binding inputs and assets

### Untrusted generator inputs

Manifests, ABI/API descriptors, symbol graphs, `.swiftinterface` files,
`.tbd` files, cross-import descriptors, and third-party binary modules are
untrusted inputs to the binding generator. Third-party frameworks and
package-delivered artifacts can be attacker-controlled.

Generator rules:

- Every input parser has enforced size limits, nesting/recursion depth
  limits, and a fuzz target.
- Malformed input is rejected with a deterministic diagnostic, never
  repaired or partially accepted.
- Module names, USRs, install names, and symbol names never influence
  filesystem paths beyond a sanitized leaf name. No path traversal, no
  absolute paths, no shell interpolation from input-derived strings.
- Symbol and mangled names are data. Before appearing in an executable
  position in generated Swift or C# (`@_silgen_name`, `LibraryImport` entry
  points), they are validated against the mangling/identifier grammar.
- The generator performs no network access.
- Generation is deterministic: identical inputs produce byte-identical
  outputs. This is required for the identity hashes below.

### Output identity

The binding identity (see "Compatibility policy") records content hashes of
the generated managed and native assets and of relevant transitive binaries.
The same hashes are compiled into the generated manifest constants, and the
runtime handshake rejects a native asset whose reported identity differs
(`SWIFT0005`), before any Swift invocation.

The hash check is an integrity and consistency check against mismatched or
recombined asset sets. It is not tamper-proofing: Apple code signing remains
the authority for binary integrity on device.

### Runtime

The runtime and support library never load or interpret manifest files at
runtime. The only manifest data that exists at runtime is the compiled-in
constant set consumed by the capability handshake.

StoreKit transaction verification rules are defined in
[storekit2.md](storekit2.md) (verify -> deduplicate -> durable grant ->
finish; unverified data grants nothing) and are not duplicated here.

## Architectural decisions to prototype before API review

1. Whether VWT and ARC operations are direct managed calls or use the shared
   native helper.
2. The low-level support library's public/internal split.
3. Swift size versus stride semantics for `ExtendedLayout`.
4. Static data-symbol import and rooting for NativeAOT.
5. The minimum manifest data required for generated closure or other
   declaration-specific arm64e pointer authentication beyond the standard
   ABI-wide discriminator table.
6. Whether direct Swift async is valuable after generated native thunks exist.
7. Prototype validation of the capability handshake specified above:
   module-initializer timing across R2R, NativeAOT, and the interpreter;
   safety and cost of the native version export; diagnostic quality of each
   rejection code.
