// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using Xunit;
using TestLibrary;

[PlatformSpecific(TestPlatforms.AnyApple)]
public class SelfContextTests
{
    private const string SwiftLib = "libSwiftSelfContext.dylib";

    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext0B7LibraryC11getInstanceSvyFZ")]
    public unsafe static extern void* getInstance();

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext0B7LibraryC14getMagicNumberSiyFTj")]
    public static extern nint getMagicNumber(SwiftSelf self);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext0B7LibraryC14getMagicNumberSiyFTj")]
    public static extern nint getMagicNumberOnStack(int dummy0, int dummy1, int dummy2, int dummy3, int dummy4, int dummy5, int dummy6, int dummy7, int dummy8, int dummy9, SwiftSelf self);

    [Fact]
    public unsafe static void TestSwiftSelfContext()
    {
        void* pointer = getInstance();
        SwiftSelf self = new SwiftSelf(pointer);
        Assert.True(self.Value != null, "Failed to obtain an instance of SwiftSelf from the Swift library.");

        int result = (int)getMagicNumber(self);
        Assert.True(result == 42, "The result from Swift does not match the expected value.");
    }

    [Fact]
    public unsafe static void TestSwiftSelfContextOnStack()
    {
        void* pointer = getInstance();
        SwiftSelf self = new SwiftSelf(pointer);
        Assert.True(self.Value != null, "Failed to obtain an instance of SwiftSelf from the Swift library.");

        int i = 0;
        int result = (int)getMagicNumberOnStack(i, i + 1, i + 2, i + 3, i + 4, i + 5, i + 6, i + 7, i + 8, i + 9, self);
        Assert.True(result == 42, "The result from Swift does not match the expected value.");
    }

    public struct FrozenEnregisteredStruct
    {
        public long A;
        public long B;
    }

    public struct FrozenNonEnregisteredStruct
    {
        public long A;
        public long B;
        public long C;
        public long D;
        public long E;
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext24FrozenEnregisteredStructV3sums5Int64VyF")]
    public static extern long SumFrozenEnregisteredStruct(SwiftSelf<FrozenEnregisteredStruct> self);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext27FrozenNonEnregisteredStructV3sums5Int64VyF")]
    public static extern long SumFrozenNonEnregisteredStruct(SwiftSelf<FrozenNonEnregisteredStruct> self);

    [Fact]
    public unsafe static void TestSelfIsFrozenEnregisteredStruct()
    {
        long sum = SumFrozenEnregisteredStruct(new SwiftSelf<FrozenEnregisteredStruct>(new FrozenEnregisteredStruct { A = 10, B = 20 }));
        Assert.Equal(30, sum);
    }

    [Fact]
    public unsafe static void TestSelfIsFrozenNonEnregisteredStruct()
    {
        long sum = SumFrozenNonEnregisteredStruct(new SwiftSelf<FrozenNonEnregisteredStruct>(new FrozenNonEnregisteredStruct { A = 10, B = 20, C = 30, D = 40, E = 50 }));
        Assert.Equal(150, sum);
    }

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext24FrozenEnregisteredStructV16sumWithExtraArgs1c1dS2f_SftF")]
    public static extern float SumFrozenEnregisteredStructWithExtraArgs(float c, float d, SwiftSelf<FrozenEnregisteredStruct> self);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext27FrozenNonEnregisteredStructV16sumWithExtraArgs1f1gS2f_SftF")]
    public static extern float SumFrozenNonEnregisteredStructWithExtraArgs(float f, float g, SwiftSelf<FrozenNonEnregisteredStruct> self);

    [Fact]
    public unsafe static void TestSelfIsFrozenEnregisteredStructWithExtraArgs()
    {
        float sum = SumFrozenEnregisteredStructWithExtraArgs(3f, 4f, new SwiftSelf<FrozenEnregisteredStruct>(new FrozenEnregisteredStruct { A = 10, B = 20 }));
        Assert.Equal(37f, sum);
    }

    [Fact]
    public unsafe static void TestSelfIsFrozenNonEnregisteredStructWithExtraArgs()
    {
        float sum = SumFrozenNonEnregisteredStructWithExtraArgs(3f, 4f, new SwiftSelf<FrozenNonEnregisteredStruct>(new FrozenNonEnregisteredStruct { A = 10, B = 20, C = 30, D = 40, E = 50 }));
        Assert.Equal(157f, sum);
    }

