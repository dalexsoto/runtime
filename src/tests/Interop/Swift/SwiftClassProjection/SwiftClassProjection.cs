// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// Native Swift class projection (roadmap Phase 6): ARC-handle ownership,
// allocating initializers and static methods with type metadata as the
// self context, overridable members through exported dispatch thunks
// (polymorphism preserved), final methods called directly, property and
// subscript accessors, failable initializers as nullable returns, and
// identity/disposal behavior proven by a deinit-counting fixture.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftClassProjection
{
    private const string SwiftLib = "libSwiftClassProjection.dylib";
    private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct MetadataResponse
    {
        public IntPtr Metadata;
        public nint State;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterCMa")]
    private static extern MetadataResponse CounterMa(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterC5startACs5Int64V_tcfC")]
    private static extern IntPtr CounterInit(long start, SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterC9validatedACSgs5Int64V_tcfC")]
    private static extern IntPtr CounterInitValidated(long start, SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterC12defaultStarts5Int64VyFZ")]
    private static extern long CounterDefaultStart(SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterC9increment2byys5Int64V_tFTj")]
    private static extern void CounterIncrement(long amount, SwiftSelf instance);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterC8snapshots5Int64VyF")]
    private static extern long CounterSnapshot(SwiftSelf instance); // final: direct symbol

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterC8describes5Int64VyFTj")]
    private static extern long CounterDescribe(SwiftSelf instance); // overridable: dispatch thunk

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterC12doubledTotals5Int64VvgTj")]
    private static extern long CounterDoubledTotal(SwiftSelf instance);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection7CounterCys5Int64VAEcigTj")]
    private static extern long CounterSubscript(long index, SwiftSelf instance);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection21makeLoudCounterAsBase5startAA0F0Cs5Int64V_tF")]
    private static extern IntPtr MakeLoudCounterAsBase(long start);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftClassProjection20liveCounterInstancess5Int64VyF")]
    private static extern long LiveCounterInstances();

    [DllImport(SwiftCoreLib)]
    private static extern void swift_release(IntPtr obj);

    /// <summary>Owned ARC handle over a projected Swift class instance.</summary>
    private sealed class CounterHandle : SafeHandle
    {
        public CounterHandle(IntPtr owned)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(owned);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        public SwiftSelf Self => new SwiftSelf((void*)handle);

        protected override bool ReleaseHandle()
        {
            swift_release(handle);
            return true;
        }
    }

    private static SwiftSelf CounterTypeContext()
    {
        MetadataResponse response = CounterMa(0);
        Assert.Equal(0, (long)response.State);
        return new SwiftSelf((void*)response.Metadata);
    }

    [Fact]
    public static void AllocateUseAndDispose()
    {
        long before = LiveCounterInstances();

        var counter = new CounterHandle(CounterInit(5, CounterTypeContext()));
        Assert.Equal(before + 1, LiveCounterInstances());

        CounterIncrement(4, counter.Self);          // via dispatch thunk
        Assert.Equal(9, CounterSnapshot(counter.Self)); // final, direct call
        Assert.Equal(18, CounterDoubledTotal(counter.Self));
        Assert.Equal(109, CounterSubscript(100, counter.Self));

        counter.Dispose();
        Assert.Equal(before, LiveCounterInstances()); // deinit ran exactly once
    }

    [Fact]
    public static void IdentityIsPerInstance()
    {
        long before = LiveCounterInstances();

        var first = new CounterHandle(CounterInit(1, CounterTypeContext()));
        var second = new CounterHandle(CounterInit(1, CounterTypeContext()));
        Assert.NotEqual(first.DangerousGetHandle(), second.DangerousGetHandle());

        CounterIncrement(10, first.Self);
        Assert.Equal(11, CounterSnapshot(first.Self));
        Assert.Equal(1, CounterSnapshot(second.Self)); // unaffected

        first.Dispose();
        second.Dispose();
        Assert.Equal(before, LiveCounterInstances());
    }

    [Fact]
    public static void OverriddenMethodDispatchesThroughThunk()
    {
        long before = LiveCounterInstances();

        var baseCounter = new CounterHandle(CounterInit(3, CounterTypeContext()));
        var loudCounter = new CounterHandle(MakeLoudCounterAsBase(3));

        // The same dispatch-thunk import virtual-dispatches per instance.
        Assert.Equal(30, CounterDescribe(baseCounter.Self));
        Assert.Equal(300, CounterDescribe(loudCounter.Self));

        baseCounter.Dispose();
        loudCounter.Dispose();
        Assert.Equal(before, LiveCounterInstances());
    }

    [Fact]
    public static void StaticMethodTakesMetadataAsContext()
    {
        Assert.Equal(7, CounterDefaultStart(CounterTypeContext()));
    }

    [Fact]
    public static void FailableInitializerReturnsNullOnFailure()
    {
        long before = LiveCounterInstances();

        Assert.Equal(IntPtr.Zero, CounterInitValidated(-1, CounterTypeContext()));
        Assert.Equal(before, LiveCounterInstances()); // nothing leaked on failure

        IntPtr valid = CounterInitValidated(6, CounterTypeContext());
        Assert.NotEqual(IntPtr.Zero, valid);
        var handle = new CounterHandle(valid);
        Assert.Equal(6, CounterSnapshot(handle.Self));
        handle.Dispose();
        Assert.Equal(before, LiveCounterInstances());
    }
}
