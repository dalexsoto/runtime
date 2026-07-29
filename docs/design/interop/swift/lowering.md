# Swift physical lowering specification

This document is normative. Open questions are explicitly marked (OQ-1 through
OQ-4) with quarantined open vectors; each must be resolved by Swift compiler
evidence or an explicit decision before the corresponding vector moves to the
closed table.

## Purpose and authority

This document specifies, normatively, how the .NET runtime lowers a frozen
unmanaged value type to a sequence of primitive elements for the Swift calling
convention (`CallConvSwift`).

Two independent implementations exist and are both in scope:

1. CoreCLR VM (`MethodTable::GetNativeSwiftPhysicalLowering`):
   [methodtable.cpp](../../../../src/coreclr/vm/methodtable.cpp)
2. Managed type system for Crossgen2 and NativeAOT
   (`SwiftPhysicalLowering.LowerTypeForSwiftSignature`):
   [SwiftPhysicalLowering.cs](../../../../src/coreclr/tools/Common/JitInterface/SwiftPhysicalLowering.cs),
   implemented as a literal byte-tag mirror of the VM algorithm

The two implementations must produce identical results for every input type.
Where they disagree with each other, or where either disagrees with this
document, this document is the arbiter, and this document in turn must agree
with observed Swift compiler behavior for every shape that Swift can express.
Divergence between execution modes (CoreCLR JIT at runtime versus
R2R/NativeAOT ahead of time) is a silent ABI corruption bug, not a
quality-of-implementation issue.

