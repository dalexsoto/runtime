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
    public static void GenericSwiftStructLayoutPerInstantiation()
    {
        // Layout is computed per instantiation, including tail packing when
        // the generic argument is itself a SwiftStruct.
        var overLong = default(SwiftGenericHolder<long>);
        Assert.Equal(16, Unsafe.SizeOf<SwiftGenericHolder<long>>());
        Assert.Equal(8, OffsetOf(ref overLong, ref overLong.B));

        var overByte = default(SwiftGenericHolder<byte>);
        Assert.Equal(2, Unsafe.SizeOf<SwiftGenericHolder<byte>>());
        Assert.Equal(1, OffsetOf(ref overByte, ref overByte.B));

        // T = SwiftInner (unpadded size 9): B tail-packs to offset 9.
        var overInner = default(SwiftGenericHolder<SwiftInner>);
        Assert.Equal(16, Unsafe.SizeOf<SwiftGenericHolder<SwiftInner>>());
        Assert.Equal(9, OffsetOf(ref overInner, ref overInner.B));
    }

    [Fact]
    public static void ReflectionEmitSwiftStructLayout()
    {
        if (!TestLibrary.Utilities.IsReflectionEmitSupported)
            return;

        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName("SwiftStructEmit"),
            System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("SwiftStructEmit");

        var typeBuilder = module.DefineType(
            "EmittedSwiftOuter",
            System.Reflection.TypeAttributes.Public
                | System.Reflection.TypeAttributes.ExtendedLayout
                | System.Reflection.TypeAttributes.Sealed,
            typeof(ValueType));
        var attributeCtor = typeof(System.Runtime.InteropServices.ExtendedLayoutAttribute)
            .GetConstructor([typeof(System.Runtime.InteropServices.ExtendedLayoutKind)])!;
        typeBuilder.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            attributeCtor, [(System.Runtime.InteropServices.ExtendedLayoutKind)2]));
        typeBuilder.DefineField("Inner", typeof(SwiftInner), System.Reflection.FieldAttributes.Public);
        typeBuilder.DefineField("C", typeof(sbyte), System.Reflection.FieldAttributes.Public);

        Type emitted = typeBuilder.CreateType();

        // SwiftStruct types are blittable, so the marshalling layout equals
        // the managed layout: the trailing field tail-packs to offset 9.
        Assert.Equal(16, System.Runtime.InteropServices.Marshal.SizeOf(emitted));
        Assert.Equal(9, (int)System.Runtime.InteropServices.Marshal.OffsetOf(emitted, "C"));
    }

    [Fact]
    public static void EnumWithExtendedLayoutIsRejected()
    {
        // Validation parity: enums may not use extended layout in either
        // the CoreCLR VM or the managed AOT type system.
        Assert.Throws<TypeLoadException>(TouchExtendedEnum);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void TouchExtendedEnum() => typeof(SwiftEnumExtended).ToString();
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
