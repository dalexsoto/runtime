// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// The runtime accepts the SwiftSelf, SwiftError*, and SwiftIndirectResult
// special arguments at any position in a signature (only SwiftSelf<T> is
// required to be last). The specials are register-carried (self, error, and
// indirect-result registers), so declarations that differ only in the position
// of a special argument bind to the same physical Swift function. These tests
// declare the same entry points with the specials at different positions and
// assert identical behavior, in both forward and reverse directions.
[PlatformSpecific(TestPlatforms.AnyApple)]
public unsafe class SpecialArgOrderingTests
{
    private const string SwiftLib = "libSwiftSpecialArgOrdering.dylib";

    private struct LargeNonFrozenStruct
    {
        public long A;
        public long B;
        public long C;
        public long D;
        public long E;
    }

    [DllImport(SwiftLib, EntryPoint = "$s23SwiftSpecialArgOrdering0D7LibraryC11getInstanceSvyFZ")]
    public static extern void* GetInstance();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s23SwiftSpecialArgOrdering12getErrorCode4froms5Int64Vs0F0_p_tF")]
    public static extern long GetErrorCode(void* error);

    // Forward: SwiftSelf declared first, middle, and last among ordinary
    // arguments, all bound to the same instance method.

    private const string CombineEntryPoint = "$s23SwiftSpecialArgOrdering0D7LibraryC7combine1a1b1cs5Int64VAI_A2ItFTj";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = CombineEntryPoint)]
    public static extern long CombineSelfFirst(SwiftSelf self, long a, long b, long c);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = CombineEntryPoint)]
    public static extern long CombineSelfMiddle(long a, SwiftSelf self, long b, long c);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = CombineEntryPoint)]
    public static extern long CombineSelfLast(long a, long b, long c, SwiftSelf self);

    [Fact]
    public static void TestForwardSelfPositions()
    {
        SwiftSelf self = new SwiftSelf(GetInstance());

        // combine(a: b: c:) returns seed + a * 2 + b * 3 + c * 5 with seed 1000.
        long expected = 1000 + 7 * 2 + 11 * 3 + 13 * 5;
        long first = CombineSelfFirst(self, 7, 11, 13);
        long middle = CombineSelfMiddle(7, self, 11, 13);
        long last = CombineSelfLast(7, 11, 13, self);

        Assert.Equal(expected, first);
        Assert.Equal(expected, middle);
        Assert.Equal(expected, last);
    }

    // Forward: SwiftError* declared first, middle, and last among ordinary
    // arguments, all bound to the same throwing free function.

    private const string ConditionallyThrowEntryPoint = "$s23SwiftSpecialArgOrdering18conditionallyThrow06shouldF01a1bs5Int64Vs5Int32V_A2GtKF";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = ConditionallyThrowEntryPoint)]
    public static extern long ConditionallyThrowErrorFirst(ref SwiftError error, int shouldThrow, long a, long b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = ConditionallyThrowEntryPoint)]
    public static extern long ConditionallyThrowErrorMiddle(int shouldThrow, ref SwiftError error, long a, long b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = ConditionallyThrowEntryPoint)]
    public static extern long ConditionallyThrowErrorLast(int shouldThrow, long a, long b, ref SwiftError error);

    [Fact]
    public static void TestForwardErrorPositionFirst()
    {
        SwiftError error = new SwiftError();
        long result = ConditionallyThrowErrorFirst(ref error, 0, 6, 9);
        Assert.True(error.Value == null, "No Swift error was expected to be thrown.");
        Assert.Equal(69, result);

        ConditionallyThrowErrorFirst(ref error, 1, 6, 9);
        Assert.True(error.Value != null, "A Swift error was expected to be thrown.");
        Assert.Equal(15, GetErrorCode(error.Value));
    }

    [Fact]
    public static void TestForwardErrorPositionMiddle()
    {
        SwiftError error = new SwiftError();
        long result = ConditionallyThrowErrorMiddle(0, ref error, 6, 9);
        Assert.True(error.Value == null, "No Swift error was expected to be thrown.");
        Assert.Equal(69, result);

        ConditionallyThrowErrorMiddle(1, ref error, 6, 9);
        Assert.True(error.Value != null, "A Swift error was expected to be thrown.");
        Assert.Equal(15, GetErrorCode(error.Value));
    }

    [Fact]
    public static void TestForwardErrorPositionLast()
    {
        SwiftError error = new SwiftError();
        long result = ConditionallyThrowErrorLast(0, 6, 9, ref error);
        Assert.True(error.Value == null, "No Swift error was expected to be thrown.");
        Assert.Equal(69, result);

        ConditionallyThrowErrorLast(1, 6, 9, ref error);
        Assert.True(error.Value != null, "A Swift error was expected to be thrown.");
        Assert.Equal(15, GetErrorCode(error.Value));
    }

    // Forward: SwiftIndirectResult declared first and after the ordinary
    // arguments; the marker always binds to the indirect-result register.

    private const string MakeLargeStructEntryPoint = "$s23SwiftSpecialArgOrdering15makeLargeStruct4base5scaleAA0f9NonFrozenG0Vs5Int64V_AHtF";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = MakeLargeStructEntryPoint)]
    public static extern void MakeLargeStructResultFirst(SwiftIndirectResult result, long baseValue, long scale);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = MakeLargeStructEntryPoint)]
    public static extern void MakeLargeStructResultLast(long baseValue, long scale, SwiftIndirectResult result);

    [Fact]
    public static void TestForwardIndirectResultPositions()
    {
        LargeNonFrozenStruct viaFirst;
        MakeLargeStructResultFirst(new SwiftIndirectResult(&viaFirst), 100, 10);

        LargeNonFrozenStruct viaLast;
        MakeLargeStructResultLast(100, 10, new SwiftIndirectResult(&viaLast));

        Assert.Equal(100, viaFirst.A);
        Assert.Equal(110, viaFirst.B);
        Assert.Equal(120, viaFirst.C);
        Assert.Equal(130, viaFirst.D);
        Assert.Equal(140, viaFirst.E);

        Assert.Equal(viaFirst.A, viaLast.A);
        Assert.Equal(viaFirst.B, viaLast.B);
        Assert.Equal(viaFirst.C, viaLast.C);
        Assert.Equal(viaFirst.D, viaLast.D);
        Assert.Equal(viaFirst.E, viaLast.E);
    }

    // Forward: SwiftSelf and SwiftError* together in both relative orders on a
    // throwing instance method.

    private const string MethodThrowEntryPoint = "$s23SwiftSpecialArgOrdering0D7LibraryC18conditionallyThrow06shouldG05values5Int64Vs5Int32V_AHtKFTj";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = MethodThrowEntryPoint)]
    public static extern long MethodThrowErrorBeforeSelf(ref SwiftError error, int shouldThrow, long value, SwiftSelf self);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = MethodThrowEntryPoint)]
    public static extern long MethodThrowSelfBeforeError(SwiftSelf self, int shouldThrow, long value, ref SwiftError error);

    [Fact]
    public static void TestForwardErrorBeforeSelf()
    {
        SwiftSelf self = new SwiftSelf(GetInstance());
        SwiftError error = new SwiftError();

        long result = MethodThrowErrorBeforeSelf(ref error, 0, 23, self);
        Assert.True(error.Value == null, "No Swift error was expected to be thrown.");
        Assert.Equal(1023, result);

        MethodThrowErrorBeforeSelf(ref error, 1, 77, self);
        Assert.True(error.Value != null, "A Swift error was expected to be thrown.");
        Assert.Equal(77, GetErrorCode(error.Value));
    }

    [Fact]
    public static void TestForwardSelfBeforeError()
    {
        SwiftSelf self = new SwiftSelf(GetInstance());
        SwiftError error = new SwiftError();

        long result = MethodThrowSelfBeforeError(self, 0, 23, ref error);
        Assert.True(error.Value == null, "No Swift error was expected to be thrown.");
        Assert.Equal(1023, result);

        MethodThrowSelfBeforeError(self, 1, 77, ref error);
        Assert.True(error.Value != null, "A Swift error was expected to be thrown.");
        Assert.Equal(77, GetErrorCode(error.Value));
    }

    // Forward: SwiftIndirectResult, SwiftSelf, and SwiftError* all present, in
    // several permutations, on a throwing instance method that returns a
    // non-frozen struct.

    private const string MakeOrThrowEntryPoint = "$s23SwiftSpecialArgOrdering0D7LibraryC22makeOrThrowLargeStruct06shouldH04baseAA0i9NonFrozenJ0Vs5Int32V_s5Int64VtKFTj";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = MakeOrThrowEntryPoint)]
    public static extern void MakeOrThrowSpecialsLeading(SwiftIndirectResult result, SwiftSelf self, ref SwiftError error, int shouldThrow, long baseValue);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = MakeOrThrowEntryPoint)]
    public static extern void MakeOrThrowSpecialsTrailing(int shouldThrow, long baseValue, SwiftIndirectResult result, SwiftSelf self, ref SwiftError error);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = MakeOrThrowEntryPoint)]
    public static extern void MakeOrThrowSpecialsInterleaved(ref SwiftError error, int shouldThrow, SwiftIndirectResult result, long baseValue, SwiftSelf self);

    [Fact]
    public static void TestForwardAllSpecialsNoThrow()
    {
        SwiftSelf self = new SwiftSelf(GetInstance());
        SwiftError error = new SwiftError();

        // makeOrThrowLargeStruct returns (seed + base, base * 2, ..., base * 5)
        // with seed 1000 and base 9.
        LargeNonFrozenStruct viaLeading;
        MakeOrThrowSpecialsLeading(new SwiftIndirectResult(&viaLeading), self, ref error, 0, 9);
        Assert.True(error.Value == null, "No Swift error was expected to be thrown.");
        Assert.Equal(1009, viaLeading.A);
        Assert.Equal(18, viaLeading.B);
        Assert.Equal(27, viaLeading.C);
        Assert.Equal(36, viaLeading.D);
        Assert.Equal(45, viaLeading.E);

        LargeNonFrozenStruct viaTrailing;
        MakeOrThrowSpecialsTrailing(0, 9, new SwiftIndirectResult(&viaTrailing), self, ref error);
        Assert.True(error.Value == null, "No Swift error was expected to be thrown.");
        Assert.Equal(viaLeading.A, viaTrailing.A);
        Assert.Equal(viaLeading.B, viaTrailing.B);
        Assert.Equal(viaLeading.C, viaTrailing.C);
        Assert.Equal(viaLeading.D, viaTrailing.D);
        Assert.Equal(viaLeading.E, viaTrailing.E);

        LargeNonFrozenStruct viaInterleaved;
        MakeOrThrowSpecialsInterleaved(ref error, 0, new SwiftIndirectResult(&viaInterleaved), 9, self);
        Assert.True(error.Value == null, "No Swift error was expected to be thrown.");
        Assert.Equal(viaLeading.A, viaInterleaved.A);
        Assert.Equal(viaLeading.B, viaInterleaved.B);
        Assert.Equal(viaLeading.C, viaInterleaved.C);
        Assert.Equal(viaLeading.D, viaInterleaved.D);
        Assert.Equal(viaLeading.E, viaInterleaved.E);
    }

    [Fact]
    public static void TestForwardAllSpecialsThrow()
    {
        SwiftSelf self = new SwiftSelf(GetInstance());
        LargeNonFrozenStruct unused;

        SwiftError error = new SwiftError();
        MakeOrThrowSpecialsLeading(new SwiftIndirectResult(&unused), self, ref error, 1, 55);
        Assert.True(error.Value != null, "A Swift error was expected to be thrown.");
        Assert.Equal(55, GetErrorCode(error.Value));

        error = new SwiftError();
        MakeOrThrowSpecialsTrailing(1, 55, new SwiftIndirectResult(&unused), self, ref error);
        Assert.True(error.Value != null, "A Swift error was expected to be thrown.");
        Assert.Equal(55, GetErrorCode(error.Value));

        error = new SwiftError();
        MakeOrThrowSpecialsInterleaved(ref error, 1, new SwiftIndirectResult(&unused), 55, self);
        Assert.True(error.Value != null, "A Swift error was expected to be thrown.");
        Assert.Equal(55, GetErrorCode(error.Value));
    }

    // Reverse: UnmanagedCallersOnly callbacks declaring SwiftSelf first,
    // middle, and last among ordinary arguments. Swift invokes the callback as
    // a closure and always places the funcContext value in the self/context
    // register, so all three positions observe the same context.

    private const string InvokeWithContextEntryPoint = "$s23SwiftSpecialArgOrdering17invokeWithContext1a1b1fs5Int64VAG_A3G_AGtXEtF";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = InvokeWithContextEntryPoint)]
    public static extern long InvokeWithContextSelfFirst(long a, long b, delegate* unmanaged[Swift]<SwiftSelf, long, long, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = InvokeWithContextEntryPoint)]
    public static extern long InvokeWithContextSelfMiddle(long a, long b, delegate* unmanaged[Swift]<long, SwiftSelf, long, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = InvokeWithContextEntryPoint)]
    public static extern long InvokeWithContextSelfLast(long a, long b, delegate* unmanaged[Swift]<long, long, SwiftSelf, long> func, void* funcContext);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long CallbackSelfFirst(SwiftSelf self, long x, long y)
    {
        return x * 3 + y * 7 + *(long*)self.Value;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long CallbackSelfMiddle(long x, SwiftSelf self, long y)
    {
        return x * 3 + y * 7 + *(long*)self.Value;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long CallbackSelfLast(long x, long y, SwiftSelf self)
    {
        return x * 3 + y * 7 + *(long*)self.Value;
    }

    [Fact]
    public static void TestReverseSelfFirst()
    {
        long context = 10000;
        long result = InvokeWithContextSelfFirst(5, 9, &CallbackSelfFirst, &context);
        Assert.Equal(10078, result);
    }

    [Fact]
    public static void TestReverseSelfMiddle()
    {
        long context = 10000;
        long result = InvokeWithContextSelfMiddle(5, 9, &CallbackSelfMiddle, &context);
        Assert.Equal(10078, result);
    }

    [Fact]
    public static void TestReverseSelfLast()
    {
        long context = 10000;
        long result = InvokeWithContextSelfLast(5, 9, &CallbackSelfLast, &context);
        Assert.Equal(10078, result);
    }

    // Reverse: UnmanagedCallersOnly callbacks declaring SwiftError* at
    // different positions. The callback obtains a genuine Swift error box (by
    // calling a throwing Swift function) and sets it through the error
    // register; the Swift caller observes the throw with try/catch.

    private const string InvokeThrowingCallbackEntryPoint = "$s23SwiftSpecialArgOrdering22invokeThrowingCallback5value1fs5Int64VAF_A2FKXEtF";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = InvokeThrowingCallbackEntryPoint)]
    public static extern long InvokeThrowingCallbackErrorFirst(long value, delegate* unmanaged[Swift]<SwiftError*, long, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = InvokeThrowingCallbackEntryPoint)]
    public static extern long InvokeThrowingCallbackErrorLast(long value, delegate* unmanaged[Swift]<long, SwiftError*, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s23SwiftSpecialArgOrdering05throwD5Error4codes5Int64VAE_tKF")]
    public static extern long ThrowOrderingError(long code, ref SwiftError error);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long CallbackErrorFirst(SwiftError* error, long x)
    {
        if (x % 2 != 0)
        {
            SwiftError innerError = new SwiftError();
            ThrowOrderingError(x * 2, ref innerError);
            *error = innerError;
            return 0;
        }

        *error = new SwiftError(null);
        return x * 100;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long CallbackErrorLast(long x, SwiftError* error)
    {
        if (x % 2 != 0)
        {
            SwiftError innerError = new SwiftError();
            ThrowOrderingError(x * 2, ref innerError);
            *error = innerError;
            return 0;
        }

        *error = new SwiftError(null);
        return x * 100;
    }

    [Fact]
    public static void TestReverseErrorPositionFirst()
    {
        // Odd input: the callback sets OrderingError.failure(code: 21 * 2),
        // which the Swift caller catches and reports as 5000 + code.
        long thrown = InvokeThrowingCallbackErrorFirst(21, &CallbackErrorFirst, null);
        Assert.Equal(5042, thrown);

        // Even input: the callback leaves the error register empty.
        long notThrown = InvokeThrowingCallbackErrorFirst(10, &CallbackErrorFirst, null);
        Assert.Equal(1000, notThrown);
    }

    [Fact]
    public static void TestReverseErrorPositionLast()
    {
        long thrown = InvokeThrowingCallbackErrorLast(21, &CallbackErrorLast, null);
        Assert.Equal(5042, thrown);

        long notThrown = InvokeThrowingCallbackErrorLast(10, &CallbackErrorLast, null);
        Assert.Equal(1000, notThrown);
    }
}
