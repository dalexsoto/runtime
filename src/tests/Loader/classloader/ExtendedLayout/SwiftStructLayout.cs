// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using Xunit;
using TestLibrary;

namespace ExtendedLayoutTests;

// ExtendedLayoutKind.SwiftStruct prototype (docs/design/interop/swift/
// abi-model.md "Extended layout for Swift value projection"): declaration
// order, no reordering, managed instance size is the Swift stride, and
// nested SwiftStruct fields occupy their unpadded Swift size so later
// fields tail-pack into the nested value's padding.
public static class SwiftStructLayoutTests
{
    private static nint OffsetOf<T, TField>(ref T value, ref TField field) where T : struct where TField : struct
        => Unsafe.ByteOffset(ref Unsafe.As<T, byte>(ref value), ref Unsafe.As<TField, byte>(ref field));

    [Fact]
    public static void SizeIsStride()
    {
        // { Int64; Int8 }: Swift size 9, stride 16. Managed size reports the
        // stride so arrays, spans, and Unsafe.SizeOf match Swift arrays.
        var v = default(SwiftInner);
        Assert.Equal(16, Unsafe.SizeOf<SwiftInner>());
        Assert.Equal(0, OffsetOf(ref v, ref v.A));
        Assert.Equal(8, OffsetOf(ref v, ref v.B));
    }

    [Fact]
    public static void NestedSwiftStructTailPacks()
    {
        // The trailing field packs into the nested value's tail padding:
        // C sits at offset 9 (the nested unpadded size), exactly as Swift
        // lays out { Inner; Int8 }.
        var v = default(SwiftOuter);
        Assert.Equal(0, OffsetOf(ref v, ref v.Inner));
        Assert.Equal(9, OffsetOf(ref v, ref v.C));
        Assert.Equal(16, Unsafe.SizeOf<SwiftOuter>());
    }

    [Fact]
    public static void EmptyStructIsZeroSized()
    {
        // Swift empty structs have size 0 and stride 1; managed zero-sized
        // handling reports instance size 1.
        Assert.Equal(1, Unsafe.SizeOf<SwiftEmpty>());
    }

    [Fact]
    public static void NestedEmptyStructOccupiesNoSpace()
    {
        var v = default(SwiftWrapsEmpty);
        Assert.Equal(0, OffsetOf(ref v, ref v.X));
        Assert.Equal(1, Unsafe.SizeOf<SwiftWrapsEmpty>());
    }

    [Fact]
    public static void SequentialNestedStructDoesNotTailPack()
    {
        // A non-SwiftStruct nested value keeps its full managed size (16),
        // so the trailing field lands at 16 and the stride grows to 24.
        var v = default(SwiftWithSequentialInner);
        Assert.Equal(16, OffsetOf(ref v, ref v.C));
        Assert.Equal(24, Unsafe.SizeOf<SwiftWithSequentialInner>());
    }
}
