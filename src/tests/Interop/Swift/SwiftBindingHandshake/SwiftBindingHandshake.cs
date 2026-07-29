// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// Pre-invocation rejection tests for the capability/version handshake
// (roadmap Phase 0; design: architecture.md "Runtime capability and version
// handshake"). The validator below is a test-hosted reference
// implementation of the specified check order and rejection codes
// SWIFT0001-SWIFT0006; the runtime-reported facts are injectable so each
// rejection is reproducible deterministically. The native module is the
// real fixture dylib: checks 4-5 call its version/identity exports (the
// only native calls permitted before validation succeeds) and a counting
// entry point proves rejected bindings never reach Swift code.
namespace Swift.Runtime.Support
{
    /// <summary>Compile-time constants a generator emits into a binding assembly.</summary>
    internal sealed class SwiftBindingManifest
    {
        public required int SchemaVersion { get; init; }
        public required string[] RequiredProfiles { get; init; }
        public required int GeneratedNativeAbiVersion { get; init; }
        public required string NativeAssetIdentity { get; init; }
        public required string TargetRid { get; init; }

        /// <summary>Pointer-authentication schema the binding was generated for ("none" on ARM64).</summary>
        public string PtrauthSchema { get; init; } = "none";
    }

    /// <summary>Facts reported by the executing runtime and host environment.</summary>
    internal sealed class SwiftRuntimeCapabilities
    {
        /// <summary>Profiles from the allowlisted implementation-profile query; never marker-derived.</summary>
        public required string[] ReportedProfiles { get; init; }

        /// <summary>Whether the low-level marker APIs (CallConvSwift et al.) exist.</summary>
        public required bool MarkerApisPresent { get; init; }

        public required int[] SupportedSchemaVersions { get; init; }
        public required string CurrentRid { get; init; }
        public required string[] AllowlistedRids { get; init; }

        /// <summary>Ptrauth schemas this runtime implements ("none" for ARM64).</summary>
        public string[] SupportedPtrauthSchemas { get; init; } = ["none"];
    }

    /// <summary>Version/identity exports of a generated native module.</summary>
    internal interface INativeBindingModule
    {
        int GetNativeAbiVersion();
        string GetAssetIdentity();
    }

    internal sealed class SwiftBindingRejectedException : Exception
    {
        public string Code { get; }

        public SwiftBindingRejectedException(string code, string message)
            : base($"{code}: {message}")
        {
            Code = code;
        }
    }

    /// <summary>
    /// Reference handshake: validates once, caches the outcome, and gates
    /// every entry point on it. Checks run in the specified fixed order so
    /// the same environment always produces the same code.
    /// </summary>
    internal sealed class SwiftBindingHandshakeState
    {
        private SwiftBindingRejectedException? _failure;
        private bool _validated;

        public int ValidationRuns { get; private set; }

