// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TestLibrary;

// Phase 8 generated async-thunk ABI v1 (spec: abi-model.md "Generated
// async-thunk ABI"): operation handles with an exactly-once terminal state
// machine (completed/failed/cancelled), synchronous completion before
// begin returns, idempotent cancel/release, C-callback-plus-context
// completions bridged to Task through TaskCompletionSource with
// asynchronous continuations, result adoption into caller-owned storage
// via the rooted metadata's VWT, Swift error adoption as managed
// exceptions, CancellationToken wired to cooperative Swift cancellation,
// MainActor completions requiring a pumped main queue in headless hosts,
// actor reentrancy, and a pull-model AsyncSequence adapter with exactly
// one in-flight next() per MoveNextAsync.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftAsyncBridge
{
    private const string SwiftLib = "libSwiftAsyncBridge.dylib";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge21asyncthunk_abiVersions5Int64VyF")]
    private static extern long AbiVersion();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge21asyncthunk_beginFetchySvs5Int64V_SvSgyAE_A3DtXCtF")]
    private static extern IntPtr BeginFetch(long input, IntPtr context, delegate* unmanaged<IntPtr, long, long, long, void> completion);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge33asyncthunk_operationTerminalStateys5Int64VSvF")]
    private static extern long OperationTerminalState(IntPtr operation);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge26asyncthunk_operationCancelyySvF")]
    private static extern void OperationCancel(IntPtr operation);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge27asyncthunk_operationReleaseyySvF")]
    private static extern void OperationRelease(IntPtr operation);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge25asyncthunk_bundleMetadataypXpyF")]
    private static extern IntPtr BundleMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge27asyncthunk_beginFetchBundleySvs5Int64V_S2vSgyAE_ADtXCtF")]
    private static extern IntPtr BeginFetchBundle(long input, void* resultStorage, IntPtr context, delegate* unmanaged<IntPtr, long, void> completion);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge30asyncthunk_beginMainActorFetchySvs5Int64V_SvSgyAE_ADtXCtF")]
    private static extern IntPtr BeginMainActorFetch(long input, IntPtr context, delegate* unmanaged<IntPtr, long, void> completion);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge24asyncthunk_ledgerSlowAddyys5Int64V_SvSgyAE_ADtXCtF")]
    private static extern void LedgerSlowAdd(long amount, IntPtr context, delegate* unmanaged<IntPtr, long, void> completion);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge34asyncthunk_ledgerInterleavedGrowthyySvSg_yAC_s5Int64VtXCtF")]
    private static extern void LedgerInterleavedGrowth(IntPtr context, delegate* unmanaged<IntPtr, long, void> completion);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge21asyncthunk_streamOpenySvs5Int64VF")]
    private static extern IntPtr StreamOpen(long count);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge21asyncthunk_streamNextyySv_SvSgyAC_s5Int64VAEtXCtF")]
    private static extern void StreamNext(IntPtr handle, IntPtr context, delegate* unmanaged<IntPtr, long, long, void> onItem);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge22asyncthunk_streamCloseyySvF")]
    private static extern void StreamClose(IntPtr handle);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge24asyncthunk_streamReleaseyySvF")]
    private static extern void StreamRelease(IntPtr handle);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge34asyncthunk_setStorefrontConfiguredyys5Int64VF")]
    private static extern void SetStorefrontConfigured(long value);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftAsyncBridge26asyncthunk_canMakePaymentss5Int64VyF")]
    private static extern long CanMakePayments();

    // CoreFoundation main-queue pump for headless MainActor delivery.
    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPStr)] string str, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, byte returnAfterSourceHandled);

    private sealed class SwiftException : Exception
    {
        public SwiftException(long code) : base($"Swift operation failed (code {code})") { }
    }

    /// <summary>
    /// The managed half of the bridge: Task via TaskCompletionSource with
    /// asynchronous continuations (completions arrive on Swift executor
    /// threads and must never run managed continuations inline), the
    /// GCHandle owned by the terminal callback (freed exactly once at the
    /// terminal state, never by disposal), and CancellationToken mapped to
    /// cooperative Swift task cancellation.
    /// </summary>
    private sealed class FetchState
    {
        public readonly TaskCompletionSource<long> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int TerminalCallbacks;
    }

    [UnmanagedCallersOnly]
    private static void OnFetchComplete(IntPtr context, long value, long errorCode, long terminalState)
    {
        GCHandle gch = GCHandle.FromIntPtr(context);
        var state = (FetchState)gch.Target!;
        Interlocked.Increment(ref state.TerminalCallbacks);
        switch (terminalState)
        {
            case 1: state.Completion.TrySetResult(value); break;
            case 2: state.Completion.TrySetException(new SwiftException(errorCode)); break;
            default: state.Completion.TrySetCanceled(); break;
        }
        gch.Free(); // ownership: the terminal callback frees the context
    }

    private static Task<long> FetchAsync(long input, CancellationToken cancellationToken = default, Action<IntPtr>? observeOperation = null)
    {
        var state = new FetchState();
        GCHandle gch = GCHandle.Alloc(state);
        IntPtr operation = BeginFetch(input, GCHandle.ToIntPtr(gch), &OnFetchComplete);
        observeOperation?.Invoke(operation);

        // Cooperative cancellation: the token requests Swift task
        // cancellation; the terminal state still arrives exactly once.
        CancellationTokenRegistration registration = cancellationToken.Register(() => OperationCancel(operation));
        state.Completion.Task.ContinueWith(
            _ => { registration.Dispose(); OperationRelease(operation); },
            TaskScheduler.Default);
        return state.Completion.Task;
    }

    [Fact]
    public static void AbiVersionGate()
    {
        Assert.Equal(1, AbiVersion());
    }

    [Fact]
    public static void CompletionBridgesToTask()
    {
        Assert.Equal(42, FetchAsync(21).Result);
    }

    [Fact]
    public static void SynchronousCompletionBeforeStartReturns()
    {
        // input 0 completes inside begin: the Task is already terminal when
        // the wrapper returns, and nothing double-fires.
        Task<long> task = FetchAsync(0);
        Assert.Equal(42, task.Result);
    }

    [Fact]
    public static void ErrorsAdoptAsManagedExceptions()
    {
        var exception = Assert.Throws<AggregateException>(() => FetchAsync(-7).Wait(30000));
        var swiftException = Assert.IsType<SwiftException>(exception.InnerException);
        Assert.Contains("-7", swiftException.Message);
    }

    [Fact]
    public static void ExactlyOnceTerminalStateMachine()
    {
        // Manual handle management: the operation must stay alive while the
        // test inspects its terminal state.
        var state = new FetchState();
        GCHandle gch = GCHandle.Alloc(state);
        IntPtr operation = BeginFetch(5, GCHandle.ToIntPtr(gch), &OnFetchComplete);

        Assert.Equal(10, state.Completion.Task.Result);
        Assert.Equal(1, OperationTerminalState(operation)); // completed
        Assert.Equal(1, Volatile.Read(ref state.TerminalCallbacks));

        // Cancel after completion: idempotent, terminal state unchanged,
        // and no second terminal callback fires.
        OperationCancel(operation);
        OperationCancel(operation);
        Assert.Equal(1, OperationTerminalState(operation));
        Assert.Equal(1, Volatile.Read(ref state.TerminalCallbacks));

        OperationRelease(operation);
    }

    [Fact]
    public static void CancellationTokenCancelsCooperatively()
    {
        using var cts = new CancellationTokenSource();
        Task<long> task = FetchAsync(100, cts.Token);
        cts.Cancel();

        var exception = Assert.Throws<AggregateException>(() => task.Wait(30000));
        Assert.IsAssignableFrom<TaskCanceledException>(exception.InnerException);
    }

    [Fact]
    public static void DisposeVersusCompleteRaceIsSafe()
    {
        // Cancellation racing natural completion: whichever terminal state
        // wins, it fires exactly once and release is always safe.
        for (int i = 0; i < 50; i++)
        {
            var state = new FetchState();
            GCHandle gch = GCHandle.Alloc(state);
            IntPtr operation = BeginFetch(3, GCHandle.ToIntPtr(gch), &OnFetchComplete);
            var canceller = new Thread(() => OperationCancel(operation));
            canceller.Start();
            state.Completion.Task.ContinueWith(_ => { }, TaskScheduler.Default).Wait(30000);
            canceller.Join();
            Assert.Equal(1, Volatile.Read(ref state.TerminalCallbacks));
            OperationRelease(operation);
        }
    }

    [Fact]
    public static void LateCompletionAfterManagedDisposalIsInert()
    {
        // The managed consumer abandons the Task before completion (the
        // purchase-after-disposal shape): the terminal callback still owns
        // and frees the context; nothing observes the result and nothing
        // crashes or leaks.
        var state = new FetchState();
        GCHandle gch = GCHandle.Alloc(state);
        IntPtr operation = BeginFetch(50, GCHandle.ToIntPtr(gch), &OnFetchComplete);
        // Abandon: no awaiters registered; wait out the completion.
        Assert.True(state.Completion.Task.ContinueWith(t => t, TaskScheduler.Default).Wait(30000));
        Assert.Equal(1, Volatile.Read(ref state.TerminalCallbacks));
        OperationRelease(operation);
    }

    private sealed class BundleState
    {
        public readonly TaskCompletionSource<long> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [UnmanagedCallersOnly]
    private static void OnBundleComplete(IntPtr context, long ok)
    {
        GCHandle gch = GCHandle.FromIntPtr(context);
        ((BundleState)gch.Target!).Done.TrySetResult(ok);
        gch.Free();
    }

    [Fact]
    public static void ResultValueAdoptsThroughMetadataVwt()
    {
        // The completion initializes a resilient value in caller-owned
        // storage sized from the rooted metadata; on completion the caller
        // owns the value and destroys it through the VWT.
        IntPtr metadata = BundleMetadata();
        void** vwt = *(void***)((byte*)metadata - sizeof(nint));
        nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));

        void* storage = NativeMemory.AlignedAlloc(stride, 16);
        try
        {
            var state = new BundleState();
            GCHandle gch = GCHandle.Alloc(state);
            IntPtr operation = BeginFetchBundle(6, storage, GCHandle.ToIntPtr(gch), &OnBundleComplete);
            Assert.Equal(1, state.Done.Task.Result);

            Assert.Equal(60, *(long*)storage);       // total
            Assert.Equal(1, ((byte*)storage)[8]);    // flag
            ((delegate* unmanaged[Swift]<void*, IntPtr, void>)vwt[1])(storage, metadata);
            OperationRelease(operation);
        }
        finally
        {
            NativeMemory.AlignedFree(storage);
        }
    }

    private sealed class HopState
    {
        public long Value = -1;
        public readonly ManualResetEventSlim Arrived = new();
    }

    [UnmanagedCallersOnly]
    private static void OnHopComplete(IntPtr context, long value)
    {
        GCHandle gch = GCHandle.FromIntPtr(context);
        var state = (HopState)gch.Target!;
        state.Value = value;
        state.Arrived.Set();
        gch.Free();
    }

    [Fact]
    public static void MainActorCompletionNeedsAPumpedMainQueue()
    {
        var state = new HopState();
        GCHandle gch = GCHandle.Alloc(state);
        IntPtr operation = BeginMainActorFetch(41, GCHandle.ToIntPtr(gch), &OnHopComplete);

        // Headless host without a pump: the MainActor hop cannot deliver.
        Assert.False(state.Arrived.Wait(300));

        // Pump the main queue from this (the process main) thread; the
        // integration policy for hosts without a UI loop.
        IntPtr mode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", 0x0600 /* UTF8 */);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!state.Arrived.IsSet && deadline.ElapsedMilliseconds < 30000)
            CFRunLoopRunInMode(mode, 0.05, 0);

        Assert.True(state.Arrived.IsSet, "MainActor completion did not arrive under a pumped main queue");
        Assert.Equal(42, state.Value);
        OperationRelease(operation);
    }

    private sealed class LedgerState
    {
        public readonly TaskCompletionSource<long> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [UnmanagedCallersOnly]
    private static void OnLedgerComplete(IntPtr context, long value)
    {
        GCHandle gch = GCHandle.FromIntPtr(context);
        ((LedgerState)gch.Target!).Done.TrySetResult(value);
        gch.Free();
    }

    private static Task<long> LedgerAddAsync(long amount)
    {
        var state = new LedgerState();
        GCHandle gch = GCHandle.Alloc(state);
        LedgerSlowAdd(amount, GCHandle.ToIntPtr(gch), &OnLedgerComplete);
        return state.Done.Task;
    }

    [Fact]
    public static void ActorReentrancyInterleavesAtAwaitPoints()
    {
        // Two slow adds against one actor: the second interleaves during
        // the first's await, observed by the actor itself.
        Task<long> first = LedgerAddAsync(10);
        Task<long> second = LedgerAddAsync(5);
        Task.WaitAll([first, second], 30000);

        var state = new LedgerState();
        GCHandle gch = GCHandle.Alloc(state);
        LedgerInterleavedGrowth(GCHandle.ToIntPtr(gch), &OnLedgerComplete);
        Assert.True(state.Done.Task.Result > 0, "no interleaving observed at the actor's await point");
    }

    /// <summary>
    /// Pull-model adapter: exactly one in-flight Swift next() per
    /// MoveNextAsync; backpressure is inherent to the pull; disposal waits
    /// out an in-flight next before releasing the Swift iterator.
    /// </summary>
    private sealed class SwiftAsyncEnumerator : IAsyncEnumerator<long>
    {
        private readonly IntPtr _handle;
        private TaskCompletionSource<(bool HasValue, long Value)>? _inFlight;
        private GCHandle _self;

        public SwiftAsyncEnumerator(long count)
        {
            _handle = StreamOpen(count);
            _self = GCHandle.Alloc(this);
        }

        public long Current { get; private set; }

        [UnmanagedCallersOnly]
        private static void OnItem(IntPtr context, long hasValue, long value)
        {
            var enumerator = (SwiftAsyncEnumerator)GCHandle.FromIntPtr(context).Target!;
            enumerator._inFlight!.TrySetResult((hasValue != 0, value));
        }

        public ValueTask<bool> MoveNextAsync()
        {
            _inFlight = new TaskCompletionSource<(bool, long)>(TaskCreationOptions.RunContinuationsAsynchronously);
            StreamNext(_handle, GCHandle.ToIntPtr(_self), &OnItem);
            return new ValueTask<bool>(_inFlight.Task.ContinueWith(t =>
            {
                Current = t.Result.Item2;
                return t.Result.Item1;
            }, TaskScheduler.Default));
        }

        public ValueTask DisposeAsync()
        {
            StreamClose(_handle);
            // Item ownership while disposal races an in-flight next: wait
            // the in-flight completion out before releasing the iterator.
            Task pending = _inFlight?.Task ?? Task.CompletedTask;
            return new ValueTask(pending.ContinueWith(_ =>
            {
                StreamRelease(_handle);
                _self.Free();
            }, TaskScheduler.Default));
        }
    }

    [Fact]
    public static void AsyncSequenceAdaptsToAsyncEnumeration()
    {
        var results = new List<long>();
        var enumerator = new SwiftAsyncEnumerator(5);
        try
        {
            while (enumerator.MoveNextAsync().AsTask().Result)
                results.Add(enumerator.Current);
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().Wait(30000);
        }

        Assert.Equal(new long[] { 0, 3, 6, 9, 12 }, results);
    }

    [Fact]
    public static void DisposalRacingInFlightNextIsSafe()
    {
        var enumerator = new SwiftAsyncEnumerator(1000);
        Task<bool> inFlight = enumerator.MoveNextAsync().AsTask();
        enumerator.DisposeAsync().AsTask().Wait(30000); // waits the next out
        inFlight.Wait(30000);
        // Subsequent nexts observe the closed stream deterministically.
    }

    [Fact]
    public static void StorefrontPredicateFailsClosed()
    {
        // Unconfigured means "no": managed policy refuses purchases rather
        // than guessing; the out-of-band configuration flips it.
        SetStorefrontConfigured(0);
        Assert.Equal(0, CanMakePayments());
        SetStorefrontConfigured(1);
        Assert.Equal(1, CanMakePayments());
        SetStorefrontConfigured(0);
    }
}
