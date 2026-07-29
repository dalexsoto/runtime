// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ILCompiler.Compiler.Tests.Assets.SwiftTypes;

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Double, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int16)]
public struct I64_D_I8_I32_UI16
{
    public long i64;
    public double d;
    public sbyte i8;
    public int i32;
    public ushort ui16;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Double, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int8)]
public struct I64_D_I8_I32_UI8
{
    public long i64;
    public double d;
    public sbyte i8;
    public int i32;
    public byte u8;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int16)]
public struct F5_S1_S0
{
    public short F0;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int16, ExpectedLoweringAttribute.Lowered.Int64)]
public struct F5_S1
{
    public ulong F0;
    public long F1;
    public F5_S1_S0 F2;
    public long F3;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int32)]
[StructLayout(LayoutKind.Sequential, Size = 3)]
public struct F5_S2_S0
{
    public short F0;
    public byte F1;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int32, ExpectedLoweringAttribute.Lowered.Int64)]
public struct F5_S2
{
    public ulong F0;
    public long F1;
    public F5_S2_S0 F2;
    public long F3;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64)]
[StructLayout(LayoutKind.Sequential, Size = 5)]
public struct ThreeByteStruct_SByte_Byte
{
    public F5_S2_S0 F0;
    public sbyte F1;
    public byte F2;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Double, ExpectedLoweringAttribute.Lowered.Float)]
[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct F2087_S0_S0
{
    public double F0;
    public float F1;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Double, ExpectedLoweringAttribute.Lowered.Float, ExpectedLoweringAttribute.Lowered.Int32, ExpectedLoweringAttribute.Lowered.Int8)]
[StructLayout(LayoutKind.Sequential, Size = 17)]
public struct F2087_S0
{
    public F2087_S0_S0 F0;
    public int F1;
    public sbyte F2;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Float, ExpectedLoweringAttribute.Lowered.Int32, ExpectedLoweringAttribute.Lowered.Int16)]
public struct F114_S0
{
    public float F0;
    public ushort F1;
    public short F2;
    public ushort F3;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Double, ExpectedLoweringAttribute.Lowered.Float, ExpectedLoweringAttribute.Lowered.Int32, ExpectedLoweringAttribute.Lowered.Int32)]
[StructLayout(LayoutKind.Sequential, Size = 20)]
struct F352_S0
{
    public double F0;
    public float F1;
    public uint F2;
    public int F3;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64)]
[InlineArray(4)]
public struct InlineArray4Longs
{
    private long l;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Float, ExpectedLoweringAttribute.Lowered.Int32, ExpectedLoweringAttribute.Lowered.Int64)]
public struct UnalignedLargeOpaque
{
    public float F0;
    public short F1;
    public short F2;
    public int F3;
    public int F4;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int16, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64)]
[StructLayout(LayoutKind.Sequential, Size = 21)]
public struct PointerSizeOpaqueBlocks
{
    public short F0;
    public nint F1;
    public int F2;
    public byte F3;
}

public struct PointerSizeOpaqueBlocksNonNaturalAlignment_S0
{
    public byte F0;
    public nint F1;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int16, ExpectedLoweringAttribute.Lowered.Int8, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64, Offsets = [0x0, 0x8, 0x10, 0x18])]
[StructLayout(LayoutKind.Sequential, Size = 21)]
public struct PointerSizeOpaqueBlocksNonNaturalAlignment
{
    public short F0;
    public PointerSizeOpaqueBlocksNonNaturalAlignment_S0 F1;
    public int F2;
    public byte F3;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64)]
public struct F128_S_S0
{
    public sbyte F0;
    public short F1;
    public int F2;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Float, ExpectedLoweringAttribute.Lowered.Int32, ExpectedLoweringAttribute.Lowered.Int64)]
public struct F128_S
{
    public float F0;
    public F128_S_S0 F1;
    public uint F2;
}

// Differential vectors from docs/design/interop/swift/lowering.md.

