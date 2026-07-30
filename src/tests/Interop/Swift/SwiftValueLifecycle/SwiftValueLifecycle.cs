// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Threading;
using Xunit;
using TestLibrary;

// Walking skeleton of the low-level Swift projection support layer
// (docs/design/interop/swift/architecture.md "Managed Swift projection
// support"): opaque metadata request/response types, generated metadata
// accessor invocation, value-witness-table prefix access, aligned native
// value storage with an explicit lifecycle, exactly-once destroy,
// exception-safe adoption, SafeHandle-based finalization, generic-static
// metadata caching without reflection, native object retain/release, error
// box retain/release, enum value witnesses, scoped borrow/inout access,
// noncopyable-copy rejection, and module lifetime leases.
// The types are test-hosted here until API review approves the support
// library surface. Calling-convention notes for every native entry point
// used here live in docs/design/interop/swift/support-library.md.
namespace Swift.Runtime.Support
{
    /// <summary>Swift metadata request values (blocking complete request).</summary>
    internal enum SwiftMetadataRequest : long
    {
        Complete = 0,
    }

    /// <summary>Response from a Swift type metadata accessor.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SwiftMetadataResponse
    {
        public IntPtr Metadata;
        public nint State; // 0 == complete

        public bool IsComplete => State == 0;
    }

    /// <summary>An opaque, validated pointer to complete Swift type metadata.</summary>
    internal readonly unsafe struct SwiftTypeMetadata
    {
        private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

        // MetadataKind values (probe-verified); class metadata starts with an
        // isa pointer instead of a small kind value.
        private const nuint KindStruct = 0x200;
        private const nuint KindEnum = 0x201;
        private const nuint KindOptional = 0x202;

        [DllImport(SwiftCoreLib)]
        private static extern IntPtr swift_conformsToProtocol(IntPtr metadata, IntPtr protocolDescriptor);

        public readonly IntPtr Value;
        private readonly SwiftModuleLease? _lease;

        internal SwiftTypeMetadata(IntPtr value, SwiftModuleLease? lease)
        {
            Value = value;
            _lease = lease;
        }

        /// <summary>The lease keeping the metadata's defining module loaded, if one was attached.</summary>
        public SwiftModuleLease? Lease => _lease;

        /// <summary>The metadata kind word (first pointer-sized word of the metadata).</summary>
        public nuint Kind => *(nuint*)Value;

        public bool IsStruct => Kind == KindStruct;
        public bool IsEnum => Kind is KindEnum or KindOptional;

        /// <summary>
        /// The nominal type descriptor stored one word after the kind for
        /// struct/enum metadata. Fails closed for other metadata kinds
        /// (classes lead with an isa pointer, not a kind).
        /// </summary>
        public IntPtr NominalTypeDescriptor
        {
            get
            {
                if (Kind is not (KindStruct or KindEnum or KindOptional))
                    throw new InvalidOperationException($"Metadata kind 0x{Kind:x} does not have a value-type descriptor.");

                IntPtr descriptor = *(IntPtr*)((byte*)Value + sizeof(nint));
                if (descriptor == IntPtr.Zero)
                    throw new InvalidOperationException("Metadata has no nominal type descriptor.");

                return descriptor;
            }
        }

        /// <summary>Looks up this type's conformance; null when the type does not conform.</summary>
        public SwiftWitnessTable? GetConformance(SwiftProtocolDescriptor protocol)
        {
            IntPtr witnessTable = swift_conformsToProtocol(Value, protocol.Value);
            return witnessTable == IntPtr.Zero ? null : new SwiftWitnessTable(witnessTable, _lease);
        }

        /// <summary>Invoke a generated metadata accessor and validate completeness before any VWT use.</summary>
        public static SwiftTypeMetadata FromAccessor(delegate* unmanaged[Swift]<long, SwiftMetadataResponse> accessor)
            => FromAccessor(accessor, lease: null);

        public static SwiftTypeMetadata FromAccessor(
            delegate* unmanaged[Swift]<long, SwiftMetadataResponse> accessor, SwiftModuleLease? lease)
        {
            SwiftMetadataResponse response = accessor((long)SwiftMetadataRequest.Complete);
            if (!response.IsComplete || response.Metadata == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Swift metadata request did not complete (state {response.State}).");
            }

            return new SwiftTypeMetadata(response.Metadata, lease);
        }

        /// <summary>The value witness table pointer stored one word before the metadata address.</summary>
        public SwiftValueWitnessTable ValueWitnessTable
            => new SwiftValueWitnessTable(*(IntPtr*)((byte*)Value - sizeof(nint)));
    }

