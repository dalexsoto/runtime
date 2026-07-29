// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Threading;
using Xunit;
using TestLibrary;

// R2R hardening suite (roadmap Phase 2): every run asserts per-method
// execution provenance for the wrapper methods that contain CallConvSwift
// calls, so one run cannot satisfy another execution mode's gate.
//
// Provenance comes from in-process runtime events (probe-verified):
// - R2RGetEntryPoint fires when a method's precompiled R2R code is bound
//   (CompilationDiagnosticKeyword).
// - MethodLoadVerbose with the Jitted flag fires when the JIT compiles the
//   method; bits 7-9 of MethodFlags carry the optimization tier, so tier-up
//   recompilations of a hot Swift-calling method are directly observable.
// - Forcing DOTNET_ReadyToRun=0 on an R2R image runs the retained IL through
//   the JIT (the Apple-mobile fallback shape), and the interpreter uses R2R
//   native code when present, IL otherwise.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftR2RProvenance
{
    private const string SwiftLib = "libSwiftR2RProvenance.dylib";
    private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s18SwiftR2RProvenance9addValues1a1bS2i_SitF")]
    private static extern nint AddValues(nint a, nint b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s18SwiftR2RProvenance9mulValues1a1bS2i_SitF")]
    private static extern nint MulValues(nint a, nint b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s18SwiftR2RProvenance13doubleOrThrow5valueS2i_tKF")]
    private static extern nint DoubleOrThrow(nint value, SwiftError* error);

    // SuppressGCTransition is what makes crossgen2 emit the direct
    // PINVOKE_TARGET fixup (IAT_PVALUE); plain P/Invokes get the indirect
    // INDIRECT_PINVOKE_TARGET cell (IAT_PPVALUE). Same Swift entry point as
    // AddValues, so both cell kinds are exercised against identical code.
    [SuppressGCTransition]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s18SwiftR2RProvenance9addValues1a1bS2i_SitF")]
    private static extern nint AddValuesDirect(nint a, nint b);

    [DllImport(SwiftCoreLib)]
    private static extern void swift_errorRelease(IntPtr error);

    // One dedicated wrapper per fact: provenance is attributed by method
    // name, and each wrapper's first call must happen under that fact's
    // listener.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ColdSwiftWrapper(nint a, nint b) => AddValues(a, b);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long HotSwiftWrapper(nint a, nint b) => MulValues(a, b);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long DirectCellSwiftWrapper(nint a, nint b) => AddValuesDirect(a, b);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ThrowingSwiftWrapper(nint value)
    {
        SwiftError error = default;
        nint result = DoubleOrThrow(value, &error);
        if (error.Value != null)
        {
            swift_errorRelease((IntPtr)error.Value);
            return long.MinValue;
        }

        return result;
    }

    private sealed class CodeSourceListener : EventListener
    {
        private const EventKeywords JitKeyword = (EventKeywords)0x10;
        private const EventKeywords CompilationDiagnosticKeyword = (EventKeywords)0x2000000000;

        public readonly ConcurrentQueue<(string Method, bool FromR2R, uint Tier)> Records = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Verbose, JitKeyword | CompilationDiagnosticKeyword);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName == "R2RGetEntryPoint")
            {
                int name = e.PayloadNames!.IndexOf("MethodName");
                if (name >= 0)
                    Records.Enqueue(((string)e.Payload![name]!, true, 0));
            }
            else if (e.EventName != null && e.EventName.StartsWith("MethodLoadVerbose"))
            {
                int name = e.PayloadNames!.IndexOf("MethodName");
                int flagsIdx = e.PayloadNames!.IndexOf("MethodFlags");
                if (name >= 0 && flagsIdx >= 0)
                {
                    ulong flags = Convert.ToUInt64(e.Payload![flagsIdx]!);
                    if ((flags & 0x8) != 0) // Jitted
                        Records.Enqueue(((string)e.Payload![name]!, false, (uint)((flags >> 7) & 0x7)));
                }
            }
        }

        public int CountR2R(string method)
        {
            int count = 0;
            foreach ((string m, bool fromR2R, _) in Records)
                if (fromR2R && m == method)
                    count++;
            return count;
        }

        public int CountJitted(string method)
        {
            int count = 0;
            foreach ((string m, bool fromR2R, _) in Records)
                if (!fromR2R && m == method)
                    count++;
            return count;
        }

        /// <summary>Event delivery is buffered; poll until the condition holds or the deadline passes.</summary>
        public bool WaitFor(Func<bool> condition, int timeoutMs = 15000)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                if (timer.ElapsedMilliseconds > timeoutMs)
                    return false;
                Thread.Sleep(50);
            }
            return true;
        }
    }

    // The runner scripts export RunCrossGen2 when the R2R images are in use;
    // DOTNET_ReadyToRun=0 on those images is the forced JIT-fallback leg.
    private static bool RunningFromR2RImage =>
        Environment.GetEnvironmentVariable("RunCrossGen2") == "1"
        && Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") != "0";

    private static bool ProvenanceObservable => !TestLibrary.Utilities.IsNativeAot;

    private static void AssertColdProvenance(CodeSourceListener listener, string wrapper)
    {
        if (RunningFromR2RImage)
        {
            // Precompiled code must be bound (through the first-call prestub
            // path) and the JIT must not compile the wrapper's initial code.
            Assert.True(listener.WaitFor(() => listener.CountR2R(wrapper) > 0),
                $"expected an R2RGetEntryPoint event for {wrapper}");
            Assert.Equal(0, listener.CountJitted(wrapper));
        }
        else if (TestLibrary.Utilities.IsCoreClrInterpreter)
        {
            // The interpreter compiles methods through the same preparation
            // path and reports a method-load event (probe-observed: jitted
            // flag, tier 2, never promoted); what distinguishes this mode
            // from R2R is that no precompiled code is bound.
            Assert.True(listener.WaitFor(() => listener.CountJitted(wrapper) > 0),
                $"expected a method-load event for {wrapper}");
            Assert.Equal(0, listener.CountR2R(wrapper));
        }
        else
        {
            // Pure JIT, or the forced JIT fallback over an R2R image's
            // retained IL: the wrapper must be jitted and no R2R code bound.
            Assert.True(listener.WaitFor(() => listener.CountJitted(wrapper) > 0),
                $"expected a jitted MethodLoadVerbose event for {wrapper}");
            Assert.Equal(0, listener.CountR2R(wrapper));
        }
    }

    [Fact]
    public static void ColdFirstCallHasExpectedCodeSource()
    {
        if (!ProvenanceObservable)
        {
            Assert.Equal(30, ColdSwiftWrapper(10, 20));
            return;
        }

        using var listener = new CodeSourceListener();

        // Very first call of this wrapper in the process: exercises the
        // first-call prestub path and lazy P/Invoke cell resolution of a
        // CallConvSwift import from whatever code source this mode uses.
        Assert.Equal(30, ColdSwiftWrapper(10, 20));

        AssertColdProvenance(listener, nameof(ColdSwiftWrapper));
    }

    [Fact]
    public static void DirectPInvokeCellHasExpectedCodeSource()
    {
        if (!ProvenanceObservable)
        {
            Assert.Equal(11, DirectCellSwiftWrapper(4, 7));
            return;
        }

        using var listener = new CodeSourceListener();

        // First call through the direct (SuppressGCTransition) target cell.
        Assert.Equal(11, DirectCellSwiftWrapper(4, 7));

        AssertColdProvenance(listener, nameof(DirectCellSwiftWrapper));
    }

    [Fact]
    public static void HotSwiftCallerTiersUp()
    {
        if (!ProvenanceObservable)
        {
            Assert.Equal(21, HotSwiftWrapper(3, 7));
            return;
        }

        if (TestLibrary.Utilities.IsCoreClrInterpreter
            || Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") == "0")
        {
            Assert.Equal(21, HotSwiftWrapper(3, 7));
            return;
        }

        using var listener = new CodeSourceListener();

        // Keep the wrapper hot until tiering recompiles it. From an R2R
        // image this proves JIT fallback and tiered recompilation on top of
        // precompiled Swift-calling code; from IL it proves tier-up across a
        // CallConvSwift call.
        bool Promoted() => RunningFromR2RImage
            ? listener.CountR2R(nameof(HotSwiftWrapper)) > 0 && listener.CountJitted(nameof(HotSwiftWrapper)) >= 1
            : listener.CountJitted(nameof(HotSwiftWrapper)) >= 2;

        long sum = 0;
        var timer = Stopwatch.StartNew();
        do
        {
            for (int i = 0; i < 500_000; i++)
                sum += HotSwiftWrapper(i, 3);
        }
        while (!Promoted() && timer.ElapsedMilliseconds < 60_000);

        Assert.True(Promoted(),
            $"expected tier-up events for {nameof(HotSwiftWrapper)}; " +
            $"r2r={listener.CountR2R(nameof(HotSwiftWrapper))} jitted={listener.CountJitted(nameof(HotSwiftWrapper))}");
        Assert.True(sum != 0);
        Assert.Equal(21, HotSwiftWrapper(3, 7));
    }

    [Fact]
    public static void ThrowingSwiftCallHasExpectedCodeSource()
    {
        if (!ProvenanceObservable)
        {
            Assert.Equal(24, ThrowingSwiftWrapper(12));
            Assert.Equal(long.MinValue, ThrowingSwiftWrapper(-3));
            return;
        }

        using var listener = new CodeSourceListener();

        // A wrapper whose body binds the Swift error register must have the
        // same provenance as plain wrappers: the error-handling lowering may
        // not silently push the method to a different code source.
        Assert.Equal(24, ThrowingSwiftWrapper(12));
        Assert.Equal(long.MinValue, ThrowingSwiftWrapper(-3));

        AssertColdProvenance(listener, nameof(ThrowingSwiftWrapper));
    }
}
