// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Runtime.Intrinsics;
using Xunit;
using TestLibrary;

[PlatformSpecific(TestPlatforms.AnyApple)]
public unsafe class SwiftSimdAbi
{
    private const string SwiftLib = "libSwiftSimdAbi.dylib";

    // Only Vector64<T>/Vector128<T> have a specified Swift lowering; see
    // docs/design/interop/swift/lowering.md "SIMD status". The CoreCLR
    // interpreter deterministically rejects vector values in Swift
    // signatures, so under the interpreter every forward test asserts the
    // rejection instead of the result.
    private static void RunOrExpectInterpreterRejection(Action test)
    {
        if (TestLibrary.Utilities.IsCoreClrInterpreter)
        {
            Assert.Throws<InvalidProgramException>(test);
        }
        else
        {
            test();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VecPair
    {
        public Vector128<float> V;
        public long X;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TwoVec
    {
        public Vector128<double> A;
        public Vector128<double> B;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FourVec
    {
        public Vector128<float> C0;
        public Vector128<float> C1;
        public Vector128<float> C2;
        public Vector128<float> C3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FiveVec
    {
        public Vector128<float> C0;
        public Vector128<float> C1;
        public Vector128<float> C2;
        public Vector128<float> C3;
        public Vector128<float> C4;
    }

    [Fact]
    public static void ManagedLayoutMatchesSwift()
    {
        // The lowering consumes managed field offsets; these must match the
        // Swift fixtures (lowering.md precondition 3). Note managed
        // Unsafe.SizeOf reports the aligned size (Swift's stride, 32), while
        // Swift's unpadded size is 24 — the lowering is offset-based, so the
        // ABI-relevant facts are the field offsets.
        Assert.Equal(32, Unsafe.SizeOf<VecPair>());
        Assert.Equal(16, (int)Marshal.OffsetOf<VecPair>(nameof(VecPair.X)));
        Assert.Equal(32, Unsafe.SizeOf<TwoVec>());
        Assert.Equal(64, Unsafe.SizeOf<FourVec>());
        Assert.Equal(80, Unsafe.SizeOf<FiveVec>());
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi13sumSimd4FloatySfs5SIMD4VySfGF")]
    private static extern float SumSimd4Float(Vector128<float> v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi13sumSimd2FloatySfs5SIMD2VySfGF")]
    private static extern float SumSimd2Float(Vector64<float> v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi14sumSimd2DoubleySds5SIMD2VySdGF")]
    private static extern double SumSimd2Double(Vector128<double> v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi13sumSimd4Int32ys0F0Vs5SIMD4VyADGF")]
    private static extern int SumSimd4Int32(Vector128<int> v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi14makeSimd4Floatys5SIMD4VySfGSf_S3ftF")]
    private static extern Vector128<float> MakeSimd4Float(float a, float b, float c, float d);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi10sumVecPairySfAA0eF0VF")]
    private static extern float SumVecPair(VecPair p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi11makeVecPairAA0eF0VyF")]
    private static extern VecPair MakeVecPair();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi9sumTwoVecySdAA0eF0VF")]
    private static extern double SumTwoVec(TwoVec t);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi10sumFourVecySfAA0eF0VF")]
    private static extern float SumFourVec(FourVec m);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi10sumFiveVecySfAA0eF0VF")]
    private static extern float SumFiveVec(FiveVec m);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi03sumB3MixySdSf_s5SIMD4VySfGSds5SIMD2VySdGtF")]
    private static extern double SumSimdMix(float a, Vector128<float> v, double b, Vector128<double> w);

    [Fact]
    public static void TestVectorArguments()
    {
        RunOrExpectInterpreterRejection(() =>
        {
            Assert.Equal(10f, SumSimd4Float(Vector128.Create(1f, 2f, 3f, 4f)));
            Assert.Equal(7f, SumSimd2Float(Vector64.Create(3f, 4f)));
            Assert.Equal(11.5, SumSimd2Double(Vector128.Create(5.25, 6.25)));
            Assert.Equal(110, SumSimd4Int32(Vector128.Create(10, 20, 30, 50)));
        });
    }

    [Fact]
    public static void TestVectorReturn()
    {
        RunOrExpectInterpreterRejection(() =>
        {
            Vector128<float> v = MakeSimd4Float(1f, 2f, 3f, 4f);
            Assert.Equal(Vector128.Create(1f, 2f, 3f, 4f), v);
        });
    }

    [Fact]
    public static void TestVectorInStruct()
    {
        RunOrExpectInterpreterRejection(() =>
        {
            var p = new VecPair { V = Vector128.Create(1f, 2f, 3f, 4f), X = 100 };
            Assert.Equal(110f, SumVecPair(p));

            var t = new TwoVec { A = Vector128.Create(1.5, 2.5), B = Vector128.Create(3.5, 4.5) };
            Assert.Equal(12.0, SumTwoVec(t));
        });
    }

    [Fact]
    public static void TestVectorStructReturn()
    {
        RunOrExpectInterpreterRejection(() =>
        {
            VecPair p = MakeVecPair();
            Assert.Equal(Vector128.Create(1.5f, 2.5f, 3.5f, 4.5f), p.V);
            Assert.Equal(42, p.X);
        });
    }

    [Fact]
    public static void TestVectorStructAtCap()
    {
        RunOrExpectInterpreterRejection(() =>
        {
            // Four 16-byte vector chunks: exactly at the 4-element cap, passed
            // directly in v0-v3 (the simd_float4x4 shape).
            var m = new FourVec
            {
                C0 = Vector128.Create(1f, 2f, 3f, 4f),
                C1 = Vector128.Create(5f, 6f, 7f, 8f),
                C2 = Vector128.Create(9f, 10f, 11f, 12f),
                C3 = Vector128.Create(13f, 14f, 15f, 16f),
            };
            Assert.Equal(136f, SumFourVec(m));
        });
    }

    [Fact]
    public static void TestVectorStructOverCap()
    {
        // Five vector chunks exceed the cap: passed by reference. No vector
        // registers are involved, so this works in every mode including the
        // CoreCLR interpreter.
        var m = new FiveVec
        {
            C0 = Vector128.Create(1f, 2f, 3f, 4f),
            C1 = Vector128.Create(5f, 6f, 7f, 8f),
            C2 = Vector128.Create(9f, 10f, 11f, 12f),
            C3 = Vector128.Create(13f, 14f, 15f, 16f),
            C4 = Vector128.Create(17f, 18f, 19f, 20f),
        };
        Assert.Equal(210f, SumFiveVec(m));
    }

    [Fact]
    public static void TestMixedScalarAndVector()
    {
        RunOrExpectInterpreterRejection(() =>
        {
            // Scalar FP values and vectors share the sequential V-register
            // file: s0, v1, d2, v3.
            double r = SumSimdMix(1f, Vector128.Create(2f, 3f, 4f, 5f), 6.0, Vector128.Create(7.0, 8.0));
            Assert.Equal(36.0, r);
        });
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s12SwiftSimdAbi06invokeB8Callback1fS2fs5SIMD4VySfG_s5Int64VtXE_tF")]
    private static extern float InvokeSimdCallback(delegate* unmanaged[Swift]<Vector128<float>, long, SwiftSelf, float> func, void* funcContext);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static float SimdCallback(Vector128<float> v, long x, SwiftSelf self)
    {
        return v[0] + v[1] + v[2] + v[3] + x;
    }

    [Fact]
    public static void TestReverseVectorCallback()
    {
        // Under the CoreCLR interpreter the reverse stub rejection would
        // surface on the native side of the boundary, so skip there.
        if (TestLibrary.Utilities.IsCoreClrInterpreter)
            return;

        Assert.Equal(105f, InvokeSimdCallback(&SimdCallback, null));
    }
}
