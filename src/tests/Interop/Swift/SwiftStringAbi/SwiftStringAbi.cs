// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Swift;
using System.Text;
using Xunit;
using TestLibrary;

// Redesigned SwiftString walking skeleton (roadmap Phase 6 "Port or
// redesign SwiftString"): a 16-byte mirror of the frozen stdlib String
// with value semantics implemented through the stdlib value witness table
// (destroy/copy are ARC-correct for small, large, and shared
// representations alike), creation through the exported String(cString:)
// initializer, borrowing calls by value, and mutation through the
// probe-verified method convention (mutable self pointer in the self
// register). Test-hosted until API review, like the rest of the support
// layer.
namespace Swift.Runtime.Support
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SwiftString : IDisposable
    {
        private const string SwiftCoreLib = "/usr/lib/swift/libswiftCore.dylib";

        // The two words of _StringObject; never interpreted managed-side.
        private ulong _countAndFlags;
        private IntPtr _object;

        [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
        [DllImport(SwiftCoreLib, EntryPoint = "$sSS7cStringSSSPys4Int8VG_tcfC")]
        private static extern SwiftString FromCString(byte* nullTerminatedUtf8);

        [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
        [DllImport(SwiftCoreLib, EntryPoint = "$sSS5countSivg")]
        private static extern nint CharacterCount(SwiftString s);

        [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
        [DllImport(SwiftCoreLib, EntryPoint = "$sSS6appendyySSF")]
        private static extern void AppendCore(SwiftString other, SwiftSelf self);

        private static IntPtr s_metadata;

        private static IntPtr Metadata
        {
            get
            {
                if (s_metadata == IntPtr.Zero)
                    s_metadata = NativeLibrary.GetExport(NativeLibrary.Load(SwiftCoreLib), "$sSSN");
                return s_metadata;
            }
        }

        private static void** Vwt => *(void***)((byte*)Metadata - sizeof(nint));

        /// <summary>Creates an owned Swift string from a managed string.</summary>
        public static SwiftString Create(string value)
        {
            byte[] utf8 = new byte[Encoding.UTF8.GetByteCount(value) + 1];
            Encoding.UTF8.GetBytes(value, utf8);
            fixed (byte* p = utf8)
            {
                return FromCString(p);
            }
        }

        /// <summary>Character count; the call borrows the value.</summary>
        public readonly int Count => (int)CharacterCount(this);

        /// <summary>Appends through the mutating method convention (self pointer in the self register).</summary>
        public void Append(SwiftString other)
        {
            AppendCore(other, new SwiftSelf(Unsafe.AsPointer(ref this)));
        }

        /// <summary>An independently owned copy via the value witness table.</summary>
        public readonly SwiftString Copy()
        {
            SwiftString copy;
            fixed (SwiftString* self = &this)
            {
                ((delegate* unmanaged[Swift]<void*, void*, IntPtr, void*>)Vwt[2])(&copy, self, Metadata);
            }
            return copy;
        }

        /// <summary>Releases the value through the value witness table; correct for every representation.</summary>
        public void Dispose()
        {
            fixed (SwiftString* self = &this)
            {
                ((delegate* unmanaged[Swift]<void*, IntPtr, void>)Vwt[1])(self, Metadata);
            }
            this = default; // empty small string: safe to double-dispose
        }
    }
}

[PlatformSpecific(TestPlatforms.AnyApple)]
public static unsafe class SwiftStringAbi
{
    private const string SwiftLib = "libSwiftStringAbi.dylib";

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s14SwiftStringAbi7utf8Sumys5Int64VSSF")]
    private static extern long Utf8Sum(Swift.Runtime.Support.SwiftString s);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s14SwiftStringAbi6concatyS2S_SStF")]
    private static extern Swift.Runtime.Support.SwiftString Concat(
        Swift.Runtime.Support.SwiftString a, Swift.Runtime.Support.SwiftString b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s14SwiftStringAbi12stringsEqualys5Int64VSS_SStF")]
    private static extern long StringsEqual(Swift.Runtime.Support.SwiftString a, Swift.Runtime.Support.SwiftString b);

    [UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
    [DllImport(SwiftLib, EntryPoint = "$s14SwiftStringAbi14equalsAlphabetys5Int64VSSF")]
    private static extern long EqualsAlphabet(Swift.Runtime.Support.SwiftString s);

    // 36 characters: guaranteed heap representation (small strings max 15 bytes).
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    private static long ExpectedUtf8Sum(string s)
    {
        long sum = 0;
        foreach (byte b in Encoding.UTF8.GetBytes(s))
            sum += b;
        return sum;
    }

    // Unicode edges (Audit follow-ups): grapheme clusters, surrogate pairs,
    // canonical equivalence, the small-vs-heap boundary, and the documented
    // Create() substitution/truncation contracts.

    [Fact]
    public static void GraphemeClusterCountsAsOneCharacter()
    {
        // ZWJ family emoji: one Character, 25 UTF-8 bytes (heap representation).
        using var s = Swift.Runtime.Support.SwiftString.Create(
            "\U0001F469\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466");
        Assert.Equal(1, s.Count);
    }

    [Fact]
    public static void SurrogatePairScalarCountsAsOneCharacter()
    {
        using var s = Swift.Runtime.Support.SwiftString.Create("\U0001D11E"); // musical G clef
        Assert.Equal(1, s.Count);
    }

    [Fact]
    public static void CanonicalEquivalenceAcrossNormalizationForms()
    {
        using var nfc = Swift.Runtime.Support.SwiftString.Create("\u00E9");        // precomposed
        using var nfd = Swift.Runtime.Support.SwiftString.Create("e\u0301");       // combining acute
        Assert.Equal(1, nfc.Count);
        Assert.Equal(1, nfd.Count);
        // Different UTF-8 bytes, canonically equal in Swift.
        Assert.Equal(1, StringsEqual(nfc, nfd));
    }

    [Fact]
    public static void SmallToHeapRepresentationBoundary()
    {
        // 15 UTF-8 bytes is the 64-bit small-string maximum; 16 goes heap.
        using var small = Swift.Runtime.Support.SwiftString.Create("abcdefghijklmno");
        using var heap = Swift.Runtime.Support.SwiftString.Create("abcdefghijklmnop");
        Assert.Equal(15, small.Count);
        Assert.Equal(16, heap.Count);

        var grown = Swift.Runtime.Support.SwiftString.Create("abcdefghijklmno");
        using var one = Swift.Runtime.Support.SwiftString.Create("p");
        grown.Append(one); // crosses small -> heap while mutating in place
        Assert.Equal(16, grown.Count);
        grown.Dispose();
    }

    [Fact]
    public static void UnpairedSurrogateSubstitutesReplacementCharacter()
    {
        // UTF-8 encoding of a lone surrogate substitutes U+FFFD; Create()
        // therefore never produces ill-formed Swift strings.
        using var s = Swift.Runtime.Support.SwiftString.Create("a\uD800b");
        using var expected = Swift.Runtime.Support.SwiftString.Create("a\uFFFDb");
        Assert.Equal(3, s.Count);
        Assert.Equal(1, StringsEqual(s, expected));
    }

    [Fact]
    public static void EmbeddedNulTruncatesInCStringCreate()
    {
        // Documented Create() contract: the C-string initializer stops at the
        // first NUL. A length-based creator is the fix if embedded NULs are
        // ever needed.
        using var s = Swift.Runtime.Support.SwiftString.Create("a\0b");
        Assert.Equal(1, s.Count);
    }

    [Fact]
    public static void SmallStringCreateCountDispose()
    {
        using var s = Swift.Runtime.Support.SwiftString.Create("héllo");
        Assert.Equal(5, s.Count);
        Assert.Equal(ExpectedUtf8Sum("héllo"), Utf8Sum(s));
    }

    [Fact]
    public static void LargeStringRoundTripsByValue()
    {
        var s = Swift.Runtime.Support.SwiftString.Create(Alphabet);
        Assert.Equal(36, s.Count);
        Assert.Equal(1, EqualsAlphabet(s));
        Assert.Equal(ExpectedUtf8Sum(Alphabet), Utf8Sum(s));
        s.Dispose();
        s.Dispose(); // double-dispose is a no-op on the empty value
    }

    [Fact]
    public static void ConcatReturnsOwnedString()
    {
        using var a = Swift.Runtime.Support.SwiftString.Create("abcdefghijklm");
        using var b = Swift.Runtime.Support.SwiftString.Create("nopqrstuvwxyz0123456789");
        using var joined = Concat(a, b);

        Assert.Equal(36, joined.Count);
        Assert.Equal(1, EqualsAlphabet(joined));
    }

    [Fact]
    public static void AppendMutatesThroughSelfRegister()
    {
        var s = Swift.Runtime.Support.SwiftString.Create("abcdefghijklm");
        using var tail = Swift.Runtime.Support.SwiftString.Create("nopqrstuvwxyz0123456789");

        s.Append(tail); // small-to-large growth in place
        Assert.Equal(36, s.Count);
        Assert.Equal(1, EqualsAlphabet(s));
        s.Dispose();
    }

    [Fact]
    public static void CopyHasIndependentValueSemantics()
    {
        var original = Swift.Runtime.Support.SwiftString.Create(Alphabet);
        var copy = original.Copy();

        original.Dispose();
        Assert.Equal(1, EqualsAlphabet(copy)); // survives the original
        Assert.Equal(36, copy.Count);
        copy.Dispose();
    }
}
