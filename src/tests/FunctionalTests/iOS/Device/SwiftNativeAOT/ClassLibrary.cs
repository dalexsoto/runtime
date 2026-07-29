// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;

// Swift interop under NativeAOT on a physical iOS device (arm64e). The library
// exposes a native SayHello entry the console template calls; it runs the core
// ABI shapes plus a Foundation round trip that crosses the arm64e shared-cache
// boundary, all through ILC-generated CallConvSwift marshalling.
public static unsafe class ClassLibrary
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Pair { public long A; public long B; }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture7addPairys5Int64VAA0D0VF")]
    private static extern long AddPair(Pair p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture7selfSumys5Int64VAD_SVtF")]
    private static extern long SelfSum(long x, void* ctx);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("SwiftFixture", EntryPoint = "$s12SwiftFixture9uuidBytesyySpys5UInt8VGF")]
    private static extern void UuidBytes(byte* outBytes);

    [UnmanagedCallersOnly(EntryPoint = nameof(SayHello))]
    public static int SayHello()
    {
        long sum = AddPair(new Pair { A = 40, B = 2 });
        Console.WriteLine($"AOTDEV addPair = {sum}");
        if (sum != 42) return 1;

        long ctxVal = 100;
        long selfSum = SelfSum(11, &ctxVal);
        Console.WriteLine($"AOTDEV selfSum = {selfSum}");
        if (selfSum != 111) return 2;

        // Extended layout is covered on device by the CoreCLR lane (ExtendedLayoutKind
        // IL types); the NativeAOT lane exercises core CallConvSwift + the arm64e boundary.

        byte* uuid = stackalloc byte[16];
        UuidBytes(uuid);
        bool anyNonZero = false;
        for (int i = 0; i < 16; i++) if (uuid[i] != 0) anyNonZero = true;
        Console.WriteLine($"AOTDEV foundation UUID version nibble = {uuid[6] >> 4}");
        if (!anyNonZero || (uuid[6] >> 4) != 4) return 4;

        // Reverse interop (Phase 11) under NativeAOT on device.
        if (ReverseInterop() != 0) return 5;

        Console.WriteLine("AOTDEV PASSED");
        return 42;
    }

    [DllImport("SwiftFixture", EntryPoint = "fixture_store_closure")]
    private static extern long StoreClosure(void* handle,
        delegate* unmanaged<long, void*, long> invoke, delegate* unmanaged<void*, void> free);
    [DllImport("SwiftFixture", EntryPoint = "fixture_call_closure")]
    private static extern long CallClosure(long index, long x);
    [DllImport("SwiftFixture", EntryPoint = "fixture_release_closures")]
    private static extern void ReleaseClosures();
    [DllImport("SwiftFixture", EntryPoint = "fixture_store_handler")]
    private static extern long StoreHandler(void* handle,
        delegate* unmanaged<long, void*, long> h, delegate* unmanaged<void*, long> l, delegate* unmanaged<void*, void> free);
    [DllImport("SwiftFixture", EntryPoint = "fixture_run_handler")]
    private static extern long RunHandler(long index, long x);
    [DllImport("SwiftFixture", EntryPoint = "fixture_release_handlers")]
    private static extern void ReleaseHandlers();
    [DllImport("SwiftFixture", EntryPoint = "fixture_store_shape")]
    private static extern long StoreShape(void* handle, delegate* unmanaged<void*, long> a, delegate* unmanaged<void*, long> p, delegate* unmanaged<void*, void> free);
    [DllImport("SwiftFixture", EntryPoint = "fixture_report_shape")]
    private static extern long ReportShape(long index);
    [DllImport("SwiftFixture", EntryPoint = "fixture_release_shapes")]
    private static extern void ReleaseShapes();
    [DllImport("SwiftFixture", EntryPoint = "fixture_store_producer")]
    private static extern long StoreProducer(void* handle, delegate* unmanaged<long, void*, long> f, delegate* unmanaged<void*, void> free);
    [DllImport("SwiftFixture", EntryPoint = "fixture_run_producer")]
    private static extern long RunProducer(long index, long seed);
    [DllImport("SwiftFixture", EntryPoint = "fixture_release_producers")]
    private static extern void ReleaseProducers();
    private sealed class Square { public long Area() => 9; public long Perimeter() => 12; }
    private sealed class Doubler { public long Produce(long seed) => seed * 2 + 1; }
    [UnmanagedCallersOnly] private static long AreaCb(void* h) => ((Square)GCHandle.FromIntPtr((IntPtr)h).Target!).Area();
    [UnmanagedCallersOnly] private static long PerimeterCb(void* h) => ((Square)GCHandle.FromIntPtr((IntPtr)h).Target!).Perimeter();
    [UnmanagedCallersOnly] private static long ProduceCb(long seed, void* h) => ((Doubler)GCHandle.FromIntPtr((IntPtr)h).Target!).Produce(seed);

    private sealed class Acc { public long Sum; public void Add(long x) => Sum += x; }
    private sealed class Handler { public long Handle(long x) => x * 2 + 5; public long Label() => 42; }
    private static int s_revFree;

    [UnmanagedCallersOnly]
    private static long RevInvoke(long x, void* h) { var a = (Acc)GCHandle.FromIntPtr((IntPtr)h).Target!; a.Add(x); return a.Sum; }
    [UnmanagedCallersOnly]
    private static void RevFree(void* h) { GCHandle.FromIntPtr((IntPtr)h).Free(); s_revFree++; }
    [UnmanagedCallersOnly]
    private static long RevHandle(long x, void* h) => ((Handler)GCHandle.FromIntPtr((IntPtr)h).Target!).Handle(x);
    [UnmanagedCallersOnly]
    private static long RevLabel(void* h) => ((Handler)GCHandle.FromIntPtr((IntPtr)h).Target!).Label();

    private static int ReverseInterop()
    {
        s_revFree = 0;
        var acc = new Acc();
        GCHandle ga = GCHandle.Alloc(acc);
        long ci = StoreClosure((void*)GCHandle.ToIntPtr(ga), &RevInvoke, &RevFree);
        CallClosure(ci, 5); CallClosure(ci, 10);
        if (acc.Sum != 15) return 1;
        ReleaseClosures();
        if (s_revFree != 1) return 2;

        s_revFree = 0;
        var handler = new Handler();
        GCHandle gh = GCHandle.Alloc(handler);
        long hi = StoreHandler((void*)GCHandle.ToIntPtr(gh), &RevHandle, &RevLabel, &RevFree);
        if (RunHandler(hi, 9) != 65) return 3;
        ReleaseHandlers();
        if (s_revFree != 1) return 4;

        s_revFree = 0;
        var sq = new Square(); GCHandle gs = GCHandle.Alloc(sq);
        long si = StoreShape((void*)GCHandle.ToIntPtr(gs), &AreaCb, &PerimeterCb, &RevFree);
        if (ReportShape(si) != 912) return 5;
        ReleaseShapes();
        if (s_revFree != 1) return 6;

        s_revFree = 0;
        var dbl = new Doubler(); GCHandle gd = GCHandle.Alloc(dbl);
        long pi = StoreProducer((void*)GCHandle.ToIntPtr(gd), &ProduceCb, &RevFree);
        if (RunProducer(pi, 20) != 41) return 7;
        ReleaseProducers();
        if (s_revFree != 1) return 8;

        Console.WriteLine("AOTDEV reverse interop = closure box + protocol proxy + subclass + assoc-type OK");
        return 0;
    }
}
