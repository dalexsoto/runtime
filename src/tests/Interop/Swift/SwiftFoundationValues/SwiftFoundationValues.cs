// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Text;
using Xunit;

// Standard Foundation values (roadmap Phase 9): UUID, Date, Data, and
// Decimal — the value types StoreKit's surface is built from. Each is
// resolved through its metadata, sized from its VWT, constructed by Swift
// into managed-owned storage, read back, copied and destroyed through the
// VWT. Decimal is the interesting one: it is an IMPORTED ObjC type
// (NSDecimal) with no exported Swift metadata accessor, so it must be
// reached through a rooted accessor rather than a direct Ma import.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftFoundationValues
{
    private const string Lib = "libSwiftFoundationValues.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues12uuidMetadataypXpyF")]
    private static extern IntPtr UuidMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues12dateMetadataypXpyF")]
    private static extern IntPtr DateMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues12dataMetadataypXpyF")]
    private static extern IntPtr DataMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues15decimalMetadataypXpyF")]
    private static extern IntPtr DecimalMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues8makeUuidyySPys5UInt8VG_SvtF")]
    private static extern void MakeUuid(byte* bytes, void* output);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues9uuidBytesyySV_Spys5UInt8VGtF")]
    private static extern void UuidBytes(void* uuid, byte* output);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues8makeDateyySd_SvtF")]
    private static extern void MakeDate(double secondsSinceReferenceDate, void* output);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues11dateSecondsySdSVF")]
    private static extern double DateSeconds(void* date);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues8makeDatayySPys5UInt8VG_s5Int64VSvtF")]
    private static extern void MakeData(byte* bytes, long count, void* output);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues9dataCountys5Int64VSVF")]
    private static extern long DataCount(void* data);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues8dataByteys5UInt8VSV_s5Int64VtF")]
    private static extern byte DataByte(void* data, long index);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues11makeDecimalyySPys5UInt8VG_s5Int64VSvtF")]
    private static extern void MakeDecimal(byte* utf8, long len, void* output);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s21SwiftFoundationValues13decimalDoubleySdSVF")]
    private static extern double DecimalDouble(void* value);

    // Pointers cannot be tuple elements (CS0306), so the VWT travels as IntPtr.
    private static (IntPtr Vwt, nuint Size, nuint Stride) Layout(IntPtr metadata)
    {
        Assert.NotEqual(IntPtr.Zero, metadata);
        void** vwt = *(void***)((byte*)metadata - sizeof(nint));
        return ((IntPtr)vwt, *(nuint*)((byte*)vwt + 8 * sizeof(nint)), *(nuint*)((byte*)vwt + 9 * sizeof(nint)));
    }

    private static void Destroy(IntPtr vwt, void* value, IntPtr metadata)
        => ((delegate* unmanaged[Swift]<void*, IntPtr, void>)((void**)vwt)[1])(value, metadata);

    private static void* CopyInto(IntPtr vwt, void* dest, void* src, IntPtr metadata)
        => ((delegate* unmanaged[Swift]<void*, void*, IntPtr, void*>)((void**)vwt)[2])(dest, src, metadata);

    [Fact]
    public static void UuidRoundTripsThroughMetadataAndVwt()
    {
        IntPtr metadata = UuidMetadata();
        var (vwt, size, stride) = Layout(metadata);
        Assert.Equal(16u, (uint)size);

        byte[] input = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
                        0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10];
        byte* storage = stackalloc byte[(int)stride];
        byte* readBack = stackalloc byte[16];

        fixed (byte* p = input)
        {
            MakeUuid(p, storage);
        }
        UuidBytes(storage, readBack);
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(input[i], readBack[i]);
        }

        Destroy(vwt, storage, metadata);
    }

    [Fact]
    public static void DateRoundTripsAcrossTheAbi()
    {
        IntPtr metadata = DateMetadata();
        var (vwt, size, stride) = Layout(metadata);
        Assert.Equal(8u, (uint)size); // a single Double

        const double Seconds = 700_000_000.5; // reference-date form is ABI-stable
        byte* storage = stackalloc byte[(int)stride];
        MakeDate(Seconds, storage);
        Assert.Equal(Seconds, DateSeconds(storage), 6);
        Destroy(vwt, storage, metadata);
    }

    [Fact]
    public static void DataIsHeapBackedAndLifecyclesThroughTheVwt()
    {
        IntPtr metadata = DataMetadata();
        var (vwt, _, stride) = Layout(metadata);

        // Large enough to force heap storage rather than an inline buffer.
        byte[] payload = new byte[512];
        for (int i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i & 0xFF);
        }

        byte* storage = stackalloc byte[(int)stride];
        byte* copy = stackalloc byte[(int)stride];

        fixed (byte* p = payload)
        {
            MakeData(p, payload.Length, storage);
        }
        Assert.Equal(payload.Length, DataCount(storage));
        Assert.Equal(payload[511], DataByte(storage, 511));

        // A VWT copy is an independent +1: both must read correctly, and both
        // must be destroyed.
        CopyInto(vwt, copy, storage, metadata);
        Assert.Equal(payload.Length, DataCount(copy));
        Assert.Equal(payload[100], DataByte(copy, 100));

        Destroy(vwt, copy, metadata);
        Assert.Equal(payload.Length, DataCount(storage)); // copy's release did not free the original
        Destroy(vwt, storage, metadata);
    }

    [Fact]
    public static void DecimalReachesMetadataThroughARootedAccessor()
    {
        // Decimal is an imported ObjC type (NSDecimal): there is no exported
        // Swift metadata accessor to import directly, so the binding must go
        // through a rooted accessor. This is the shape StoreKit prices need.
        IntPtr metadata = DecimalMetadata();
        var (vwt, size, stride) = Layout(metadata);
        Assert.Equal(20u, (uint)size); // NSDecimal is 20 bytes

        byte[] text = Encoding.UTF8.GetBytes("19.99");
        byte* storage = stackalloc byte[(int)stride];
        fixed (byte* p = text)
        {
            MakeDecimal(p, text.Length, storage);
        }

        Assert.Equal(19.99, DecimalDouble(storage), 6);
        Destroy(vwt, storage, metadata);
    }
}