        public void ValidateBindingCompatibility(
            SwiftBindingManifest manifest,
            SwiftRuntimeCapabilities capabilities,
            INativeBindingModule module)
        {
            ValidationRuns++;
            try
            {
                // 1. Manifest schema version (managed-only).
                if (Array.IndexOf(capabilities.SupportedSchemaVersions, manifest.SchemaVersion) < 0)
                {
                    throw new SwiftBindingRejectedException(
                        "SWIFT0003", $"Manifest schema version {manifest.SchemaVersion} is unsupported.");
                }

                // 2. Implementation profile (managed-only). An empty reported
                // set with markers present is marker-only support.
                foreach (string required in manifest.RequiredProfiles)
                {
                    if (Array.IndexOf(capabilities.ReportedProfiles, required) < 0)
                    {
                        if (capabilities.ReportedProfiles.Length == 0 && capabilities.MarkerApisPresent)
                        {
                            throw new SwiftBindingRejectedException(
                                "SWIFT0002", "Only the low-level marker APIs are present.");
                        }

                        throw new SwiftBindingRejectedException(
                            "SWIFT0001", $"Implementation profile '{required}' is absent or older than required.");
                    }
                }

                // 3. Target allowlist (managed-only), including the
                // pointer-authentication schema: an unknown or unsupported
                // ptrauth schema (e.g. an arm64e binding on an ARM64
                // runtime, or a schema this runtime has never heard of)
                // fails closed before any native call.
                if (manifest.TargetRid != capabilities.CurrentRid
                    || Array.IndexOf(capabilities.AllowlistedRids, manifest.TargetRid) < 0)
                {
                    throw new SwiftBindingRejectedException(
                        "SWIFT0006", $"Target cell '{manifest.TargetRid}' is not allowlisted.");
                }

                if (Array.IndexOf(capabilities.SupportedPtrauthSchemas, manifest.PtrauthSchema) < 0)
                {
                    throw new SwiftBindingRejectedException(
                        "SWIFT0006", $"Pointer-authentication schema '{manifest.PtrauthSchema}' is not supported by this runtime.");
                }

                // 4. Generated native ABI version (first permitted native call).
                int nativeVersion = module.GetNativeAbiVersion();
                if (nativeVersion != manifest.GeneratedNativeAbiVersion)
                {
                    throw new SwiftBindingRejectedException(
                        "SWIFT0004", $"Generated native ABI version {nativeVersion} != {manifest.GeneratedNativeAbiVersion}.");
                }

                // 5. Native asset identity.
                string identity = module.GetAssetIdentity();
                if (identity != manifest.NativeAssetIdentity)
                {
                    throw new SwiftBindingRejectedException(
                        "SWIFT0005", "Native asset identity differs from the manifest.");
                }

                _validated = true;
            }
            catch (SwiftBindingRejectedException rejection)
            {
                _failure = rejection;
                throw;
            }
        }

        /// <summary>The per-entrypoint gate: rethrows the cached failure without revalidating.</summary>
        public void EnsureUsable()
        {
            if (_failure != null)
                throw _failure;
            if (!_validated)
                throw new SwiftBindingRejectedException("SWIFT0001", "Binding was never validated.");
        }
    }
}