    // Reverse P/Invoke: Swift invokes a managed UnmanagedCallersOnly function as a
    // closure. The closure context register is where Swift passes a by-reference
    // self value, so the managed function receives it as SwiftSelf<T>.

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext38sumFrozenNonEnregisteredStructCallback1fs5Int64VA2E_AEtXE_tF")]
    public unsafe static extern long SumFrozenNonEnregisteredStructCallback(delegate* unmanaged[Swift]<long, long, SwiftSelf<FrozenNonEnregisteredStruct>, long> func, void* funcContext);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s16SwiftSelfContext35sumFrozenEnregisteredStructCallback1fs5Int64VA2E_AeA0efG0VtXE_tF")]
    public unsafe static extern long SumFrozenEnregisteredStructCallback(delegate* unmanaged[Swift]<long, long, SwiftSelf<FrozenEnregisteredStruct>, long> func, void* funcContext);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long SumSelfByRefCallback(long a, long b, SwiftSelf<FrozenNonEnregisteredStruct> self)
    {
        FrozenNonEnregisteredStruct s = self.Value;
        return a + b + s.A + s.B + s.C + s.D + s.E;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvSwift)])]
    private static long SumSelfEnregisteredCallback(long a, long b, SwiftSelf<FrozenEnregisteredStruct> self)
    {
        return a + b + self.Value.A + self.Value.B;
    }

    [Fact]
    public unsafe static void TestReverseSelfIsFrozenNonEnregisteredStruct()
    {
        var s = new FrozenNonEnregisteredStruct { A = 100, B = 200, C = 300, D = 400, E = 500 };
        long result = SumFrozenNonEnregisteredStructCallback(&SumSelfByRefCallback, &s);
        Assert.Equal(11 + 22 + 1500, result);
    }

    [Fact]
    public unsafe static void TestReverseSelfIsFrozenEnregisteredStruct()
    {
        long result = SumFrozenEnregisteredStructCallback(&SumSelfEnregisteredCallback, null);
        Assert.Equal(1000 + 2000 + 30 + 40, result);
    }

    // Direct invocation of Swift functions through delegate* unmanaged[Swift]
    // function pointers (calli) rather than P/Invoke declarations.

    [Fact]
    public unsafe static void TestDirectCalliWithEnregisteredSelf()
    {
        IntPtr lib = NativeLibrary.Load(SwiftLib, System.Reflection.Assembly.GetExecutingAssembly(), null);
        var fn = (delegate* unmanaged[Swift]<SwiftSelf<FrozenEnregisteredStruct>, long>)NativeLibrary.GetExport(lib, "$s16SwiftSelfContext24FrozenEnregisteredStructV3sums5Int64VyF");
        long sum = fn(new SwiftSelf<FrozenEnregisteredStruct>(new FrozenEnregisteredStruct { A = 10, B = 20 }));
        Assert.Equal(30, sum);
    }

    [Fact]
    public unsafe static void TestDirectCalliWithByRefSelf()
    {
        IntPtr lib = NativeLibrary.Load(SwiftLib, System.Reflection.Assembly.GetExecutingAssembly(), null);
        var fn = (delegate* unmanaged[Swift]<SwiftSelf<FrozenNonEnregisteredStruct>, long>)NativeLibrary.GetExport(lib, "$s16SwiftSelfContext27FrozenNonEnregisteredStructV3sums5Int64VyF");
        long sum = fn(new SwiftSelf<FrozenNonEnregisteredStruct>(new FrozenNonEnregisteredStruct { A = 1, B = 2, C = 3, D = 4, E = 5 }));
        Assert.Equal(15, sum);
    }

    // Plain (pointer-sized) SwiftSelf through calli: the shape protocol
    // witness dispatch uses (self address in the self register, ordinary
    // trailing arguments). The class dispatch thunk reads only the self
    // register, so extra ordinary arguments verify that x20 routing is
    // position-independent for indirect calls too.

    [Fact]
    public unsafe static void TestDirectCalliWithPlainSelf()
    {
        void* pointer = getInstance();
        IntPtr lib = NativeLibrary.Load(SwiftLib, System.Reflection.Assembly.GetExecutingAssembly(), null);
        var fn = (delegate* unmanaged[Swift]<SwiftSelf, nint>)NativeLibrary.GetExport(lib, "$s16SwiftSelfContext0B7LibraryC14getMagicNumberSiyFTj");
        Assert.Equal(42, (long)fn(new SwiftSelf(pointer)));
    }

    [Fact]
    public unsafe static void TestDirectCalliWithPlainSelfAfterArgs()
    {
        void* pointer = getInstance();
        IntPtr lib = NativeLibrary.Load(SwiftLib, System.Reflection.Assembly.GetExecutingAssembly(), null);
        var fn = (delegate* unmanaged[Swift]<nint, nint, SwiftSelf, nint>)NativeLibrary.GetExport(lib, "$s16SwiftSelfContext0B7LibraryC14getMagicNumberSiyFTj");
        Assert.Equal(42, (long)fn(111, 222, new SwiftSelf(pointer)));
    }

    [Fact]
    public unsafe static void TestDirectCalliWithPlainSelfBeforeArgs()
    {
        void* pointer = getInstance();
        IntPtr lib = NativeLibrary.Load(SwiftLib, System.Reflection.Assembly.GetExecutingAssembly(), null);
        var fn = (delegate* unmanaged[Swift]<SwiftSelf, nint, nint, nint>)NativeLibrary.GetExport(lib, "$s16SwiftSelfContext0B7LibraryC14getMagicNumberSiyFTj");
        Assert.Equal(42, (long)fn(new SwiftSelf(pointer), 111, 222));
    }
}
