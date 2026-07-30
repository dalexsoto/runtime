// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Threading;
using Xunit;
using TestLibrary;

[PlatformSpecific(TestPlatforms.AnyApple)]
public unsafe class ReentrancyTests
{
    private const string SwiftLib = "libSwiftReentrancy.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s15SwiftReentrancy5funcA1fs5Int64VA2EXE_tF")]
    public static extern long FuncA(delegate* unmanaged[Swift]<long, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s15SwiftReentrancy5funcB1gs5Int64VA2EXE_tF")]
    public static extern long FuncB(delegate* unmanaged[Swift]<long, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s15SwiftReentrancy9swiftLeaf1xs5Int64VAE_tF")]
    public static extern long SwiftLeaf(long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s15SwiftReentrancy7recurse1n1fs5Int64VAF_A2FXEtF")]
    public static extern long Recurse(long n, delegate* unmanaged[Swift]<long, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s15SwiftReentrancy14invokeCallback1x1fs5Int64VAF_A2FXEtF")]
    public static extern long InvokeCallback(long x, delegate* unmanaged[Swift]<long, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s15SwiftReentrancy05throwB5Error4codes5Int64VAE_tKF")]
    public static extern long ThrowReentrancyError(long code, ref SwiftError error);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s15SwiftReentrancy19middleObservesError1x1fs5Int64VAF_A2FKXEtF")]
    public static extern long MiddleObservesError(long x, delegate* unmanaged[Swift]<long, SwiftError*, long> func, void* funcContext);

    // Three-level nesting: managed -> funcA -> OuterCallback -> funcB ->
    // InnerCallback -> swiftLeaf. The composed arithmetic proves every level
    // ran: swiftLeaf(20) = 60, InnerCallback = 160, funcB = 162,
    // OuterCallback = 172, funcA = 173.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long InnerCallback(long y)
    {
        return SwiftLeaf(y) + 100;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long OuterCallback(long x)
    {
        return FuncB(&InnerCallback, null) + x;
    }

    [Fact]
    public static void TestNestedCallbacksThreeLevels()
    {
        long result = FuncA(&OuterCallback, null);
        Assert.Equal(173, result);
    }

    // Recursion through the interop boundary: recurse(n) calls the managed
    // callback with n - 1, and the managed callback calls recurse again, so
    // every level crosses the boundary in both directions.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long RecursiveCallback(long n)
    {
        return Recurse(n, &RecursiveCallback, null);
    }

    [Fact]
    public static void TestRecursionThroughBoundary()
    {
        // recurse accumulates n + f(n - 1), so a depth of 100 sums 1..100.
        long result = Recurse(100, &RecursiveCallback, null);
        Assert.Equal(5050, result);
    }

    // Reentrancy with managed state: the callback increments a managed static
    // counter, records the observed ordering, and re-enters Swift until the
    // counter reaches five.

    private static int s_counter;
    private static long s_orderTrace;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long CountingCallback(long x)
    {
        s_counter++;
        s_orderTrace = s_orderTrace * 10 + s_counter;
        if (s_counter < 5)
        {
            return InvokeCallback(x + 1, &CountingCallback, null) + 1;
        }
        return x;
    }

    [Fact]
    public static void TestReentrancyWithManagedState()
    {
        s_counter = 0;
        s_orderTrace = 0;

        // Innermost invocation observes x = 5 and returns it; each of the four
        // outer invocations adds one on the way out.
        long result = InvokeCallback(1, &CountingCallback, null);
        Assert.Equal(9, result);
        Assert.Equal(5, s_counter);
        // Each callback invocation appended the counter value it observed, so
        // the trace proves the invocations ran in order.
        Assert.Equal(12345, s_orderTrace);
    }

    // Concurrent reentrancy: multiple threads run the three-level nesting in
    // parallel, exercising thread safety of the reverse-entry paths.

    [Fact]
    public static void TestConcurrentNestedCallbacks()
    {
        const int ThreadCount = 4;
        const int Iterations = 100;

        long[] totals = new long[ThreadCount];
        Thread[] threads = new Thread[ThreadCount];
        for (int t = 0; t < ThreadCount; t++)
        {
            int index = t;
            threads[index] = new Thread(() =>
            {
                long total = 0;
                for (int i = 0; i < Iterations; i++)
                {
                    total += FuncA(&OuterCallback, null);
                }
                totals[index] = total;
            });
            threads[index].Start();
        }

        long grandTotal = 0;
        for (int t = 0; t < ThreadCount; t++)
        {
            threads[t].Join();
            Assert.Equal(173 * Iterations, totals[t]);
            grandTotal += totals[t];
        }

        Assert.Equal(173 * Iterations * ThreadCount, grandTotal);
    }

    // Nested callback where the inner frame uses SwiftError: the inner managed
    // callback obtains a genuine Swift error box (by calling a throwing Swift
    // function) and propagates it through the error register. The middle Swift
    // layer observes the error with try/catch and propagates a flag value to
    // the outermost managed caller.

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long InnerErrorCallback(long x, SwiftError* error)
    {
        if (x >= 0)
        {
            SwiftError innerError = new SwiftError();
            ThrowReentrancyError(x + 35, ref innerError);
            *error = innerError;
            return 0;
        }

        *error = new SwiftError(null);
        return x * -100;
    }

    [Fact]
    public static void TestNestedCallbackWithInnerError()
    {
        // The callback rethrows ReentrancyError.failure(code: 7 + 35), which
        // the Swift middle layer catches and reports as 1000 + code.
        long thrown = MiddleObservesError(7, &InnerErrorCallback, null);
        Assert.Equal(1042, thrown);

        // With a negative input the callback leaves the error register empty
        // and its plain return value flows back out.
        long notThrown = MiddleObservesError(-3, &InnerErrorCallback, null);
        Assert.Equal(300, notThrown);
    }
}