    /// <summary>
    /// View over the required prefix of a Swift value witness table.
    /// Slot layout (pointer-sized slots): 0 initializeBufferWithCopyOfBuffer,
    /// 1 destroy, 2 initializeWithCopy, 3 assignWithCopy, 4 initializeWithTake,
    /// 5 assignWithTake, 6/7 single-payload enum tag witnesses, 8 size,
    /// 9 stride, 10 flags(uint32) + extraInhabitantCount(uint32); enum types
    /// append 11 getEnumTag, 12 destructiveProjectEnumData,
    /// 13 destructiveInjectEnumTag (slot offsets probe-verified on ARM64).
    /// </summary>
    internal readonly unsafe struct SwiftValueWitnessTable
    {
        private readonly IntPtr _vwt;

        public SwiftValueWitnessTable(IntPtr vwt) => _vwt = vwt;

        private void** Slots => (void**)_vwt;

        // ValueWitnessFlags bits (probe-verified on ARM64 macOS, Swift 6.4).
        public const uint FlagHasEnumWitnesses = 0x0020_0000;
        public const uint FlagIsNonCopyable = 0x0080_0000;

        public nuint Size => *(nuint*)((byte*)_vwt + 8 * sizeof(nint));
        public nuint Stride => *(nuint*)((byte*)_vwt + 9 * sizeof(nint));
        public uint Flags => *(uint*)((byte*)_vwt + 10 * sizeof(nint));
        public nuint Alignment => (nuint)((Flags & 0xFF) + 1);
        public bool HasEnumWitnesses => (Flags & FlagHasEnumWitnesses) != 0;
        public bool IsNonCopyable => (Flags & FlagIsNonCopyable) != 0;

        public void Destroy(void* value, IntPtr metadata)
            => ((delegate* unmanaged[Swift]<void*, IntPtr, void>)Slots[1])(value, metadata);

        // The shared native helper path (roadmap Phase 4): the mandatory route
        // for pointer-authenticated targets. Managed code hands the helper the
        // witness SLOT ADDRESS so the (future arm64e) authentication happens at
        // the source storage address; on arm64 the helper is a plain load+call.
        private const string PtrauthHelperLib = "libSwiftPtrauthHelper.dylib";

        [DllImport(PtrauthHelperLib)]
        private static extern void SwiftPtrauth_Destroy(void** slotAddr, void* value, IntPtr metadata);

        [DllImport(PtrauthHelperLib)]
        private static extern void* SwiftPtrauth_InitializeWithCopy(void** slotAddr, void* dest, void* src, IntPtr metadata);

        public void DestroyViaHelper(void* value, IntPtr metadata)
            => SwiftPtrauth_Destroy(&Slots[1], value, metadata);

        public void* InitializeWithCopyViaHelper(void* dest, void* src, IntPtr metadata)
            => SwiftPtrauth_InitializeWithCopy(&Slots[2], dest, src, metadata);

        public void* InitializeWithCopy(void* dest, void* src, IntPtr metadata)
            => ((delegate* unmanaged[Swift]<void*, void*, IntPtr, void*>)Slots[2])(dest, src, metadata);

        public void* InitializeWithTake(void* dest, void* src, IntPtr metadata)
            => ((delegate* unmanaged[Swift]<void*, void*, IntPtr, void*>)Slots[4])(dest, src, metadata);

        private void RequireEnumWitnesses()
        {
            if (!HasEnumWitnesses)
                throw new InvalidOperationException("The type does not provide enum value witnesses.");
        }

        /// <summary>The current case tag: payload cases take 0..n-1 in declaration order, then no-payload cases.</summary>
        public uint GetEnumTag(void* value, IntPtr metadata)
        {
            RequireEnumWitnesses();
            return ((delegate* unmanaged[Swift]<void*, IntPtr, uint>)Slots[11])(value, metadata);
        }

        /// <summary>Destructively replaces the enum value with its raw payload in place.</summary>
        public void DestructiveProjectEnumData(void* value, IntPtr metadata)
        {
            RequireEnumWitnesses();
            ((delegate* unmanaged[Swift]<void*, IntPtr, void>)Slots[12])(value, metadata);
        }

        /// <summary>Re-tags an in-place raw payload back into a valid enum value.</summary>
        public void DestructiveInjectEnumTag(void* value, uint tag, IntPtr metadata)
        {
            RequireEnumWitnesses();
            ((delegate* unmanaged[Swift]<void*, uint, IntPtr, void>)Slots[13])(value, tag, metadata);
        }
    }

    /// <summary>An opaque, validated pointer to a Swift protocol descriptor.</summary>
    internal readonly struct SwiftProtocolDescriptor
    {
        public readonly IntPtr Value;

        public SwiftProtocolDescriptor(IntPtr value)
        {
            if (value == IntPtr.Zero)
                throw new ArgumentException("Protocol descriptor must be non-null.", nameof(value));
            Value = value;
        }
    }

    /// <summary>
    /// A protocol witness table: slot 0 holds the conformance descriptor and
    /// requirement witnesses follow from slot 1 (probe-verified layout).
    /// </summary>
    internal readonly unsafe struct SwiftWitnessTable
    {
        public readonly IntPtr Value;
        private readonly SwiftModuleLease? _lease;

        internal SwiftWitnessTable(IntPtr value, SwiftModuleLease? lease)
        {
            Value = value;
            _lease = lease;
        }

        /// <summary>The lease keeping the conformance's defining module loaded, if one was attached.</summary>
        public SwiftModuleLease? Lease => _lease;

        public IntPtr ConformanceDescriptor => ((IntPtr*)Value)[0];

        /// <summary>The witness for the protocol requirement at the given zero-based index.</summary>
        public IntPtr GetRequirementWitness(int index) => ((IntPtr*)Value)[1 + index];
    }

