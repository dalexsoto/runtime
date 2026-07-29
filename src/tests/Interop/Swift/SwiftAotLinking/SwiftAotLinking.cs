// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// NativeAOT linking coverage (roadmap Phase 2):
// - eager P/Invokes: the fixture dylib is DirectPInvoke-bound at link time
//   under NativeAOT (and loads lazily via dlopen everywhere else);
// - generated native Swift asset linking: a Swift static archive is linked
//   INTO the test binary and its functions, resilient values, classes, and
//   metadata accessor all resolve at link time;
// - static system-framework imports: the system Swift runtime
//   (libswiftCore) is bound through its SDK TBD stub instead of dlopen.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftAotLinking
{
    private const string DylibLib = "libSwiftAotLinking.dylib";
    private const string StaticLib = "SwiftAotStatic";
    private const string EagerSwiftCoreLib = "libswiftCore";
    private const string LazySwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct MetadataResponse
    {
        public IntPtr Metadata;
        public nint State; // 0 == complete
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(DylibLib, EntryPoint = "$s15SwiftAotLinking8dylibAdd1a1bS2i_SitF")]
    private static extern nint DylibAdd(nint a, nint b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(DylibLib, EntryPoint = "$s15SwiftAotLinking14makeDylibValue1x1yAA0eF0VSi_SdtF")]
    private static extern void MakeDylibValue(SwiftIndirectResult result, nint x, double y);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(DylibLib, EntryPoint = "$s15SwiftAotLinking13dylibValueSumySdAA05DylibE0VF")]
    private static extern double DylibValueSum(void* value);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(DylibLib, EntryPoint = "$s15SwiftAotLinking10DylibValueVMa")]
    private static extern MetadataResponse DylibValueMa(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StaticLib, EntryPoint = "$s14SwiftAotStatic9staticAdd1a1bS2i_SitF")]
    private static extern nint StaticAdd(nint a, nint b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StaticLib, EntryPoint = "$s14SwiftAotStatic04makeC5Value1x1yAA0cE0VSi_SitF")]
    private static extern void MakeStaticValue(SwiftIndirectResult result, nint x, nint y);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StaticLib, EntryPoint = "$s14SwiftAotStatic14staticValueSumySiAA0cE0VF")]
    private static extern nint StaticValueSum(void* value);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StaticLib, EntryPoint = "$s14SwiftAotStatic0C5ValueVMa")]
    private static extern MetadataResponse StaticValueMa(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StaticLib, EntryPoint = "$s14SwiftAotStatic04makeC3Box7payloadAA0cE0CSi_tF")]
    private static extern IntPtr MakeStaticBox(nint payload);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StaticLib, EntryPoint = "$s14SwiftAotStatic16staticBoxPayloadySiAA0cE0CF")]
    private static extern nint StaticBoxPayload(IntPtr box);

    // Eager, link-time-bound imports of the system Swift runtime (AOT only;
    // the plain module name has no dlopen fallback elsewhere).
    [DllImport(EagerSwiftCoreLib, EntryPoint = "swift_retain")]
    private static extern IntPtr EagerSwiftRetain(IntPtr obj);

    [DllImport(EagerSwiftCoreLib, EntryPoint = "swift_release")]
    private static extern void EagerSwiftRelease(IntPtr obj);

    [DllImport(LazySwiftCoreLib, EntryPoint = "swift_release")]
    private static extern void LazySwiftRelease(IntPtr obj);

    private static void ValidateMetadataResponse(MetadataResponse response)
    {
        Assert.Equal(0, (long)response.State);
        Assert.NotEqual(IntPtr.Zero, response.Metadata);

        // VWT prefix sanity: size <= stride, power-of-two alignment.
        void** vwt = *(void***)((byte*)response.Metadata - sizeof(nint));
        nuint size = *(nuint*)((byte*)vwt + 8 * sizeof(nint));
        nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));
        uint flags = *(uint*)((byte*)vwt + 10 * sizeof(nint));
        Assert.True(size > 0 && size <= stride);
        Assert.True(nuint.IsPow2((flags & 0xFF) + 1));
    }

    [Fact]
    public static void DylibBindingWorks()
    {
        // Under NativeAOT this import is DirectPInvoke: the symbol was
        // resolved by the linker and the dylib loads eagerly with the
        // process. Elsewhere it is an ordinary lazy dlopen P/Invoke.
        Assert.Equal(30, (long)DylibAdd(10, 20));

        void* buffer = NativeMemory.AlignedAlloc(64, 16);
        try
        {
            MakeDylibValue(new SwiftIndirectResult(buffer), 4, 2.5);
            Assert.Equal(6.5, DylibValueSum(buffer));
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
        }

        // The metadata accessor is imported as a plain entry point; under
        // NativeAOT it is rooted through the link-time symbol reference.
        ValidateMetadataResponse(DylibValueMa(0));
    }

    [Fact]
    public static void StaticArchiveLinksAndRuns()
    {
        if (!TestLibrary.Utilities.IsNativeAot)
            return; // static linking exists only in the NativeAOT build

        Assert.Equal(30, (long)StaticAdd(10, 20));

        // Resilient value round-trip entirely within statically linked code:
        // indirect-result construction, opaque argument passing.
        void* buffer = NativeMemory.AlignedAlloc(64, 16);
        try
        {
            MakeStaticValue(new SwiftIndirectResult(buffer), 12, 30);
            Assert.Equal(42, (long)StaticValueSum(buffer));
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
        }

        // Metadata/conformance data of the statically linked type is
        // reachable through its rooted accessor symbol after dead-stripping.
        ValidateMetadataResponse(StaticValueMa(0));
    }

    [Fact]
    public static void StaticSystemSwiftRuntimeImports()
    {
        if (!TestLibrary.Utilities.IsNativeAot)
            return; // the TBD-bound module name exists only under NativeAOT

        // A class instance allocated by statically linked Swift code, with
        // ARC balanced through the eagerly bound system Swift runtime.
        IntPtr box = MakeStaticBox(99);
        Assert.NotEqual(IntPtr.Zero, box);
        Assert.Equal(99, (long)StaticBoxPayload(box));

        IntPtr retained = EagerSwiftRetain(box);
        Assert.Equal(box, retained);
        Assert.Equal(99, (long)StaticBoxPayload(box));

        EagerSwiftRelease(box);
        Assert.Equal(99, (long)StaticBoxPayload(box)); // still alive: +1 left
        EagerSwiftRelease(box);
    }

    [Fact]
    public static void LazySystemSwiftRuntimeStillWorks()
    {
        // The absolute-path system import stays lazy in every mode; both
        // binding shapes for the same library must coexist in one binary.
        IntPtr box;
        if (TestLibrary.Utilities.IsNativeAot)
        {
            box = MakeStaticBox(7);
        }
        else
        {
            // No statically linked fixture outside NativeAOT; nothing to
            // release, but the lazy import itself must still resolve.
            LazySwiftRelease(IntPtr.Zero);
            return;
        }

        Assert.Equal(7, (long)StaticBoxPayload(box));
        LazySwiftRelease(box);
    }
}
