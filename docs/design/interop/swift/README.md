# Swift interoperability design and implementation plan

> [!IMPORTANT]
> This directory is an investigation and implementation plan, not an approved
> product commitment or a completed Swift binding system.

## Goal

The end state is broad, AOT-first interoperability between .NET and Swift on
Apple platforms, with StoreKit 2 as the first end-to-end product target.
Bindings must work across:

- CoreCLR JIT
- CoreCLR ReadyToRun (R2R)
- NativeAOT
- CoreCLR interpreter

"Broad" means every public declaration is discovered and deterministically
classified as direct, generated thunk, Objective-C, facade-only, or unsupported.
It does not promise a 1:1 C# projection for every Swift declaration.

The runtime should absorb ABI-sensitive work that is common to every binding,
while generated code should retain language- and API-specific policy. The
design must minimize hand-written Swift glue without moving the whole Swift
language or object model into the JIT.

## Headline recommendation

Use a three-tier model:

1. **Direct Swift ABI calls** for declarations whose ABI can be expressed with
   `CallConvSwift`, generated metadata/witness arguments, and runtime-managed
   value storage.
2. **Generated Swift/native thunks** for compiler-owned behavior such as async
   contexts, actor hops, reabstraction, resilient witness dispatch, and opaque
   results. Their managed-facing ABI uses primitives and versioned opaque
   handles by default. These are mechanical binding artifacts, not
   hand-written facades.
3. **C or Objective-C facades** only as a compatibility fallback or when a
   deliberately stable domain-specific API is more valuable than a direct
   language projection.

Generated thunks are the initial correctness baseline for broad SDK coverage.
Direct binding is
enabled declaration by declaration after compiler-produced lowering and
ownership data are proven complete.

```text
Swift SDK interface + compiler ABI/API descriptors + symbol graph + .tbd
                                |
                                v
                     versioned binding manifest
                                |
               +----------------+----------------+
               |                |                |
               v                v                v
       direct Swift ABI   generated native  C/Objective-C
          signatures          thunks           fallback
               |                |                |
               +----------------+----------------+
                                |
                                v
       generated managed projection + Swift runtime support library
                                |
                                v
             JIT / R2R / NativeAOT / CoreCLR interpreter
```

The manifest is produced at build time by the Swift toolchain. Ordinary managed
signatures remain the preferred runtime contract. A new runtime descriptor or
R2R fixup should be added only where ECMA signatures and generated stubs cannot
represent the ABI.

## Success criteria

The project is successful when:

- StoreKit 2 product lookup, purchase, entitlement enumeration, transaction
  updates, verification, and finishing work without hand-written per-API glue.
- Generated bindings use the same source model across every applicable in-scope
  execution mode.
- Resilient Swift values are allocated, copied, moved, and destroyed through
  metadata and value-witness operations rather than hard-coded layouts.
- Generic metadata and protocol witnesses are generated correctly.
- Swift errors never leak or unwind across an unmanaged boundary.
- Swift async and actor-isolated calls map to `Task`/`ValueTask` and
  `IAsyncEnumerable<T>` without blocking Swift executors.
- Trimming and NativeAOT require no reflection-based discovery or runtime code
  generation.
- The tool reports every skipped declaration with a stable reason code; it
  never silently drops unsupported API.
- Running the generator against every supported ARM64 Apple SDK target, plus
  ARM64e system-ABI validation inputs, produces a coverage report and a
  reproducible set of projections or diagnostics.

## Documents

- [Status rollup (start here)](status.md)
- [Current runtime state](current-state.md)
- [Prior art and Hopper findings](prior-art.md)
- [Proposed architecture](architecture.md)
- [Swift ABI and managed type model](abi-model.md)
- [Swift physical lowering specification](lowering.md)
- [Binding manifest schema](manifest.md)
- [Swift projection support layer](support-library.md)
- [Direct Swift async: verified ABI and candidate design](async-direct.md)
- [ARM64 execution-mode plan](backends.md)
- [StoreKit 2 vertical slice](storekit2.md)
- [Implementation roadmap and checklist](roadmap.md)
- [Validation and compatibility plan](validation.md)
- [Coverage dashboard and regression baseline](coverage.md)
- [Mixed-stack diagnostics](diagnostics.md)
- [Upstreaming plan](upstreaming.md)
- [Decision packages for area owners](decision-packages.md)
- [Research references](references.md)

## Design rules

1. **Compiler output is authoritative.** Do not reconstruct production call
   signatures from `.swiftinterface` text or demangled names alone.
2. **Direct where stable, thunk where compiler-owned.** A thunk is not a
   failure if it is generated, thin, and preserves the Swift value model.
3. **AOT first.** Runtime reflection and runtime-generated machine code cannot
   be required for correctness.