    /// <summary>
    /// Refcounted lease on a native module. Wrappers that cache pointers into
    /// a module (metadata, VWTs, witnesses, accessors) hold a lease so the
    /// defining library cannot be freed underneath them.
    /// </summary>
    internal sealed class SwiftModuleLease : SafeHandle
    {
        public SwiftModuleLease(string libraryName, Assembly assembly)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(NativeLibrary.Load(libraryName, assembly, searchPath: null));
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        public IntPtr GetExport(string name)
        {
            bool addedRef = false;
            DangerousAddRef(ref addedRef);
            try
            {
                return NativeLibrary.GetExport(handle, name);
            }
            finally
            {
                if (addedRef)
                    DangerousRelease();
            }
        }

        protected override bool ReleaseHandle()
        {
            NativeLibrary.Free(handle);
            return true;
        }
    }

    /// <summary>A type token providing a Swift metadata accessor without reflection.</summary>
    internal unsafe interface ISwiftTypeToken
    {
        static abstract delegate* unmanaged[Swift]<long, SwiftMetadataResponse> GetAccessor();

        /// <summary>Optional module lease attached to metadata resolved through this token.</summary>
        static virtual SwiftModuleLease? GetModuleLease() => null;
    }

    /// <summary>Generic-static metadata cache: one resolved metadata per token type, no reflection.</summary>
    internal static unsafe class SwiftMetadataCache<TToken>
        where TToken : struct, ISwiftTypeToken
    {
        private static SwiftModuleLease? s_lease;
        private static IntPtr s_metadata;

        public static SwiftTypeMetadata Metadata
        {
            get
            {
                if (Volatile.Read(ref s_metadata) == IntPtr.Zero)
                {
                    // Racy-init is benign: accessors are idempotent and leases
                    // are refcounted. The lease publishes before the metadata
                    // pointer so no reader sees metadata without its lease.
                    s_lease = TToken.GetModuleLease();
                    Volatile.Write(ref s_metadata, SwiftTypeMetadata.FromAccessor(TToken.GetAccessor()).Value);
                }

                return new SwiftTypeMetadata(s_metadata, s_lease);
            }
        }
    }

    /// <summary>
    /// Owned, aligned native storage for a single Swift value with an explicit
    /// lifecycle: Uninitialized -> Initialized -> (Moved | Disposed), with
    /// exactly-once destroy enforced across threads and the finalizer.
    /// </summary>
    internal sealed unsafe class SwiftValueStorage : SafeHandle
    {
        private const int StateUninitialized = 0;
        private const int StateInitialized = 1;
        private const int StateConsumed = 2; // moved-from or destroyed

        private readonly IntPtr _metadata;
        private readonly SwiftValueWitnessTable _vwt;
        private readonly SwiftModuleLease? _lease; // roots the defining module while this value lives
        private int _state;
        private int _borrows;

        // Memory-pressure policy (support-library.md): native allocations of
        // known size held behind small managed wrappers are accounted to the
        // GC above this threshold so collection pacing sees them. Swift class
        // instances have no knowable size and are intentionally not accounted
        // (explicit dispose plus the soak lane cover them).
        private const nuint MemoryPressureThreshold = 4096;
        private readonly bool _pressureAdded;

        public SwiftValueStorage(SwiftTypeMetadata metadata)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            _metadata = metadata.Value;
            _vwt = metadata.ValueWitnessTable;
            _lease = metadata.Lease;
            SetHandle((IntPtr)NativeMemory.AlignedAlloc(_vwt.Stride, _vwt.Alignment));
            if (_vwt.Stride >= MemoryPressureThreshold)
            {
                GC.AddMemoryPressure((long)_vwt.Stride);
                _pressureAdded = true;
            }
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        public void* Pointer => (void*)handle;

        public bool IsInitialized => Volatile.Read(ref _state) == StateInitialized;

        /// <summary>Exception-safe adoption: the value only becomes owned if the initializer succeeds.</summary>
        public void AdoptFrom(Action<IntPtr> initializer)
        {
            if (Interlocked.CompareExchange(ref _state, StateUninitialized, StateUninitialized) != StateUninitialized)
                throw new InvalidOperationException("Storage already holds or held a value.");

            initializer(handle);

            // Publish ownership only after the initializer completed without throwing.
            if (Interlocked.CompareExchange(ref _state, StateInitialized, StateUninitialized) != StateUninitialized)
                throw new InvalidOperationException("Concurrent initialization detected.");
        }