[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftBindingHandshakeTests
{
    private const string SwiftLib = "libSwiftBindingHandshake.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s21SwiftBindingHandshake23bindingNativeAbiVersionSiyF")]
    private static extern nint BindingNativeAbiVersion();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s21SwiftBindingHandshake20bindingIdentityWord0s6UInt64VyF")]
    private static extern ulong BindingIdentityWord0();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s21SwiftBindingHandshake20bindingIdentityWord1s6UInt64VyF")]
    private static extern ulong BindingIdentityWord1();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s21SwiftBindingHandshake15bindingEntryAdd1a1bS2i_SitF")]
    private static extern nint BindingEntryAdd(nint a, nint b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s21SwiftBindingHandshake21bindingEntryCallCountSiyF")]
    private static extern nint BindingEntryCallCount();

    private const string GoodIdentity = "112233445566778899aabbccddeeff00";

    private sealed class RealNativeModule : Swift.Runtime.Support.INativeBindingModule
    {
        public int NativeCalls;

        public int GetNativeAbiVersion()
        {
            NativeCalls++;
            return (int)BindingNativeAbiVersion();
        }

        public string GetAssetIdentity()
        {
            NativeCalls++;
            return $"{BindingIdentityWord0():x16}{BindingIdentityWord1():x16}";
        }
    }

    private static Swift.Runtime.Support.SwiftBindingManifest GoodManifest() => new()
    {
        SchemaVersion = 3,
        RequiredProfiles = ["SwiftInterop.Sync1", "SwiftInterop.Values1"],
        GeneratedNativeAbiVersion = 2,
        NativeAssetIdentity = GoodIdentity,
        TargetRid = "osx-arm64",
    };

    private static Swift.Runtime.Support.SwiftRuntimeCapabilities GoodCapabilities() => new()
    {
        ReportedProfiles = ["SwiftInterop.Sync1", "SwiftInterop.Values1", "SwiftInterop.Generics1"],
        MarkerApisPresent = true,
        SupportedSchemaVersions = [3],
        CurrentRid = "osx-arm64",
        AllowlistedRids = ["osx-arm64"],
    };

    private static string RejectionCode(
        Swift.Runtime.Support.SwiftBindingManifest manifest,
        Swift.Runtime.Support.SwiftRuntimeCapabilities capabilities,
        out RealNativeModule module)
    {
        var state = new Swift.Runtime.Support.SwiftBindingHandshakeState();
        var local = new RealNativeModule();
        module = local;
        var rejection = Assert.Throws<Swift.Runtime.Support.SwiftBindingRejectedException>(
            () => state.ValidateBindingCompatibility(manifest, capabilities, local));
        return rejection.Code;
    }

    [Fact]
    public static void SuccessPathReachesSwiftExactlyWhenValidated()
    {
        long entriesBefore = BindingEntryCallCount();

        var state = new Swift.Runtime.Support.SwiftBindingHandshakeState();
        state.ValidateBindingCompatibility(GoodManifest(), GoodCapabilities(), new RealNativeModule());

        state.EnsureUsable();
        Assert.Equal(30, (long)BindingEntryAdd(10, 20));
        Assert.Equal(entriesBefore + 1, (long)BindingEntryCallCount());
    }

    [Fact]
    public static void AbsentOrOlderProfileRejectsWithoutNativeCalls()
    {
        var manifest = GoodManifest();
        manifest = new Swift.Runtime.Support.SwiftBindingManifest
        {
            SchemaVersion = manifest.SchemaVersion,
            RequiredProfiles = ["SwiftInterop.Sync2"], // newer than anything reported
            GeneratedNativeAbiVersion = manifest.GeneratedNativeAbiVersion,
            NativeAssetIdentity = manifest.NativeAssetIdentity,
            TargetRid = manifest.TargetRid,
        };

        Assert.Equal("SWIFT0001", RejectionCode(manifest, GoodCapabilities(), out RealNativeModule module));
        Assert.Equal(0, module.NativeCalls); // checks 1-3 are managed-only
    }

    [Fact]
    public static void MarkerOnlySupportRejects()
    {
        var capabilities = new Swift.Runtime.Support.SwiftRuntimeCapabilities
        {
            ReportedProfiles = [], // markers exist, no implementation profile
            MarkerApisPresent = true,
            SupportedSchemaVersions = [3],
            CurrentRid = "osx-arm64",
            AllowlistedRids = ["osx-arm64"],
        };

        Assert.Equal("SWIFT0002", RejectionCode(GoodManifest(), capabilities, out RealNativeModule module));
        Assert.Equal(0, module.NativeCalls);
    }

    [Fact]
    public static void SchemaMismatchRejectsFirstDeterministically()
    {
        foreach (int badSchema in new[] { 2, 4 }) // older and newer than supported
        {
            var manifest = new Swift.Runtime.Support.SwiftBindingManifest
            {
                SchemaVersion = badSchema,
                RequiredProfiles = ["SwiftInterop.Missing1"], // also violates check 2
                GeneratedNativeAbiVersion = 99,               // also violates check 4
                NativeAssetIdentity = "ffff",                 // also violates check 5
                TargetRid = "linux-x64",                      // also violates check 3
            };

            // The schema check runs first, so an environment violating every
            // check still reports SWIFT0003 — repeatedly.
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal("SWIFT0003", RejectionCode(manifest, GoodCapabilities(), out RealNativeModule module));
                Assert.Equal(0, module.NativeCalls);
            }
        }
    }

    [Fact]
    public static void NonAllowlistedTargetCellRejects()
    {
        var manifest = new Swift.Runtime.Support.SwiftBindingManifest
        {
            SchemaVersion = 3,
            RequiredProfiles = ["SwiftInterop.Sync1"],
            GeneratedNativeAbiVersion = 2,
            NativeAssetIdentity = GoodIdentity,
            TargetRid = "ios-arm64", // binding built for another cell
        };

        Assert.Equal("SWIFT0006", RejectionCode(manifest, GoodCapabilities(), out RealNativeModule module));
        Assert.Equal(0, module.NativeCalls);
    }

    /// <summary>
    /// Capabilities detected from the LIVE runtime rather than injected. This is
    /// what a shipped binding would do at load: ask the runtime it is actually
    /// running on what it implements.
    /// </summary>
    private static Swift.Runtime.Support.SwiftRuntimeCapabilities DetectLiveCapabilities()
    {
        // The marker types are the low-level surface. Their presence alone does
        // NOT mean the runtime implements the Swift calling convention.
        bool markers = Type.GetType("System.Runtime.InteropServices.Swift.SwiftSelf, System.Runtime") != null
            && Type.GetType("System.Runtime.InteropServices.Swift.SwiftError, System.Runtime") != null;

        // The implementation profile is claimed only when the runtime actually
        // supports the convention. CallConvSwift on a runtime without support
        // throws at JIT/marshalling time, so probing it is the honest test.
        var profiles = new List<string>();
        if (markers && SwiftConventionWorks())
        {
            profiles.Add("SwiftInterop.Sync1");
            profiles.Add("SwiftInterop.Values1");
        }

        Swift.Runtime.Support.SwiftRuntimeCapabilities reference = GoodCapabilities();
        return new Swift.Runtime.Support.SwiftRuntimeCapabilities
        {
            MarkerApisPresent = markers,
            ReportedProfiles = profiles.ToArray(),
            SupportedSchemaVersions = reference.SupportedSchemaVersions,
            CurrentRid = reference.CurrentRid,
            AllowlistedRids = reference.AllowlistedRids,
            SupportedPtrauthSchemas = reference.SupportedPtrauthSchemas,
        };
    }

    private static bool SwiftConventionWorks()
    {
        try
        {
            // A trivial CallConvSwift call into the fixture. On a runtime without
            // Swift support this throws (InvalidProgram/Marshal/NotSupported)
            // rather than executing.
            return SwiftProbeAdd(40, 2) == 42;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport("libSwiftBindingHandshake.dylib",
        EntryPoint = "$s21SwiftBindingHandshake9probeAddsys5Int64VAD_ADtF")]
    private static extern long SwiftProbeAdd(long a, long b);

    [Fact]
    public static void LiveRuntimeCapabilitiesGateTheBinding()
    {
        // Cross-version compatibility, from the runtime's own answer rather than
        // an injected fixture: THIS runtime implements the Swift profile, so a
        // binding requiring it is accepted...
        Swift.Runtime.Support.SwiftRuntimeCapabilities live = DetectLiveCapabilities();
        Assert.True(live.MarkerApisPresent);
        Assert.Contains("SwiftInterop.Sync1", live.ReportedProfiles);

        // Accept path: validation succeeds and the native module IS consulted.
        var acceptState = new Swift.Runtime.Support.SwiftBindingHandshakeState();
        var accepted = new RealNativeModule();
        acceptState.ValidateBindingCompatibility(GoodManifest(), live, accepted);
        Assert.True(accepted.NativeCalls > 0);

        // ...while the SAME detection on a runtime WITHOUT the implementation
        // (markers present, convention non-functional — the shape of an older
        // .NET that carries the marker types but not the convention) is refused
        // with SWIFT0002, before any native call. The distinction between
        // "types exist" and "convention works" is the whole point: a version
        // check on the marker types alone would wrongly accept.
        var markerOnly = new Swift.Runtime.Support.SwiftRuntimeCapabilities
        {
            MarkerApisPresent = live.MarkerApisPresent,
            ReportedProfiles = [],
            SupportedSchemaVersions = live.SupportedSchemaVersions,
            CurrentRid = live.CurrentRid,
            AllowlistedRids = live.AllowlistedRids,
            SupportedPtrauthSchemas = live.SupportedPtrauthSchemas,
        };
        Assert.Equal("SWIFT0002", RejectionCode(GoodManifest(), markerOnly, out RealNativeModule refused));
        Assert.Equal(0, refused.NativeCalls); // refused BEFORE any native call
    }

    [Fact]
    public static void UnknownPtrauthSchemaFailsClosed()
    {
        // The schema-level half of the negative ptrauth test: bindings
        // carrying an arm64e schema (or any unknown schema) are rejected
        // pre-invocation on a runtime that does not implement it. The
        // discriminator-value-level negative test requires arm64e hardware.
        foreach (string schema in new[] { "arm64e-v1", "quantum-v9" })
        {
            var manifest = new Swift.Runtime.Support.SwiftBindingManifest
            {
                SchemaVersion = 3,
                RequiredProfiles = ["SwiftInterop.Sync1"],
                GeneratedNativeAbiVersion = 2,
                NativeAssetIdentity = GoodIdentity,
                TargetRid = "osx-arm64",
                PtrauthSchema = schema,
            };

            Assert.Equal("SWIFT0006", RejectionCode(manifest, GoodCapabilities(), out RealNativeModule module));
            Assert.Equal(0, module.NativeCalls); // rejected before any native call
        }

        // The declared "none" schema continues to pass.
        var state = new Swift.Runtime.Support.SwiftBindingHandshakeState();
        state.ValidateBindingCompatibility(GoodManifest(), GoodCapabilities(), new RealNativeModule());
        state.EnsureUsable();
    }

    [Fact]
    public static void NativeAbiVersionMismatchRejects()
    {
        var manifest = new Swift.Runtime.Support.SwiftBindingManifest
        {
            SchemaVersion = 3,
            RequiredProfiles = ["SwiftInterop.Sync1"],
            GeneratedNativeAbiVersion = 3, // module exports 2
            NativeAssetIdentity = GoodIdentity,
            TargetRid = "osx-arm64",
        };

        Assert.Equal("SWIFT0004", RejectionCode(manifest, GoodCapabilities(), out RealNativeModule module));
        Assert.Equal(1, module.NativeCalls); // version export only, no identity read
    }

    [Fact]
    public static void AssetIdentityMismatchRejects()
    {
        var manifest = new Swift.Runtime.Support.SwiftBindingManifest
        {
            SchemaVersion = 3,
            RequiredProfiles = ["SwiftInterop.Sync1"],
            GeneratedNativeAbiVersion = 2,
            NativeAssetIdentity = "0000000000000000ffffffffffffffff", // recombined asset
            TargetRid = "osx-arm64",
        };

        Assert.Equal("SWIFT0005", RejectionCode(manifest, GoodCapabilities(), out RealNativeModule module));
        Assert.Equal(2, module.NativeCalls); // version + identity exports only
    }

    [Fact]
    public static void RejectedBindingNeverReachesSwiftAndFailureIsCached()
    {
        long entriesBefore = BindingEntryCallCount();

        var state = new Swift.Runtime.Support.SwiftBindingHandshakeState();
        var manifest = new Swift.Runtime.Support.SwiftBindingManifest
        {
            SchemaVersion = 3,
            RequiredProfiles = ["SwiftInterop.Missing1"],
            GeneratedNativeAbiVersion = 2,
            NativeAssetIdentity = GoodIdentity,
            TargetRid = "osx-arm64",
        };

        var first = Assert.Throws<Swift.Runtime.Support.SwiftBindingRejectedException>(
            () => state.ValidateBindingCompatibility(manifest, GoodCapabilities(), new RealNativeModule()));
        Assert.Equal("SWIFT0001", first.Code);

        // Every gated entry point rethrows the captured failure without
        // revalidating, and the Swift entry is never invoked.
        for (int i = 0; i < 3; i++)
        {
            var again = Assert.Throws<Swift.Runtime.Support.SwiftBindingRejectedException>(state.EnsureUsable);
            Assert.Same(first, again);
        }

        Assert.Equal(1, state.ValidationRuns);
        Assert.Equal(entriesBefore, (long)BindingEntryCallCount());
    }

    [Fact]
    public static void UnvalidatedBindingFailsClosed()
    {
        var state = new Swift.Runtime.Support.SwiftBindingHandshakeState();
        Assert.Throws<Swift.Runtime.Support.SwiftBindingRejectedException>(state.EnsureUsable);
    }
}
