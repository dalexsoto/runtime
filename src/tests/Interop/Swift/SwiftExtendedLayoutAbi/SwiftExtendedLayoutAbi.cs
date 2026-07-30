// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// End-to-end ABI proof for ExtendedLayoutKind.SwiftStruct (roadmap Phase 5
// "P/Invoke argument and return tests for extended layouts"): the managed
// mirrors are byte-identical to the Swift fixture because the trailing
// field tail-packs to offset 9 — a shape C# sequential layout cannot
// express — so by-value argument and return round-trips must observe every
// field, including the tail-packed one.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftExtendedLayoutAbi
{
    private const string SwiftLib = "libSwiftExtendedLayoutAbi.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftExtendedLayoutAbi8sumOuterys5Int64VAA0F0VF")]
    private static extern long SumOuter(ExtOuter v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftExtendedLayoutAbi9makeOuter1a1b1cAA0F0Vs5Int64V_s4Int8VAKtF")]
    private static extern ExtOuter MakeOuter(long a, sbyte b, sbyte c);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftExtendedLayoutAbi11outerFieldCys5Int64VAA5OuterVF")]
    private static extern long OuterFieldC(ExtOuter v);

    [StructLayout(LayoutKind.Sequential)]
    private struct MetadataResponse
    {
        public IntPtr Metadata;
        public nint State;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftExtendedLayoutAbi5OuterVMa")]
    private static extern MetadataResponse OuterMa(long request);

    [Fact]
    public static void SwiftMetadataAgreesWithManagedLayout()
    {
        // Differential against the compiler's own value witness table: the
        // managed instance size must equal Swift's stride, and Swift's
        // unpadded size and alignment must match the extended-layout rules.
        MetadataResponse response = OuterMa(0);
        Assert.Equal(0, (long)response.State);
        void** vwt = *(void***)((byte*)response.Metadata - sizeof(nint));
        nuint size = *(nuint*)((byte*)vwt + 8 * sizeof(nint));
        nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));
        uint flags = *(uint*)((byte*)vwt + 10 * sizeof(nint));

        Assert.Equal(10u, (uint)size);    // unpadded Swift size (tail-packed)
        Assert.Equal(16u, (uint)stride);  // managed instance size below
        Assert.Equal((uint)Unsafe.SizeOf<ExtOuter>(), (uint)stride);
        Assert.Equal(8u, (flags & 0xFF) + 1); // alignment
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftExtendedLayoutAbi13sumGPairInnerys5Int64VAA0F0VyAA0G0VGF")]
    private static extern long SumGPairInner(ExtGPairInner p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftExtendedLayoutAbi14makeGPairInneryAA0F0VyAA0G0VGs5Int64V_s4Int8VAKtF")]
    private static extern ExtGPairInner MakeGPairInner(long a, sbyte b, sbyte c);

    [Fact]
    public static void FrozenGenericInstantiationProjectsDirectly()
    {
        // GPair<Inner>: the frozen generic instantiation tail-packs Second
        // to offset 9, and the SwiftStruct mirror composes identically.
        var v = default(ExtGPairInner);
        Assert.Equal(16, Unsafe.SizeOf<ExtGPairInner>());
        Assert.Equal(9, (int)Unsafe.ByteOffset(ref Unsafe.As<ExtGPairInner, byte>(ref v), ref Unsafe.As<sbyte, byte>(ref v.Second)));

        v.First.A = 500;
        v.First.B = 3;
        v.Second = 8;
        Assert.Equal(511, SumGPairInner(v));

        ExtGPairInner made = MakeGPairInner(1000, 2, 4);
        Assert.Equal(1000, made.First.A);
        Assert.Equal(2, made.First.B);
        Assert.Equal(4, made.Second);
    }

    [Fact]
    public static void ManagedLayoutMatchesSwift()
    {
        var v = default(ExtOuter);
        Assert.Equal(16, Unsafe.SizeOf<ExtOuter>());
        Assert.Equal(9, (int)Unsafe.ByteOffset(ref Unsafe.As<ExtOuter, byte>(ref v), ref Unsafe.As<sbyte, byte>(ref v.C)));
    }

    [Fact]
    public static void TailPackedArgumentRoundTrips()
    {
        var v = default(ExtOuter);
        v.Inner.A = 1000;
        v.Inner.B = 7;
        v.C = 25;

        Assert.Equal(1032, SumOuter(v));
        Assert.Equal(25, OuterFieldC(v)); // the tail-packed byte specifically
    }

    [Fact]
    public static void TailPackedReturnRoundTrips()
    {
        ExtOuter v = MakeOuter(123456789, 11, 22);
        Assert.Equal(123456789, v.Inner.A);
        Assert.Equal(11, v.Inner.B);
        Assert.Equal(22, v.C);
        Assert.Equal(123456789 + 11 + 22, SumOuter(v));
    }
}
