// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Threading;
using Xunit;
using TestLibrary;

// Phase 7 existentials + closures (probe-verified layouts): opaque
// existential containers ({3-word buffer, metadata, witness table}, 40
// bytes; small values inline, large values boxed), protocol compositions
// (one extra witness table per protocol), class-bound existentials
// ({object, witness table}, 16 bytes), container lifetime through the
// EXISTENTIAL type's own VWT, managed witness dispatch for the inline case
// with thunk-routed dispatch everywhere else, PATs through a closed
// generic thunk, returned Swift closures ({fn, context} with context in
// the self register), and escaping managed callbacks boxed with
// exactly-once GCHandle release.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftExistentialsAndClosures
{
    private const string SwiftLib = "libSwiftExistentialsAndClosures.dylib";
    private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures26speakerExistentialMetadataypXpyF")]
    private static extern IntPtr SpeakerExistentialMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures24comboExistentialMetadataypXpyF")]
    private static extern IntPtr ComboExistentialMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures29refSpeakerExistentialMetadataypXpyF")]
    private static extern IntPtr RefSpeakerExistentialMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures20makeSmallSpeakerIntoyySv_s5Int64VtF")]
    private static extern void MakeSmallSpeakerInto(void* p, long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures18makeBigSpeakerIntoyySv_s5Int64VtF")]
    private static extern void MakeBigSpeakerInto(void* p, long seed);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures13makeComboIntoyySv_s5Int64VtF")]
    private static extern void MakeComboInto(void* p, long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures18makeRefSpeakerIntoyySv_s5Int64VtF")]
    private static extern void MakeRefSpeakerInto(void* p, long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures13liveRefThingss5Int64VyF")]
    private static extern long LiveRefThings();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures13skthunk_speakys5Int64VSVF")]
    private static extern long ThunkSpeak(void* container);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures16skthunk_comboSumys5Int64VSVF")]
    private static extern long ThunkComboSum(void* container);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures16skthunk_refSpeakys5Int64VSVF")]
    private static extern long ThunkRefSpeak(void* container);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures22skthunk_runIntProducerys5Int64VADF")]
    private static extern long RunIntProducer(long seed);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures9makeAdderys5Int64VADcADF")]
    private static extern SwiftClosure MakeAdder(long baseValue);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures21skthunk_storeEscapingyySv_s5Int64VSv_ADtXCySvXCtF")]
    private static extern void StoreEscaping(
        IntPtr context,
        delegate* unmanaged<IntPtr, long, long> invoke,
        delegate* unmanaged<IntPtr, void> onRelease);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures20skthunk_invokeStoredys5Int64VADF")]
    private static extern long InvokeStored(long value);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s28SwiftExistentialsAndClosures19skthunk_clearStoredyyF")]
    private static extern void ClearStored();

    [DllImport(SwiftCoreLib)]
    private static extern void swift_release(IntPtr obj);

    /// <summary>A thick Swift function value: entry point plus retained context box.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SwiftClosure
    {
        public IntPtr Function;
        public IntPtr Context;
    }

    private static void** Vwt(IntPtr metadata) => *(void***)((byte*)metadata - sizeof(nint));

    private static (nuint Size, nuint Stride) VwtSizes(IntPtr metadata)
    {
        void** vwt = Vwt(metadata);
        return (*(nuint*)((byte*)vwt + 8 * sizeof(nint)), *(nuint*)((byte*)vwt + 9 * sizeof(nint)));
    }

    private static void CopyWith(IntPtr metadata, void* dest, void* src)
        => ((delegate* unmanaged[Swift]<void*, void*, IntPtr, void*>)Vwt(metadata)[2])(dest, src, metadata);

    private static void DestroyWith(IntPtr metadata, void* value)
        => ((delegate* unmanaged[Swift]<void*, IntPtr, void>)Vwt(metadata)[1])(value, metadata);

    [Fact]
    public static void OpaqueExistentialContainerLifetime()
    {
        // The existential type's own VWT understands container semantics
        // (inline versus boxed) so copy/destroy are correct for both.
        IntPtr metadata = SpeakerExistentialMetadata();
        Assert.Equal(((nuint)40, (nuint)40), VwtSizes(metadata));

        byte* small = stackalloc byte[40];
        MakeSmallSpeakerInto(small, 21);
        Assert.Equal(21, ThunkSpeak(small));

        byte* big = stackalloc byte[40];
        MakeBigSpeakerInto(big, 10); // 10+11+12+13 = 46, boxed payload
        byte* bigCopy = stackalloc byte[40];
        CopyWith(metadata, bigCopy, big);
        DestroyWith(metadata, big);
        Assert.Equal(46, ThunkSpeak(bigCopy)); // the copy owns the box
        DestroyWith(metadata, bigCopy);
        DestroyWith(metadata, small);
    }

    [Fact]
    public static void ManagedWitnessDispatchForInlinePayloads()
    {
        // Container: [0..2] inline buffer, [3] payload metadata, [4] witness
        // table. For inline payloads the witness self is the buffer address.
        byte* container = stackalloc byte[40];
        MakeSmallSpeakerInto(container, 33);

        IntPtr payloadMetadata = ((IntPtr*)container)[3];
        IntPtr witnessTable = ((IntPtr*)container)[4];
        Assert.NotEqual(IntPtr.Zero, payloadMetadata);
        Assert.NotEqual(IntPtr.Zero, witnessTable);

        var speak = (delegate* unmanaged[Swift]<IntPtr, IntPtr, SwiftSelf, long>)((IntPtr*)witnessTable)[1];
        Assert.Equal(33, speak(payloadMetadata, witnessTable, new SwiftSelf(container)));

        DestroyWith(SpeakerExistentialMetadata(), container);
    }

    [Fact]
    public static void CompositionCarriesOneWitnessTablePerProtocol()
    {
        IntPtr metadata = ComboExistentialMetadata();
        Assert.Equal(((nuint)48, (nuint)48), VwtSizes(metadata)); // + one wtable

        byte* container = stackalloc byte[48];
        MakeComboInto(container, 4);

        // Composition witness tables follow CANONICAL protocol order (the
        // sorted order the mangler uses), not source order: for
        // 'Speaker & Louder' the Louder table comes first (empirically
        // verified — using source order dispatches the wrong method).
        IntPtr louderTable = ((IntPtr*)container)[4];
        IntPtr speakerTable = ((IntPtr*)container)[5];
        Assert.NotEqual(speakerTable, louderTable);

        IntPtr payloadMetadata = ((IntPtr*)container)[3];
        var loudness = (delegate* unmanaged[Swift]<IntPtr, IntPtr, SwiftSelf, long>)((IntPtr*)louderTable)[1];
        var speak = (delegate* unmanaged[Swift]<IntPtr, IntPtr, SwiftSelf, long>)((IntPtr*)speakerTable)[1];
        Assert.Equal(40, loudness(payloadMetadata, louderTable, new SwiftSelf(container)));
        Assert.Equal(4, speak(payloadMetadata, speakerTable, new SwiftSelf(container)));
        Assert.Equal(44, ThunkComboSum(container));

        DestroyWith(metadata, container);
    }

    [Fact]
    public static void ClassExistentialStorageIsTwoWordsWithArc()
    {
        IntPtr metadata = RefSpeakerExistentialMetadata();
        Assert.Equal(((nuint)16, (nuint)16), VwtSizes(metadata));
        long before = LiveRefThings();

        byte* container = stackalloc byte[16];
        MakeRefSpeakerInto(container, 8);
        Assert.Equal(before + 1, LiveRefThings());
        Assert.Equal(16, ThunkRefSpeak(container));

        byte* copy = stackalloc byte[16];
        CopyWith(metadata, copy, container);
        Assert.Equal(before + 1, LiveRefThings()); // same instance, retained
        DestroyWith(metadata, container);
        Assert.Equal(before + 1, LiveRefThings()); // alive via the copy
        Assert.Equal(16, ThunkRefSpeak(copy));
        DestroyWith(metadata, copy);
        Assert.Equal(before, LiveRefThings());
    }

    [Fact]
    public static void AssociatedTypesGoThroughClosedGenericThunks()
    {
        Assert.Equal(42, RunIntProducer(6));
    }

    [Fact]
    public static void ReturnedSwiftClosureInvokesWithContextInSelfRegister()
    {
        SwiftClosure adder = MakeAdder(100);
        Assert.NotEqual(IntPtr.Zero, adder.Function);
        Assert.NotEqual(IntPtr.Zero, adder.Context);

        var invoke = (delegate* unmanaged[Swift]<long, SwiftSelf, long>)adder.Function;
        Assert.Equal(105, invoke(5, new SwiftSelf((void*)adder.Context)));
        Assert.Equal(142, invoke(42, new SwiftSelf((void*)adder.Context)));

        swift_release(adder.Context); // the context box is +1 owned
    }

    // ---- Escaping managed callback with exactly-once release ----

    private static int s_releases;
    private static long s_multiplier;

    [UnmanagedCallersOnly]
    private static long InvokeCallback(IntPtr context, long value)
    {
        var target = (StrongBox<long>)GCHandle.FromIntPtr(context).Target!;
        return value * target.Value;
    }

    [UnmanagedCallersOnly]
    private static void ReleaseCallback(IntPtr context)
    {
        GCHandle.FromIntPtr(context).Free();
        Interlocked.Increment(ref s_releases);
    }

    [Fact]
    public static void EscapingCallbackBalancesGcHandleAndArcExactlyOnce()
    {
        s_releases = 0;
        var state = new StrongBox<long>(3);
        GCHandle gch = GCHandle.Alloc(state);

        StoreEscaping(GCHandle.ToIntPtr(gch), &InvokeCallback, &ReleaseCallback);
        Assert.Equal(12, InvokeStored(4));
        Assert.Equal(30, InvokeStored(10));
        Assert.Equal(0, s_releases); // still stored: no release yet

        ClearStored(); // the box deinits: GCHandle freed exactly once
        Assert.Equal(1, s_releases);
        Assert.Equal(-1, InvokeStored(1)); // nothing stored anymore
    }

    [UnmanagedCallersOnly]
    private static long ReentrantCallback(IntPtr context, long value)
    {
        // Re-enter Swift from inside the callback: the stored closure calls
        // back into this method for the inner value.
        if (value > 0)
            return value + InvokeStored(-value);
        return value * 100;
    }

    [Fact]
    public static void ReentrantInvocationAndConcurrentClearAreSafe()
    {
        s_releases = 0;
        var state = new StrongBox<long>(1);
        GCHandle gch = GCHandle.Alloc(state);
        StoreEscaping(GCHandle.ToIntPtr(gch), &ReentrantCallback, &ReleaseCallback);

        // Reentrancy: outer(5) -> inner(-5) -> -500; 5 + (-500) = -495.
        Assert.Equal(-495, InvokeStored(5));

        // Concurrent invocations racing a clear: ARC keeps the box alive
        // through in-flight calls; the release still happens exactly once.
        var threads = new Thread[4];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(() =>
            {
                for (int n = 0; n < 1000; n++)
                    InvokeStored(-1);
            });
            threads[i].Start();
        }

        Thread.Sleep(5);
        ClearStored();
        foreach (Thread thread in threads)
            thread.Join();

        Assert.Equal(1, s_releases);
    }
}
