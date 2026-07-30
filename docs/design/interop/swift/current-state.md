# Current runtime state

## Summary

The runtime already contains a substantial low-level Swift ABI implementation.
It is sufficient for manually authored synchronous calls involving scalars,
pointers, frozen unmanaged values, special Swift registers, and callbacks. It
is not yet a general Swift language binding system.

The implementation shipped incrementally beginning in .NET 9 and now spans
RyuJIT, Crossgen2, NativeAOT, and the CoreCLR interpreter on Apple ARM64.
The main missing layers are:

- compiler-backed module ingestion and binding generation;
- resilient value storage and lifetime management;
- metadata and protocol witness support;
- general Swift standard-library projections;
- existentials, closures, and protocol proxies;
- Swift async and actor isolation;
- complete CoreCLR execution-mode coverage and dedicated R2R/NativeAOT tests.

## Scope boundary

The repository also contains an existing partial Swift implementation in Mono,
but it is outside this design's implementation, validation, compatibility, and
parity scope. No roadmap item may require Mono changes. Because the low-level
marker types are shared, the generated-binding capability handshake must check
for an explicitly allowlisted implementation profile (CoreCLR JIT/R2R,
CoreCLR interpreter, or NativeAOT) rather than assuming that partial
`CallConvSwift` support implies the full profile.

CoreCLR also contains an existing AMD64 Swift calling-convention path. It is
historical baseline only: this plan neither extends nor removes it and adds no
AMD64 validation or compatibility obligation.

## Public surface

The public low-level surface is in `System.Runtime`:

| API | Purpose |
|---|---|
| `CallConvSwift` | Marks the native Swift calling convention. |
| `SwiftSelf` | Places a pointer-sized context in Swift's self/context register. |
| `SwiftSelf<T>` | Places a frozen unmanaged value in the self position, using Swift physical lowering when possible. |
| `SwiftError` | Receives or returns Swift's error-register value. |
| `SwiftIndirectResult` | Places a return-buffer address in Swift's indirect-result register. |

Sources:

- [CallingConventions.cs](../../../../src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/CallingConventions.cs)
- [SwiftTypes.cs](../../../../src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/Swift/SwiftTypes.cs)

These are passive ABI marker types. They do not own Swift objects, retain or
release errors, allocate resilient values, invoke metadata accessors, or map
Swift errors to managed exceptions.

## Metadata encoding

Swift uses the existing extensible unmanaged calling-convention encoding:

1. The ECMA signature is unmanaged.
2. `CallConvSwift` is an optional custom modifier on the return type.
3. P/Invokes use
   `[UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]`.
4. Reverse calls use
   `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]`.
5. Function pointers use `delegate* unmanaged[Swift]<...>`.

The managed type system parses and re-emits this modifier in
[UnmanagedCallingConventions.cs](../../../../src/coreclr/tools/Common/TypeSystem/Interop/UnmanagedCallingConventions.cs).
`LibraryImportGenerator` forwards `UnmanagedCallConvAttribute`, but has no
Swift-specific projection or marshalling logic.

The particular unmanaged convention does not participate in function-pointer
type identity under the current ECMA augment
([Ecma-335-Augments.md](../../specs/Ecma-335-Augments.md)). Generated code must
therefore preserve the original signature and should not depend on unsafe
function-pointer casts to diagnose convention mismatches.

## Physical lowering

Swift frozen values are recursively lowered to primitive pieces. The runtime:

- preserves naturally aligned `float`, `double`, 64-bit integer, pointer, and
  function-pointer ranges;
- treats other or misaligned ranges as opaque bytes;
- coalesces opaque bytes within pointer-sized regions;
- decomposes opaque regions to aligned 8/4/2/1-byte integer pieces;
- passes values indirectly when the resulting sequence has more than four
  elements;
- omits empty padding;
- handles inline arrays and explicit layout.

There are two in-scope implementations:

1. Managed type system for Crossgen2 and NativeAOT:
   [SwiftPhysicalLowering.cs](../../../../src/coreclr/tools/Common/JitInterface/SwiftPhysicalLowering.cs)
2. CoreCLR VM:
   [methodtable.cpp](../../../../src/coreclr/vm/methodtable.cpp)

This duplication is a correctness risk. A common executable implementation is
not immediately practical because the components use different type systems,
so the plan requires a normative algorithm plus generated differential test
vectors. The normative algorithm and seed vector corpus now live in
[lowering.md](lowering.md); that specification supersedes this summary and is
the arbiter when the implementations disagree.

The managed implementation is a literal byte-tag mirror of the VM algorithm;
two confirmed explicit-layout divergences (OQ-2/OQ-3 in
[lowering.md](lowering.md)) were fixed by the rewrite, and the vector corpus
V22-V27 is pinned by the executable ILC lowering tests. Current known gaps
include SIMD, the open misaligned-double vector OV4, and a checked-in
VM-side executable counterpart for the differential vectors (the VM results
were captured with ad-hoc Checked-JIT dump probes).

## Register model

