// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Numerics;
using Xunit;
using TestLibrary;

[PlatformSpecific(TestPlatforms.AnyApple)]
public class InvalidCallingConvTests
{
    // Dummy class with a dummy attribute
    public class StringClass
    {
        public string value { get; set; }
    }
    private const string SwiftLib = "libSwiftInvalidCallConv.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftInvalidCallConv10simpleFuncyyF")]
    public static extern void FuncWithTwoSelfParameters(SwiftSelf self1, SwiftSelf self2);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftInvalidCallConv10simpleFuncyyF")]
    public static extern void FuncWithTwoErrorParameters(ref SwiftError error1, ref SwiftError error2);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftInvalidCallConv10simpleFuncyyF")]
    public static extern void FuncWithMixedParameters(SwiftSelf self1, SwiftSelf self2, ref SwiftError error1, ref SwiftError error2);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftInvalidCallConv10simpleFuncyyF")]
    public static extern void FuncWithSwiftErrorAsArg(SwiftError error1);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftInvalidCallConv10simpleFuncyyF")]
    public static extern void FuncWithNonPrimitiveArg(StringClass arg1);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftInvalidCallConv10simpleFuncyyF")]
    public static extern void FuncWithSIMDArg(Vector4 vec);

    [Fact]
    public static void TestFuncWithTwoSelfParameters()
    {
        // Invalid due to multiple SwiftSelf arguments.
        SwiftSelf self = new SwiftSelf();
        Assert.Throws<InvalidProgramException>(() => FuncWithTwoSelfParameters(self, self));
    }

    [Fact]
    public static void TestFuncWithTwoErrorParameters()
    {
        // Invalid due to multiple SwiftError arguments.
        SwiftError error = new SwiftError();
        Assert.Throws<InvalidProgramException>(() => FuncWithTwoErrorParameters(ref error, ref error));
    }

    [Fact]
    public static void TestFuncWithMixedParameters()
    {
        // Invalid due to multiple SwiftSelf/SwiftError arguments.
        SwiftSelf self = new SwiftSelf();
        SwiftError error = new SwiftError();
        Assert.Throws<InvalidProgramException>(() => FuncWithMixedParameters(self, self, ref error, ref error));
    }

    [Fact]
    public static void TestFuncWithSwiftErrorAsArg()
    {
        // Invalid due to SwiftError not passed as a pointer.
        SwiftError error = new SwiftError();
        Assert.Throws<InvalidProgramException>(() => FuncWithSwiftErrorAsArg(error));
    }

    [Fact]
    public static void TestFuncWithNonPrimitiveArg()
    {
        // Invalid due to a non-primitive argument.
        StringClass arg1 = new StringClass();
        arg1.value = "fail";
        Exception ex = Assert.ThrowsAny<Exception>(() => FuncWithNonPrimitiveArg(arg1));
        Assert.True(
            ex is InvalidProgramException or MarshalDirectiveException or PlatformNotSupportedException,
            $"Unexpected exception type: {ex.GetType().FullName}");
    }

    [Fact]
    public static void TestFuncWithSIMDArg()
    {
        // Invalid due to a SIMD argument.
        Vector4 vec = new Vector4(); // Using Vector4 as it is a SIMD type across all architectures for Mono
        Assert.Throws<InvalidProgramException>(() => FuncWithSIMDArg(vec));
    }

    public struct SimpleStruct
    {
        public long Value;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftInvalidCallConv10simpleFuncyyF")]
    public static extern void FuncWithSelfTNotLast(SwiftSelf<SimpleStruct> self, int x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftInvalidCallConv10simpleFuncyyF")]
    public static extern void FuncWithDupSelfAndSelfT(SwiftSelf self, SwiftSelf<SimpleStruct> selfT);

    [Fact]
    public static void TestFuncWithSelfTNotLast()
    {
        // Invalid because SwiftSelf<T> must be the last argument in the signature.
        Assert.Throws<InvalidProgramException>(() => FuncWithSelfTNotLast(new SwiftSelf<SimpleStruct>(), 0));
    }

    [Fact]
    public static void TestFuncWithDupSelfAndSelfT()
    {
        // Invalid due to both SwiftSelf and SwiftSelf<T> being present.
        Assert.Throws<InvalidProgramException>(() => FuncWithDupSelfAndSelfT(new SwiftSelf(), new SwiftSelf<SimpleStruct>()));
    }

    // Reverse P/Invoke validation: compiling an UnmanagedCallersOnly method with an
    // invalid Swift signature must throw InvalidProgramException before any native
    // caller can reach it.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static void ReverseFuncWithSelfTNotLast(SwiftSelf<SimpleStruct> self, int x) { }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static void ReverseFuncWithDupSelfAndSelfT(SwiftSelf self, SwiftSelf<SimpleStruct> selfT) { }

    private static void PrepareReverseMethod(string name)
    {
        MethodInfo method = typeof(InvalidCallingConvTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        RuntimeHelpers.PrepareMethod(method.MethodHandle);
    }

    [Fact]
    public static void TestReverseFuncWithSelfTNotLast()
    {
        // The CoreCLR interpreter validates reverse signatures when the native
        // entry stub is created, which PrepareMethod does not exercise.
        if (TestLibrary.Utilities.IsCoreClrInterpreter)
            return;

        // Invalid because SwiftSelf<T> must be the last argument in the signature.
        Assert.Throws<InvalidProgramException>(() => PrepareReverseMethod(nameof(ReverseFuncWithSelfTNotLast)));
    }

    [Fact]
    public static void TestReverseFuncWithDupSelfAndSelfT()
    {
        if (TestLibrary.Utilities.IsCoreClrInterpreter)
            return;

        // Invalid due to both SwiftSelf and SwiftSelf<T> being present.
        Assert.Throws<InvalidProgramException>(() => PrepareReverseMethod(nameof(ReverseFuncWithDupSelfAndSelfT)));
    }
}