        public void InitializeByCopyFrom(SwiftValueStorage source)
        {
            if (_vwt.IsNonCopyable)
                throw new InvalidOperationException("The value is noncopyable; use InitializeByTakeFrom.");
            if (!source.IsInitialized)
                throw new InvalidOperationException("Source holds no value.");
            if (Interlocked.CompareExchange(ref _state, StateUninitialized, StateUninitialized) != StateUninitialized)
                throw new InvalidOperationException("Storage already holds or held a value.");

            _vwt.InitializeWithCopy(Pointer, source.Pointer, _metadata);
            Volatile.Write(ref _state, StateInitialized);
        }

        public void InitializeByTakeFrom(SwiftValueStorage source)
        {
            // Consume the source exactly once; it must not be destroyed after a take.
            if (Interlocked.CompareExchange(ref source._state, StateConsumed, StateInitialized) != StateInitialized)
                throw new InvalidOperationException("Source holds no value.");
            if (Interlocked.CompareExchange(ref _state, StateUninitialized, StateUninitialized) != StateUninitialized)
                throw new InvalidOperationException("Storage already holds or held a value.");

            _vwt.InitializeWithTake(Pointer, source.Pointer, _metadata);
            Volatile.Write(ref _state, StateInitialized);
        }

        /// <summary>
        /// Scoped shared borrow: the callback receives the value address while
        /// a SafeHandle reference pins the memory, so a racing Dispose defers
        /// the free until the borrow ends.
        /// </summary>
        public T WithBorrowed<T>(Func<IntPtr, T> reader)
        {
            bool addedRef = false;
            DangerousAddRef(ref addedRef);
            try
            {
                Interlocked.Increment(ref _borrows);
                try
                {
                    if (Volatile.Read(ref _state) != StateInitialized)
                        throw new InvalidOperationException("Storage holds no value.");
                    return reader(handle);
                }
                finally
                {
                    Interlocked.Decrement(ref _borrows);
                }
            }
            finally
            {
                if (addedRef)
                    DangerousRelease();
            }
        }

        public void WithBorrowed(Action<IntPtr> reader)
            => WithBorrowed(ptr => { reader(ptr); return 0; });

        /// <summary>
        /// Scoped exclusive (inout-style) access. Exclusivity against other
        /// borrows is the caller's obligation, matching Swift's law of
        /// exclusivity; violations are only best-effort detected.
        /// </summary>
        public void WithMutable(Action<IntPtr> mutator)
            => WithBorrowed(ptr => { mutator(ptr); return 0; });

        /// <summary>Destroys the held value exactly once. Safe to call concurrently and repeatedly.</summary>
        public void DestroyValue()
        {
            // Best-effort exclusivity check, same stance as the Swift runtime:
            // destroying a currently-borrowed value is a caller bug.
            if (Volatile.Read(ref _borrows) != 0)
                throw new InvalidOperationException("Cannot destroy a borrowed value.");

            if (Interlocked.CompareExchange(ref _state, StateConsumed, StateInitialized) == StateInitialized)
            {
                _vwt.Destroy(Pointer, _metadata);
            }
        }

        protected override bool ReleaseHandle()
        {
            if (_pressureAdded)
            {
                GC.RemoveMemoryPressure((long)_vwt.Stride);
            }
            // The finalizer/dispose path also destroys exactly once. SafeHandle
            // guarantees this runs only after all borrows released their refs.
            if (Interlocked.CompareExchange(ref _state, StateConsumed, StateInitialized) == StateInitialized)
            {
                _vwt.Destroy((void*)handle, _metadata);
            }

            NativeMemory.AlignedFree((void*)handle);
            GC.KeepAlive(_lease); // the module must outlive the last witness call
            return true;
        }
    }

    /// <summary>Owned handle to a native Swift object, balanced with swift_release.</summary>
    internal sealed class SwiftObjectHandle : SafeHandle
    {
        private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

        [DllImport(SwiftCoreLib)]
        internal static extern IntPtr swift_retain(IntPtr obj);

        [DllImport(SwiftCoreLib)]
        internal static extern void swift_release(IntPtr obj);

        public SwiftObjectHandle(IntPtr owned)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(owned);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <summary>Creates an additional owned handle by retaining the object.</summary>
        public SwiftObjectHandle Retain()
        {
            bool addedRef = false;
            DangerousAddRef(ref addedRef);
            try
            {
                return new SwiftObjectHandle(swift_retain(handle));
            }
            finally
            {
                if (addedRef)
                    DangerousRelease();
            }
        }

        protected override bool ReleaseHandle()
        {
            swift_release(handle);
            return true;
        }
    }

    /// <summary>Owned handle to a Swift error box, balanced with swift_errorRelease.</summary>
    internal sealed class SwiftErrorHandle : SafeHandle
    {
        private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

        [DllImport(SwiftCoreLib)]
        private static extern void swift_errorRetain(IntPtr error);

        [DllImport(SwiftCoreLib)]
        private static extern void swift_errorRelease(IntPtr error);

