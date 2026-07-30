// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// Direct generic Swift calls from closed, nongeneric managed entry points
// (roadmap Phase 6 generics). IR-verified physical convention: opaque
// values by pointer, generic returns through the indirect-result register,
// then Self type metadata, then witness tables in requirement declaration
// order:
//   applyDoubler<T: Doubler>          (ptr, T, T.Doubler) -> i64
//   combineRequirements<T: D & T>     (ptr, T, T.Doubler, T.Tagger) -> i64
//   genericSum<T: AdditiveArithmetic> (sret, ptr, ptr, T, T.AA)
// Witness tables come from the exported conformance symbols or from
// swift_conformsToProtocol; both routes are exercised.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftGenericCalls
{
    private const string SwiftLib = "libSwiftGenericCalls.dylib";
    private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls12applyDoublerys5Int64VxAA0E0RzlF")]
    private static extern long ApplyDoubler(void* value, IntPtr typeMetadata, IntPtr doublerWitness);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls19combineRequirementsys5Int64VxAA7DoublerRzAA6TaggerRzlF")]
    private static extern long CombineRequirements(void* value, IntPtr typeMetadata, IntPtr doublerWitness, IntPtr taggerWitness);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls10genericSumyxx_xts18AdditiveArithmeticRzlF")]
    private static extern void GenericSum(SwiftIndirectResult result, void* a, void* b, IntPtr typeMetadata, IntPtr arithmeticWitness);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls6MarkedVMa")]
    private static extern MetadataResponse MarkedMa(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls21resilientCellMetadataypXpyF")]
    private static extern IntPtr ResilientCellMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls21makeResilientCellInto_6stored5extraySv_s5Int64Vs4Int8VtF")]
    private static extern void MakeResilientCellInto(void* p, long stored, sbyte extra);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls13int64MetadataypXpyF")]
    private static extern IntPtr Int64Metadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls14doubleMetadataypXpyF")]
    private static extern IntPtr DoubleMetadata();

    [DllImport(SwiftCoreLib)]
    private static extern IntPtr swift_conformsToProtocol(IntPtr metadata, IntPtr protocolDescriptor);

    [StructLayout(LayoutKind.Sequential)]
    private struct MetadataResponse
    {
        public IntPtr Metadata;
        public nint State;
    }

    // The managed mirror of Marked (a frozen { Int64 } struct) only backs
    // stack storage whose address feeds the opaque generic parameter.
    [StructLayout(LayoutKind.Sequential)]
    private struct Marked
    {
        public long Value;
    }

    private static IntPtr s_lib;

    private static IntPtr Lib
    {
        get
        {
            if (s_lib == IntPtr.Zero)
                s_lib = NativeLibrary.Load(SwiftLib, Assembly.GetExecutingAssembly(), null);
            return s_lib;
        }
    }

    private static IntPtr MarkedMetadata()
    {
        MetadataResponse response = MarkedMa(0);
        Assert.Equal(0, (long)response.State);
        return response.Metadata;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls19boxedMarkedMetadataypXpyF")]
    private static extern IntPtr BoxedMarkedMetadata();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s17SwiftGenericCalls16boxedIntMetadataypXpyF")]
    private static extern IntPtr BoxedIntMetadata();

    [Fact]
    public static void ConditionalConformanceResolvesPerInstantiation()
    {
        IntPtr doublerDescriptor = NativeLibrary.GetExport(Lib, "$s17SwiftGenericCalls7DoublerMp");

        // Box<Marked> satisfies 'where T: Doubler': the runtime lookup
        // returns the conditional witness table and the generic call works.
        IntPtr conditionalWitness = swift_conformsToProtocol(BoxedMarkedMetadata(), doublerDescriptor);
        Assert.NotEqual(IntPtr.Zero, conditionalWitness);

        var boxed = new Marked { Value = 21 }; // Box<Marked> == { Marked } == { Int64 }
        Assert.Equal(21 * 2 + 1, ApplyDoubler(&boxed, BoxedMarkedMetadata(), conditionalWitness));

        // Box<Int64> does not satisfy the condition: fail-closed null.
        Assert.Equal(IntPtr.Zero, swift_conformsToProtocol(BoxedIntMetadata(), doublerDescriptor));
    }

    [Fact]
    public static void GenericCallWithSingleRequirement()
    {
        IntPtr witness = NativeLibrary.GetExport(Lib, "$s17SwiftGenericCalls6MarkedVAA7DoublerAAWP");
        var value = new Marked { Value = 21 };

        Assert.Equal(42, ApplyDoubler(&value, MarkedMetadata(), witness));
    }

    [Fact]
    public static void WitnessTablesFollowRequirementDeclarationOrder()
    {
        // Two distinct witness tables, passed in the declaration order of
        // the requirements (T: Doubler & Tagger -> T.Doubler, T.Tagger).
        IntPtr doubler = NativeLibrary.GetExport(Lib, "$s17SwiftGenericCalls6MarkedVAA7DoublerAAWP");
        IntPtr tagger = NativeLibrary.GetExport(Lib, "$s17SwiftGenericCalls6MarkedVAA6TaggerAAWP");
        Assert.NotEqual(doubler, tagger);

        var value = new Marked { Value = 21 };
        Assert.Equal(21 * 2 + 21 + 1000, CombineRequirements(&value, MarkedMetadata(), doubler, tagger));
    }

    [Fact]
    public static void GenericOverPrimitivesThroughRuntimeConformanceLookup()
    {
        // Runtime conformance lookup against a standard-library protocol:
        // the same route a generated binding without an exported WP symbol
        // must take.
        IntPtr arithmetic = NativeLibrary.GetExport(
            NativeLibrary.Load(SwiftCoreLib), "$ss18AdditiveArithmeticMp");

        IntPtr int64Metadata = Int64Metadata();
        IntPtr int64Witness = swift_conformsToProtocol(int64Metadata, arithmetic);
        Assert.NotEqual(IntPtr.Zero, int64Witness);

        long a = 40, b = 2, sum = 0;
        GenericSum(new SwiftIndirectResult(&sum), &a, &b, int64Metadata, int64Witness);
        Assert.Equal(42, sum);

        IntPtr doubleMetadata = DoubleMetadata();
        IntPtr doubleWitness = swift_conformsToProtocol(doubleMetadata, arithmetic);
        Assert.NotEqual(IntPtr.Zero, doubleWitness);

        double x = 1.25, y = 2.25, result = 0;
        GenericSum(new SwiftIndirectResult(&result), &x, &y, doubleMetadata, doubleWitness);
        Assert.Equal(3.5, result);
    }

    [Fact]
    public static void ResilientValueFlowsThroughOpaqueGenericParameter()
    {
        // A resilient value produced into caller-owned opaque storage and
        // consumed through a generic entry point: the closed, nongeneric
        // interop shape for resilient generics.
        IntPtr metadata = ResilientCellMetadata();
        IntPtr witness = NativeLibrary.GetExport(Lib, "$s17SwiftGenericCalls13ResilientCellVAA7DoublerAAWP");

        byte* storage = stackalloc byte[16];
        MakeResilientCellInto(storage, 10, 5);
        Assert.Equal(25, ApplyDoubler(storage, metadata, witness));
    }
}
