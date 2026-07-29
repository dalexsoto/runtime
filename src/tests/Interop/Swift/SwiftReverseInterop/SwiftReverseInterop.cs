// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Runtime.Loader;
using System.Threading;
using Xunit;

// A public type used to root a collectible AssemblyLoadContext from a GCHandle —
// the same kind of handle the Swift box holds on a projected managed object.
public sealed class AlcRootProbe { }

// Reverse interop (Phase 11): a MANAGED object projected into Swift as a closure.
// Swift invokes the managed callback (reverse call), the callback reaches managed
// state through a GCHandle carried in the closure's box, and the box's ARC deinit
// frees the GCHandle — so the managed object's lifetime is tied to the Swift
// closure's across the GC/ARC boundary.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftReverseInterop
{
    private const string Lib = "libSwiftReverseInterop.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop19storeManagedClosureys5Int64VSv_A2D_SvtXCySvXCtF")]
    private static extern long StoreManagedClosure(void* handle,
        delegate* unmanaged<long, void*, long> invoke,
        delegate* unmanaged<void*, void> free);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop10callStoredys5Int64VAD_ADtF")]
    private static extern long CallStored(long index, long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop11storedCounts5Int64VyF")]
    private static extern long StoredCount();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop16releaseAllStoredyyF")]
    private static extern void ReleaseAllStored();

    // The managed state Swift will drive through the reverse callback.
    private sealed class Accumulator
    {
        public long Sum;
        public int Calls;
        public void Add(long x) { Sum += x; Calls++; }
    }

    private static int s_freeCount;

    // Swift calls this (reverse) with the loop value and the GCHandle carried in
    // the box. It unwraps the managed object and mutates it — genuine managed
    // work driven by Swift.
    [UnmanagedCallersOnly]
    private static long Invoke(long x, void* handle)
    {
        var acc = (Accumulator)GCHandle.FromIntPtr((IntPtr)handle).Target!;
        acc.Add(x);
        return acc.Sum;
    }

    // The box's deinit calls this when Swift releases the closure: free the GC
    // root deterministically.
    [UnmanagedCallersOnly]
    private static void Free(void* handle)
    {
        GCHandle.FromIntPtr((IntPtr)handle).Free();
        Interlocked.Increment(ref s_freeCount);
    }

    [Fact]
    public static void ManagedClosureBoxDrivesManagedStateAndFreesDeterministically()
    {
        Interlocked.Exchange(ref s_freeCount, 0);
        var acc = new Accumulator();
        GCHandle gch = GCHandle.Alloc(acc);
        long idx = StoreManagedClosure((void*)GCHandle.ToIntPtr(gch), &Invoke, &Free);

        // Swift invokes the managed callback repeatedly; managed state accumulates.
        Assert.Equal(5, CallStored(idx, 5));
        Assert.Equal(15, CallStored(idx, 10));
        Assert.Equal(30, CallStored(idx, 15));
        Assert.Equal(30, acc.Sum);
        Assert.Equal(3, acc.Calls);

        // Swift still holds the closure, so the GC root is intact — nothing freed.
        Assert.Equal(0, Volatile.Read(ref s_freeCount));

        // Swift drops the closure -> box deinit -> the GCHandle is freed, once.
        ReleaseAllStored();
        Assert.Equal(0, StoredCount());
        Assert.Equal(1, Volatile.Read(ref s_freeCount));
    }

    [Fact]
    public static void MultipleBoxesAreIndependentAndEachFreedOnce()
    {
        Interlocked.Exchange(ref s_freeCount, 0);
        var a = new Accumulator();
        var b = new Accumulator();
        GCHandle ga = GCHandle.Alloc(a), gb = GCHandle.Alloc(b);

        long ia = StoreManagedClosure((void*)GCHandle.ToIntPtr(ga), &Invoke, &Free);
        long ib = StoreManagedClosure((void*)GCHandle.ToIntPtr(gb), &Invoke, &Free);

        CallStored(ia, 100);
        CallStored(ib, 7);
        CallStored(ia, 100);

        // No cross-talk: each closure drove only its own managed object.
        Assert.Equal(200, a.Sum);
        Assert.Equal(7, b.Sum);

        ReleaseAllStored();
        Assert.Equal(2, Volatile.Read(ref s_freeCount)); // both handles freed, once each
    }

    // ---- Protocol proxy: a managed object as a Swift protocol conformer ----

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop19storeManagedHandlerys5Int64VSv_A2D_SvtXCADSvXCySvXCtF")]
    private static extern long StoreManagedHandler(void* handle,
        delegate* unmanaged<long, void*, long> handleFn,
        delegate* unmanaged<void*, long> labelFn,
        delegate* unmanaged<void*, void> free);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop16runStoredHandlerys5Int64VAD_ADtF")]
    private static extern long RunStoredHandler(long index, long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop15releaseHandlersyyF")]
    private static extern void ReleaseHandlers();

    // The managed type that implements the Swift protocol Handler.
    private sealed class ManagedHandler
    {
        private readonly long _bias;
        public ManagedHandler(long bias) => _bias = bias;
        public long Handle(long x) => (x * 2) + _bias;
        public long Label() => 42;
    }

    [UnmanagedCallersOnly]
    private static long HandleCb(long x, void* h)
        => ((ManagedHandler)GCHandle.FromIntPtr((IntPtr)h).Target!).Handle(x);

    [UnmanagedCallersOnly]
    private static long LabelCb(void* h)
        => ((ManagedHandler)GCHandle.FromIntPtr((IntPtr)h).Target!).Label();

    [Fact]
    public static void ManagedObjectConformsToSwiftProtocolAndIsDispatchedByWitnessTable()
    {
        Interlocked.Exchange(ref s_freeCount, 0);
        var handler = new ManagedHandler(bias: 5);
        GCHandle gch = GCHandle.Alloc(handler);
        long idx = StoreManagedHandler((void*)GCHandle.ToIntPtr(gch), &HandleCb, &LabelCb, &Free);

        // Swift dispatches through the witness table (generic + existential) into
        // the managed methods: runStoredHandler = handle(x) + label().
        //   handle(9) = 9*2 + 5 = 23 ; label() = 42 ; total = 65.
        Assert.Equal(65, RunStoredHandler(idx, 9));
        //   handle(0) = 5 ; + 42 = 47.
        Assert.Equal(47, RunStoredHandler(idx, 0));

        Assert.Equal(0, Volatile.Read(ref s_freeCount));
        ReleaseHandlers();
        Assert.Equal(1, Volatile.Read(ref s_freeCount)); // proxy released -> box deinit -> handle freed
    }

    // ---- Managed subclass of an open Swift class (virtual override) ----

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop17storeManagedShapeys5Int64VSv_ADSvXCADSvXCySvXCtF")]
    private static extern long StoreManagedShape(void* handle,
        delegate* unmanaged<void*, long> areaFn, delegate* unmanaged<void*, long> perimeterFn,
        delegate* unmanaged<void*, void> free);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop11reportShapeys5Int64VADF")]
    private static extern long ReportShape(long index);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop11areaOfShapeys5Int64VADF")]
    private static extern long AreaOfShape(long index);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop13releaseShapesyyF")]
    private static extern void ReleaseShapes();

    // The managed subclass logic overriding the Swift class's virtual methods.
    private sealed class Square
    {
        private readonly long _side;
        public Square(long side) => _side = side;
        public long Area() => _side * _side;
        public long Perimeter() => 4 * _side;
    }

    [UnmanagedCallersOnly]
    private static long AreaCb(void* h) => ((Square)GCHandle.FromIntPtr((IntPtr)h).Target!).Area();
    [UnmanagedCallersOnly]
    private static long PerimeterCb(void* h) => ((Square)GCHandle.FromIntPtr((IntPtr)h).Target!).Perimeter();

    [Fact]
    public static void ManagedOverridesAreDispatchedBySwiftsVtable()
    {
        Interlocked.Exchange(ref s_freeCount, 0);
        var square = new Square(side: 3);
        GCHandle gch = GCHandle.Alloc(square);
        long idx = StoreManagedShape((void*)GCHandle.ToIntPtr(gch), &AreaCb, &PerimeterCb, &Free);

        // report() is a base-class `final` that calls the VIRTUAL area()/perimeter().
        // The vtable dispatch lands in the managed overrides:
        //   area = 9, perimeter = 12 -> report = 9*100 + 12 = 912.
        Assert.Equal(912, ReportShape(idx));
        // Polymorphic dispatch through the base type also reaches the override.
        Assert.Equal(9, AreaOfShape(idx));

        Assert.Equal(0, Volatile.Read(ref s_freeCount));
        ReleaseShapes();
        Assert.Equal(1, Volatile.Read(ref s_freeCount));
    }

    // ---- Associated-type proxy ----

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop20storeManagedProducerys5Int64VSv_A2D_SvtXCySvXCtF")]
    private static extern long StoreManagedProducer(void* handle,
        delegate* unmanaged<long, void*, long> produceFn, delegate* unmanaged<void*, void> free);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop11runProducerys5Int64VAD_ADtF")]
    private static extern long RunProducer(long index, long seed);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftReverseInterop16releaseProducersyyF")]
    private static extern void ReleaseProducers();

    private sealed class Doubler { public long Produce(long seed) => seed * 2 + 1; }

    [UnmanagedCallersOnly]
    private static long ProduceCb(long seed, void* h)
        => ((Doubler)GCHandle.FromIntPtr((IntPtr)h).Target!).Produce(seed);

    [Fact]
    public static void ManagedConformerBindsAProtocolAssociatedType()
    {
        Interlocked.Exchange(ref s_freeCount, 0);
        var doubler = new Doubler();
        GCHandle gch = GCHandle.Alloc(doubler);
        long idx = StoreManagedProducer((void*)GCHandle.ToIntPtr(gch), &ProduceCb, &Free);

        // The Swift generic `genericProduce<P: Producer>` resolves P.Output to
        // Int64 (the associated-type witness) and dispatches into managed:
        //   produce(20) = 20*2 + 1 = 41.
        Assert.Equal(41, RunProducer(idx, 20));
        Assert.Equal(1, RunProducer(idx, 0));

        Assert.Equal(0, Volatile.Read(ref s_freeCount));
        ReleaseProducers();
        Assert.Equal(1, Volatile.Read(ref s_freeCount));
    }

    // ---- Lifetime rooting: the basis of the collectible-context policy ----

    private static long s_rootIdx;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference StoreAndDropStrongRef()
    {
        var acc = new Accumulator();
        var weak = new WeakReference(acc);
        GCHandle gch = GCHandle.Alloc(acc);
        // After this returns, the ONLY strong root to `acc` is the GCHandle the
        // Swift box holds; the local `acc` goes out of scope.
        s_rootIdx = StoreManagedClosure((void*)GCHandle.ToIntPtr(gch), &Invoke, &Free);
        return weak;
    }

    [Fact]
    public static void SwiftHeldProjectionRootsTheManagedObjectUntilReleased()
    {
        Interlocked.Exchange(ref s_freeCount, 0);
        WeakReference weak = StoreAndDropStrongRef();

        // While Swift holds the closure, its GCHandle is a strong GC root: the
        // managed object survives collection and is still callable. This is why
        // a COLLECTIBLE AssemblyLoadContext containing this object cannot unload
        // while Swift holds the projection — the object is strongly reachable.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(weak.IsAlive, "Swift-held projection failed to root the managed object across GC");
        Assert.Equal(7, CallStored(s_rootIdx, 7)); // still valid: the callback runs

        // Releasing the closure frees the GCHandle; the object becomes
        // collectible (and a collectible ALC could then unload).
        ReleaseAllStored();
        Assert.Equal(1, Volatile.Read(ref s_freeCount));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive, "managed object was not collected after Swift released the projection");
    }

    // ---- Collectible AssemblyLoadContext policy ----
    //
    // The rooting test proves the box holds a strong GCHandle on the projected
    // object. The unloading policy is the direct consequence: a strong handle to
    // an object whose type lives in a collectible ALC keeps that ALC loaded,
    // because an ALC cannot unload while any of its objects is strongly reachable.
    // Freeing the handle (what the box's deinit does) lets the ALC unload. This
    // is CoreCLR-only — NativeAOT has no unloadable contexts, so the test is a
    // no-op there (documented policy: reverse projections from a collectible ALC
    // are unsupported under NativeAOT, which is fully static).

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference AlcWeak, GCHandle Handle) LoadCollectibleAndRootFromHandle()
    {
        var alc = new AssemblyLoadContext("swift-reverse-collectible", isCollectible: true);
        // Load a fresh copy of this assembly into the collectible context; its
        // AlcRootProbe type is a *distinct* type owned by that ALC.
        Assembly copy = alc.LoadFromAssemblyPath(typeof(AlcRootProbe).Assembly.Location);
        Type probeType = copy.GetType(typeof(AlcRootProbe).FullName!)!;
        object probe = Activator.CreateInstance(probeType)!;
        // The same kind of strong handle the Swift box holds on a projection.
        GCHandle handle = GCHandle.Alloc(probe);
        var weak = new WeakReference(alc);
        alc.Unload();
        return (weak, handle);
    }

    [Fact]
    public static void SwiftStyleHandleKeepsACollectibleContextLoadedUntilFreed()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return; // NativeAOT: no unloadable ALCs; policy is "unsupported", nothing to exercise.

        (WeakReference alcWeak, GCHandle handle) = LoadCollectibleAndRootFromHandle();

        // Unload was requested, but the strong handle roots an object owned by the
        // ALC, so the ALC stays loaded — exactly as when Swift holds the box.
        for (int i = 0; i < 10; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.True(alcWeak.IsAlive, "collectible context unloaded while a Swift-style handle rooted its object");

        // Freeing the handle (the box's deinit) lets the ALC unload.
        handle.Free();
        for (int i = 0; i < 10 && alcWeak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(alcWeak.IsAlive, "collectible context did not unload after the Swift-style handle was freed");
    }
}