The worked vectors in this document are the seed corpus for the differential
tests required by [current-state.md](current-state.md) ("a normative algorithm
plus generated differential test vectors") and [validation.md](validation.md).

## Scope

This specification covers:

- lowering of unmanaged value types used as by-value arguments, by-value
  returns, and `SwiftSelf<T>` payloads in `CallConvSwift` signatures;
- both lowering implementations listed above, and the JIT-EE contract that
  carries the result (`getSwiftLowering`, `CORINFO_SWIFT_LOWERING` in
  [corinfo.h](../../../../src/coreclr/inc/corinfo.h)).

It does not cover:

- register and stack assignment of the lowered elements (owned by the JIT ABI
  classifier in [abi.cpp](../../../../src/coreclr/jit/abi.cpp) and the
  interpreter call-stub generator);
- resilient Swift values, which are never lowered and always use indirect
  conventions (see [abi-model.md](abi-model.md));
- Swift enum payload/spare-bit layout;
- SIMD vector types (explicitly unspecified; see "SIMD status").

The lowering algorithm itself is target-independent given an 8-byte pointer
size. The in-scope execution target is 64-bit (ARM64 primary; the historical
AMD64 path uses the same algorithm).

## Relationship to Swift's algorithm

Swift's documented convention
([CallingConvention.rst](https://github.com/swiftlang/swift/blob/main/docs/ABI/CallingConvention.rst))
maps a value's bytes to a "legal type sequence", remaps misaligned or small
integer ranges to opaque ranges, chunks opaque ranges into integers, and
passes the value indirectly when the sequence exceeds a cap:

> "There should also be a cap on the number of values. ... 4 is probably a
> reasonable cap here."

For lowering purposes the *maximum voluntary integer size* on all in-scope
targets is 8 bytes, so the natural alignment of an 8-byte integer is 8.
(The document's worked examples use a 4-byte voluntary size; that is not the
64-bit Apple configuration.)

Empirical grounding (Apple Swift 6.4, `swiftlang-6.4.0.23.5`, macOS 11
triples; Swift ABI probe clusters A and D):

- 2 × `Int64` struct: 2 slots, returned directly in `x0`/`x1` (`{ i64, i64 }`
  in `swiftcc` IR).
- 6 × `Int64` struct: 6 slots, indirect via `sret` (`x8` on ARM64).
- 32-byte homogeneous byte aggregate: exactly 4 slots, still passed directly
  in `x0`–`x3`; the cap is inclusive (≤ 4 is direct).
- `Int16`+`Int32`+`UInt64` struct: lowers to `{ i64, i64 }`, confirming that
  interior padding inside a pointer-sized block is absorbed into an integer
  chunk (see step 3 below and vector V3).
- Tuples lower by the identical algorithm as structs.
- Non-frozen types compiled with library evolution are always indirect
  regardless of size; lowering applies to frozen/fixed layouts only.

## Result contract

Lowering produces a `CORINFO_SWIFT_LOWERING`
([corinfo.h](../../../../src/coreclr/inc/corinfo.h)):

```c
#define MAX_SWIFT_LOWERED_ELEMENTS 4

struct CORINFO_SWIFT_LOWERING
{
    bool byReference;
    CorInfoType loweredElements[MAX_SWIFT_LOWERED_ELEMENTS];
    uint32_t offsets[MAX_SWIFT_LOWERED_ELEMENTS];
    size_t numLoweredElements;
};
```

- `byReference == true` means the value is passed or returned indirectly; the
  other fields are then meaningless and must not be read.
- Otherwise `numLoweredElements` is 0–4 and each element is one of
  `CORINFO_TYPE_FLOAT`, `CORINFO_TYPE_DOUBLE`, `CORINFO_TYPE_LONG`,
  `CORINFO_TYPE_INT`, `CORINFO_TYPE_SHORT`, `CORINFO_TYPE_BYTE`.
- Integer element signedness is not significant; only the size is.
- `offsets[i]` is the byte offset of element `i` within the managed value.
- `numLoweredElements == 0` with `byReference == false` is valid and means the
  value occupies no registers or stack (empty type).

Notation used below: a sequence is written `(long@0, double@8, short@24)`;
`by-reference` denotes `byReference == true`; `()` denotes the empty sequence.

## Preconditions

1. The type is a value type containing no GC references and no byrefs.
   Managed types must be rejected by the front end before lowering is
   requested; the managed implementation asserts and degrades to
   `by-reference`, which callers must not rely on.
2. Pointer size is 8. Swift interop is 64-bit only.
3. The managed layout (field offsets and total size) must match the layout of
   the corresponding Swift type byte for byte. Lowering consumes only the
   managed layout; it cannot detect a mismatch. Frozen Swift layout follows
   Swift's universal layout algorithm
   ([TypeLayout.rst](https://github.com/swiftlang/swift/blob/main/docs/ABI/TypeLayout.rst)),
   verified empirically for Swift 6.4 (fixed offsets, explicit padding,
   compile-time size/stride).
4. Auto-layout managed structs lower deterministically over whatever layout
   the runtime assigned, but bindings must use sequential or explicit layout
   that matches Swift. `Nullable<T>` never matches Swift `Optional<T>` layout
   and must not be used for it (see [abi-model.md](abi-model.md)).
5. A .NET empty struct has size 1 while a Swift empty struct has size 0.
   Both lower to `()` (see "Empty types"), but nesting an empty struct inside
   a larger struct produces different offsets on the two sides and violates
   precondition 3.

## Normative algorithm

The algorithm is specified on a byte-tag model, which is how the VM
implements it. The managed implementation uses interval arithmetic; it must
be observationally identical.

Let `N` be the total instance byte size of the type. Define a tag array
`B[0..N)` with values from `{Empty, Opaque, Int64, Float, Double}`,
initialized to `Empty`.

### Step 1 — byte tagging

Walk the instance fields recursively in layout order. Nested value-type
fields are flattened at their absolute offsets. For an `[InlineArray(k)]`
type, the single element field is processed `k` times at offsets
`base + i * sizeof(element)` for `i` in `0..k`. Enum fields lower as their
underlying primitive.

Each leaf field at absolute offset `o` receives a proposed tag and size:

| Field type | Tag | Size | Natural alignment |
|---|---|---:|---:|
| `float` | `Float` | 4 | 4 |
| `double` | `Double` | 8 | 8 |
| `long`, `ulong` | `Int64` | 8 | 8 |
| `nint`, `nuint`, pointers, function pointers | `Int64` | 8 | 8 |
| all other leaf content (`bool`, `char`, 1/2/4-byte integers) | `Opaque` | field size | 1 |

Then apply `SetRange(o, s, tag)`:

1. If `o` is not a multiple of the tag's natural alignment, replace the tag
   with `Opaque` ("misalignment rule").
2. If any byte in `[o, o+s)` already carries a non-`Empty` tag different from
   the proposed tag, replace the tag with `Opaque` and extend the range as
   follows, where `A` is the natural alignment of the first conflicting
   existing tag (scanning from `o` upward): `o' = align_down(o, A)`;
   `s' = align_up(s + o', A) - o'`; the written range is `[o', o'+s')`
   ("overlap rule"; reachable only via explicit layout). Only the first
   conflicting byte's tag participates; the original range's tail beyond
   `o'+s'` is not written.
3. Write the tag over the (possibly extended) range.

Note N1 (pointer-family tag): both implementations tag `nint`/`nuint`/
pointer/function-pointer fields `Opaque` (8 bytes) rather than `Int64`. A
naturally aligned pointer still emits as `long` at its own offset, but as an
opaque range it participates in step 3's gap bridging (see vector V27) —
tagging pointers `Int64` is NOT equivalent and is non-conforming. (The
managed implementation historically used `Int64` and was rewritten to match
the VM.)

### Step 2 — interval formation

Scan `B` from offset 0 and form intervals:

- `Empty` bytes belong to no interval.
- A new interval starts at offset `i` when: `i == 0`, or `B[i] != B[i-1]`, or
  `B[i] == Float` and `i % 4 == 0`, or `B[i]` is `Double`/`Int64` and
  `i % 8 == 0`.

The alignment-based split guarantees that adjacent same-typed fields (for
example two consecutive `double` fields) form distinct intervals, one per
field, while a single field's bytes never split. Consecutive `Opaque` bytes
always form one interval (maximal run).

### Step 3 — opaque gap bridging

Two consecutive `Opaque` intervals separated only by `Empty` bytes are merged
into a single `Opaque` interval — the intervening `Empty` bytes become part of
the opaque payload — when the first interval's end sentinel falls in the same
8-byte block as the second interval's start:

> Merge `A` (earlier) and `B` (later) iff
> `A.end / 8 == B.start / 8`
> (where `A.end` is the exclusive end sentinel).

Apply left to right; each merge decision compares against the ORIGINAL
previous interval's end sentinel, not the accumulated merged interval.
Note that an interval ending exactly on a block boundary (`A.end % 8 == 0`)
therefore bridges a gap into the following block (vectors V25 and V27).

This rule is what absorbs interior padding: `{short; int; ulong}` tags
`Opaque[0,2)`, `Empty[2,4)`, `Opaque[4,8)`, `Int64[8,16)`; bridging yields
`Opaque[0,8)` and the final sequence `(long@0, long@8)`, which is exactly
Swift's empirical `{ i64, i64 }` for the same struct (vector V3).

Note N2 (resolved formulation divergence — see OQ-2): the managed
implementation historically used the last-occupied-byte formulation
(`(A.end − 1) / 8`), which never bridges across a block boundary. It was
rewritten to the VM's end-sentinel formulation above; vectors V25 and V27
pin the behavior.

### Step 4 — element emission

Emit elements in interval offset order:

- `Float` interval → `float@offset`.
- `Double` interval → `double@offset`.
- `Int64` interval → `long@offset`.
- `Opaque` interval `[p, p+r)` → greedy decomposition; while `r > 0`:
  1. if `r > 4` and `p % 8 == 0`: emit `long@p`; `p += 8`; `r -= 8`;
  2. else if `r > 2` and `p % 4 == 0`: emit `int@p`; `p += 4`; `r -= 4`;
  3. else if `r > 1` and `p % 2 == 0`: emit `short@p`; `p += 2`; `r -= 2`;
  4. else: emit `byte@p`; `p += 1`; `r -= 1`.

  `r` may go negative: the final element may extend past the end of the
  opaque interval, including past the end of the struct, but never into a
  following non-empty interval (see invariant I4).

The greedy decomposition is chosen so that a naturally aligned struct built
from the lowered elements has the same padding as the original type: the only
non-8-byte non-integer element is the 4-byte `float`, so a correctly aligned
opaque run larger than 4 bytes at an 8-byte boundary can always widen to
`long` without colliding with a subsequent element.

### Step 5 — by-reference decision

If the total number of emitted elements exceeds `MAX_SWIFT_LOWERED_ELEMENTS`
(4), the result is `by-reference` and the element data is discarded.
Exactly 4 elements is direct (empirically confirmed at the boundary).
Zero elements is direct with an empty sequence.

## Invariants

- I1 Determinism: the result is a pure function of the managed layout
  (offsets, sizes, field kinds). No ordering of equal inputs may change it.
- I2 Monotonicity: `offsets[]` is strictly increasing.
- I3 Self-alignment: every element's offset is a multiple of its own size;
  consequently no element ever crosses an 8-byte block boundary.
- I4 Coverage: every non-`Empty` byte is covered by exactly one element.
  Elements may additionally cover `Empty` (padding) bytes, but only bytes
  bridged in step 3 or absorbed by the bounded extension in step 4; an
  element never overlaps a byte belonging to another interval.
- I5 Type fidelity: `float`/`double` elements arise only from `float`/`double`
  fields at naturally aligned offsets. Everything else is integral.
- I6 Cap: `byReference == (element count > 4)`. There is no other
  by-reference trigger in lowering (resilience is decided upstream).
- I7 Compositionality: the lowering of a struct equals the lowering of its
  leaf fields placed at their absolute offsets; nesting depth is irrelevant.
- I8 Natural packing: for a type whose non-padding content is laid out
  sequentially at natural alignment from offset 0, the lowered offsets equal
  the offsets obtained by packing the lowered element sequence at natural
  alignment. (This is how
  [SwiftLoweringTests.cs](../../../../src/coreclr/tools/aot/ILCompiler.Compiler.Tests/SwiftLoweringTests.cs)
  computes default expected offsets; explicit-layout vectors override it.)
- I9 Padding alone produces nothing: a type whose bytes are all `Empty`
  lowers to `()`.

## Special rules and edge cases

### Empty types

A struct with no instance fields (including .NET's 1-byte empty struct, and
any `Size`-padded struct with no fields) lowers to `()` — zero elements,
direct. Swift zero-sized frozen structs likewise consume no registers.
See precondition 5 for the nesting caveat.

### Elements extending past the end of the value

A 3-byte struct lowers to `(int@0)`; a 5-byte struct lowers to `(long@0)`.
Consumers (JIT, interpreter stubs) must load and store such elements without
assuming the managed value's allocation extends to the element's end; the
element never extends past the end of the 8-byte block containing its start
(I3), and never into another interval (I4).

### Inline arrays

`[InlineArray(k)]` types lower as `k` repetitions of the element field at
stride `sizeof(element)` (step 1). The 4-element cap applies to the total:
`[InlineArray(4)] long` → `(long@0, long@8, long@16, long@24)`;
`[InlineArray(5)] long` → `by-reference`.
The VM reads the repeat count from the `InlineArray` attribute blob; the
managed implementation expands via `TypeWithRepeatedFields`. Both must agree
with the flattened-field model.

### Explicit layout

Explicit layout is supported and exercises the misalignment rule, the overlap
rule, and gap bridging. Overlapping fields (unions) force the overlapped
region opaque with alignment extension (step 1, overlap rule). Shapes that
Swift itself cannot express (arbitrary overlaps, arbitrary gaps) still must
lower deterministically and identically in both implementations; Swift
evidence arbitrates only where an equivalent Swift/C shape exists
(for example `#pragma pack` C structs — see OQ-1).

### Marshalled (native) layout path

For non-blittable types the VM has a parallel path over
`EEClassNativeLayoutInfo` native field descriptors (`FLOAT` size 4/8 →
`Float`/`Double`, `INTEGER` size 8 → `Int64`, `NESTED` recursion, otherwise
`Opaque`). The algorithm from step 1 onward is identical. `CallConvSwift`
interop uses blittable types in practice; for blittable types the managed and
native layouts coincide and the two VM paths must agree.

### HFA

Swift lowering has no homogeneous-float-aggregate concept and none is needed.
A homogeneous `float`/`double` struct falls out of the general rules as
individual `float`/`double` elements (cap 4), and the downstream classifier
assigns consecutive FP registers. The coincidence with C ABI HFA treatment
for small aggregates is not relied upon; do not route Swift values through
the HFA classifier.

### SIMD status

Lowering of `Vector64/128/256/512<T>`, `Vector<T>`, and other 16-byte-aligned
vector types is deliberately unspecified in this draft. RyuJIT currently
rejects SIMD types in `CallConvSwift` signatures
([current-state.md](current-state.md)). The implementations may internally
produce a lowering for structs containing vector fields (by recursing into
their integer backing fields); such results are not a contract and must not
be exposed until a future revision specifies vector lowering against Swift
SIMD evidence.

## Worked differential vectors

All vectors assume pointer size 8. "ILC test" vectors are encoded in
[SwiftTypes.cs](../../../../src/coreclr/tools/aot/ILCompiler.Compiler.Tests/ILCompiler.Compiler.Tests.Assets/SwiftTypes.cs)
and verified by
[SwiftLoweringTests.cs](../../../../src/coreclr/tools/aot/ILCompiler.Compiler.Tests/SwiftLoweringTests.cs).
"Swift 6.4" vectors were verified against Swift compiler output in the ABI
probe. Layout is sequential natural unless stated.

| ID | Definition (C#) | Size | Lowered sequence | Provenance |
|---|---|---:|---|---|
| V1 | `{ long; long; }` | 16 | `(long@0, long@8)` | Swift 6.4: `{i64,i64}`, `x0`/`x1` |
| V2 | `{ double; double; }` | 16 | `(double@0, double@8)` | Swift 6.4: `{double,double}` |
| V3 | `{ short; int; ulong; }` | 16 | `(long@0, long@8)` | Swift 6.4: `{i64,i64}`; interior padding bridged |
| V4 | 32 opaque bytes (`[InlineArray(32)] byte`) | 32 | `(long@0, long@8, long@16, long@24)` | Swift 6.4: 4×`i64` in `x0`–`x3`; cap inclusive |
| V5 | `{ long ×6 }` | 48 | `by-reference` | Swift 6.4: `sret`, `x8` |
| V6 | `{ }` (empty struct) | 1 | `()` | rule (I9) |
| V7 | `{ long; double; sbyte; int; ushort; }` | 32 | `(long@0, double@8, long@16, short@24)` | ILC test `I64_D_I8_I32_UI16` |
| V8 | `{ long; double; sbyte; int; byte; }` | 32 | `(long@0, double@8, long@16, byte@24)` | ILC test `I64_D_I8_I32_UI8` |
| V9 | `{ ulong; long; {short}; long; }` | 32 | `(long@0, long@8, short@16, long@24)` | ILC test `F5_S1`; exactly at cap |
| V10 | `{ ulong; long; {short;byte} Size=3; long; }` | 32 | `(long@0, long@8, int@16, long@24)` | ILC test `F5_S2`; element pads past 3-byte range |
| V11 | `{ {short;byte} Size=3; sbyte; byte; } Size=5` | 5 | `(long@0)` | ILC test; element extends past struct end |
| V12 | `{ {double;float} Size=12; int; sbyte; } Size=17` | 17 | `(double@0, float@8, int@12, byte@16)` | ILC test `F2087_S0` |
| V13 | `{ float; ushort; short; ushort; }` | 12 | `(float@0, int@4, short@8)` | ILC test `F114_S0` |
| V14 | `{ double; float; uint; int; } Size=20` | 20 | `(double@0, float@8, int@12, int@16)` | ILC test `F352_S0` |
| V15 | `[InlineArray(4)] long` | 32 | `(long@0, long@8, long@16, long@24)` | ILC test `InlineArray4Longs` |
| V16 | `{ float; short; short; int; int; }` | 16 | `(float@0, int@4, long@8)` | ILC test `UnalignedLargeOpaque` |
| V17 | `{ short; nint; int; byte; } Size=21` | 21 | `(short@0, long@8, long@16)` | ILC test `PointerSizeOpaqueBlocks`; pointer + trailing opaque |
| V18 | `{ short; {byte; nint}; int; byte; }` | 29 | `(short@0, byte@8, long@16, long@24)` | ILC test `PointerSizeOpaqueBlocksNonNaturalAlignment`; non-natural offsets |
| V19 | `{ float ×4 }` | 16 | `(float@0, float@4, float@8, float@12)` | derived; homogeneous, at cap |
| V20 | `{ float ×5 }` | 20 | `by-reference` | derived; 5 elements |
| V21 | `[Explicit] { byte@0; float@2; }` | 8 | `(long@0)` | derived; misaligned float → opaque, bridged, widened |
| V22 (DV-8ON4-PACK4) | `[Pack=4] { int; long; }` | 12 | `(long@0, int@8)` | Swift 6.4: Clang-imported `#pragma pack(4)` struct lowers `{i64,i32}` on arm64 and x86_64; runtime-verified. Misaligned `long` dissolves into opaque chunks |
| V23 (DV-8ON4-PACK4-TAIL) | `[Pack=4] { int; long; int; }` | 16 | `(long@0, long@8)` | Swift 6.4 probe, cluster E |
| V24 (DV-8ON4-PACK4-MID) | `[Pack=4] { long; int; long; }` | 20 | `(long@0, long@8, int@16)` | Swift 6.4 probe, cluster E |
| V25 | `[Explicit] { int@8; int@12; short@18; } Size=20` | 20 | `(long@8, int@16)` | formerly OV2; VM-verified, gap bridged at block boundary |
| V26 | `[Explicit] { double@0; byte@2; }` | 8 | `(long@0)` | formerly OV3; VM-verified, overlap forces aligned opaque range |
| V27 | `[Explicit] { nint@0; short@10; } Size=12` | 12 | `(long@0, int@8)` | VM-verified; pointer-as-opaque participates in gap bridging |

Element notation: `long` = `CORINFO_TYPE_LONG`, `int` = `CORINFO_TYPE_INT`,
`short` = `CORINFO_TYPE_SHORT`, `byte` = `CORINFO_TYPE_BYTE`,
`float` = `CORINFO_TYPE_FLOAT`, `double` = `CORINFO_TYPE_DOUBLE`.

### Open vectors

These vectors are part of the differential corpus but their expected results
are not yet final. Each is tracked in "Open questions" below.

| ID | Definition (C#) | Size | Current result | Status |
|---|---|---:|---|---|
| OV4 | `[Pack=4] { int; double; }` | 12 | `(long@0, int@8)` expected by the OQ-1 rule | OPEN — misaligned `double` variant (DV-8ON4-FP) not yet probed |

## Open questions

### OQ-1 — 8-byte values at 4-byte alignment ("8-on-4") — RESOLVED

Resolved empirically (Swift 6.4, probe cluster E, `DV-8ON4-PACK4`): an
8-byte primitive at a non-8-aligned offset never survives as an 8-byte
tagged element. Swift remaps its bytes to opaque and re-chunks greedily at
naturally aligned storage-unit boundaries, exactly as both .NET
implementations already do. For the Clang-imported `#pragma pack(4)` struct
`{ int32_t a; int64_t b; }` (size 12), Swift IRGen produces `{ i64, i32 }`
on arm64 and x86_64 — verified in IR, at call sites in disassembly, and by
executing a differential runner. The managed and VM implementations produce
the identical sequence `(long@0, int@8)`, so current .NET behavior is
correct and vectors V22-V24 are closed.

Normative rule: a .NET packed or explicit-layout struct is call-compatible
with the corresponding Clang-imported packed C struct exactly when the
managed field offsets equal the C offsets; the misalignment rule plus greedy
opaque chunking then reproduce Swift's sequence.

Remaining follow-up: the misaligned `double` variant (OV4, `DV-8ON4-FP`)
is expected to behave identically by the same rule but has not been probed;
`float`/`double` tags require natural alignment, so a misaligned `double`
must dissolve into opaque chunks the same way.

### OQ-2 — opaque gap bridging at a block boundary — RESOLVED

The divergence was CONFIRMED with executable evidence (Checked-JIT dump
versus the managed implementation driven through the ILC type system): the
VM produced `(long@8, int@16)` for V25 while the managed implementation
produced `(long@8, short@18)` — physically different register contents
between CoreCLR JIT and NativeAOT/R2R.

Resolution: the shape is not Swift-expressible (sequential/C layouts cannot
place a gap at the start of a block), so parity, not Swift evidence, decides.
The VM's end-sentinel formulation was chosen because the VM is what the
runtime JIT executes and cannot be changed without affecting deployed JIT
behavior. The managed implementation was rewritten as a literal byte-tag
mirror of the VM algorithm, eliminating this divergence class; V25 and V27
are pinned by executable tests in
[SwiftLoweringTests.cs](../../../../src/coreclr/tools/aot/ILCompiler.Compiler.Tests/SwiftLoweringTests.cs).

### OQ-3 — overlapping explicit-layout fields — RESOLVED

The divergence was CONFIRMED with executable evidence: the VM produced
`(long@0)` for V26 while the managed implementation's interval combiner
truncated the pre-existing `Double` interval and produced `(int@0)`,
dropping bytes `[3,8)` in violation of invariant I4.

Resolution: the VM behavior is normative (overlapped storage is a union;
Swift imports unions as opaque aggregates; dropping tagged bytes violates
coverage). The managed implementation was rewritten as a literal byte-tag
mirror of the VM algorithm; V26 is pinned by an executable test.

### OQ-4 — SIMD

See "SIMD status". Unspecified, currently rejected downstream; a future
revision must specify vector lowering before the JIT accepts SIMD.

## Conformance requirements

1. The VM and managed implementations must produce identical
   `CORINFO_SWIFT_LOWERING` results (element kinds, offsets, count,
   `byReference`) for every unmanaged value type, including explicit-layout
   and `Size`-padded types.
2. Every closed vector above must be encoded in an executable differential
   test that runs both implementations. The existing ILC unit test covers
   the managed side; a VM-side counterpart (or a cross-dump comparison in the
   Swift interop test tree under
   [src/tests/Interop/Swift](../../../../src/tests/Interop/Swift)) is
   required.
3. Open vectors must be encoded as tests marked with their OQ number, either
   asserting the provisional result or asserting the known divergence, so
   that resolution is a deliberate test change.
4. Any change to either implementation requires a matching change to this
   specification and its vectors in the same PR.
5. New vectors derived from Swift compiler evidence must record the toolchain
   version and the observed `swiftcc` IR or disassembly shape.

## References

- VM implementation: `MethodTable::GetNativeSwiftPhysicalLowering` and the
  anonymous-namespace helpers (`SwiftPhysicalLoweringTag`, `SetLoweringRange`,
  interval build/merge/emit) in
  [methodtable.cpp](../../../../src/coreclr/vm/methodtable.cpp).
- Managed implementation:
  [SwiftPhysicalLowering.cs](../../../../src/coreclr/tools/Common/JitInterface/SwiftPhysicalLowering.cs)
  and
  [FieldLayoutIntervalCalculator.cs](../../../../src/coreclr/tools/Common/TypeSystem/Common/FieldLayoutIntervalCalculator.cs).
- JIT-EE contract: `getSwiftLowering`, `CORINFO_SWIFT_LOWERING`,
  `MAX_SWIFT_LOWERED_ELEMENTS` in
  [corinfo.h](../../../../src/coreclr/inc/corinfo.h).
- Consumers: `SwiftABIClassifier` in
  [abi.cpp](../../../../src/coreclr/jit/abi.cpp);
  [callstubgenerator.cpp](../../../../src/coreclr/vm/callstubgenerator.cpp).
- Existing vectors:
  [SwiftLoweringTests.cs](../../../../src/coreclr/tools/aot/ILCompiler.Compiler.Tests/SwiftLoweringTests.cs),
  [SwiftTypes.cs](../../../../src/coreclr/tools/aot/ILCompiler.Compiler.Tests/ILCompiler.Compiler.Tests.Assets/SwiftTypes.cs).
- Swift authority:
  [CallingConvention.rst](https://github.com/swiftlang/swift/blob/main/docs/ABI/CallingConvention.rst),
  [TypeLayout.rst](https://github.com/swiftlang/swift/blob/main/docs/ABI/TypeLayout.rst),
  [CallingConventionSummary.rst](https://github.com/swiftlang/swift/blob/main/docs/ABI/CallingConventionSummary.rst).
- Empirical evidence: Swift ABI probe, clusters A (value types) and D
  (interop cross-check), Apple Swift 6.4 (`swiftlang-6.4.0.23.5`).
- Context: [current-state.md](current-state.md) "Physical lowering" (this
  specification supersedes that summary), [abi-model.md](abi-model.md),
  [validation.md](validation.md).
