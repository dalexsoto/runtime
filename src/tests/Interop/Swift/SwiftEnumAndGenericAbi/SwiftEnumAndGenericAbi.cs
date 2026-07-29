// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// Frozen generic struct instantiations and the enum matrix (roadmap
// Phase 5): Optional, nested Optional, multi-payload, and generic enums
// inspected through the value-witness enum interface, plus concrete
// generic-struct instantiations crossing the ABI by value. Layout numbers
// (sizes, strides, tag order, nested-optional growth) are probe-verified
// against the Swift compiler.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftEnumAndGenericAbi
{
    private const string SwiftLib = "libSwiftEnumAndGenericAbi.dylib";

    [StructLayout(LayoutKind.Sequential)]
    public struct PairInt64
    {
        public long First;
        public sbyte Second;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PairDouble
    {
        public double First;
        public sbyte Second;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi10sumPairIntys5Int64VAA0G0VyADGF")]
    private static extern long SumPairInt(PairInt64 p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi13sumPairDoubleySdAA0G0VySdGF")]
    private static extern double SumPairDouble(PairDouble p);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi11makePairIntyAA0G0Vys5Int64VGAF_s4Int8VtF")]
    private static extern PairInt64 MakePairInt(long a, sbyte b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi15pairIntMetadataypXpyF")]
    private static extern IntPtr PairIntMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi19optionalIntMetadataypXpyF")]
    private static extern IntPtr OptionalIntMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi25nestedOptionalIntMetadataypXpyF")]
    private static extern IntPtr NestedOptionalIntMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi20multiPayloadMetadataypXpyF")]
    private static extern IntPtr MultiPayloadMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi21genericBoxIntMetadataypXpyF")]
    private static extern IntPtr GenericBoxIntMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi20makeMultiPayloadInto_4kind7payloadySv_Sis5Int64VtF")]
    private static extern void MakeMultiPayloadInto(void* p, nint kind, long payload);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi19makeOptionalIntInto_8hasValue7payloadySv_Sbs5Int64VtF")]
    private static extern void MakeOptionalIntInto(void* p, byte hasValue, long payload);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s22SwiftEnumAndGenericAbi04makeD10BoxIntInto_8hasValue7payloadySv_Sbs5Int64VtF")]
    private static extern void MakeGenericBoxIntInto(void* p, byte hasValue, long payload);

    // Minimal VWT view (probe-verified slots; see support-library.md).
    private readonly struct Vwt
    {
        private readonly void** _slots;

        public Vwt(IntPtr metadata) => _slots = *(void***)((byte*)metadata - sizeof(nint));

        public nuint Size => *(nuint*)((byte*)_slots + 8 * sizeof(nint));
        public nuint Stride => *(nuint*)((byte*)_slots + 9 * sizeof(nint));
        public uint Flags => *(uint*)((byte*)_slots + 10 * sizeof(nint));
        public uint ExtraInhabitants => *(uint*)((byte*)_slots + 10 * sizeof(nint) + 4);
        public nuint Alignment => (nuint)((Flags & 0xFF) + 1);
        public bool HasEnumWitnesses => (Flags & 0x0020_0000) != 0;

        public void Destroy(void* value, IntPtr metadata)
            => ((delegate* unmanaged[Swift]<void*, IntPtr, void>)_slots[1])(value, metadata);

        public uint GetEnumTag(void* value, IntPtr metadata)
            => ((delegate* unmanaged[Swift]<void*, IntPtr, uint>)_slots[11])(value, metadata);

        public void ProjectPayload(void* value, IntPtr metadata)
            => ((delegate* unmanaged[Swift]<void*, IntPtr, void>)_slots[12])(value, metadata);

        public void InjectTag(void* value, uint tag, IntPtr metadata)
            => ((delegate* unmanaged[Swift]<void*, uint, IntPtr, void>)_slots[13])(value, tag, metadata);
    }

    [Fact]
    public static void FrozenGenericInstantiationsCrossByValue()
    {
        Assert.Equal(107, SumPairInt(new PairInt64 { First = 100, Second = 7 }));
        Assert.Equal(3.0, SumPairDouble(new PairDouble { First = 1.0, Second = 2 }));

        PairInt64 made = MakePairInt(123, 45);
        Assert.Equal(123, made.First);
        Assert.Equal(45, made.Second);
    }

    [Fact]
    public static void GenericInstantiationMetadataAgreesWithMirror()
    {
        var vwt = new Vwt(PairIntMetadata());
        Assert.Equal(9u, (uint)vwt.Size);      // unpadded: Int64 + Int8
        Assert.Equal(16u, (uint)vwt.Stride);
        Assert.Equal(8u, (uint)vwt.Alignment);
        // The managed sequential mirror has matching offsets and stride.
        Assert.Equal((uint)Unsafe.SizeOf<PairInt64>(), (uint)vwt.Stride);
    }

    [Fact]
    public static void OptionalIntIsSinglePayloadWithTagByte()
    {
        IntPtr metadata = OptionalIntMetadata();
        var vwt = new Vwt(metadata);
        Assert.True(vwt.HasEnumWitnesses);
        Assert.Equal(9u, (uint)vwt.Size); // Int64 has no spare bits: +1 tag byte

        byte* buffer = stackalloc byte[16];
        MakeOptionalIntInto(buffer, 1, 42);
        Assert.Equal(0u, vwt.GetEnumTag(buffer, metadata)); // payload case first
        vwt.ProjectPayload(buffer, metadata);
        Assert.Equal(42, *(long*)buffer);
        vwt.InjectTag(buffer, 0, metadata);
        vwt.Destroy(buffer, metadata);

        MakeOptionalIntInto(buffer, 0, 0);
        Assert.Equal(1u, vwt.GetEnumTag(buffer, metadata)); // none
        vwt.Destroy(buffer, metadata);
    }

    [Fact]
    public static void NestedOptionalAddsASecondTagByte()
    {
        var inner = new Vwt(OptionalIntMetadata());
        var nested = new Vwt(NestedOptionalIntMetadata());
        // Optional<Int64> has no spare values left for the outer level, so
        // nesting grows the size by exactly one more tag byte (9 -> 10)
        // while the stride stays 16.
        Assert.Equal(9u, (uint)inner.Size);
        Assert.Equal(10u, (uint)nested.Size);
        Assert.Equal(16u, (uint)nested.Stride);
        Assert.True(nested.HasEnumWitnesses);
    }

    [Fact]
    public static void MultiPayloadTagsFollowDeclarationOrder()
    {
        IntPtr metadata = MultiPayloadMetadata();
        var vwt = new Vwt(metadata);
        Assert.True(vwt.HasEnumWitnesses);
        Assert.Equal(9u, (uint)vwt.Size); // two 8-byte payloads + tag byte

        byte* buffer = stackalloc byte[16];

        // Payload cases take tags 0..n-1 in declaration order (a, b), then
        // the no-payload cases (c, d) continue the sequence.
        (nint kind, uint tag)[] cases = [(0, 0u), (1, 1u), (2, 2u), (3, 3u)];
        foreach ((nint kind, uint tag) in cases)
        {
            MakeMultiPayloadInto(buffer, kind, 84);
            Assert.Equal(tag, vwt.GetEnumTag(buffer, metadata));
            vwt.Destroy(buffer, metadata);
        }

        // Project each payload case destructively and read the raw payload.
        MakeMultiPayloadInto(buffer, 0, 84);
        vwt.ProjectPayload(buffer, metadata);
        Assert.Equal(84, *(long*)buffer);

        MakeMultiPayloadInto(buffer, 1, 84);
        vwt.ProjectPayload(buffer, metadata);
        Assert.Equal(42.0, *(double*)buffer); // fixture stores payload * 0.5
    }

    [Fact]
    public static void GenericEnumInstantiationBehavesLikeSinglePayload()
    {
        IntPtr metadata = GenericBoxIntMetadata();
        var vwt = new Vwt(metadata);
        Assert.True(vwt.HasEnumWitnesses);
        Assert.Equal(9u, (uint)vwt.Size);

        byte* buffer = stackalloc byte[16];
        MakeGenericBoxIntInto(buffer, 1, 77);
        Assert.Equal(0u, vwt.GetEnumTag(buffer, metadata)); // boxed
        vwt.ProjectPayload(buffer, metadata);
        Assert.Equal(77, *(long*)buffer);
        vwt.InjectTag(buffer, 0, metadata);
        vwt.Destroy(buffer, metadata);

        MakeGenericBoxIntInto(buffer, 0, 0);
        Assert.Equal(1u, vwt.GetEnumTag(buffer, metadata)); // empty
        vwt.Destroy(buffer, metadata);
    }
}
