// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;

// Soak lane (Audit follow-ups): high-iteration churn over the hot interop
// paths with exact Swift-side deinit balance, exact GCHandle terminal
// accounting, and a generous RSS bound. Iteration count scales via
// DOTNET_SwiftSoakIterations (CI default keeps the suite fast; the recorded
// 10^6 evidence runs locally).
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftSoak
{
    private const string Lib = "libSwiftSoak.dylib";
    private const string CoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s9SwiftSoak11makeTrackedAA0D0CyF")]
    private static extern IntPtr MakeTracked();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s9SwiftSoak11trackedLives5Int64VyF")]
    private static extern long TrackedLive();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s9SwiftSoak14makeClosureBoxyAA0dE0Cs5Int64VSv_AFtXC_ySvXCSvtF")]
    private static extern IntPtr MakeClosureBox(
        delegate* unmanaged<void*, long, long> invoke,
        delegate* unmanaged<void*, void> release,
        void* context);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s9SwiftSoak16invokeClosureBoxys5Int64VAA0dE0C_ADtF")]
    private static extern long InvokeClosureBox(IntPtr box, long arg);

    [DllImport(CoreLib)]
    private static extern void swift_release(IntPtr obj);

    private sealed class SoakTarget
    {
        public long Sum;
    }

    private static long s_released;

    [UnmanagedCallersOnly]
    private static long OnInvoke(void* context, long arg)
    {
        var target = (SoakTarget)GCHandle.FromIntPtr((IntPtr)context).Target!;
        target.Sum += arg;
        return target.Sum;
    }

    [UnmanagedCallersOnly]
    private static void OnRelease(void* context)
    {
        GCHandle.FromIntPtr((IntPtr)context).Free();
        System.Threading.Interlocked.Increment(ref s_released);
    }

    private static int Iterations()
    {
        string? env = Environment.GetEnvironmentVariable("DOTNET_SwiftSoakIterations");
        return env != null && int.TryParse(env, out int n) && n > 0 ? n : 10_000;
    }

    [Fact]
    public static void ObjectChurnBalancesExactly()
    {
        int iterations = Iterations();
        long before = TrackedLive();

        for (int i = 0; i < iterations; i++)
        {
            swift_release(MakeTracked());
        }

        Assert.Equal(before, TrackedLive());
        Console.WriteLine($"SOAK objects: {iterations} iterations, live balance exact");
    }

    [Fact]
    public static void ClosureBoxChurnFreesEveryHandle()
    {
        int iterations = Iterations();
        long releasedBefore = s_released;

        for (int i = 0; i < iterations; i++)
        {
            var target = new SoakTarget();
            GCHandle handle = GCHandle.Alloc(target);
            IntPtr box = MakeClosureBox(&OnInvoke, &OnRelease, (void*)GCHandle.ToIntPtr(handle));
            InvokeClosureBox(box, i);
            swift_release(box); // deinit -> OnRelease -> handle freed exactly once
        }

        Assert.Equal(releasedBefore + iterations, s_released);
        Console.WriteLine($"SOAK closures: {iterations} iterations, every GCHandle freed");
    }

    // The cross-runtime reference-cycle policy (support-library.md): a Swift
    // object holding a strong GCHandle to a managed object that in turn owns
    // the Swift reference is collectible by NEITHER collector. The test
    // demonstrates the forbidden shape leaking, then breaks it through the
    // policy's explicit terminal owner.
    private sealed class CycleNode
    {
        public IntPtr SwiftBox;
    }

    private static void BuildCycle(out WeakReference weak, out IntPtr boxOut)
    {
        var node = new CycleNode();
        GCHandle handle = GCHandle.Alloc(node); // strong: the Swift box's reference to managed
        node.SwiftBox = MakeClosureBox(&OnInvoke, &OnRelease, (void*)GCHandle.ToIntPtr(handle));
        weak = new WeakReference(node);
        boxOut = node.SwiftBox;
    }

    [Fact]
    public static void CrossRuntimeCycleNeedsExplicitTerminalOwner()
    {
        long releasedBefore = System.Threading.Interlocked.Read(ref s_released);
        BuildCycle(out WeakReference weak, out IntPtr box);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // The forbidden shape: neither collector reclaims the cycle.
        Assert.True(weak.IsAlive);
        Assert.Equal(releasedBefore, System.Threading.Interlocked.Read(ref s_released));

        // The policy's terminal owner: the managed side explicitly releases
        // its Swift reference (the wrapper-Dispose role), which runs the
        // Swift deinit, which frees the GCHandle — unwinding the cycle.
        swift_release(box);
        Assert.Equal(releasedBefore + 1, System.Threading.Interlocked.Read(ref s_released));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive);
    }

    [Fact]
    public static void SoakRssStaysBounded()
    {
        int iterations = Iterations();

        // Warm up, then measure growth across the full mixed loop.
        RunMixed(Math.Max(1000, iterations / 100));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long rssBefore = Environment.WorkingSet;

        RunMixed(iterations);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long rssAfter = Environment.WorkingSet;

        long growth = rssAfter - rssBefore;
        Console.WriteLine($"SOAK rss: {iterations} mixed iterations, growth {growth / (1024.0 * 1024.0):F1} MB");
        // A real per-iteration leak of >=64 bytes would exceed this at 10^6.
        Assert.True(growth < 64L * 1024 * 1024, $"RSS grew {growth} bytes over {iterations} iterations");
    }

    private static void RunMixed(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            swift_release(MakeTracked());

            var target = new SoakTarget();
            GCHandle handle = GCHandle.Alloc(target);
            IntPtr box = MakeClosureBox(&OnInvoke, &OnRelease, (void*)GCHandle.ToIntPtr(handle));
            InvokeClosureBox(box, i);
            swift_release(box);

            using var s = Swift.Runtime.Support.SwiftStringMini.Create("soak-iteration-string-" + (i & 1023));
        }
    }
}

namespace Swift.Runtime.Support
{
    // Minimal owned Swift string for churn (mirrors the support-library shape).
    internal unsafe struct SwiftStringMini : IDisposable
    {
        private long _a, _b;

        [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
        [DllImport("/usr/lib/swift/libswiftCore.dylib", EntryPoint = "$sSS7cStringSSSPys4Int8VG_tcfC")]
        private static extern SwiftStringMini FromCString(byte* cString);

        private static IntPtr s_metadata;

        private static IntPtr Metadata
        {
            get
            {
                if (s_metadata == IntPtr.Zero)
                    s_metadata = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/swift/libswiftCore.dylib"), "$sSSN");
                return s_metadata;
            }
        }

        public static SwiftStringMini Create(string value)
        {
            byte[] utf8 = new byte[System.Text.Encoding.UTF8.GetByteCount(value) + 1];
            System.Text.Encoding.UTF8.GetBytes(value, utf8);
            fixed (byte* p = utf8)
            {
                return FromCString(p);
            }
        }

        public void Dispose()
        {
            void** vwt = *(void***)((byte*)Metadata - sizeof(nint));
            fixed (SwiftStringMini* self = &this)
            {
                ((delegate* unmanaged[Swift]<void*, IntPtr, void>)vwt[1])(self, Metadata);
            }
            this = default;
        }
    }
}
