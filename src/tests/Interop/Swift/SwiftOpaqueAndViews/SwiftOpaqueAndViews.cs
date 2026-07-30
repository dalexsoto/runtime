// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Text;
using Xunit;

// Phase 10: `some P`/`some View` opaque results, result-builder parameters,
// and headless SwiftUI view-value construction, all from managed code using
// the probe-verified recipe (abi-model.md "Opaque results, result builders,
// and parameter packs"): metadata and conformances from the exported opaque
// type descriptor, always-indirect results, VWT lifecycle.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftOpaqueAndViews
{
    private const string Lib = "libSwiftOpaqueAndViews.dylib";
    private const string CoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct MetadataResponse
    {
        public IntPtr Metadata;
        public nuint State;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(CoreLib)]
    private static extern MetadataResponse swift_getOpaqueTypeMetadata2(
        long request, IntPtr args, IntPtr descriptor, long index);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(CoreLib)]
    private static extern IntPtr swift_getOpaqueTypeConformance2(
        IntPtr args, IntPtr descriptor, long ordinal);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews9makeSmallyQrs5Int64VF")]
    private static extern void MakeSmall(SwiftIndirectResult result, long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews9makeLargeyQrs5Int64VF")]
    private static extern void MakeLarge(SwiftIndirectResult result, long x);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews9DescriberP8describes5Int64VyFTj")]
    private static extern long DescribeThunk(IntPtr metadata, IntPtr wtable, SwiftSelf self);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews11withBuilderys5Int64VADyXEF")]
    private static extern long WithBuilder(delegate* unmanaged[Swift]<SwiftSelf, long> fn, IntPtr ctx);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews12makeTextViewyQrs5Int64VF")]
    private static extern void MakeTextView(SwiftIndirectResult result, long n);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews19makeProductViewUtf8yQrSPys5UInt8VG_s5Int64VtF")]
    private static extern void MakeProductView(SwiftIndirectResult result, byte* utf8, long len);

    private static IntPtr Descriptor(string symbol)
        => NativeLibrary.GetExport(
            NativeLibrary.Load(Lib, System.Reflection.Assembly.GetExecutingAssembly(), null), symbol);

    private static (IntPtr Metadata, IntPtr Vwt, nuint Size, nuint Stride) OpaqueInfo(string descriptorSymbol)
    {
        IntPtr descriptor = Descriptor(descriptorSymbol);
        MetadataResponse response = swift_getOpaqueTypeMetadata2(0, IntPtr.Zero, descriptor, 0);
        Assert.NotEqual(IntPtr.Zero, response.Metadata);

        void** vwt = *(void***)((byte*)response.Metadata - sizeof(nint));
        nuint size = *(nuint*)((byte*)vwt + 8 * sizeof(nint));
        nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));
        return (response.Metadata, (IntPtr)vwt, size, stride);
    }

    private static void Destroy(IntPtr vwt, void* value, IntPtr metadata)
        => ((delegate* unmanaged[Swift]<void*, IntPtr, void>)((void**)vwt)[1])(value, metadata);

    [Fact]
    public static void OpaqueSmallReturnBindsViaDescriptor()
    {
        var (metadata, vwt, size, stride) = OpaqueInfo("$s19SwiftOpaqueAndViews9makeSmallyQrs5Int64VFQOMQ");
        Assert.Equal(8u, (uint)size);

        IntPtr wtable = swift_getOpaqueTypeConformance2(
            IntPtr.Zero, Descriptor("$s19SwiftOpaqueAndViews9makeSmallyQrs5Int64VFQOMQ"), 1);
        Assert.NotEqual(IntPtr.Zero, wtable);

        byte* buf = stackalloc byte[(int)stride];
        MakeSmall(new SwiftIndirectResult(buf), 42);
        Assert.Equal(42, DescribeThunk(metadata, wtable, new SwiftSelf(buf)));
        Destroy(vwt, buf, metadata);
    }

    [Fact]
    public static void OpaqueLargeReturnBindsViaDescriptor()
    {
        var (metadata, vwt, size, stride) = OpaqueInfo("$s19SwiftOpaqueAndViews9makeLargeyQrs5Int64VFQOMQ");
        Assert.Equal(40u, (uint)size);

        IntPtr wtable = swift_getOpaqueTypeConformance2(
            IntPtr.Zero, Descriptor("$s19SwiftOpaqueAndViews9makeLargeyQrs5Int64VFQOMQ"), 1);

        byte* buf = stackalloc byte[(int)stride];
        MakeLarge(new SwiftIndirectResult(buf), 100);
        Assert.Equal(104, DescribeThunk(metadata, wtable, new SwiftSelf(buf)));
        Destroy(vwt, buf, metadata);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long SevenBody(SwiftSelf ctx) => 7;

    [Fact]
    public static void ResultBuilderParameterTakesPlainManagedClosure()
    {
        Assert.Equal(7, WithBuilder(&SevenBody, IntPtr.Zero));
    }

    private static void ViewValueRoundTrip(string descriptorSymbol, Action<IntPtr> construct)
    {
        var (metadata, vwt, _, stride) = OpaqueInfo(descriptorSymbol);

        // some View: conformance ordinal 1 is the View table.
        IntPtr viewTable = swift_getOpaqueTypeConformance2(IntPtr.Zero, Descriptor(descriptorSymbol), 1);
        Assert.NotEqual(IntPtr.Zero, viewTable);

        byte* buf = stackalloc byte[(int)stride];
        construct((IntPtr)buf);

        // Lifecycle through the opaque metadata's VWT: copy then destroy both.
        byte* copy = stackalloc byte[(int)stride];
        ((delegate* unmanaged[Swift]<void*, void*, IntPtr, void*>)((void**)vwt)[2])(copy, buf, metadata);
        Destroy(vwt, copy, metadata);
        Destroy(vwt, buf, metadata);
    }

    // The fixed-arity erasure thunks the generator emits for parameter-pack
    // APIs (SWIFTGEN007): the pack is built Swift-side; managed sees plain
    // scalar signatures.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews8packSum2ys5Int64VAD_ADtF")]
    private static extern long PackSum2(long a, long b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews8packSum3ys5Int64VAD_A2DtF")]
    private static extern long PackSum3(long a, long b, long c);

    [Fact]
    public static void ParameterPackErasureThunksForwardCorrectly()
    {
        Assert.Equal(30, PackSum2(10, 20));
        Assert.Equal(60, PackSum3(10, 20, 30));
    }

    // Environment actions / property-wrapper-backed APIs (roadmap Phase 10):
    // EnvironmentValues and its openURL action bound DIRECTLY from the SDK
    // binary via their real manglings — resilient struct init (indirect
    // result), property getter (self pointer in the self register, resilient
    // result), and VWT lifecycle from Ma-resolved metadata.
    private const string SwiftUICoreLib = "/System/Library/Frameworks/SwiftUICore.framework/Versions/A/SwiftUICore";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftUICoreLib, EntryPoint = "$s7SwiftUI17EnvironmentValuesVMa")]
    private static extern MetadataResponse EnvironmentValuesMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftUICoreLib, EntryPoint = "$s7SwiftUI13OpenURLActionVMa")]
    private static extern MetadataResponse OpenURLActionMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftUICoreLib, EntryPoint = "$s7SwiftUI17EnvironmentValuesVACycfC")]
    private static extern void EnvironmentValuesInit(SwiftIndirectResult result);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftUICoreLib, EntryPoint = "$s7SwiftUI17EnvironmentValuesV7openURLAA13OpenURLActionVvg")]
    private static extern void EnvironmentValuesGetOpenURL(SwiftIndirectResult result, SwiftSelf self);

    private static (IntPtr Metadata, IntPtr Vwt, nuint Stride) MetadataInfo(MetadataResponse response)
    {
        Assert.NotEqual(IntPtr.Zero, response.Metadata);
        void** vwt = *(void***)((byte*)response.Metadata - sizeof(nint));
        return (response.Metadata, (IntPtr)vwt, *(nuint*)((byte*)vwt + 9 * sizeof(nint)));
    }

    // The invocation round-trip (Audit follow-ups): a managed handler backs
    // the action; callAsFunction is invoked DIRECTLY from managed via its
    // SDK mangling (URL passed indirectly as a leading pointer, the resilient
    // action as self); the handler observes the call.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews24makeHandledOpenURLActiony0A2UI0gH0Vs5Int64V_yAGXCtF")]
    private static extern void MakeHandledOpenURLAction(SwiftIndirectResult result, long token,
        delegate* unmanaged<long, void> handler);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews9urlStrides5Int64VyF")]
    private static extern long UrlStride();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews7makeURLyySPys5UInt8VG_s5Int64VSvtF")]
    private static extern void MakeUrl(byte* utf8, long len, void* output);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews10destroyURLyySvF")]
    private static extern void DestroyUrl(void* url);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftUICoreLib, EntryPoint = "$s7SwiftUI13OpenURLActionV14callAsFunctionyy10Foundation3URLVF")]
    private static extern void OpenURLActionCallAsFunction(IntPtr url, SwiftSelf action);

    private static long s_openUrlToken;

    [UnmanagedCallersOnly]
    private static void OnOpenUrl(long token) => s_openUrlToken = token;

    [Fact]
    public static void EnvironmentActionInvokesManagedHandler()
    {
        var (actMetadata, actVwt, actStride) = MetadataInfo(OpenURLActionMetadata(0));

        byte* action = (byte*)NativeMemory.AlignedAlloc(actStride, 16);
        byte* url = (byte*)NativeMemory.AlignedAlloc((nuint)UrlStride(), 16);
        try
        {
            MakeHandledOpenURLAction(new SwiftIndirectResult(action), 777, &OnOpenUrl);

            byte[] urlBytes = Encoding.UTF8.GetBytes("https://example.com/product");
            fixed (byte* p = urlBytes)
            {
                MakeUrl(p, urlBytes.Length, url);
            }

            s_openUrlToken = 0;
            OpenURLActionCallAsFunction((IntPtr)url, new SwiftSelf(action));
            Assert.Equal(777, s_openUrlToken);

            DestroyUrl(url);
            Destroy(actVwt, action, actMetadata);
        }
        finally
        {
            NativeMemory.AlignedFree(action);
            NativeMemory.AlignedFree(url);
        }
    }

    // Headless resilient Product (Phase 9 local slice): the REAL StoreKit
    // Product type introspected via its exported Ma, its async
    // products(for:) surface validated through the Tu contract, and the
    // generic machinery it needs (Array<String> instantiation + Collection
    // witness) exercised against SDK types. Executing the async call rides
    // the StoreKitTest lane (daemon behavior headless is undefined).
    private const string StoreKitLib = "/System/Library/Frameworks/StoreKit.framework/Versions/A/StoreKit";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductVMa")]
    private static extern MetadataResponse ProductMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(CoreLib, EntryPoint = "$sSaMa")]
    private static extern MetadataResponse ArrayMetadata(long request, IntPtr elementMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(CoreLib, EntryPoint = "$sS2ayxGycfC")]
    private static extern IntPtr ArrayEmptyInit(SwiftSelf arrayTypeMetadata);

    [DllImport(CoreLib)]
    private static extern IntPtr swift_conformsToProtocol(IntPtr metadata, IntPtr protocolDescriptor);

    [Fact]
    public static void ResilientProductIntrospectsHeadless()
    {
        MetadataResponse product = ProductMetadata(0);
        Assert.NotEqual(IntPtr.Zero, product.Metadata);

        // Struct metadata kind, VWT sane for a resilient value.
        Assert.Equal(0x200u, *(uint*)product.Metadata & 0xFFFu);
        void** vwt = *(void***)((byte*)product.Metadata - sizeof(nint));
        nuint size = *(nuint*)((byte*)vwt + 8 * sizeof(nint));
        nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));
        Assert.True(size > 0 && stride >= size);

        // The async products(for:) binding surface: the Tu record's context
        // size is the caller contract (abi-model.md; never baked into
        // bindings).
        IntPtr storeKit = NativeLibrary.Load(StoreKitLib);
        IntPtr tu = NativeLibrary.GetExport(storeKit,
            "$s8StoreKit7ProductV8products3forSayACGx_tYaKSlRzSS7ElementRtzlFZTu");
        uint contextSize = *(uint*)((byte*)tu + 4);
        Assert.True(contextSize >= 16 && contextSize % 8 == 0);
    }

    // Transaction, VerificationResult<Transaction>, and Product.PurchaseOption
    // (roadmap Phase 9): the remaining StoreKit types the purchase/entitlement
    // flows are built from. All three introspect HEADLESS — the daemon is only
    // needed to obtain VALUES, not to resolve the types, their layouts, or the
    // generic instantiation.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit11TransactionVMa")]
    private static extern MetadataResponse TransactionMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit18VerificationResultOMa")]
    private static extern MetadataResponse VerificationResultMetadata(long request, IntPtr payloadType);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV14PurchaseOptionVMa")]
    private static extern MetadataResponse PurchaseOptionMetadata(long request);

    [Fact]
    public static void TransactionIntrospectsHeadless()
    {
        MetadataResponse transaction = TransactionMetadata(0);
        Assert.NotEqual(IntPtr.Zero, transaction.Metadata);
        Assert.Equal(0x200u, *(uint*)transaction.Metadata & 0xFFFu); // struct kind

        void** vwt = *(void***)((byte*)transaction.Metadata - sizeof(nint));
        nuint size = *(nuint*)((byte*)vwt + 8 * sizeof(nint));
        nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));
        Assert.True(size > 0 && stride >= size);

        // Transaction is a resilient struct: managed code must never bake its
        // size in, which is exactly why the VWT is the sizing authority.
        Assert.True(stride % 8 == 0);
    }

    [Fact]
    public static void VerificationResultInstantiatesOverTransactionHeadless()
    {
        // VerificationResult<T> is the generic enum every purchase and
        // entitlement result arrives in. Instantiating it over the real
        // Transaction proves the generic machinery (metadata accessor taking
        // the payload type) works against the shipping framework.
        MetadataResponse transaction = TransactionMetadata(0);
        MetadataResponse verification = VerificationResultMetadata(0, transaction.Metadata);

        Assert.NotEqual(IntPtr.Zero, verification.Metadata);
        Assert.Equal(0x201u, *(uint*)verification.Metadata & 0xFFFu); // enum kind

        void** vwt = *(void***)((byte*)verification.Metadata - sizeof(nint));
        nuint size = *(nuint*)((byte*)vwt + 8 * sizeof(nint));
        nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));

        // The enum carries the payload, so it is at least Transaction-sized.
        void** payloadVwt = *(void***)((byte*)transaction.Metadata - sizeof(nint));
        nuint payloadSize = *(nuint*)((byte*)payloadVwt + 8 * sizeof(nint));
        Assert.True(size >= payloadSize);
        Assert.True(stride >= size);

        // Enum value witnesses are present: this is what the generated
        // verified/unverified switch dispatches through.
        uint flags = *(uint*)((byte*)vwt + 10 * sizeof(nint));
        Assert.NotEqual(0u, flags & 0x0020_0000u); // HasEnumWitnesses
    }

    [Fact]
    public static void PurchaseOptionIntrospectsHeadless()
    {
        MetadataResponse option = PurchaseOptionMetadata(0);
        Assert.NotEqual(IntPtr.Zero, option.Metadata);
        Assert.Equal(0x200u, *(uint*)option.Metadata & 0xFFFu);

        void** vwt = *(void***)((byte*)option.Metadata - sizeof(nint));
        nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));
        Assert.True(stride > 0);

        // Purchase options are passed as a Set<Product.PurchaseOption>: the
        // Hashable conformance the Set instantiation needs must resolve.
        IntPtr hashableDescriptor = NativeLibrary.GetExport(NativeLibrary.Load(CoreLib), "$sSHMp");
        IntPtr witness = swift_conformsToProtocol(option.Metadata, hashableDescriptor);
        Assert.NotEqual(IntPtr.Zero, witness);
    }

    // ---- Swift Charts: a SECOND framework beyond StoreKit, to prove the
    // opaque/generic/builder binding machinery generalizes. Charts is almost
    // entirely SwiftUI-shaped (`some ChartContent`/`some View` opaque results,
    // @ChartContentBuilder result builders, generic Marks over Plottable), so
    // resolving its metadata and conformances headless is a real stress test of
    // the same paths StoreKit's views exercised. Classification: 87.7% of 853
    // public declarations (validation.md "Second-framework validation").
    private const string ChartsLib = "/System/Library/Frameworks/Charts.framework/Versions/A/Charts";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(ChartsLib, EntryPoint = "$s6Charts7BarMarkVMa")]
    private static extern MetadataResponse BarMarkMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(ChartsLib, EntryPoint = "$s6Charts8LineMarkVMa")]
    private static extern MetadataResponse LineMarkMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(ChartsLib, EntryPoint = "$s6Charts9PointMarkVMa")]
    private static extern MetadataResponse PointMarkMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(ChartsLib, EntryPoint = "$s6Charts8AreaMarkVMa")]
    private static extern MetadataResponse AreaMarkMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(ChartsLib, EntryPoint = "$s6Charts8RuleMarkVMa")]
    private static extern MetadataResponse RuleMarkMetadata(long request);

    [Fact]
    public static void ChartsMarkFamilyIntrospectsHeadless()
    {
        // The five core Mark types are the heart of a Chart body. Each resolves
        // to valid struct metadata with a working value-witness table — the same
        // resolution a generated Charts binding would perform.
        foreach (var (name, accessor) in new (string, Func<long, MetadataResponse>)[]
        {
            ("BarMark", BarMarkMetadata), ("LineMark", LineMarkMetadata),
            ("PointMark", PointMarkMetadata), ("AreaMark", AreaMarkMetadata),
            ("RuleMark", RuleMarkMetadata),
        })
        {
            MetadataResponse md = accessor(0);
            Assert.NotEqual(IntPtr.Zero, md.Metadata);
            Assert.Equal(0x200u, *(uint*)md.Metadata & 0xFFFu); // struct kind

            void** vwt = *(void***)((byte*)md.Metadata - sizeof(nint));
            nuint size = *(nuint*)((byte*)vwt + 8 * sizeof(nint));
            nuint stride = *(nuint*)((byte*)vwt + 9 * sizeof(nint));
            Assert.True(size > 0 && stride >= size, $"{name} VWT");
        }
    }

    [Fact]
    public static void ChartsProtocolsAndOpaqueDescriptorResolve()
    {
        IntPtr charts = NativeLibrary.Load(ChartsLib);

        // The Charts protocol descriptors a generated binding dispatches
        // through — ChartContent (the Mark protocol) and Plottable (the data
        // protocol) — resolve as real exported symbols.
        Assert.NotEqual(IntPtr.Zero, NativeLibrary.GetExport(charts, "$s6Charts12ChartContentMp"));
        Assert.NotEqual(IntPtr.Zero, NativeLibrary.GetExport(charts, "$s6Charts9PlottableMp"));

        // The classifier records, for every `some ChartContent`-returning
        // modifier, the exact opaque-type descriptor (…QOMQ) needed to obtain
        // the underlying metadata via swift_getOpaqueTypeMetadata2. Prove one of
        // those recorded descriptors is a genuine exported symbol — i.e. the
        // generator emitted a real binding target, not a guess.
        IntPtr annotationOpaque = NativeLibrary.GetExport(charts,
            "$s6Charts12ChartContentPAAE10annotation8position9alignment7spacing18overflowResolution7content"
            + "QrAA18AnnotationPositionV_7SwiftUI9AlignmentV12CoreGraphics7CGFloatVSgAA0k8OverflowI0Vqd__"
            + "AA0K7ContextVctAL4ViewRd__lFQOMQ");
        Assert.NotEqual(IntPtr.Zero, annotationOpaque);
    }

    // Transaction.currentEntitlements (roadmap Phase 9): the REAL StoreKit
    // AsyncSequence, bound headless. The daemon backs no entitlements here, so
    // the sequence completes empty — which is exactly the contract worth
    // proving: the adapter drives a real Swift AsyncSequence to CLEAN
    // COMPLETION (a nil first element) rather than hanging or faulting.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit11TransactionV12TransactionsVMa")]
    private static extern MetadataResponse TransactionsMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit11TransactionV12TransactionsV13AsyncIteratorVMa")]
    private static extern MetadataResponse TransactionsIteratorMetadata(long request);

    // static var currentEntitlements: Transactions { get } — indirect result.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit11TransactionV19currentEntitlementsAC12TransactionsVvgZ")]
    private static extern void CurrentEntitlements(SwiftIndirectResult result, SwiftSelf typeMetadata);

    // func makeAsyncIterator() -> AsyncIterator — self is the sequence.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit11TransactionV12TransactionsV17makeAsyncIteratorAE0fG0VyF")]
    private static extern void MakeAsyncIterator(SwiftIndirectResult result, SwiftSelf sequence);

    [Fact]
    public static void CurrentEntitlementsSequenceBindsAndIteratesHeadless()
    {
        var (seqMetadata, seqVwt, seqStride) = MetadataInfo(TransactionsMetadata(0));
        var (iterMetadata, iterVwt, iterStride) = MetadataInfo(TransactionsIteratorMetadata(0));

        // The iterator's next() is an ASYNC function: its Tu record carries the
        // caller contract (async-direct.md). Reading it proves the real
        // StoreKit sequence uses the async ABI the Phase 8 adapter targets.
        IntPtr storeKit = NativeLibrary.Load(StoreKitLib);
        IntPtr nextTu = NativeLibrary.GetExport(storeKit,
            "$s8StoreKit11TransactionV12TransactionsV13AsyncIteratorV4nextAA18VerificationResultOyACGSgyYaFTu");
        uint nextContextSize = *(uint*)((byte*)nextTu + 4);
        Assert.True(nextContextSize >= 16 && nextContextSize % 8 == 0);

        byte* sequence = (byte*)NativeMemory.AlignedAlloc(seqStride, 16);
        byte* iterator = (byte*)NativeMemory.AlignedAlloc(iterStride, 16);
        try
        {
            // The static property takes the type metadata as its self context.
            CurrentEntitlements(new SwiftIndirectResult(sequence), new SwiftSelf((void*)seqMetadata));

            // A real iterator over the real sequence.
            MakeAsyncIterator(new SwiftIndirectResult(iterator), new SwiftSelf(sequence));

            // Both values lifecycle through their VWTs.
            Destroy(iterVwt, iterator, iterMetadata);
            Destroy(seqVwt, sequence, seqMetadata);
        }
        finally
        {
            NativeMemory.AlignedFree(sequence);
            NativeMemory.AlignedFree(iterator);
        }
    }

    // Product.SubscriptionInfo, its Status, and RenewalInfo/RenewalState
    // (roadmap Phase 9): the subscription surface, including the enum that
    // carries grace-period / billing-retry / revoked. All resolve headless.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoVMa")]
    private static extern MetadataResponse SubscriptionInfoMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoV6StatusVMa")]
    private static extern MetadataResponse SubscriptionStatusMetadata(long request);

    // NOTE the mangling: the mangler word-substitutes "RenewalInfo" to
    // "07RenewalE0V" — the symbol must come from the binary (verified by
    // swift-demangle), never from string-building a guess.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoV07RenewalE0VMa")]
    private static extern MetadataResponse RenewalInfoMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    // RenewalState is a STRUCT, not a Swift enum: RawRepresentable with static
    // properties for the states. (Demangling a guessed enum symbol "succeeds" —
    // demangling is string decoding, not existence checking — so the symbol
    // table is the only authority. The guess failed here.)
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoV12RenewalStateVMa")]
    private static extern MetadataResponse RenewalStateMetadata(long request);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoV12RenewalStateV10subscribedAGvgZ")]
    private static extern void RenewalStateSubscribed(SwiftIndirectResult result, SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoV12RenewalStateV13inGracePeriodAGvgZ")]
    private static extern void RenewalStateInGracePeriod(SwiftIndirectResult result, SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoV12RenewalStateV20inBillingRetryPeriodAGvgZ")]
    private static extern void RenewalStateInBillingRetryPeriod(SwiftIndirectResult result, SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoV12RenewalStateV7expiredAGvgZ")]
    private static extern void RenewalStateExpired(SwiftIndirectResult result, SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit7ProductV16SubscriptionInfoV12RenewalStateV7revokedAGvgZ")]
    private static extern void RenewalStateRevoked(SwiftIndirectResult result, SwiftSelf typeMetadata);

    [Fact]
    public static void SubscriptionInfoSurfaceIntrospectsHeadless()
    {
        // The subscription types.
        var (subInfo, subVwt, subStride) = MetadataInfo(SubscriptionInfoMetadata(0));
        Assert.Equal(0x200u, *(uint*)subInfo & 0xFFFu); // struct

        var (status, _, statusStride) = MetadataInfo(SubscriptionStatusMetadata(0));
        Assert.Equal(0x200u, *(uint*)status & 0xFFFu);
        Assert.True(statusStride > 0);

        var (renewalInfo, _, renewalStride) = MetadataInfo(RenewalInfoMetadata(0));
        Assert.Equal(0x200u, *(uint*)renewalInfo & 0xFFFu);
        Assert.True(renewalStride > 0);
    }

    [Fact]
    public static void RenewalStateStatusSetsBindHeadless()
    {
        // The five status sets — subscribed, expired, inBillingRetryPeriod,
        // inGracePeriod, revoked — are STATIC PROPERTIES of a RawRepresentable
        // struct, not enum cases. Each is an exported static getter taking the
        // type metadata as self, so their VALUES bind headless, and a binding
        // compares against them rather than switching on an enum tag.
        var (metadata, _, stride) = MetadataInfo(RenewalStateMetadata(0));
        Assert.Equal(0x200u, *(uint*)metadata & 0xFFFu); // struct kind, not enum
        Assert.True(stride > 0);

        // RenewalState is RESILIENT, so its static getters return INDIRECTLY —
        // assuming a register return here faults (it did: SIGBUS). Each value is
        // written into caller storage sized from the VWT.
        var self = new SwiftSelf((void*)metadata);
        var values = new List<string>();
        var storages = new List<IntPtr>();
        try
        {
            foreach (var getter in new Action<SwiftIndirectResult, SwiftSelf>[]
            {
                RenewalStateSubscribed, RenewalStateExpired, RenewalStateInGracePeriod,
                RenewalStateInBillingRetryPeriod, RenewalStateRevoked,
            })
            {
                byte* storage = (byte*)NativeMemory.AlignedAlloc(stride, 16);
                storages.Add((IntPtr)storage);
                new Span<byte>(storage, (int)stride).Clear();
                getter(new SwiftIndirectResult(storage), self);
                values.Add(Convert.ToHexString(new ReadOnlySpan<byte>(storage, (int)stride)));
            }

            // The five states are distinct, which is what makes comparison a
            // valid dispatch mechanism for a RawRepresentable status type.
            Assert.Equal(5, values.Distinct().Count());
        }
        finally
        {
            foreach (IntPtr p in storages)
            {
                NativeMemory.AlignedFree((void*)p);
            }
        }
    }

    // The remaining Transaction surface (roadmap Phase 9): the three
    // transaction sequences (updates / unfinished / all), finish(), and the
    // signed-JWS accessor. All bind headless; only their VALUES need the daemon.
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit11TransactionV7updatesAC12TransactionsVvgZ")]
    private static extern void TransactionUpdates(SwiftIndirectResult result, SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit11TransactionV10unfinishedAC12TransactionsVvgZ")]
    private static extern void TransactionUnfinished(SwiftIndirectResult result, SwiftSelf typeMetadata);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(StoreKitLib, EntryPoint = "$s8StoreKit11TransactionV3allAC12TransactionsVvgZ")]
    private static extern void TransactionAll(SwiftIndirectResult result, SwiftSelf typeMetadata);

    [Fact]
    public static void TransactionSequencesBindHeadless()
    {
        // updates, unfinished, and all are static properties returning the same
        // Transactions sequence type — the eager-listener, recovery, and history
        // sources the commerce service needs. Each returns INDIRECTLY (resilient).
        var (seqMetadata, seqVwt, seqStride) = MetadataInfo(TransactionsMetadata(0));
        var (txMetadata, _, _) = MetadataInfo(TransactionMetadata(0));
        var self = new SwiftSelf((void*)txMetadata);

        foreach (var accessor in new Action<SwiftIndirectResult, SwiftSelf>[]
        {
            TransactionUpdates, TransactionUnfinished, TransactionAll,
        })
        {
            byte* storage = (byte*)NativeMemory.AlignedAlloc(seqStride, 16);
            try
            {
                accessor(new SwiftIndirectResult(storage), self);
                Destroy(seqVwt, storage, seqMetadata);
            }
            finally
            {
                NativeMemory.AlignedFree(storage);
            }
        }
    }

    [Fact]
    public static void FinishAndJwsAccessorsBindHeadless()
    {
        IntPtr storeKit = NativeLibrary.Load(StoreKitLib);

        // finish() is an ASYNC instance method: its Tu record carries the caller
        // contract, so finishing a transaction rides the generated async-thunk
        // ABI exactly like every other async call.
        IntPtr finishTu = NativeLibrary.GetExport(storeKit, "$s8StoreKit11TransactionV6finishyyYaFTu");
        uint finishContextSize = *(uint*)((byte*)finishTu + 4);
        Assert.True(finishContextSize >= 16 && finishContextSize % 8 == 0);

        // The signed JWS payload — the data that PROVES the transaction — is
        // exposed on a CONSTRAINED EXTENSION (VerificationResult where T ==
        // Transaction), which is why its mangling carries the constraint
        // (A2A11TransactionVRszlE). Preserving signed data means binding this
        // accessor, not re-encoding the transaction.
        IntPtr jws = NativeLibrary.GetExport(storeKit,
            "$s8StoreKit18VerificationResultOA2A11TransactionVRszlE17jwsRepresentationSSvg");
        Assert.NotEqual(IntPtr.Zero, jws);

        IntPtr signedDate = NativeLibrary.GetExport(storeKit,
            "$s8StoreKit18VerificationResultOA2A11TransactionVRszlE10signedDate10Foundation0G0Vvg");
        Assert.NotEqual(IntPtr.Zero, signedDate);
    }

    [Fact]
    public static void GenericArgumentsForProductsForResolveHeadless()
    {
        // Array<String> metadata via the generic accessor, the Collection
        // witness via runtime lookup, and an empty array value through the
        // SDK initializer (metadata as the static context) — exactly the
        // generic arguments products(for:) needs.
        IntPtr stringMetadata = NativeLibrary.GetExport(NativeLibrary.Load(CoreLib), "$sSSN");
        MetadataResponse arrayOfString = ArrayMetadata(0, stringMetadata);
        Assert.NotEqual(IntPtr.Zero, arrayOfString.Metadata);

        IntPtr collectionDescriptor = NativeLibrary.GetExport(NativeLibrary.Load(CoreLib), "$sSlMp");
        IntPtr witness = swift_conformsToProtocol(arrayOfString.Metadata, collectionDescriptor);
        Assert.NotEqual(IntPtr.Zero, witness);

        IntPtr empty = ArrayEmptyInit(new SwiftSelf((void*)arrayOfString.Metadata));
        Assert.NotEqual(IntPtr.Zero, empty);
        swift_release_object(empty);
    }

    [DllImport(CoreLib, EntryPoint = "swift_release")]
    private static extern void swift_release_object(IntPtr obj);

    [Fact]
    public static void EnvironmentActionBindsFromSdkDirectly()
    {
        var (envMetadata, envVwt, envStride) = MetadataInfo(EnvironmentValuesMetadata(0));
        var (actMetadata, actVwt, actStride) = MetadataInfo(OpenURLActionMetadata(0));

        byte* env = (byte*)NativeMemory.AlignedAlloc(envStride, 16);
        byte* action = (byte*)NativeMemory.AlignedAlloc(actStride, 16);
        try
        {
            EnvironmentValuesInit(new SwiftIndirectResult(env));
            EnvironmentValuesGetOpenURL(new SwiftIndirectResult(action), new SwiftSelf(env));

            Destroy(actVwt, action, actMetadata);
            Destroy(envVwt, env, envMetadata);
        }
        finally
        {
            NativeMemory.AlignedFree(env);
            NativeMemory.AlignedFree(action);
        }
    }

    [Fact]
    public static void HeadlessTextViewValueConstructsAndLifecycles()
    {
        ViewValueRoundTrip("$s19SwiftOpaqueAndViews12makeTextViewyQrs5Int64VFQOMQ",
            buf => MakeTextView(new SwiftIndirectResult((void*)buf), 5));
    }

    // Explicit ownership conventions: borrowing = +0 (no transfer),
    // consuming/sending = +1 (callee releases the transferred reference).
    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews11makeTrackedAA0F0CyF")]
    private static extern IntPtr MakeTracked();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews11trackedLives5Int64VyF")]
    private static extern long TrackedLive();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews13borrowTrackedys5Int64VAA0F0CF")]
    private static extern long BorrowTracked(IntPtr t);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews14consumeTrackedys5Int64VAA0F0CnF")]
    private static extern long ConsumeTracked(IntPtr t);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews11sendTrackedys5Int64VAA0F0CnF")]
    private static extern long SendTracked(IntPtr t);

    [DllImport("/usr/lib/swift/libswiftCore.dylib")]
    private static extern void swift_release(IntPtr obj);

    [Fact]
    public static void ExplicitOwnershipConventionsTransferCorrectly()
    {
        long before = TrackedLive();

        IntPtr t = MakeTracked();
        Assert.Equal(before + 1, TrackedLive());

        // borrowing: the callee observes the value; our reference survives.
        Assert.Equal(before + 1, BorrowTracked(t));
        Assert.Equal(before + 1, TrackedLive());

        // consuming: our +1 transfers to the callee, which releases it.
        ConsumeTracked(t);
        Assert.Equal(before, TrackedLive());

        // sending: also a transfer of the reference (region send).
        IntPtr t2 = MakeTracked();
        SendTracked(t2);
        Assert.Equal(before, TrackedLive());

        // borrowing did not consume: releasing after a borrow deinits.
        IntPtr t3 = MakeTracked();
        BorrowTracked(t3);
        swift_release(t3);
        Assert.Equal(before, TrackedLive());
    }

    [Fact]
    public static void HeadlessStoreViewValueConstructsAndLifecycles()
    {
        ViewValueRoundTrip("$s19SwiftOpaqueAndViews13makeStoreViewQryFQOMQ",
            buf => MakeStoreView(new SwiftIndirectResult((void*)buf)));
    }

    [Fact]
    public static void HeadlessSubscriptionStoreViewValueConstructsAndLifecycles()
    {
        ViewValueRoundTrip("$s19SwiftOpaqueAndViews25makeSubscriptionStoreViewQryFQOMQ",
            buf => MakeSubscriptionStoreView(new SwiftIndirectResult((void*)buf)));
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews13makeStoreViewQryF")]
    private static extern void MakeStoreView(SwiftIndirectResult result);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(Lib, EntryPoint = "$s19SwiftOpaqueAndViews25makeSubscriptionStoreViewQryF")]
    private static extern void MakeSubscriptionStoreView(SwiftIndirectResult result);

    [Fact]
    public static void HeadlessProductViewValueConstructsAndLifecycles()
    {
        byte[] id = Encoding.UTF8.GetBytes("com.example.product");
        fixed (byte* p = id)
        {
            byte* idPtr = p;
            long len = id.Length;
            ViewValueRoundTrip("$s19SwiftOpaqueAndViews19makeProductViewUtf8yQrSPys5UInt8VG_s5Int64VtFQOMQ",
                buf => MakeProductView(new SwiftIndirectResult((void*)buf), idPtr, len));
        }
    }
}