4. **Keep Swift language policy out of the JIT.** The JIT owns physical calling
   conventions, special registers, and transition correctness. Generated code
   and a managed support library own metadata, witnesses, ownership, and API
   projection.
5. **Do not byte-copy nontrivial Swift values.** Use value witness operations.
6. **Do not infer resilient layout.** Use metadata, accessors, and generated
   thunks.
7. **Do not unwind exceptions across interop.** Translate errors at generated
   boundaries.
8. **Do not depend on dynamic code.** Pre-generate closed callback, closure,
   async, and protocol proxy shapes.
9. **Reuse Objective-C bindings.** Clang-imported and `@objc` APIs should use
   the existing .NET Apple interop stack unless direct Swift ABI access is
   necessary.
10. **Fail closed.** Unknown ownership, pointer-authentication, async, or ABI
    manifest data must select a generated thunk or reject the declaration.

## Initial scope

The first product target set is:

- macOS on ARM64;
- iOS devices on ARM64 with ARM64e system-ABI validation;
- iOS Simulator on ARM64;
- Mac Catalyst on ARM64;
- tvOS devices on ARM64 with ARM64e system-ABI validation;
- tvOS Simulator on ARM64.

The implementation scope is managed ARM64 targets plus ARM64e-capable
device/system-ABI validation. AMD64/x64/x86_64, 32-bit architectures,
`arm64_32`, and `armv7k` are explicit non-goals.
watchOS execution is out of scope because its required architecture/runtime
matrix does not fit this profile.
visionOS may receive a future compiler-only manifest survey, but runtime
execution and release gates are outside this roadmap until CoreCLR and workload
targets exist.

The managed code-generation target remains ARM64. ARM64e is the Apple
system-framework, dyld-cache, pointer-authentication, and real-device ABI
validation profile; this plan does not introduce a new .NET
`TargetArchitecture` value for ARM64e.

Non-Apple Swift is outside this roadmap and creates no product, compatibility,
or validation gate.

Exact target triples and minimum deployment targets are defined in
[backends.md](backends.md).

## Explicit non-goals for the first product milestone

- A 1:1 mapping for every Swift language feature.
- Mono implementation, validation, compatibility, or parity work.
- AMD64/x64/x86_64 and non-ARM64 architecture support.
- Runtime synthesis of arbitrary Swift metadata, witness tables, task objects,
  coroutine frames, or closure contexts.
- Dynamic invocation of arbitrary Swift declarations by name.
- Automatic export of novel managed classes as native Swift classes.
- Replacing the existing Objective-C registrar and binding stack.
- Hiding all allocation or conversion costs behind apparently free C# APIs.

## Ownership

> [!NOTE]
> PROPOSED — pending explicit assignment. The Phase 0 owner-assignment item
> stays open until named owners replace TBD.

One component owns the manifest schema and the capability handshake: the
Swift support library in `dotnet/runtime`
(`src/libraries/System.Runtime.InteropServices.Swift`), with this directory
as the normative specification home. The schema, the profile strings, and the
handshake diagnostic codes version together under that single owner.

| Area | Owning component | Proposed owning team | Owner |
|---|---|---|---|
| Manifest schema, profile strings, capability handshake | `System.Runtime.InteropServices.Swift` in `dotnet/runtime` | .NET interop team | TBD |
| Support library and native ABI helper | `src/libraries/...Swift` + `src/native/libs/...Swift.Native` | .NET interop team | TBD |
| `CallConvSwift` execution: JIT, R2R, NativeAOT, interpreter | CoreCLR codegen and VM | CoreCLR team | TBD |
| Binding generator and SDK binding packs | Dedicated repository (proposal: a `dotnet` org repo graduating from `runtimelab`) | Bindings/generator team | TBD |
| Workload integration: toolchain invocation, packaging, signing, App Store validation | .NET Apple workload (`dotnet/macios`) | .NET Apple workload team | TBD |

Cross-component rules:

- Manifest schema changes require sign-off from the schema owner and the
  generator owner.
- New profile strings require sign-off from the schema owner and the CoreCLR
  owner of every runtime that will report them.
- The generator repository consumes the schema; it never forks or extends it.
- This proposal supersedes the roadmap Phase 12 item "Choose long-term
  repository and package ownership for the binding tool" only when approved;
  until then both remain open.

## Research snapshot

This plan was prepared against:

- `dotnet/runtime` commit `6c5849144dc8a23aa0760080d6f7784fed60da37`
- `dotnet/runtimelab` `feature/swift-bindings` commit
  `4c3431344dafa611a9a2ba82a603a0250fb80046`
- `chkn/Xamarin.SwiftUI` commit
  `7e9de93abe1ec19405f03b927282d052d0eeb752`
- Xcode 27 beta 2, Swift 6.4, and Apple SDK 27.0

See [references.md](references.md) for pinned source and issue links.
