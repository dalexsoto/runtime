// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.InteropServices.Swift;

// Swift interop on the iOS simulator (roadmap Phase 0/5 simulator half):
// CallConvSwift P/Invokes into a Swift dylib built for the
// arm64-apple-ios-simulator triple — frozen-struct lowering and the self
// register — running under the CoreCLR interpreter and R2R lanes that the
// sibling functional tests already establish. No signing required.
public struct Pair
{
    public long A;
    public long B;
}

public static unsafe class Program
{
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture7addPairys5Int64VAA0D0VF")]
    private static extern long AddPair(Pair p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture7selfSumys5Int64VAD_SVtF")]
    private static extern long SelfSum(long x, void* ctx);

    // Extended-layout (nested tail-packing) across the simulator ABI: Swift
    // packs Outer.c into Inner's trailing padding (stride 16, c at offset 9);
    // the managed mirrors carry ExtendedLayoutKind.SwiftStruct.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture8sumOuterys5Int64VAA0D0VF")]
    private static extern long SumOuter(ExtOuter v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture9makeOuter1a1b1cAA0D0Vs5Int64V_s4Int8VAKtF")]
    private static extern ExtOuter MakeOuter(long a, sbyte b, sbyte c);

    private static int ExtendedLayout()
    {
        // Managed layout must match Swift's: size 16, C tail-packed at 9.
        int size = Unsafe.SizeOf<ExtOuter>();
        ExtOuter probe = default;
        long cOffset = (long)Unsafe.ByteOffset(ref Unsafe.As<ExtOuter, byte>(ref probe),
            ref Unsafe.As<sbyte, byte>(ref probe.C));
        Console.WriteLine($"SIMLANE extOuter size = {size}, C offset = {cOffset}");
        if (size != 16 || cOffset != 9)
        {
            Console.WriteLine("SIMLANE FAILED (layout)");
            return 1;
        }

        // And the value must survive a Swift round trip by value.
        ExtOuter v = MakeOuter(100, 7, 3);
        long total = SumOuter(v);
        Console.WriteLine($"SIMLANE sumOuter = {total}");
        if (v.Inner.A != 100 || v.Inner.B != 7 || v.C != 3 || total != 110)
        {
            Console.WriteLine("SIMLANE FAILED (extended layout round trip)");
            return 1;
        }
        return 0;
    }

    // @MainActor across the app host: a console host HANGS here forever
    // (abi-model.md "@MainActor async calls require a main-queue pump"); an app
    // host runs a main run loop, which drains the main queue. This test proves
    // that claim on a real app host instead of asserting it.
    [DllImport("SwiftFixture", EntryPoint = "fixture_call_mainactor")]
    private static extern void CallMainActor(void* context,
        delegate* unmanaged<void*, long, void> callback);

    private static long s_mainActorResult;
    private static int s_mainActorFired;

    [UnmanagedCallersOnly]
    private static void OnMainActorDone(void* context, long value)
    {
        s_mainActorResult = value;
        Interlocked.Exchange(ref s_mainActorFired, 1);
    }

    private static int MainActorOnAppHost()
    {
        CallMainActor(null, &OnMainActorDone);

        // Yield to the app's run loop (already running on the main thread) and
        // wait for the main actor's work to land.
        for (int i = 0; i < 100 && Volatile.Read(ref s_mainActorFired) == 0; i++)
        {
            Thread.Sleep(50);
        }

        if (Volatile.Read(ref s_mainActorFired) != 1 || s_mainActorResult != 42)
        {
            Console.WriteLine("SIMLANE FAILED (@MainActor did not complete on the app host)");
            return 1;
        }

        Console.WriteLine($"SIMLANE mainActor = {s_mainActorResult} (app host pumps the main queue)");
        return 0;
    }

    public static int Main()
    {
        long sum = AddPair(new Pair { A = 40, B = 2 });
        Console.WriteLine($"SIMLANE addPair = {sum}");
        if (sum != 42)
        {
            Console.WriteLine("SIMLANE FAILED");
            return 1;
        }

        long ctxValue = 100;
        long selfSum = SelfSum(11, &ctxValue);
        Console.WriteLine($"SIMLANE selfSum = {selfSum}");
        if (selfSum != 111)
        {
            Console.WriteLine("SIMLANE FAILED");
            return 1;
        }

        if (ExtendedLayout() != 0)
        {
            return 1;
        }

        if (MainActorOnAppHost() != 0)
        {
            return 1;
        }

        Console.WriteLine("SIMLANE PASSED");
        return 42;
    }
}
