// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Versioning;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.CompilerServices
{
    public static partial class RuntimeFeature
    {
        /// <summary>
        /// Name of the Portable PDB feature.
        /// </summary>
        public const string PortablePdb = nameof(PortablePdb);

        /// <summary>
        /// Indicates that this version of runtime supports default interface method implementations.
        /// </summary>
        public const string DefaultImplementationsOfInterfaces = nameof(DefaultImplementationsOfInterfaces);

        /// <summary>
        /// Indicates that this version of runtime supports the Unmanaged calling convention value.
        /// </summary>
        public const string UnmanagedSignatureCallingConvention = nameof(UnmanagedSignatureCallingConvention);

        /// <summary>
        /// Indicates that this version of runtime supports covariant returns in overrides of methods declared in classes.
        /// </summary>
        public const string CovariantReturnsOfClasses = nameof(CovariantReturnsOfClasses);

        /// <summary>
        /// Represents a runtime feature where types can define ref fields.
        /// </summary>
        public const string ByRefFields = nameof(ByRefFields);

        /// <summary>
        /// Represents a runtime feature where byref-like types can be used in Generic parameters.
        /// </summary>
        public const string ByRefLikeGenerics = nameof(ByRefLikeGenerics);

        /// <summary>
        /// Indicates that this version of runtime supports virtual static members of interfaces.
        /// </summary>
        public const string VirtualStaticsInInterfaces = nameof(VirtualStaticsInInterfaces);

        /// <summary>
        /// Indicates that this version of runtime supports <see cref="System.IntPtr" /> and <see cref="System.UIntPtr" /> as numeric types.
        /// </summary>
        public const string NumericIntPtr = nameof(NumericIntPtr);

        // Swift interop capability profiles. Each string names a versioned,
        // immutable capability contract (docs/design/interop/swift/abi-model.md,
        // "Named ABI profiles"); a runtime reports a profile only when it
        // implements the profile's runtime obligations for the executing mode.
        // Marker-type presence never implies a profile, and runtimes with
        // partial CallConvSwift support report none of them. Consumers compare
        // exact strings by set containment; there is no ordering or wildcard
        // semantics. The reserved SwiftInterop.AsyncDirect1 profile is
        // deliberately absent: it must not be reported by any runtime before
        // its contract is specified and approved.

        /// <summary>
        /// Indicates that this runtime implements the synchronous Swift calling-convention
        /// profile: <c>CallConvSwift</c> calls and callbacks including the self, error, and
        /// indirect-result registers and frozen-struct physical lowering.
        /// </summary>
        public const string SwiftInteropSync1 = "SwiftInterop.Sync1";

        /// <summary>
        /// Indicates that this runtime implements the Swift dynamic value and metadata
        /// ownership profile (metadata accessor invocation and value-witness operations
        /// through <see cref="SwiftInteropSync1"/> calls).
        /// </summary>
        public const string SwiftInteropValues1 = "SwiftInterop.Values1";

        /// <summary>
        /// Indicates that this runtime implements the Swift generics profile: generic
        /// metadata and witness-table trailing arguments passed as ordinary arguments.
        /// </summary>
        public const string SwiftInteropGenerics1 = "SwiftInterop.Generics1";

        /// <summary>
        /// Indicates that this runtime implements the Swift reverse-interop profile:
        /// rooted <see cref="System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"/>
        /// reverse entry points suitable for generated closure boxes and protocol proxies.
        /// </summary>
        public const string SwiftInteropProxies1 = "SwiftInterop.Proxies1";

        /// <summary>
        /// Indicates that this runtime implements the generated Swift async-thunk profile:
        /// <see cref="SwiftInteropSync1"/> calls plus ordinary C callbacks; no direct
        /// Swift async convention is required or implied.
        /// </summary>
        public const string SwiftInteropAsyncThunk1 = "SwiftInterop.AsyncThunk1";

        /// <summary>
        /// Checks whether a certain feature is supported by the Runtime.
        /// </summary>
        public static bool IsSupported(string feature)
        {
            return feature switch
            {
                PortablePdb or
                CovariantReturnsOfClasses or
                ByRefFields or
                ByRefLikeGenerics or
                UnmanagedSignatureCallingConvention or
                DefaultImplementationsOfInterfaces or
                VirtualStaticsInInterfaces or
                NumericIntPtr => true,

                SwiftInteropSync1 or
                SwiftInteropValues1 or
                SwiftInteropGenerics1 or
                SwiftInteropProxies1 or
                SwiftInteropAsyncThunk1 => IsSwiftInteropSupported,

                nameof(IsDynamicCodeSupported) => IsDynamicCodeSupported,
                nameof(IsDynamicCodeCompiled) => IsDynamicCodeCompiled,
                nameof(IsMultithreadingSupported) => IsMultithreadingSupported,
                _ => false,
            };
        }

        /// <summary>
        /// Whether this runtime build implements the Swift interop capability
        /// profiles. The first-release profile scope is CoreCLR-family runtimes
        /// (JIT, R2R, interpreter, and NativeAOT) on Apple ARM64 targets; Mono
        /// never reports the profiles.
        /// </summary>
        internal static bool IsSwiftInteropSupported =>
#if (CORECLR || NATIVEAOT) && TARGET_APPLE && TARGET_ARM64
            true;
#else
            false;
#endif

        /// <summary>
        /// Gets a value that indicates whether the runtime supports multithreading, including
        /// creating threads and using blocking synchronization primitives. This property
        /// returns <see langword="false"/> on platforms or configurations where multithreading
        /// is not supported or is disabled, such as single-threaded browser environments and WASI.
        /// </summary>
        [UnsupportedOSPlatformGuard("browser")]
        [UnsupportedOSPlatformGuard("wasi")]
        [FeatureSwitchDefinition("System.Runtime.CompilerServices.RuntimeFeature.IsMultithreadingSupported")]
        public static bool IsMultithreadingSupported
#if FEATURE_MULTITHREADING
            => true;
#else
            => false;
#endif

#if FEATURE_WASM_MANAGED_THREADS
        internal static void ThrowIfMultithreadingIsNotSupported()
        {
            System.Threading.Thread.AssureBlockingPossible();
        }
#elif !FEATURE_MULTITHREADING
        [DoesNotReturn]
        internal static void ThrowIfMultithreadingIsNotSupported()
        {
            throw new PlatformNotSupportedException();
        }
#else
        [Conditional("unnecessary")]
        internal static void ThrowIfMultithreadingIsNotSupported()
        {
        }
#endif
    }
}
