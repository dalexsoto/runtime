// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// Layout edge cases at the Swift call boundary (roadmap Phase 1): empty and
// zero-sized values, explicit-size and explicit-offset gaps, misaligned
// fields, and [InlineArray] shapes; see lowering.md for the normative rules
// each case exercises (empty tag dropping, opaque gap bridging via the
// end-sentinel, misalignment -> opaque, inline-array field replication).
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftLayoutEdgeCases
{
    private const string SwiftLib = "libSwiftLayoutEdgeCases.dylib";

    // An empty managed struct has no fields: every byte is a gap, so the
    // value vanishes from the physical signature exactly like Swift's
    // zero-sized 'Empty'.
    public struct Empty
    {
    }

    public struct OnlyEmpties
    {
    }

    // The Swift mirror declares 'e: Empty; x: Int64' — the zero-sized field
    // contributes nothing, so the managed mirror omits it and offsets match.
    [StructLayout(LayoutKind.Sequential)]
    public struct WrapsEmpty
    {
        public long X;
    }

    // Trailing padding only: bytes past the last field are dropped by the
    // end-sentinel rule, so this lowers as a single Int64.
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public struct PaddedLong
    {
        public long X;
    }

    // Explicit offsets with an interior gap: a@0, gap, b@12. Typed fields
    // around a pure gap stay separate lowered elements (only opaque
    // intervals bridge gaps), so this lowers as (i32, i32) — physically
    // identical to a two-Int32-parameter Swift signature.
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct GappedInts
    {
        [FieldOffset(0)] public int A;
        [FieldOffset(12)] public int B;
    }

    // A double at offset 4 is misaligned, so its bytes lower opaquely; the
    // Swift mirror reconstructs the value from the raw bits.
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    public struct MisalignedDouble
    {
        [FieldOffset(0)] public int I;
        [FieldOffset(4)] public double D;
    }

    [InlineArray(4)]
    public struct FloatQuad
    {
        private float _element0;
    }

    [InlineArray(3)]
    public struct ByteTriple
    {
        private byte _element0;
    }

    // Five 8-byte chunks exceed the four-element cap: passed by reference.
    [InlineArray(5)]
    public struct LongFive
    {
        private long _element0;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases16takeEmptyBetween1a1e1bs5Int64VAG_AA0F0VAGtF")]
    private static extern long TakeEmptyBetween(long a, Empty e, long b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases15takeOnlyEmpties1v1xs5Int64VAA0fG0V_AFtF")]
    private static extern long TakeOnlyEmpties(OnlyEmpties v, long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases13sumWrapsEmptyys5Int64VAA0fG0VF")]
    private static extern long SumWrapsEmpty(WrapsEmpty v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases13sumWrapsEmptyys5Int64VAA0fG0VF")]
    private static extern long SumWrapsEmptyPadded(PaddedLong v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases14sumGappedPartsys5Int64Vs5Int32V_AFtF")]
    private static extern long SumGappedInts(GappedInts v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases19sumMisalignedDoubleySdAA0fG0VF")]
    private static extern double SumMisalignedDouble(MisalignedDouble v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases12sumFloatQuadySfAA0fG0VF")]
    private static extern float SumFloatQuad(FloatQuad v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases13makeFloatQuadyAA0fG0VSf_S3ftF")]
    private static extern FloatQuad MakeFloatQuad(float a, float b, float c, float d);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases13sumByteTripleys5Int64VAA0fG0VF")]
    private static extern long SumByteTriple(ByteTriple v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftLayoutEdgeCases11sumLongFiveys5Int64VAA0fG0VF")]
    private static extern long SumLongFive(LongFive v);

    [Fact]
    public static void EmptyValuesVanishFromTheSignature()
    {
        // The zero-sized argument is dropped: a and b land in x0/x1.
        Assert.Equal(30, TakeEmptyBetween(10, default, 20));

        // A struct made only of empties is itself zero-sized.
        Assert.Equal(21, TakeOnlyEmpties(default, 7));
    }

    [Fact]
    public static void ZeroSizedFieldsContributeNothing()
    {
        Assert.Equal(43, SumWrapsEmpty(new WrapsEmpty { X = 42 }));
    }

    [Fact]
    public static void TrailingPaddingIsDropped()
    {
        // Size=16 with one long: the trailing 8 bytes are gap-only, so the
        // lowering is identical to a plain Int64 argument.
        Assert.Equal(16, Unsafe.SizeOf<PaddedLong>());
        Assert.Equal(6, SumWrapsEmptyPadded(new PaddedLong { X = 5 }));
    }

    [Fact]
    public static void InteriorGapsSplitTypedElements()
    {
        var v = new GappedInts { A = 1000, B = 2345 };
        Assert.Equal(3345, SumGappedInts(v)); // a -> w0, b -> w1
    }

    [Fact]
    public static void MisalignedDoubleLowersOpaquely()
    {
        var v = new MisalignedDouble { I = 2, D = 0.75 };
        Assert.Equal(2.75, SumMisalignedDouble(v));
    }

    [Fact]
    public static void InlineArrayOfFloatsUsesFloatRegisters()
    {
        var quad = new FloatQuad();
        quad[0] = 1.5f;
        quad[1] = 2.5f;
        quad[2] = 3.5f;
        quad[3] = 4.5f;
        Assert.Equal(12f, SumFloatQuad(quad));
    }

    [Fact]
    public static void InlineArrayRoundTripsAsReturn()
    {
        FloatQuad quad = MakeFloatQuad(1f, 2f, 3f, 4f);
        Assert.Equal(1f, quad[0]);
        Assert.Equal(2f, quad[1]);
        Assert.Equal(3f, quad[2]);
        Assert.Equal(4f, quad[3]);
    }

    [Fact]
    public static void InlineArrayOfBytesLowersOpaquely()
    {
        var triple = new ByteTriple();
        triple[0] = 11;
        triple[1] = 22;
        triple[2] = 33;
        Assert.Equal(66, SumByteTriple(triple));
    }

    [Fact]
    public static void InlineArrayOverCapPassesByReference()
    {
        var five = new LongFive();
        for (int i = 0; i < 5; i++)
            five[i] = (i + 1) * 100;
        Assert.Equal(1500, SumLongFive(five));
    }
}