// V22 (DV-8ON4-PACK4): a misaligned 8-byte primitive never survives as an
// 8-byte tagged element; its bytes dissolve into opaque chunks re-emitted at
// aligned boundaries. Matches Swift 6.4 lowering of the Clang-imported
// '#pragma pack(4)' struct.
[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int32)]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Packed8On4
{
    public int F0;
    public long F1;
}

// V23 (DV-8ON4-PACK4-TAIL)
[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64)]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Packed8On4Tail
{
    public int F0;
    public long F1;
    public int F2;
}

// V24 (DV-8ON4-PACK4-MID)
[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int32)]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Packed8On4Mid
{
    public long F0;
    public int F1;
    public long F2;
}

// V25 (formerly open vector OV2): two opaque intervals separated by a gap
// bridge when the first interval's end sentinel shares a pointer-sized block
// with the second interval's start, matching the CoreCLR VM formulation.
[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int32, Offsets = [0x8, 0x10])]
[StructLayout(LayoutKind.Explicit, Size = 20)]
public struct BlockBoundaryGapBridge
{
    [FieldOffset(8)] public int F0;
    [FieldOffset(12)] public int F1;
    [FieldOffset(18)] public short F2;
}

// V26 (formerly open vector OV3): an overlapping explicit-layout field forces
// the full conflicting range opaque, extended to the conflicting tag's
// natural alignment, matching the CoreCLR VM.
[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, Offsets = [0x0])]
[StructLayout(LayoutKind.Explicit)]
public struct OverlappingDoubleByte
{
    [FieldOffset(0)] public double F0;
    [FieldOffset(2)] public byte F1;
}

// V27: pointers lower as opaque pointer-sized ranges (not typed Int64
// intervals), so they participate in opaque gap bridging exactly like the
// CoreCLR VM's ranges do.
[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Int64, ExpectedLoweringAttribute.Lowered.Int32)]
[StructLayout(LayoutKind.Explicit, Size = 12)]
public struct PointerBlockBoundaryGapBridge
{
    [FieldOffset(0)] public nint F0;
    [FieldOffset(10)] public short F1;
}


// Vector lowering vectors (ARM64; docs/design/interop/swift/lowering.md "SIMD status").
// Vector64<T>/Vector128<T> lower as single hardware-vector elements; each
// vector chunk counts one element toward the 4-element cap.

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Vector128)]
public struct SingleVector128
{
    public System.Runtime.Intrinsics.Vector128<float> F0;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Vector64)]
public struct SingleVector64
{
    public System.Runtime.Intrinsics.Vector64<float> F0;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Vector128, ExpectedLoweringAttribute.Lowered.Int64)]
public struct VectorAndScalar
{
    public System.Runtime.Intrinsics.Vector128<float> F0;
    public long F1;
}

[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Vector128, ExpectedLoweringAttribute.Lowered.Vector128, ExpectedLoweringAttribute.Lowered.Vector128, ExpectedLoweringAttribute.Lowered.Vector128)]
public struct FourVector128
{
    public System.Runtime.Intrinsics.Vector128<float> F0;
    public System.Runtime.Intrinsics.Vector128<float> F1;
    public System.Runtime.Intrinsics.Vector128<float> F2;
    public System.Runtime.Intrinsics.Vector128<float> F3;
}

[ExpectedLowering]
public struct FiveVector128
{
    public System.Runtime.Intrinsics.Vector128<float> F0;
    public System.Runtime.Intrinsics.Vector128<float> F1;
    public System.Runtime.Intrinsics.Vector128<float> F2;
    public System.Runtime.Intrinsics.Vector128<float> F3;
    public System.Runtime.Intrinsics.Vector128<float> F4;
}

// Vector256<T> has no direct mapping but its two Vector128 fields lower as
// two 16-byte vector chunks, matching Swift's pre-splitting of 32-byte
// vectors.
[ExpectedLowering(ExpectedLoweringAttribute.Lowered.Vector128, ExpectedLoweringAttribute.Lowered.Vector128)]
public struct SingleVector256
{
    public System.Runtime.Intrinsics.Vector256<float> F0;
}
