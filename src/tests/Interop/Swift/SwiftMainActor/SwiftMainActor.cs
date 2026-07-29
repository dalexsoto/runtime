// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

// A @MainActor-isolated Swift async function only completes if the process
// DRAINS THE MAIN DISPATCH QUEUE. A host that never services the main queue
// hangs on such a call forever, with no error and no diagnostic — the callback
// simply never arrives.
//
// This is load-bearing for StoreKit: Product.purchase(options:) is @MainActor
// async (abi-model.md "Isolation must fail closed" records the detection), so a
// console-style .NET host cannot call it without pumping the main run loop.
// App hosts (iOS/tvOS/Catalyst/macOS app bundles) already have a main run loop
// and are unaffected.
[PlatformSpecific(TestPlatforms.OSX)]
public static unsafe class SwiftMainActor
{
    private const string Lib = "libSwiftMainActor.dylib";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(Lib, EntryPoint = "swiftmainactor_call_mainactor")]
    private static extern void CallMainActor(void* context,
        delegate* unmanaged<void*, long, void> callback);

    [DllImport(Lib, EntryPoint = "swiftmainactor_call_nonisolated")]
    private static extern void CallNonisolated(void* context,
        delegate* unmanaged<void*, long, void> callback);

    // Drains the main queue for a bounded interval, on the calling (main) thread.
    [DllImport(CoreFoundation)]
    private static extern int CFRunLoopRunInMode(IntPtr mode, double seconds,
        [MarshalAs(UnmanagedType.U1)] bool returnAfterSourceHandled);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    private static long s_result;
    private static int s_fired;

    [UnmanagedCallersOnly]
    private static void OnCompleted(void* context, long value)
    {
        s_result = value;
        Interlocked.Exchange(ref s_fired, 1);
    }

    private static void Reset()
    {
        s_result = 0;
        Interlocked.Exchange(ref s_fired, 0);
    }

    private static bool WaitWithoutPumping(int milliseconds)
    {
        // A plain sleep/spin: the main queue is NEVER serviced.
        for (int i = 0; i < milliseconds / 50; i++)
        {
            if (Volatile.Read(ref s_fired) == 1)
            {
                return true;
            }
            Thread.Sleep(50);
        }
        return Volatile.Read(ref s_fired) == 1;
    }

    private static bool WaitPumpingMainQueue(int milliseconds)
    {
        IntPtr defaultMode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", 0x0600); // UTF8
        for (int i = 0; i < milliseconds / 50; i++)
        {
            if (Volatile.Read(ref s_fired) == 1)
            {
                return true;
            }
            // Servicing the main run loop drains the main queue, which is what
            // lets the main actor's executor run.
            CFRunLoopRunInMode(defaultMode, 0.05, false);
        }
        return Volatile.Read(ref s_fired) == 1;
    }

    // ---- The support-layer remedy ----
    //
    // A silent infinite hang is the worst failure mode available, and unlike the
    // StoreKit daemon gate this one is entirely under our control. So the
    // support layer DETECTS an unserviced main queue and turns the hang into a
    // named, catchable error BEFORE the call is made.

    // dispatch_get_main_queue() is a C MACRO, not an exported function: it
    // resolves to the address of the global queue object _dispatch_main_q.
    private static IntPtr GetMainQueue()
        => NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/system/libdispatch.dylib"), "_dispatch_main_q");

    [DllImport("/usr/lib/system/libdispatch.dylib", EntryPoint = "dispatch_async_f")]
    private static extern void DispatchAsync(IntPtr queue, void* context,
        delegate* unmanaged<void*, void> work);

    private static int s_mainQueueTick;

    [UnmanagedCallersOnly]
    private static void OnMainQueueTick(void* context) => Interlocked.Exchange(ref s_mainQueueTick, 1);

    /// <summary>
    /// Is the process actually servicing its main dispatch queue? Enqueue a
    /// probe block and see whether it runs. This is the only honest test: the
    /// answer depends on what the HOST does, not on anything observable in the
    /// binding.
    /// </summary>
    public static bool IsMainQueueServiced(int timeoutMilliseconds = 250)
    {
        Interlocked.Exchange(ref s_mainQueueTick, 0);
        DispatchAsync(GetMainQueue(), null, &OnMainQueueTick);

        for (int i = 0; i < timeoutMilliseconds / 10; i++)
        {
            if (Volatile.Read(ref s_mainQueueTick) == 1)
            {
                return true;
            }
            Thread.Sleep(10);
        }
        return Volatile.Read(ref s_mainQueueTick) == 1;
    }

    /// <summary>
    /// Thrown INSTEAD of hanging when a @MainActor-isolated Swift call is made
    /// from a host that does not service the main queue.
    /// </summary>
    public sealed class SwiftHostRequirementException : InvalidOperationException
    {
        public SwiftHostRequirementException(string code, string message)
            : base($"{code}: {message}") => Code = code;

        public string Code { get; }
    }

    /// <summary>
    /// Guard emitted ahead of any @MainActor-isolated binding.
    /// </summary>
    public static void RequireServicedMainQueue(string declaration)
    {
        if (!IsMainQueueServiced())
        {
            throw new SwiftHostRequirementException("SWIFT0007",
                $"'{declaration}' is @MainActor-isolated, but this process does not service its main "
                + "dispatch queue, so the call would never complete. Run a main run loop (an app host "
                + "does this already), or call it from a host that does.");
        }
    }

    [Fact]
    public static void UnservicedMainQueueIsDetectedRatherThanHanging()
    {
        // The test host does not pump, so the guard must FIRE — turning what
        // would be a permanent hang into a catchable error naming the cause.
        Assert.False(IsMainQueueServiced());

        var ex = Assert.Throws<SwiftHostRequirementException>(
            () => RequireServicedMainQueue("StoreKit.Product.purchase(options:)"));
        Assert.Equal("SWIFT0007", ex.Code);
        Assert.Contains("does not service its main dispatch queue", ex.Message);
    }

    [Fact]
    public static void NonisolatedAsyncCompletesWithoutPumpingTheMainQueue()
    {
        Reset();
        CallNonisolated(null, &OnCompleted);
        Assert.True(WaitWithoutPumping(3000), "a nonisolated async call must not need the main queue");
        Assert.Equal(42, s_result);
    }

    [Fact]
    public static void MainActorAsyncHangsWithoutPumpingTheMainQueue()
    {
        // THE HAZARD, asserted: without a main-queue drain the callback never
        // arrives. This is not a slow call — it is a permanent hang, and it is
        // exactly what a console-style host calling StoreKit's purchase() would
        // experience.
        Reset();
        CallMainActor(null, &OnCompleted);
        Assert.False(WaitWithoutPumping(2000),
            "expected a @MainActor async call to HANG without a main-queue drain");
    }

    [Fact]
    public static void MainActorAsyncCompletesWhenTheMainQueueIsPumped()
    {
        // ...and the remedy: service the main run loop and the same call
        // completes. An app host does this already; a console host must.
        Reset();
        CallMainActor(null, &OnCompleted);
        Assert.True(WaitPumpingMainQueue(5000),
            "a @MainActor async call must complete once the main queue is drained");
        Assert.Equal(42, s_result);
    }
}
