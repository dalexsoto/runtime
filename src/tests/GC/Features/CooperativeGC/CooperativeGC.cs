// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using Xunit;

public class CooperativeGC
{
    private sealed class LoopState
    {
        public readonly int[] Values = [42];
        public int Stop;
        public int Ready;
        public int Result;
    }

    [SkipOnPlatform(TestPlatforms.Browser | TestPlatforms.Wasi, "Requires managed threads.")]
    [Fact]
    public static void TestEntryPoint()
    {
        RunLoop(false);
        RunLoop(true);

        // A literal self-branch has no exit. Process isolation ends this background
        // thread after proving that collection can rendezvous with it.
        var state = new LoopState();
        var selfLoop = new Thread(() => CooperativeLoops.SelfLoop(ref state.Ready)) { IsBackground = true };
        selfLoop.Start();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref state.Ready) != 0, TimeSpan.FromSeconds(10)));
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Console.WriteLine("COOPERATIVE_GC_LOOPS_PASS");
    }

    private static void RunLoop(bool useLeave)
    {
        var state = new LoopState();
        var worker = new Thread(() =>
        {
            state.Result = useLeave
                ? CooperativeLoops.LeaveLoop(ref state.Stop, ref state.Ready, ref state.Values[0])
                : CooperativeLoops.SwitchLoop(ref state.Stop, ref state.Ready, ref state.Values[0]);
        })
        {
            IsBackground = true
        };
        worker.Start();
        try
        {
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref state.Ready) != 0, TimeSpan.FromSeconds(10)));
            for (int iteration = 0; iteration < 3; iteration++)
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        finally
        {
            Volatile.Write(ref state.Stop, 1);
            Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        }
        Assert.Equal(42, state.Result);
    }
}
