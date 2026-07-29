// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Threading;
using Xunit;
using TestLibrary;

// SafeHandle release during shutdown (roadmap Phase 2): a child process
// exits — both via Environment.Exit and by returning from Main — while
// Swift-object SafeHandles are live: some disposed, some leaked to the
// finalizer, and one actively making Swift calls on another thread. The
// parent asserts the child reaches its expected exit code and neither
// crashes nor hangs. Neither CoreCLR nor NativeAOT runs finalizers at
// shutdown, so the property under test is a clean, prompt exit with live
// native handles, not end-of-life finalization.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftShutdownLifetime
{
    private const string SwiftLib = "libSwiftShutdownLifetime.dylib";
    private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";
    private const string ScenarioVariable = "SWIFT_SHUTDOWN_SCENARIO";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s21SwiftShutdownLifetime12makeResource7payloadAA0E0CSi_tF")]
    private static extern IntPtr MakeResource(nint payload);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s21SwiftShutdownLifetime15resourcePayloadySiAA8ResourceCF")]
    private static extern nint ResourcePayload(IntPtr resource);

    [DllImport(SwiftCoreLib)]
    private static extern void swift_release(IntPtr obj);

    private sealed class ResourceHandle : SafeHandle
    {
        public ResourceHandle(IntPtr owned)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(owned);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            swift_release(handle);
            return true;
        }
    }

    private static string? Scenario => Environment.GetEnvironmentVariable(ScenarioVariable);

    /// <summary>Live handles in every state, plus a worker mid-call on another thread.</summary>
    private static void SetUpLiveHandlesForShutdown()
    {
        // Disposed before exit.
        var disposed = new ResourceHandle(MakeResource(1));
        Assert.Equal(1, (long)ResourcePayload(disposed.DangerousGetHandle()));
        disposed.Dispose();

        // Reachable at exit: never disposed, never finalized.
        var leakedReachable = new ResourceHandle(MakeResource(2));
        Assert.Equal(2, (long)ResourcePayload(leakedReachable.DangerousGetHandle()));
        GC.KeepAlive(leakedReachable);
        s_pinnedForShutdown = leakedReachable;

        // Unreachable at exit: eligible for finalization that shutdown will
        // never run; must not destabilize the exit path.
        _ = new ResourceHandle(MakeResource(3));
        GC.Collect();

        // A background thread continuously calling into Swift through a live
        // handle while the process exits underneath it.
        var workerReady = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            var handle = new ResourceHandle(MakeResource(4));
            workerReady.Set();
            while (true)
            {
                if (ResourcePayload(handle.DangerousGetHandle()) != 4)
                    Environment.FailFast("Swift call returned a corrupt result during shutdown.");
            }
        })
        {
            IsBackground = true,
        };
        worker.Start();
        workerReady.Wait();
    }

    private static ResourceHandle? s_pinnedForShutdown;

    private static Process SpawnChild(string scenario)
    {
        // Reproduce this process's invocation (host + assembly + args, or
        // the NativeAOT binary + args) with the scenario variable set.
        // GetCommandLineArgs does not include host options, so the entry
        // assembly path is re-inserted when running hosted.
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
        };
        string entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;
        if (entryAssembly.Length != 0)
            startInfo.ArgumentList.Add(entryAssembly);
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
            startInfo.ArgumentList.Add(args[i]);
        startInfo.Environment[ScenarioVariable] = scenario;

        Process child = Process.Start(startInfo)!;
        return child;
    }

    private static void RunParentScenario(string scenario, int expectedExitCode)
    {
        using Process child = SpawnChild(scenario);
        Assert.True(child.WaitForExit(120_000),
            $"child '{scenario}' hung at shutdown with live Swift SafeHandles");
        Assert.Equal(expectedExitCode, child.ExitCode);
    }

    [Fact]
    public static void ExplicitExitWithLiveHandles()
    {
        if (Scenario == "exit")
        {
            SetUpLiveHandlesForShutdown();
            Environment.Exit(42);
        }

        if (Scenario != null)
            return; // some other scenario owns this child process

        RunParentScenario("exit", 42);
    }

    [Fact]
    public static void MainReturnWithLiveHandles()
    {
        if (Scenario == "return")
        {
            // Set up and return: the child's runner finishes the remaining
            // facts and exits through the normal path (exit code 100) with
            // the handles still alive and the worker mid-call.
            SetUpLiveHandlesForShutdown();
            return;
        }

        if (Scenario != null)
            return;

        RunParentScenario("return", 100);
    }
}