| Target | Self/context | Error | Indirect result | Integer results | Floating results |
|---|---|---|---|---|---|
| Apple ARM64 | X20 | X21 | X8 | X0-X3 | V0-V3 |

CoreCLR definitions are in
[targetarm64.h](../../../../src/coreclr/jit/targetarm64.h).

Swift async additionally uses X22. The runtime does not currently model that
convention.

## CoreCLR JIT

RyuJIT currently supports:

- managed-to-Swift P/Invoke and `calli`;
- Swift-to-managed `UnmanagedCallersOnly`;
- `SwiftSelf`, `SwiftError`, and `SwiftIndirectResult`;
- frozen struct arguments and returns;
- lowered arguments split between registers and stack;
- reverse-call argument reassembly;
- Swift error-register clearing and capture.

Primary implementation:

- [importercalls.cpp](../../../../src/coreclr/jit/importercalls.cpp)
- [abi.cpp](../../../../src/coreclr/jit/abi.cpp)
- [lclvars.cpp](../../../../src/coreclr/jit/lclvars.cpp)
- [flowgraph.cpp](../../../../src/coreclr/jit/flowgraph.cpp)
- [codegencommon.cpp](../../../../src/coreclr/jit/codegencommon.cpp)

Reverse `SwiftSelf<T>` is complete: a by-reference-lowered self arrives as a
pointer in the self register and is handled through the implicit-byref path,
while a directly-lowered self is validated and then lowered like a trailing
ordinary struct parameter. Reverse signatures enforce the same rules as
forward calls (`SwiftSelf<T>` must be last, no duplicate self, struct shape)
with `InvalidProgramException` diagnostics.

Vector64/Vector128 values are supported on ARM64 (arguments, returns,
struct fields, and reverse callbacks) with dedicated vector element kinds in
the lowering contract; other SIMD types are rejected with a deterministic
error, as is any SIMD value on non-ARM64 targets and in the CoreCLR
interpreter's register-passed positions.

Important gaps:

- `SwiftAsync` is an explicit importer TODO.

Platform validation fails closed: targets without `SWIFT_SUPPORT` reject
`CallConvSwift` calls and methods with a deterministic platform-limitation
error rather than silently using the default convention. RID/execution-mode
allowlisting beyond that is owned by the capability handshake
([architecture.md](architecture.md)).

## ReadyToRun

Crossgen2 includes the shared managed Swift lowering and passes the Swift
calling convention to RyuJIT. There is no Swift-specific R2R import-cell or
fixup format.

Current R2R code uses the ordinary P/Invoke target fixups:

- `READYTORUN_FIXUP_IndirectPInvokeTarget`
- `READYTORUN_FIXUP_PInvokeTarget`

See:

- [readytorun.h](../../../../src/coreclr/inc/readytorun.h)
- [ILCompiler.ReadyToRun.csproj](../../../../src/coreclr/tools/aot/ILCompiler.ReadyToRun/ILCompiler.ReadyToRun.csproj)

The generated code contains the Swift register and lowering assumptions; the
fixup resolves only the target address. This is adequate for the existing
synchronous ABI profile. It does not provide a Swift ABI version marker,
metadata/witness fixup, async continuation fixup, or fallback policy for an
unknown Swift ABI manifest.

## NativeAOT

NativeAOT uses the same managed type system and RyuJIT lowering. Lazy P/Invokes
encode `CallConvSwift` in the generated `calli` signature; eager imports unwrap
to the original method's convention.

Apple NativeAOT already links Swift runtime support where required. The common
Swift runtime tests are reused, but there is no dedicated NativeAOT Swift test
project covering:

- first-call and lazy-import cells;
- static framework linking;
- data-symbol preservation;
- reverse callback rooting;
- trimming;
- pointer authentication;
- generated native Swift thunks.

## CoreCLR interpreter

The CoreCLR interpreter gained Swift support in 2026. It currently supports
Apple ARM64 only and implements:

- forward P/Invoke and `calli`;
- reverse P/Invoke;
- X20 self, X21 error, and X8 indirect result;
- argument and return lowering;
- marshalled and transient-IL P/Invoke detection;
- error preservation across interpreter/native transitions.

Primary implementation:

- [callstubgenerator.cpp](../../../../src/coreclr/vm/callstubgenerator.cpp)
- [arm64/asmhelpers.S](../../../../src/coreclr/vm/arm64/asmhelpers.S)

Reverse by-reference `SwiftSelf<T>` is supported: the reverse call stub copies
the self value from the pointer in X20 onto the interpreter stack by value,
and signature validation matches the RyuJIT rules (`SwiftSelf<T>` last, no
duplicate self, struct shape).

Known gaps:

- no Swift async context.

## Existing tests

Eight runtime suites under
[src/tests/Interop/Swift](../../../../src/tests/Interop/Swift) contain 333
facts:

