// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Threading;

// Swift interop on a PHYSICAL iOS device (arm64-apple-ios, A17 Pro / arm64e).
// On device there is no JIT: this runs under R2R and the CoreCLR interpreter
// (and, separately, NativeAOT). The payload mirrors the desktop/simulator
// ABI coverage: frozen struct, self register, nested tail-packing, a
// @MainActor call that needs the app run loop, and a Foundation round trip
// that crosses into the arm64e shared cache.
public static unsafe class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Pair { public long A; public long B; }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture7addPairys5Int64VAA0D0VF")]
    private static extern long AddPair(Pair p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture7selfSumys5Int64VAD_SVtF")]
    private static extern long SelfSum(long x, void* ctx);

    // ExtendedLayoutKind.SwiftStruct mirrors (authored in IL).
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture8sumOuterys5Int64VAA0D0VF")]
    private static extern long SumOuter(ExtOuter v);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture9makeOuter1a1b1cAA0D0Vs5Int64V_s4Int8VAKtF")]
    private static extern ExtOuter MakeOuter(long a, sbyte b, sbyte c);

    [DllImport("SwiftFixture", EntryPoint = "fixture_call_mainactor")]
    private static extern void CallMainActor(void* context,
        delegate* unmanaged<void*, long, void> callback);

    // Swift Charts render on device: managed supplies the bar data, Swift Charts
    // rasterizes a real Chart headless via ImageRenderer, managed validates it.
    [DllImport("SwiftFixture", EntryPoint = "fixture_render_chart")]
    private static extern void RenderChart(long* values, long count, void* context,
        delegate* unmanaged<void*, int, int, int, void> callback);

    private static int s_chartW, s_chartH, s_chartNonBg, s_chartFired;

    [UnmanagedCallersOnly]
    private static void OnChartRendered(void* context, int w, int h, int nonBg)
    {
        s_chartW = w; s_chartH = h; s_chartNonBg = nonBg;
        Interlocked.Exchange(ref s_chartFired, 1);
    }

    private static int RenderChartOnDevice()
    {
        long[] values = [3, 1, 4, 1, 5, 9, 2, 6];
        Interlocked.Exchange(ref s_chartFired, 0);
        fixed (long* p = values)
        {
            RenderChart(p, values.Length, null, &OnChartRendered);
        }
        // The device app host pumps the main run loop, so the @MainActor render
        // completes while this waits (same contract the @MainActor test relies on).
        for (int i = 0; i < 200 && Volatile.Read(ref s_chartFired) == 0; i++)
        {
            Thread.Sleep(50);
        }
        if (Volatile.Read(ref s_chartFired) != 1)
        {
            return Fail("chart render never completed");
        }
        Console.WriteLine($"DEVLANE chart rendered = {s_chartW}x{s_chartH}, nonBackground = {s_chartNonBg}");
        if (s_chartW < 240 || s_chartH < 160 || s_chartNonBg < 1000)
        {
            return Fail("chart render produced no real image");
        }
        return 0;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture9uuidBytesyySpys5UInt8VGF")]
    private static extern void UuidBytes(byte* outBytes);

    private static long s_mainActorResult;
    private static int s_mainActorFired;

    [UnmanagedCallersOnly]
    private static void OnMainActorDone(void* context, long value)
    {
        s_mainActorResult = value;
        Interlocked.Exchange(ref s_mainActorFired, 1);
    }

    private static int Fail(string why)
    {
        Console.WriteLine($"DEVLANE FAILED ({why})");
        return 1;
    }

    // ---- Reverse interop (Phase 11): managed objects projected into Swift ----
    [DllImport("SwiftFixture", EntryPoint = "fixture_store_closure")]
    private static extern long StoreClosure(void* handle,
        delegate* unmanaged<long, void*, long> invoke, delegate* unmanaged<void*, void> free);
    [DllImport("SwiftFixture", EntryPoint = "fixture_call_closure")]
    private static extern long CallClosure(long index, long x);
    [DllImport("SwiftFixture", EntryPoint = "fixture_release_closures")]
    private static extern void ReleaseClosures();
    [DllImport("SwiftFixture", EntryPoint = "fixture_store_handler")]
    private static extern long StoreHandler(void* handle,
        delegate* unmanaged<long, void*, long> handleFn, delegate* unmanaged<void*, long> labelFn,
        delegate* unmanaged<void*, void> free);
    [DllImport("SwiftFixture", EntryPoint = "fixture_run_handler")]
    private static extern long RunHandler(long index, long x);
    [DllImport("SwiftFixture", EntryPoint = "fixture_release_handlers")]
    private static extern void ReleaseHandlers();
    [DllImport("SwiftFixture", EntryPoint = "fixture_store_shape")]
    private static extern long StoreShape(void* handle,
        delegate* unmanaged<void*, long> areaFn, delegate* unmanaged<void*, long> perimeterFn, delegate* unmanaged<void*, void> free);
    [DllImport("SwiftFixture", EntryPoint = "fixture_report_shape")]
    private static extern long ReportShape(long index);
    [DllImport("SwiftFixture", EntryPoint = "fixture_release_shapes")]
    private static extern void ReleaseShapes();
    [DllImport("SwiftFixture", EntryPoint = "fixture_store_producer")]
    private static extern long StoreProducer(void* handle, delegate* unmanaged<long, void*, long> produceFn, delegate* unmanaged<void*, void> free);
    [DllImport("SwiftFixture", EntryPoint = "fixture_run_producer")]
    private static extern long RunProducer(long index, long seed);
    [DllImport("SwiftFixture", EntryPoint = "fixture_release_producers")]
    private static extern void ReleaseProducers();

    private sealed class Acc { public long Sum; public void Add(long x) => Sum += x; }
    private sealed class Handler { public long Handle(long x) => x * 2 + 5; public long Label() => 42; }
    private sealed class Square { public long Area() => 9; public long Perimeter() => 12; }
    private sealed class Doubler { public long Produce(long seed) => seed * 2 + 1; }
    private static int s_revFree;

    [UnmanagedCallersOnly] private static long AreaCb(void* h) => ((Square)GCHandle.FromIntPtr((IntPtr)h).Target!).Area();
    [UnmanagedCallersOnly] private static long PerimeterCb(void* h) => ((Square)GCHandle.FromIntPtr((IntPtr)h).Target!).Perimeter();
    [UnmanagedCallersOnly] private static long ProduceCb(long seed, void* h) => ((Doubler)GCHandle.FromIntPtr((IntPtr)h).Target!).Produce(seed);

    [UnmanagedCallersOnly]
    private static long RevInvoke(long x, void* h)
    {
        var a = (Acc)GCHandle.FromIntPtr((IntPtr)h).Target!;
        a.Add(x);
        return a.Sum;
    }
    [UnmanagedCallersOnly]
    private static void RevFree(void* h) { GCHandle.FromIntPtr((IntPtr)h).Free(); Interlocked.Increment(ref s_revFree); }
    [UnmanagedCallersOnly]
    private static long RevHandle(long x, void* h) => ((Handler)GCHandle.FromIntPtr((IntPtr)h).Target!).Handle(x);
    [UnmanagedCallersOnly]
    private static long RevLabel(void* h) => ((Handler)GCHandle.FromIntPtr((IntPtr)h).Target!).Label();

    private static int ReverseInteropOnDevice()
    {
        // Closure box: managed object driven by Swift-invoked callbacks, freed
        // deterministically when Swift drops the closure.
        Interlocked.Exchange(ref s_revFree, 0);
        var acc = new Acc();
        GCHandle ga = GCHandle.Alloc(acc);
        long ci = StoreClosure((void*)GCHandle.ToIntPtr(ga), &RevInvoke, &RevFree);
        CallClosure(ci, 5);
        CallClosure(ci, 10);
        if (acc.Sum != 15) return Fail("reverse closure box did not drive managed state");
        ReleaseClosures();
        if (Volatile.Read(ref s_revFree) != 1) return Fail("reverse closure box GCHandle not freed once");

        // Protocol proxy: managed object as a Swift protocol conformer, dispatched
        // through the witness table.
        Interlocked.Exchange(ref s_revFree, 0);
        var handler = new Handler();
        GCHandle gh = GCHandle.Alloc(handler);
        long hi = StoreHandler((void*)GCHandle.ToIntPtr(gh), &RevHandle, &RevLabel, &RevFree);
        long r = RunHandler(hi, 9); // handle(9)=23 + label()=42 = 65
        if (r != 65) return Fail($"reverse protocol proxy dispatch wrong: {r}");
        ReleaseHandlers();
        if (Volatile.Read(ref s_revFree) != 1) return Fail("reverse protocol proxy GCHandle not freed once");

        // Open-class subclassing: managed overrides dispatched by Swift's vtable.
        Interlocked.Exchange(ref s_revFree, 0);
        var sq = new Square();
        GCHandle gs = GCHandle.Alloc(sq);
        long si = StoreShape((void*)GCHandle.ToIntPtr(gs), &AreaCb, &PerimeterCb, &RevFree);
        if (ReportShape(si) != 912) return Fail("reverse subclass vtable dispatch wrong"); // 9*100+12
        ReleaseShapes();
        if (Volatile.Read(ref s_revFree) != 1) return Fail("reverse subclass GCHandle not freed once");

        // Associated-type proxy: generic dispatch resolving P.Output into managed.
        Interlocked.Exchange(ref s_revFree, 0);
        var dbl = new Doubler();
        GCHandle gd = GCHandle.Alloc(dbl);
        long pi = StoreProducer((void*)GCHandle.ToIntPtr(gd), &ProduceCb, &RevFree);
        if (RunProducer(pi, 20) != 41) return Fail("reverse associated-type dispatch wrong"); // 20*2+1
        ReleaseProducers();
        if (Volatile.Read(ref s_revFree) != 1) return Fail("reverse producer GCHandle not freed once");

        Console.WriteLine("DEVLANE reverse interop = closure box + protocol proxy + subclass + assoc-type OK");
        return 0;
    }

    public static int Main()
    {
        long sum = AddPair(new Pair { A = 40, B = 2 });
        Console.WriteLine($"DEVLANE addPair = {sum}");
        if (sum != 42) return Fail("addPair");

        long ctxVal = 100;
        long selfSum = SelfSum(11, &ctxVal);
        Console.WriteLine($"DEVLANE selfSum = {selfSum}");
        if (selfSum != 111) return Fail("selfSum");

        // Extended layout: managed size 16, tail-packed C at offset 9, and a
        // by-value round trip through Swift.
        int size = Unsafe.SizeOf<ExtOuter>();
        ExtOuter probe = default;
        long cOffset = (long)Unsafe.ByteOffset(ref Unsafe.As<ExtOuter, byte>(ref probe),
            ref Unsafe.As<sbyte, byte>(ref probe.C));
        Console.WriteLine($"DEVLANE extOuter size = {size}, C offset = {cOffset}");
        if (size != 16 || cOffset != 9) return Fail("extended layout");

        ExtOuter v = MakeOuter(100, 7, 3);
        long total = SumOuter(v);
        Console.WriteLine($"DEVLANE sumOuter = {total}");
        if (v.Inner.A != 100 || v.Inner.B != 7 || v.C != 3 || total != 110)
            return Fail("extended layout round trip");

        // @MainActor: an app host runs a main run loop, so the call completes.
        CallMainActor(null, &OnMainActorDone);
        for (int i = 0; i < 100 && Volatile.Read(ref s_mainActorFired) == 0; i++)
            Thread.Sleep(50);
        if (Volatile.Read(ref s_mainActorFired) != 1 || s_mainActorResult != 42)
            return Fail("@MainActor did not complete on the device app host");
        Console.WriteLine($"DEVLANE mainActor = {s_mainActorResult} (app host pumps the main queue)");

        // arm64e boundary: a Foundation UUID's 16 bytes, non-zero.
        byte* uuid = stackalloc byte[16];
        UuidBytes(uuid);
        bool anyNonZero = false;
        for (int i = 0; i < 16; i++) if (uuid[i] != 0) anyNonZero = true;
        Console.WriteLine($"DEVLANE foundation UUID version nibble = {uuid[6] >> 4}");
        if (!anyNonZero || (uuid[6] >> 4) != 4) // RFC 4122 v4
            return Fail("Foundation UUID across the arm64e boundary");

        // Swift Charts: render a real chart to a bitmap on device.
        if (RenderChartOnDevice() != 0)
            return 1;

        // Reverse interop: managed objects projected into Swift on device.
        if (ReverseInteropOnDevice() != 0)
            return 1;

        Console.WriteLine("DEVLANE PASSED");
        return 42;
    }
}
