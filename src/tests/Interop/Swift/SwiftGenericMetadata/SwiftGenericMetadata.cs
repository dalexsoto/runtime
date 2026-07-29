// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// Generic metadata instantiation (roadmap Phase 6): stdlib generic
// accessors called with direct trailing metadata arguments
// (Optional/Array), the array form for generic contexts with more than
// three parameters (a pointer to a metadata argument buffer,
// probe-verified), and a SwiftArray<Int64> skeleton whose lifetime runs
// through the instantiated metadata's value witness table.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftGenericMetadata
{
    private const string SwiftLib = "libSwiftGenericMetadata.dylib";
    private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct MetadataResponse
    {
        public IntPtr Metadata;
        public nint State;
    }

    // Stdlib generic accessors: request plus direct metadata arguments.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftCoreLib, EntryPoint = "$sSqMa")]
    private static extern MetadataResponse OptionalMa(long request, IntPtr elementMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftCoreLib, EntryPoint = "$sSaMa")]
    private static extern MetadataResponse ArrayMa(long request, IntPtr elementMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftCoreLib, EntryPoint = "$sSa5countSivg")]
    private static extern nint ArrayCount(SwiftArrayInt64 array, IntPtr elementMetadata);

    // More than three generic parameters: the accessor takes a pointer to a
    // buffer of metadata arguments instead of direct parameters.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata4QuadVMa")]
    private static extern MetadataResponse QuadMa(long request, IntPtr* argumentBuffer);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata04quadC0ypXpyF")]
    private static extern IntPtr QuadMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata05int64C0ypXpyF")]
    private static extern IntPtr Int64Metadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata06doubleC0ypXpyF")]
    private static extern IntPtr DoubleMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata04int8C0ypXpyF")]
    private static extern IntPtr Int8Metadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata05int32C0ypXpyF")]
    private static extern IntPtr Int32Metadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata011optionalIntC0ypXpyF")]
    private static extern IntPtr OptionalIntMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata08arrayIntC0ypXpyF")]
    private static extern IntPtr ArrayIntMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata12makeIntArray5countSays5Int64VGAE_tF")]
    private static extern SwiftArrayInt64 MakeIntArray(long count);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata8arraySumys5Int64VSayADGF")]
    private static extern long ArraySum(SwiftArrayInt64 array);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata13appendToArrayyySays5Int64VGz_ADtF")]
    private static extern void AppendToArray(SwiftArrayInt64* array, long value);

    /// <summary>One-word Array&lt;Int64&gt; mirror; lifetime via the instantiated VWT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SwiftArrayInt64 : IDisposable
    {
        private IntPtr _buffer;

        private static IntPtr Metadata => ArrayIntMetadata();

        private static void** Vwt(IntPtr metadata) => *(void***)((byte*)metadata - sizeof(nint));

        public readonly int Count => (int)ArrayCount(this, Int64Metadata());

        public readonly SwiftArrayInt64 Copy()
        {
            IntPtr metadata = Metadata;
            SwiftArrayInt64 copy;
            fixed (SwiftArrayInt64* self = &this)
            {
                ((delegate* unmanaged[Swift]<void*, void*, IntPtr, void*>)Vwt(metadata)[2])(&copy, self, metadata);
            }
            return copy;
        }

        public void Dispose()
        {
            IntPtr metadata = Metadata;
            fixed (SwiftArrayInt64* self = &this)
            {
                ((delegate* unmanaged[Swift]<void*, IntPtr, void>)Vwt(metadata)[1])(self, metadata);
            }
            _buffer = IntPtr.Zero;
        }
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata11optionalSumys5Int64VADSgF")]
    private static extern long OptionalSum(SwiftOptionalInt64 v);

    /// <summary>
    /// Optional&lt;Int64&gt; skeleton: payload word plus tag byte (the Swift
    /// layout for a payload with no spare bits), constructed and inspected
    /// purely through the enum value witnesses. Lowering (i64, i8) lets it
    /// pass by value.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SwiftOptionalInt64
    {
        private long _payload;
        private byte _tag;

        private static IntPtr Metadata => OptionalIntMetadata();

        private static void** Vwt => *(void***)((byte*)Metadata - sizeof(nint));

        public static SwiftOptionalInt64 Some(long value)
        {
            SwiftOptionalInt64 result = default;
            result._payload = value;
            ((delegate* unmanaged[Swift]<void*, uint, IntPtr, void>)Vwt[13])(&result, 0, Metadata);
            return result;
        }

        public static SwiftOptionalInt64 None()
        {
            SwiftOptionalInt64 result = default;
            ((delegate* unmanaged[Swift]<void*, uint, IntPtr, void>)Vwt[13])(&result, 1, Metadata);
            return result;
        }

        public readonly bool HasValue
        {
            get
            {
                fixed (SwiftOptionalInt64* self = &this)
                {
                    return ((delegate* unmanaged[Swift]<void*, IntPtr, uint>)Vwt[11])(self, Metadata) == 0;
                }
            }
        }

        public long Value
        {
            get
            {
                fixed (SwiftOptionalInt64* self = &this)
                {
                    ((delegate* unmanaged[Swift]<void*, IntPtr, void>)Vwt[12])(self, Metadata);
                    long value = self->_payload;
                    ((delegate* unmanaged[Swift]<void*, uint, IntPtr, void>)Vwt[13])(self, 0, Metadata);
                    return value;
                }
            }
        }
    }

    [Fact]
    public static void OptionalSkeletonRoundTripsByValue()
    {
        SwiftOptionalInt64 some = SwiftOptionalInt64.Some(42);
        Assert.True(some.HasValue);
        Assert.Equal(42, some.Value);
        Assert.Equal(42, OptionalSum(some)); // (i64, i8) by-value lowering

        SwiftOptionalInt64 none = SwiftOptionalInt64.None();
        Assert.False(none.HasValue);
        Assert.Equal(-1, OptionalSum(none));
    }

    // Set and Dictionary: generic contexts carrying a Hashable witness.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftCoreLib, EntryPoint = "$sShMa")]
    private static extern MetadataResponse SetMa(long request, IntPtr elementMetadata, IntPtr hashableWitness);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftCoreLib, EntryPoint = "$sSDMa")]
    private static extern MetadataResponse DictionaryMa(long request, IntPtr keyMetadata, IntPtr valueMetadata, IntPtr keyHashableWitness);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftCoreLib, EntryPoint = "$sSh5countSivg")]
    private static extern nint SetCount(SwiftHandleWord set, IntPtr elementMetadata, IntPtr hashableWitness);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftCoreLib, EntryPoint = "$sSD5countSivg")]
    private static extern nint DictionaryCount(SwiftHandleWord dict, IntPtr keyMetadata, IntPtr valueMetadata, IntPtr keyHashableWitness);

    [DllImport(SwiftCoreLib)]
    private static extern IntPtr swift_conformsToProtocol(IntPtr metadata, IntPtr protocolDescriptor);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata10makeIntSet5countShys5Int64VGAE_tF")]
    private static extern SwiftHandleWord MakeIntSet(long count);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata11setContainsys5Int64VShyADG_ADtF")]
    private static extern long SetContains(SwiftHandleWord set, long value);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata06setIntC0ypXpyF")]
    private static extern IntPtr SetIntMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata11makeIntDict5countSDys5Int64VAEGAE_tF")]
    private static extern SwiftHandleWord MakeIntDict(long count);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata10dictLookupys5Int64VSDyA2DG_ADtF")]
    private static extern long DictLookup(SwiftHandleWord dict, long key);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata07dictIntC0ypXpyF")]
    private static extern IntPtr DictIntMetadata();

    /// <summary>A one-word Swift collection handle destroyed through a given metadata's VWT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SwiftHandleWord
    {
        private IntPtr _storage;

        public void DisposeWith(IntPtr metadata)
        {
            void** vwt = *(void***)((byte*)metadata - sizeof(nint));
            fixed (SwiftHandleWord* self = &this)
            {
                ((delegate* unmanaged[Swift]<void*, IntPtr, void>)vwt[1])(self, metadata);
            }
            _storage = IntPtr.Zero;
        }
    }

    private static IntPtr Int64HashableWitness()
    {
        IntPtr hashable = NativeLibrary.GetExport(NativeLibrary.Load(SwiftCoreLib), "$sSHMp");
        IntPtr witness = swift_conformsToProtocol(Int64Metadata(), hashable);
        Assert.NotEqual(IntPtr.Zero, witness);
        return witness;
    }

    [Fact]
    public static void SetInstantiatesAndRoundTrips()
    {
        // Set<T: Hashable>: the accessor and members carry the Hashable
        // witness after the element metadata.
        MetadataResponse response = SetMa(0, Int64Metadata(), Int64HashableWitness());
        Assert.Equal(0, (long)response.State);
        Assert.Equal(SetIntMetadata(), response.Metadata);

        SwiftHandleWord set = MakeIntSet(4); // {0, 5, 10, 15}
        Assert.Equal(4, (int)SetCount(set, Int64Metadata(), Int64HashableWitness()));
        Assert.Equal(1, SetContains(set, 10));
        Assert.Equal(0, SetContains(set, 11));
        set.DisposeWith(response.Metadata);
    }

    [Fact]
    public static void DictionaryInstantiatesAndRoundTrips()
    {
        MetadataResponse response = DictionaryMa(0, Int64Metadata(), Int64Metadata(), Int64HashableWitness());
        Assert.Equal(0, (long)response.State);
        Assert.Equal(DictIntMetadata(), response.Metadata);

        SwiftHandleWord dict = MakeIntDict(5); // i -> i*7
        Assert.Equal(5, (int)DictionaryCount(dict, Int64Metadata(), Int64Metadata(), Int64HashableWitness()));
        Assert.Equal(21, DictLookup(dict, 3));
        Assert.Equal(-1, DictLookup(dict, 99));
        dict.DisposeWith(response.Metadata);
    }

    private static (nuint Size, nuint Stride) VwtSizes(IntPtr metadata)
    {
        void** vwt = *(void***)((byte*)metadata - sizeof(nint));
        return (*(nuint*)((byte*)vwt + 8 * sizeof(nint)), *(nuint*)((byte*)vwt + 9 * sizeof(nint)));
    }

    private static uint ExtraInhabitants(IntPtr metadata)
    {
        void** vwt = *(void***)((byte*)metadata - sizeof(nint));
        return *(uint*)((byte*)vwt + 10 * sizeof(nint) + 4);
    }

    // Single-payload tag witnesses (VWT slots 6/7): valid on EVERY type;
    // the compiler owns the spare-bit encodings.
    private static uint GetEnumTagSinglePayload(void* value, uint emptyCases, IntPtr metadata)
    {
        void** vwt = *(void***)((byte*)metadata - sizeof(nint));
        return ((delegate* unmanaged[Swift]<void*, uint, IntPtr, uint>)vwt[6])(value, emptyCases, metadata);
    }

    private static void StoreEnumTagSinglePayload(void* value, uint whichCase, uint emptyCases, IntPtr metadata)
    {
        void** vwt = *(void***)((byte*)metadata - sizeof(nint));
        ((delegate* unmanaged[Swift]<void*, uint, uint, IntPtr, void>)vwt[7])(value, whichCase, emptyCases, metadata);
    }

    [Fact]
    public static void BoolSpareBitsDiscriminateThroughWitnesses()
    {
        // Swift Bool: one byte, 0/1 payload, 254 extra inhabitants —
        // Optional<Bool> (and Optional<Optional<Bool>>) stay one byte with
        // nil encoded as 2 (probe-verified). Managed mirrors never claim
        // spare bits themselves; discrimination goes through the
        // single-payload witnesses so the encodings stay compiler-owned.
        IntPtr boolMetadata = NativeLibrary.GetExport(NativeLibrary.Load(SwiftCoreLib), "$sSbN");
        Assert.Equal(254u, ExtraInhabitants(boolMetadata));

        MetadataResponse optionalBool = OptionalMa(0, boolMetadata);
        Assert.Equal(0, (long)optionalBool.State);
        Assert.Equal(((nuint)1, (nuint)1), VwtSizes(optionalBool.Metadata));

        byte storage = 1; // true
        Assert.Equal(0u, GetEnumTagSinglePayload(&storage, 1, boolMetadata)); // payload
        storage = 0; // false
        Assert.Equal(0u, GetEnumTagSinglePayload(&storage, 1, boolMetadata));

        StoreEnumTagSinglePayload(&storage, 1, 1, boolMetadata); // write 'nil'
        Assert.Equal(2, storage); // the first extra inhabitant
        Assert.Equal(1u, GetEnumTagSinglePayload(&storage, 1, boolMetadata));
    }

    [Fact]
    public static void ObjectPointerSpareBitsEncodeNilAsZero()
    {
        // A class-reference word (Array's buffer ref) has pointer extra
        // inhabitants: nil is the zero word, and Optional<Array<Int64>>
        // stays one word (probe-verified).
        IntPtr arrayMetadata = ArrayIntMetadata();
        Assert.True(ExtraInhabitants(arrayMetadata) > 0);

        MetadataResponse optionalArray = OptionalMa(0, arrayMetadata);
        Assert.Equal(0, (long)optionalArray.State);
        Assert.Equal(((nuint)8, (nuint)8), VwtSizes(optionalArray.Metadata));

        ulong nilWord = 0;
        Assert.Equal(1u, GetEnumTagSinglePayload(&nilWord, 1, arrayMetadata)); // nil

        SwiftArrayInt64 array = MakeIntArray(2);
        Assert.Equal(0u, GetEnumTagSinglePayload(&array, 1, arrayMetadata)); // payload
        array.Dispose();
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata11scaledCount5count5scale4biass5Int64VAG_A2GtF")]
    private static extern long ScaledCount(long count, long scale, long bias);

    // Without library evolution, a public stored var exports its address
    // accessor (vau) rather than a getter; read the value through it.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata15configuredCounts5Int64Vvau")]
    private static extern long* ConfiguredCountAddress();

    private static long ConfiguredCount() => *ConfiguredCountAddress();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s20SwiftGenericMetadata18setConfiguredCountyys5Int64VF")]
    private static extern void SetConfiguredCount(long value);

    [Fact]
    public static void DefaultArgumentsMaterializeFromPublicApi()
    {
        // Default-argument generator symbols are never part of the stable
        // ABI (they stay local even under library evolution); clients
        // re-emit the serialized default expressions, which the compiler
        // guarantees reference only public declarations. A binding does the
        // same: 'count' defaults to the public configuredCount (read via
        // its getter), 'scale' to the literal 10 from the interface.
        long countDefault = ConfiguredCount();
        long scaleDefault = 10;
        Assert.Equal(3 * 10 + 1, ScaledCount(countDefault, scaleDefault, 1));

        // The materialized default tracks the public state, exactly like a
        // Swift caller.
        SetConfiguredCount(5);
        Assert.Equal(5 * 10 + 1, ScaledCount(ConfiguredCount(), scaleDefault, 1));
        SetConfiguredCount(3);
    }

    [Fact]
    public static void StdlibGenericAccessorsWithDirectArguments()
    {
        MetadataResponse optional = OptionalMa(0, Int64Metadata());
        Assert.Equal(0, (long)optional.State);
        Assert.Equal(OptionalIntMetadata(), optional.Metadata); // same instantiation

        MetadataResponse array = ArrayMa(0, Int64Metadata());
        Assert.Equal(0, (long)array.State);
        Assert.Equal(ArrayIntMetadata(), array.Metadata);
        Assert.Equal(((nuint)8, (nuint)8), VwtSizes(array.Metadata)); // one word
    }

    [Fact]
    public static void LargeGenericContextUsesArgumentBuffer()
    {
        // Four generic parameters: pass a buffer of metadata pointers.
        IntPtr* arguments = stackalloc IntPtr[4]
        {
            Int64Metadata(), DoubleMetadata(), Int8Metadata(), Int32Metadata(),
        };

        MetadataResponse response = QuadMa(0, arguments);
        Assert.Equal(0, (long)response.State);
        Assert.Equal(QuadMetadata(), response.Metadata);

        // Quad<Int64, Double, Int8, Int32>: a@0, b@8, c@16, d@20 -> 24/24.
        (nuint size, nuint stride) = VwtSizes(response.Metadata);
        Assert.Equal(24u, (uint)size);
        Assert.Equal(24u, (uint)stride);
    }

    [Fact]
    public static void ArraySkeletonOwnsCountsAndSums()
    {
        SwiftArrayInt64 array = MakeIntArray(5); // 0,3,6,9,12
        Assert.Equal(5, array.Count);
        Assert.Equal(30, ArraySum(array));
        array.Dispose();
    }

    [Fact]
    public static void ArrayCopyIsIndependentlyOwned()
    {
        SwiftArrayInt64 array = MakeIntArray(4); // 0,3,6,9
        SwiftArrayInt64 copy = array.Copy();

        array.Dispose();
        Assert.Equal(4, copy.Count); // survives the original
        Assert.Equal(18, ArraySum(copy));
        copy.Dispose();
    }

    [Fact]
    public static void InoutArrayAppendsInPlace()
    {
        SwiftArrayInt64 array = MakeIntArray(3); // 0,3,6
        AppendToArray(&array, 100);              // inout: may reallocate the word
        Assert.Equal(4, array.Count);
        Assert.Equal(109, ArraySum(array));
        array.Dispose();
    }
}