| Suite | Facts | Scope |
|---|---:|---|
| `SwiftAbiStress` | 100 | Scalar and frozen-struct arguments |
| `SwiftRetAbiStress` | 100 | Frozen-struct returns |
| `SwiftCallbackAbiStress` | 100 | Reverse calls, struct values, closure context |
| `SwiftErrorHandling` | 6 | Forward and reverse error register |
| `SwiftSelfContext` | 10 | Pointer and frozen-value self, forward and reverse, direct `calli` |
| `SwiftIndirectResult` | 2 | Forward and reverse indirect result |
| `SwiftInvalidCallConv` | 10 | Invalid signatures and unsupported values, forward and reverse |
| `SwiftInlineArray` | 5 | Inline-array lowering |

Coverage limitations:

- only selected fixtures compile Swift with library evolution;
- no dynamic resilient-layout, ownership, generic, existential, optional, enum,
  vector, async, or actor coverage;
- no dedicated R2R or NativeAOT import-cell test;
- the checked-in stress suites predate the checked-in generator
  ([StressGenerator](../../../../src/tests/Interop/Swift/StressGenerator));
  new generated suites come from the seedable model, and the historical
  suites remain as-is.

## Production use: Apple cryptography

The repository's production proof is
[pal_swiftbindings.swift](../../../../src/native/libs/System.Security.Cryptography.Native.Apple/pal_swiftbindings.swift)
and its managed declarations under
[Common/src/Interop/OSX/System.Security.Cryptography.Native.Apple](../../../../src/libraries/Common/src/Interop/OSX/System.Security.Cryptography.Native.Apple).

It contains 24 Swift entrypoints covering AEAD, digest, HKDF, and X25519.
Notable properties:

- exported names are fixed with underscored `@_silgen_name`;
- signatures use Swift ABI-compatible buffer structs and `SwiftError`;
- generics and protocols remain inside the Swift shim;
- lifetime is implemented per binding with `SafeHandle` and explicit free
  functions;
- there is no reusable metadata, VWT, ARC, or error support layer.

This proves the current calling convention is production-capable, but it also
shows why a hand-written Swift shim per subsystem does not scale to Apple SDKs.

## Adjacent foundation: ExtendedLayout

.NET 11 main contains:

- `ExtendedLayoutAttribute`;
- `ExtendedLayoutKind.CStruct`;
- `ExtendedLayoutKind.CUnion`;
- ECMA metadata support for the fourth layout-mask value;
- CoreCLR, NativeAOT, R2R, Reflection.Emit, ILAsm, and Roslyn plumbing.

Sources:

- [ExtendedLayoutAttribute.cs](../../../../src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/ExtendedLayoutAttribute.cs)
- [ExtendedLayoutKind.cs](../../../../src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/ExtendedLayoutKind.cs)
- [Ecma-335-Augments.md](../../specs/Ecma-335-Augments.md)

The approved proposal
[dotnet/runtime#100896](https://github.com/dotnet/runtime/issues/100896)
was originally motivated by Swift generic struct and enum layout. The planned
`SwiftStruct` and `SwiftEnum` kinds are not implemented. Open design questions
include the distinction between Swift size and stride, spare-bit
discriminators, `SwiftBool`, and pointer spare bits.

Additional current gaps include:

- simulator tests are broadly disabled with unresolved triage markers;
- no generic extended-layout tests exist;
- no P/Invoke argument/return tests prove the call ABI of extended layouts;
- `ExtendedLayoutAttribute` exposes no public `Kind` property, so reflection
  must inspect custom attribute data.

## Adjacent foundation: runtime async

The runtime-async feature adds a managed continuation argument/result ABI for
Task-returning IL methods. It is specified in:

- [runtime-async.md](../../specs/runtime-async.md)
- [runtime-async-codegen.md](../../coreclr/botr/runtime-async-codegen.md)

It now has substantial JIT, R2R, NativeAOT, and CoreCLR interpreter support.
It is **not** Swift's async ABI:

- .NET runtime async uses managed `Continuation` objects and .NET-specific
  resumption stubs.
- Swift async uses the Swift async context register, compiler-generated context
  layouts, task/executor runtime calls, and `swifttailcc`.

The features may share Task completion, diagnostics, and scheduling
infrastructure, but their physical ABIs must remain separate. Runtime async is
currently disabled for Apple-mobile test projects, so the first StoreKit 2
implementation cannot depend on it. The original Apple-mobile interpreter crash
([#124044](https://github.com/dotnet/runtime/issues/124044)) was fixed by
[#125282](https://github.com/dotnet/runtime/pull/125282), but the broad
source/test exclusion remains.

## Gap summary

| Area | State |
|---|---|
| Synchronous Swift calling convention | Substantial, production-used |
| Frozen unmanaged values | Implemented |
| R2R and NativeAOT code generation | Shared implementation, weak dedicated coverage |
| CoreCLR interpreter | Apple ARM64 only |
| Swift layout for generic structs/enums | Proposed, not implemented |
| Resilient values and VWT ownership | Not implemented |
| Metadata and witnesses | Generator/support-library responsibility, absent from runtime repo |
| Swift classes and ARC | Raw pointers only |
| Existentials and protocols | Not implemented |
| Closures | Raw function/context shapes only |
| Swift errors | Raw register support only |
| Swift async and actors | Not implemented |
| Module ingestion and projections | Not implemented |
| StoreKit 2 | Experimental prior-art tooling only |