        /// <summary>Adopts an owned (+1) error box, e.g. one produced in the error register by a Swift throw.</summary>
        public SwiftErrorHandle(IntPtr owned)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(owned);
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <summary>Creates an additional owned handle by retaining the error box.</summary>
        public SwiftErrorHandle Retain()
        {
            bool addedRef = false;
            DangerousAddRef(ref addedRef);
            try
            {
                swift_errorRetain(handle);
                return new SwiftErrorHandle(handle);
            }
            finally
            {
                if (addedRef)
                    DangerousRelease();
            }
        }

        protected override bool ReleaseHandle()
        {
            swift_errorRelease(handle);
            return true;
        }
    }
}

[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftValueLifecycleTests
{
    private const string SwiftLib = "libSwiftValueLifecycle.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle013makeResilientB04seedAA0eB0VSi_tF")]
    private static extern void MakeResilientValue(SwiftIndirectResult result, nint seed);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle09resilientB3SumySdAA09ResilientB0VF")]
    private static extern double ResilientValueSum(void* value);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle16trackerLiveCountSiyF")]
    private static extern nint TrackerLiveCount();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle11makeTracker7payloadAA0E0CSi_tF")]
    private static extern IntPtr MakeTracker(nint payload);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle14trackerPayloadySiAA7TrackerCF")]
    private static extern nint TrackerPayload(IntPtr tracker);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle17throwTrackedError7payloadySi_tKF")]
    private static extern void ThrowTrackedError(nint payload, SwiftError* error);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle19trackedErrorPayloadySis0E0_pF")]
    private static extern nint TrackedErrorPayload(IntPtr errorBox);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle17makeResilientEnum4kind7payloadAA0eF0OSi_SitF")]
    private static extern void MakeResilientEnum(SwiftIndirectResult result, nint kind, nint payload);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle21resilientEnumDescribeySiAA09ResilientE0OF")]
    private static extern nint ResilientEnumDescribe(void* value);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle014scaleResilientB0_2byyAA0eB0Vz_SitF")]
    private static extern void ScaleResilientValue(void* value, nint factor);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s19SwiftValueLifecycle23makeNoncopyableResource7payloadAA0eF0VSi_tF")]
    private static extern void MakeNoncopyableResource(SwiftIndirectResult result, nint payload);

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

    private struct ResilientValueToken : Swift.Runtime.Support.ISwiftTypeToken
    {
        public static delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse> GetAccessor()
            => (delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse>)
                NativeLibrary.GetExport(Lib, "$s19SwiftValueLifecycle09ResilientB0VMa");
    }

    private struct ResilientEnumToken : Swift.Runtime.Support.ISwiftTypeToken
    {
        public static delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse> GetAccessor()
            => (delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse>)
                NativeLibrary.GetExport(Lib, "$s19SwiftValueLifecycle13ResilientEnumOMa");
    }

    private struct NoncopyableResourceToken : Swift.Runtime.Support.ISwiftTypeToken
    {
        public static delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse> GetAccessor()
            => (delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse>)
                NativeLibrary.GetExport(Lib, "$s19SwiftValueLifecycle19NoncopyableResourceVMa");
    }

    private struct TrackerClassToken : Swift.Runtime.Support.ISwiftTypeToken
    {
        public static delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse> GetAccessor()
            => (delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse>)
                NativeLibrary.GetExport(Lib, "$s19SwiftValueLifecycle7TrackerCMa");
    }

    private struct LeasedResilientValueToken : Swift.Runtime.Support.ISwiftTypeToken
    {
        public static Swift.Runtime.Support.SwiftModuleLease? GetModuleLease()
            => new Swift.Runtime.Support.SwiftModuleLease(SwiftLib, Assembly.GetExecutingAssembly());

        public static delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse> GetAccessor()
            => (delegate* unmanaged[Swift]<long, Swift.Runtime.Support.SwiftMetadataResponse>)
                NativeLibrary.GetExport(Lib, "$s19SwiftValueLifecycle09ResilientB0VMa");
    }

    private static Swift.Runtime.Support.SwiftTypeMetadata GetMetadata()
        => Swift.Runtime.Support.SwiftMetadataCache<ResilientValueToken>.Metadata;

    [Fact]
    public static void MetadataAccessorAndWitnessTable()
    {
        var metadata = GetMetadata();
        Assert.NotEqual(IntPtr.Zero, metadata.Value);

        var vwt = metadata.ValueWitnessTable;
        Assert.True(vwt.Size > 0);
        Assert.True(vwt.Stride >= vwt.Size);
        Assert.True(nuint.IsPow2(vwt.Alignment));
        Assert.True(vwt.Alignment >= 8); // contains a class reference and an Int
    }

    [Fact]
    public static void HelperRoutedWitnessCallsMatchDirect()
    {
        // The shared-helper path (mandatory for ptrauth targets) must behave
        // identically to direct witness calls: copy via helper, read, destroy
        // via helper, with exact tracker balance.
        var metadata = GetMetadata();
        var vwt = metadata.ValueWitnessTable;
        long before = TrackerLiveCount();

        var source = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        source.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 20));
        Assert.Equal(before + 1, TrackerLiveCount());

        byte* copy = stackalloc byte[(int)vwt.Stride];
        vwt.InitializeWithCopyViaHelper(copy, source.Pointer, metadata.Value);

        // 20 + 30.0 + 20 == 70, through the helper-made copy.
        Assert.Equal(70.0, ResilientValueSum(copy));

        // The helper copy took a real retain: destroying the source leaves the
        // tracked instance alive through the copy's reference...
        source.DestroyValue();
        source.Dispose();
        Assert.Equal(before + 1, TrackerLiveCount());

        // ...and destroying the copy through the helper performs the deinit.
        vwt.DestroyViaHelper(copy, metadata.Value);
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void AllocateAdoptReadDestroy()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 10));
        Assert.Equal(before + 1, TrackerLiveCount());

        // 10 + 15.0 + 10 == 35
        Assert.Equal(35.0, ResilientValueSum(storage.Pointer));

        storage.DestroyValue();
        Assert.Equal(before, TrackerLiveCount());

        // Exactly-once: further destroys and dispose must not double-release.
        storage.DestroyValue();
        storage.Dispose();
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void CopyIsArcBalanced()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        var first = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        first.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 4));

        var second = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        second.InitializeByCopyFrom(first);

        // A copy retains the same tracker instance rather than creating one.
        Assert.Equal(before + 1, TrackerLiveCount());
        Assert.Equal(ResilientValueSum(first.Pointer), ResilientValueSum(second.Pointer));

        first.DestroyValue();
        Assert.Equal(before + 1, TrackerLiveCount()); // still alive via the copy
        second.DestroyValue();
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void TakeConsumesSourceExactlyOnce()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        var source = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        source.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 6));

        var destination = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        destination.InitializeByTakeFrom(source);

        Assert.False(source.IsInitialized);
        Assert.Equal(before + 1, TrackerLiveCount());
        Assert.Equal(21.0, ResilientValueSum(destination.Pointer)); // 6 + 9 + 6

        // Destroying the moved-from source must be a no-op.
        source.DestroyValue();
        source.Dispose();
        Assert.Equal(before + 1, TrackerLiveCount());

        destination.DestroyValue();
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void FinalizerDestroysOwnedValue()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        CreateAndDrop(metadata);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(before, TrackerLiveCount());

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CreateAndDrop(Swift.Runtime.Support.SwiftTypeMetadata metadata)
        {
            var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
            storage.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 3));
            Assert.True(storage.IsInitialized);
        }
    }

    [Fact]
    public static void FailedAdoptionLeavesStorageUnowned()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        Assert.Throws<InvalidOperationException>(
            () => storage.AdoptFrom(_ => throw new InvalidOperationException("initializer failed")));

        Assert.False(storage.IsInitialized);
        storage.DestroyValue(); // must be a no-op
        storage.Dispose();
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void ObjectHandlesBalanceRetainRelease()
    {
        long before = TrackerLiveCount();

        var handle = new Swift.Runtime.Support.SwiftObjectHandle(MakeTracker(77));
        Assert.Equal(before + 1, TrackerLiveCount());
        Assert.Equal(77, (long)TrackerPayload(handle.DangerousGetHandle()));

        var second = handle.Retain();
        Assert.Equal(before + 1, TrackerLiveCount()); // same instance, extra reference

        handle.Dispose();
        Assert.Equal(before + 1, TrackerLiveCount()); // still alive via second
        Assert.Equal(77, (long)TrackerPayload(second.DangerousGetHandle()));

        second.Dispose();
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void ConcurrentLifecycleIsLeakFree()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        Thread[] threads = new Thread[4];
        Exception? failure = null;
        for (int t = 0; t < threads.Length; t++)
        {
            threads[t] = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 100; i++)
                    {
                        var a = new Swift.Runtime.Support.SwiftValueStorage(metadata);
                        a.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), i));
                        var b = new Swift.Runtime.Support.SwiftValueStorage(metadata);
                        b.InitializeByCopyFrom(a);
                        a.DestroyValue();
                        b.DestroyValue();
                        a.Dispose();
                        b.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }
            });
            threads[t].Start();
        }

        foreach (Thread t in threads)
            t.Join();

        Assert.Null(failure);
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void ErrorHandlesBalanceRetainRelease()
    {
        long before = TrackerLiveCount();

        // The error register hands us a +1 owned error box.
        SwiftError error = default;
        ThrowTrackedError(55, &error);
        Assert.True(error.Value != null);
        Assert.Equal(before + 1, TrackerLiveCount());

        var handle = new Swift.Runtime.Support.SwiftErrorHandle((IntPtr)error.Value);
        Assert.Equal(55, (long)TrackedErrorPayload(handle.DangerousGetHandle()));

        var second = handle.Retain();
        Assert.Equal(before + 1, TrackerLiveCount()); // same box, extra reference

        handle.Dispose();
        Assert.Equal(before + 1, TrackerLiveCount()); // still alive via second
        Assert.Equal(55, (long)TrackedErrorPayload(second.DangerousGetHandle()));

        second.Dispose();
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void EnumWitnessesExposeTags()
    {
        var metadata = Swift.Runtime.Support.SwiftMetadataCache<ResilientEnumToken>.Metadata;
        var vwt = metadata.ValueWitnessTable;
        Assert.True(vwt.HasEnumWitnesses);
        long before = TrackerLiveCount();

        // Payload cases take tags 0..n-1 in declaration order (number == 0,
        // tracked == 1), then no-payload cases (empty == 2).
        (nint kind, uint expectedTag, nint describe)[] cases =
        [
            (0, 2u, -1),
            (1, 0u, 77),
            (2, 1u, 77),
        ];
        foreach ((nint kind, uint expectedTag, nint describe) in cases)
        {
            var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
            storage.AdoptFrom(ptr => MakeResilientEnum(new SwiftIndirectResult((void*)ptr), kind, 77));
            storage.WithBorrowed(ptr =>
            {
                Assert.Equal(expectedTag, vwt.GetEnumTag((void*)ptr, metadata.Value));
                Assert.Equal(describe, ResilientEnumDescribe((void*)ptr));
            });
            storage.DestroyValue();
            storage.Dispose();
        }

        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void EnumProjectAndInjectRoundTrip()
    {
        var metadata = Swift.Runtime.Support.SwiftMetadataCache<ResilientEnumToken>.Metadata;
        var vwt = metadata.ValueWitnessTable;

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeResilientEnum(new SwiftIndirectResult((void*)ptr), 1, 1234));
        storage.WithMutable(ptr =>
        {
            Assert.Equal(0u, vwt.GetEnumTag((void*)ptr, metadata.Value));
            vwt.DestructiveProjectEnumData((void*)ptr, metadata.Value);
            Assert.Equal(1234, *(nint*)ptr); // the raw Int payload, exposed in place
            *(nint*)ptr = 4321;
            vwt.DestructiveInjectEnumTag((void*)ptr, 0u, metadata.Value);
        });
        Assert.Equal(4321, (long)ResilientEnumDescribe(storage.Pointer));

        storage.DestroyValue();
        storage.Dispose();
    }

    [Fact]
    public static void EnumWitnessesRequireEnumType()
    {
        var metadata = GetMetadata(); // ResilientValue is a struct
        var vwt = metadata.ValueWitnessTable;
        Assert.False(vwt.HasEnumWitnesses);

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 1));
        storage.WithBorrowed(ptr =>
            Assert.Throws<InvalidOperationException>(() => vwt.GetEnumTag((void*)ptr, metadata.Value)));
        storage.DestroyValue();
        storage.Dispose();
    }

    [Fact]
    public static void BorrowAndInoutMutationHelpers()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 8));

        Assert.Equal(28.0, storage.WithBorrowed(ptr => ResilientValueSum((void*)ptr))); // 8 + 12 + 8

        // inout: the callee mutates the value in place through the borrowed address.
        storage.WithMutable(ptr => ScaleResilientValue((void*)ptr, 3));
        Assert.Equal(68.0, storage.WithBorrowed(ptr => ResilientValueSum((void*)ptr))); // 24 + 36 + 8

        storage.DestroyValue();
        Assert.Equal(before, TrackerLiveCount());

        // A borrow of a destroyed value must be rejected.
        Assert.Throws<InvalidOperationException>(() => storage.WithBorrowed(_ => 0));
        storage.Dispose();
    }

    [Fact]
    public static void DestroyWhileBorrowedIsRejected()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 5));

        storage.WithBorrowed(_ => Assert.Throws<InvalidOperationException>(() => storage.DestroyValue()));

        storage.DestroyValue(); // legal once the borrow ended
        storage.Dispose();
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void DisposeDuringBorrowDefersRelease()
    {
        var metadata = GetMetadata();
        long before = TrackerLiveCount();

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 7));

        using var borrowStarted = new ManualResetEventSlim();
        using var disposeIssued = new ManualResetEventSlim();
        double sum = 0;
        var reader = new Thread(() => sum = storage.WithBorrowed(ptr =>
        {
            borrowStarted.Set();
            disposeIssued.Wait();
            return ResilientValueSum((void*)ptr);
        }));
        reader.Start();

        borrowStarted.Wait();
        storage.Dispose(); // must defer destroy+free until the borrow's ref drains
        disposeIssued.Set();
        reader.Join();

        Assert.Equal(24.5, sum); // 7 + 10.5 + 7, read safely after Dispose was requested
        Assert.Equal(before, TrackerLiveCount()); // the deferred release still destroyed the value
    }

    [Fact]
    public static void NoncopyableValuesRejectCopyButAllowTake()
    {
        var metadata = Swift.Runtime.Support.SwiftMetadataCache<NoncopyableResourceToken>.Metadata;
        Assert.True(metadata.ValueWitnessTable.IsNonCopyable);
        Assert.False(GetMetadata().ValueWitnessTable.IsNonCopyable);
        long before = TrackerLiveCount();

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeNoncopyableResource(new SwiftIndirectResult((void*)ptr), 5));
        Assert.Equal(before + 1, TrackerLiveCount());

        var destination = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        Assert.Throws<InvalidOperationException>(() => destination.InitializeByCopyFrom(storage));

        // A take (move) is the atomic one-consume operation and stays legal.
        destination.InitializeByTakeFrom(storage);
        Assert.False(storage.IsInitialized);
        Assert.Equal(before + 1, TrackerLiveCount());

        destination.DestroyValue();
        Assert.Equal(before, TrackerLiveCount());
        storage.Dispose();
        destination.Dispose();
    }

    [Fact]
    public static void NominalDescriptorsExposeValueTypeKinds()
    {
        var structMetadata = GetMetadata();
        Assert.True(structMetadata.IsStruct);
        Assert.False(structMetadata.IsEnum);
        Assert.NotEqual(IntPtr.Zero, structMetadata.NominalTypeDescriptor);

        var enumMetadata = Swift.Runtime.Support.SwiftMetadataCache<ResilientEnumToken>.Metadata;
        Assert.True(enumMetadata.IsEnum);
        Assert.NotEqual(IntPtr.Zero, enumMetadata.NominalTypeDescriptor);
        Assert.NotEqual(structMetadata.NominalTypeDescriptor, enumMetadata.NominalTypeDescriptor);

        // Class metadata leads with an isa pointer, not a kind: the
        // value-type descriptor accessor must fail closed.
        var classMetadata = Swift.Runtime.Support.SwiftMetadataCache<TrackerClassToken>.Metadata;
        Assert.False(classMetadata.IsStruct);
        Assert.Throws<InvalidOperationException>(() => classMetadata.NominalTypeDescriptor);
    }

    [Fact]
    public static void ConformanceLookupFindsWitnessAndCallsIt()
    {
        var metadata = GetMetadata();
        var protocol = new Swift.Runtime.Support.SwiftProtocolDescriptor(
            NativeLibrary.GetExport(Lib, "$s19SwiftValueLifecycle11DescribableMp"));

        Swift.Runtime.Support.SwiftWitnessTable? conformance = metadata.GetConformance(protocol);
        Assert.True(conformance.HasValue);
        var witnessTable = conformance!.Value;

        // The runtime lookup must agree with the exported witness table.
        Assert.Equal(
            NativeLibrary.GetExport(Lib, "$s19SwiftValueLifecycle09ResilientB0VAA11DescribableAAWP"),
            witnessTable.Value);
        Assert.NotEqual(IntPtr.Zero, witnessTable.ConformanceDescriptor);

        // Protocol witness convention (probe-verified): self address in the
        // self register, Self metadata and witness table trailing.
        var describedValue = (delegate* unmanaged[Swift]<IntPtr, IntPtr, SwiftSelf, nint>)
            witnessTable.GetRequirementWitness(0);

        long before = TrackerLiveCount();
        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 21));

        long result = storage.WithBorrowed(
            ptr => (long)describedValue(metadata.Value, witnessTable.Value, new SwiftSelf((void*)ptr)));
        Assert.Equal(21, result);

        storage.DestroyValue();
        storage.Dispose();
        Assert.Equal(before, TrackerLiveCount());
    }

    [Fact]
    public static void NonConformingTypeHasNoWitnessTable()
    {
        var protocol = new Swift.Runtime.Support.SwiftProtocolDescriptor(
            NativeLibrary.GetExport(Lib, "$s19SwiftValueLifecycle11DescribableMp"));

        var enumMetadata = Swift.Runtime.Support.SwiftMetadataCache<ResilientEnumToken>.Metadata;
        Assert.Null(enumMetadata.GetConformance(protocol));
    }

    [Fact]
    public static void ModuleLeaseKeepsDefiningModuleAlive()
    {
        var metadata = Swift.Runtime.Support.SwiftMetadataCache<LeasedResilientValueToken>.Metadata;
        Assert.NotNull(metadata.Lease);
        Assert.False(metadata.Lease!.IsClosed);
        Assert.Equal(GetMetadata().Value, metadata.Value); // same accessor, same metadata
        Assert.Null(GetMetadata().Lease); // tokens without a lease attach none

        // Exports resolve through the lease while it is held.
        Assert.NotEqual(IntPtr.Zero, metadata.Lease!.GetExport("$s19SwiftValueLifecycle16trackerLiveCountSiyF"));

        var storage = new Swift.Runtime.Support.SwiftValueStorage(metadata);
        storage.AdoptFrom(ptr => MakeResilientValue(new SwiftIndirectResult((void*)ptr), 2));
        Assert.Equal(7.0, storage.WithBorrowed(ptr => ResilientValueSum((void*)ptr))); // 2 + 3 + 2
        storage.DestroyValue();
        storage.Dispose();
    }
}
