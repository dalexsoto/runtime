// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

// Objective-C wrapper reuse for @objc Swift classes (roadmap Phase 6): the
// same instance obtained from a Swift entry point is driven through the
// Objective-C runtime (objc_msgSend with registered selectors), and state
// mutated through the Objective-C route is observed through the Swift ABI
// route — so existing Objective-C interop wrappers can front @objc Swift
// classes without a parallel projection.
[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftObjCReuse
{
    private const string SwiftLib = "libSwiftObjCReuse.dylib";
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";
    private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s14SwiftObjCReuse10makeBeacon5levelAA0E0Cs5Int64V_tF")]
    private static extern IntPtr MakeBeacon(long level);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s14SwiftObjCReuse11beaconLevelys5Int64VAA6BeaconCF")]
    private static extern long BeaconLevel(IntPtr beacon);

    [DllImport(ObjCLib)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLib)]
    private static extern IntPtr object_getClass(IntPtr obj);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern long objc_msgSend_Int64(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern long objc_msgSend_Int64_Int64(IntPtr receiver, IntPtr selector, long arg);

    // Provenance-aware release: @objc/NSObject-derived instances are
    // Objective-C-refcounted, so swift_release is WRONG for them (it
    // crashes in swift_release_dealloc); swift_unknownObjectRelease
    // handles native-Swift, Objective-C, and tagged-pointer provenance.
    [DllImport(SwiftCoreLib)]
    private static extern void swift_unknownObjectRelease(IntPtr obj);

    [DllImport(SwiftCoreLib)]
    private static extern IntPtr swift_unknownObjectRetain(IntPtr obj);

    [Fact]
    public static void SameInstanceWorksThroughBothRuntimes()
    {
        IntPtr beacon = MakeBeacon(5);
        Assert.NotEqual(IntPtr.Zero, object_getClass(beacon)); // a real ObjC object

        // Read through Objective-C, mutate through Objective-C...
        Assert.Equal(5, objc_msgSend_Int64(beacon, sel_registerName("level")));
        Assert.Equal(9, objc_msgSend_Int64_Int64(beacon, sel_registerName("boost:"), 4));

        // ...and observe the mutation through the Swift ABI on the same
        // instance, then back again.
        Assert.Equal(9, BeaconLevel(beacon));
        Assert.Equal(11, objc_msgSend_Int64_Int64(beacon, sel_registerName("boost:"), 2));
        Assert.Equal(11, BeaconLevel(beacon));

        // Retain/release through the provenance-aware entry points.
        swift_unknownObjectRetain(beacon);
        swift_unknownObjectRelease(beacon);
        swift_unknownObjectRelease(beacon);
    }
}
