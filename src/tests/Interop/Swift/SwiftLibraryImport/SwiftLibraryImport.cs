// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// LibraryImport with UnmanagedCallConv(CallConvSwift) (roadmap Phase 6
// "Emit raw native declarations with LibraryImport and
// UnmanagedCallConv(CallConvSwift)"): the source-generated interop path
// must preserve the Swift calling convention for blittable signatures,
// including struct lowering, struct returns, and the error register.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe partial class SwiftLibraryImport
{
    private const string SwiftLib = "libSwiftLibraryImport.dylib";

    [StructLayout(LayoutKind.Sequential)]
    public struct Payload
    {
        public long A;
        public double B;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [LibraryImport(SwiftLib, EntryPoint = "$s18SwiftLibraryImport10addPayloadys5Int64VAA0E0V_ADtF")]
    private static partial long AddPayload(Payload p, long delta);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [LibraryImport(SwiftLib, EntryPoint = "$s18SwiftLibraryImport11makePayload1a1bAA0E0Vs5Int64V_SdtF")]
    private static partial Payload MakePayload(long a, double b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [LibraryImport(SwiftLib, EntryPoint = "$s18SwiftLibraryImport12doubleOrFailys5Int64VADKF")]
    private static partial long DoubleOrFail(long value, SwiftError* error);

    [DllImport("/usr/lib/swift/libswiftCore.dylib")]
    private static extern void swift_errorRelease(IntPtr error);

    // Raw pointer/buffer wrappers (roadmap Phase 6): Swift's
    // UnsafeRawBufferPointer stores the start and END addresses (not a
    // count) and lowers like any frozen two-word struct; UnsafeRawPointer
    // is a single word.
    [StructLayout(LayoutKind.Sequential)]
    public struct SwiftRawBufferPointer
    {
        public IntPtr Position;
        public IntPtr End;

        public SwiftRawBufferPointer(void* baseAddress, nint count)
        {
            Position = (IntPtr)baseAddress;
            End = (IntPtr)((byte*)baseAddress + count);
        }
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [LibraryImport(SwiftLib, EntryPoint = "$s18SwiftLibraryImport12sumRawBufferys5Int64VSWF")]
    private static partial long SumRawBuffer(SwiftRawBufferPointer buffer);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [LibraryImport(SwiftLib, EntryPoint = "$s18SwiftLibraryImport10fillBuffer_4seedySw_s5UInt8VtF")]
    private static partial void FillBuffer(SwiftRawBufferPointer buffer, byte seed);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [LibraryImport(SwiftLib, EntryPoint = "$s18SwiftLibraryImport14advancePointer_2byS2V_s5Int64VtF")]
    private static partial IntPtr AdvancePointer(IntPtr p, long offset);

    [Fact]
    public static void RawBufferPointerRoundTrips()
    {
        byte* storage = stackalloc byte[8];
        var buffer = new SwiftRawBufferPointer(storage, 8);

        FillBuffer(buffer, 10); // Swift writes 10,11,...,17
        Assert.Equal(17, storage[7]);
        Assert.Equal(10 * 8 + 28, SumRawBuffer(buffer));
    }

    [Fact]
    public static void RawPointerRoundTrips()
    {
        byte* storage = stackalloc byte[16];
        Assert.Equal((IntPtr)(storage + 5), AdvancePointer((IntPtr)storage, 5));
    }

    [Fact]
    public static void StructArgumentLowersThroughLibraryImport()
    {
        Assert.Equal(115, AddPayload(new Payload { A = 100, B = 5.0 }, 10));
    }

    [Fact]
    public static void StructReturnLowersThroughLibraryImport()
    {
        Payload p = MakePayload(7, 2.5);
        Assert.Equal(7, p.A);
        Assert.Equal(2.5, p.B);
    }

    [Fact]
    public static void ErrorRegisterWorksThroughLibraryImport()
    {
        SwiftError error = default;
        Assert.Equal(42, DoubleOrFail(21, &error));
        Assert.True(error.Value == null);

        DoubleOrFail(-3, &error);
        Assert.True(error.Value != null);
        swift_errorRelease((IntPtr)error.Value);
    }
}
