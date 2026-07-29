// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Text;
using Xunit;
using TestLibrary;

// Phase 7 errors + enums: a generic SwiftException over the owning error
// handle with known-mapping support, the optional NSError bridge (error
// boxes are NSError-compatible on Darwin), typed-throws normalization
// through a thunk, managed-exception containment in reverse callbacks with
// Swift-side translation, unknown-resilient-case preservation, and
// borrow-safe payload extraction (project a copy, never the borrowed
// original).
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftErrorsAndEnums
{
    private const string SwiftLib = "libSwiftErrorsAndEnums.dylib";
    private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums17throwCatalogErroryys5Int64VKF")]
    private static extern void ThrowCatalogError(long code, SwiftError* error);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums23skthunk_fetchNormalizedys5Int64VAD_SpyADGtF")]
    private static extern long FetchNormalized(long value, long* output);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums31skthunk_translateManagedFailureys5Int64VSvSg_AdEXCtF")]
    private static extern long TranslateManagedFailure(IntPtr context, delegate* unmanaged<IntPtr, long> callback);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums15makeChannelIntoyySv_s5Int64VADtF")]
    private static extern void MakeChannelInto(void* p, long kind, long payload);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums15channelDescribeys5Int64VSVF")]
    private static extern long ChannelDescribe(void* p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums15channelMetadataypXpyF")]
    private static extern IntPtr ChannelMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums15makeTrackedIntoyySv_s5Int64VtF")]
    private static extern void MakeTrackedInto(void* p, long value);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums12trackedValueys5Int64VSVF")]
    private static extern long TrackedValue(void* p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums15trackedMetadataypXpyF")]
    private static extern IntPtr TrackedMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums11counterLives5Int64VyF")]
    private static extern long CounterLive();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftErrorsAndEnums23skthunk_bridgeToNSErroryS2vF")]
    private static extern IntPtr BridgeToNSError(IntPtr errorBox);

    [DllImport(SwiftCoreLib)]
    private static extern void swift_errorRelease(IntPtr error);

    [DllImport(SwiftCoreLib)]
    private static extern void swift_unknownObjectRelease(IntPtr obj);

    [DllImport(ObjCLib)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern long objc_msgSend_Int64(IntPtr receiver, IntPtr selector);

    // ---- Generic SwiftException over the owning error handle ----

    private sealed class SwiftErrorHandle : SafeHandle
    {
        public SwiftErrorHandle(IntPtr owned)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(owned);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            swift_errorRelease(handle);
            return true;
        }
    }

    /// <summary>Generic managed surface of a Swift error: owns the box.</summary>
    private class SwiftException : Exception
    {
        public SwiftErrorHandle Error { get; }

        public SwiftException(SwiftErrorHandle error, string message)
            : base(message)
        {
            Error = error;
        }

        /// <summary>Known-mapping factory: frameworks map codes to specific types.</summary>
        public static SwiftException Create(IntPtr errorBox)
        {
            var handle = new SwiftErrorHandle(errorBox);
            // The NSError bridge runs in the thunk (a raw box's lazy ObjC
            // accessors need Foundation bridging hooks a pure host process
            // cannot rely on); the bridged object answers objc_msgSend.
            IntPtr bridged = BridgeToNSError(errorBox);
            long code = objc_msgSend_Int64(bridged, sel_registerName("code"));
            swift_unknownObjectRelease(bridged);
            return code == 404
                ? new CatalogNotFoundException(handle)
                : new SwiftException(handle, $"Swift error (code {code})");
        }
    }

    private sealed class CatalogNotFoundException : SwiftException
    {
        public CatalogNotFoundException(SwiftErrorHandle error)
            : base(error, "catalog entry not found")
        {
        }
    }

    private static void InvokeThrowing(long code)
    {
        SwiftError error = default;
        ThrowCatalogError(code, &error);
        if (error.Value != null)
            throw SwiftException.Create((IntPtr)error.Value);
    }

    [Fact]
    public static void SwiftExceptionWrapsOwningHandle()
    {
        var exception = Assert.Throws<SwiftException>(() => InvokeThrowing(11));
        Assert.False(exception.Error.IsInvalid);
        exception.Error.Dispose();
    }

    [Fact]
    public static void KnownFrameworkErrorMapping()
    {
        // The known-code mapping selects the specific exception type.
        var notFound = Assert.Throws<CatalogNotFoundException>(() => InvokeThrowing(404));
        notFound.Error.Dispose();
    }

    [Fact]
    public static void NsErrorBridgeExposesCode()
    {
        SwiftError error = default;
        ThrowCatalogError(77, &error);
        Assert.True(error.Value != null);

        // Bridge in the thunk, then read the code through objc_msgSend on
        // the bridged NSError without touching Swift internals.
        IntPtr bridged = BridgeToNSError((IntPtr)error.Value);
        Assert.Equal(77, objc_msgSend_Int64(bridged, sel_registerName("code")));
        swift_unknownObjectRelease(bridged);
        swift_errorRelease((IntPtr)error.Value);
    }

    [Fact]
    public static void TypedThrowsNormalizesThroughThunk()
    {
        long output = 0;
        Assert.Equal(0, FetchNormalized(21, &output));
        Assert.Equal(42, output);

        // The typed error never crosses raw: it arrives as its code.
        Assert.Equal(-5, FetchNormalized(-5, &output));
    }

    [UnmanagedCallersOnly]
    private static long ThrowingManagedCallback(IntPtr context)
    {
        try
        {
            throw new InvalidOperationException("managed failure");
        }
        catch (InvalidOperationException)
        {
            // Every managed exception is caught at the boundary and reported
            // as a code; nothing propagates into Swift frames.
            return 13;
        }
    }

    [Fact]
    public static void ManagedExceptionsAreCaughtAndTranslated()
    {
        // Swift receives the failure code, throws a real Swift error, and
        // the test observes the translated result.
        Assert.Equal(-13, TranslateManagedFailure(IntPtr.Zero, &ThrowingManagedCallback));
    }

    // ---- Enums ----

    private static void DestroyWith(IntPtr metadata, void* value)
    {
        void** vwt = *(void***)((byte*)metadata - sizeof(nint));
        ((delegate* unmanaged[Swift]<void*, IntPtr, void>)vwt[1])(value, metadata);
    }

    [Fact]
    public static void UnknownResilientCasesArePreservedNotSwitched()
    {
        // The client policy for resilient enums: hold the value opaquely and
        // pass it back; never enumerate tags beyond the known set. Kind 2
        // plays the "added after the client compiled" case.
        IntPtr metadata = ChannelMetadata();
        byte* storage = stackalloc byte[16];

        MakeChannelInto(storage, 2, 9);
        // No managed switch: the value round-trips to Swift intact.
        Assert.Equal(9000, ChannelDescribe(storage));
        DestroyWith(metadata, storage);

        MakeChannelInto(storage, 1, 5);
        Assert.Equal(5, ChannelDescribe(storage));
        DestroyWith(metadata, storage);
    }

    [Fact]
    public static void PayloadExtractionNeverCorruptsBorrowedValues()
    {
        IntPtr metadata = TrackedMetadata();
        void** vwt = *(void***)((byte*)metadata - sizeof(nint));
        long before = CounterLive();

        byte* original = stackalloc byte[16];
        MakeTrackedInto(original, 123);
        Assert.Equal(before + 1, CounterLive());

        // Destructive projection is only legal on an owned COPY: copy via
        // the VWT, project the copy, and the borrowed original stays valid.
        byte* copy = stackalloc byte[16];
        ((delegate* unmanaged[Swift]<void*, void*, IntPtr, void*>)vwt[2])(copy, original, metadata);
        Assert.Equal(before + 1, CounterLive()); // same instance, retained

        ((delegate* unmanaged[Swift]<void*, IntPtr, void>)vwt[12])(copy, metadata); // project the copy
        IntPtr payload = *(IntPtr*)copy; // the class reference payload
        Assert.NotEqual(IntPtr.Zero, payload);

        // The original is untouched and fully usable after the projection.
        Assert.Equal(123, TrackedValue(original));

        // Re-inject the copy so it can be destroyed as a valid enum.
        ((delegate* unmanaged[Swift]<void*, uint, IntPtr, void>)vwt[13])(copy, 0, metadata);
        DestroyWith(metadata, copy);
        DestroyWith(metadata, original);
        Assert.Equal(before, CounterLive()); // ARC balanced exactly
    }
}
