// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SwiftStressGenerator
{
    /// <summary>
    /// Deterministic pseudo-random source (splitmix64). Deliberately not
    /// System.Random: the output of this generator must be byte-identical for a
    /// given seed regardless of the .NET version the tool runs on.
    /// </summary>
    internal sealed class SeededRandom
    {
        private ulong _state;

        public SeededRandom(ulong seed) => _state = seed;

        public ulong NextUInt64()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform value in [minInclusive, maxExclusive).</summary>
        public int Next(int minInclusive, int maxExclusive)
        {
            ulong range = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt64() % range);
        }

        public bool NextBool(double probability) => NextUInt64() < (ulong)(probability * ulong.MaxValue);

        public T Pick<T>(IReadOnlyList<T> items) => items[Next(0, items.Count)];
    }

    internal enum PrimitiveKind
    {
        Int8, UInt8, Int16, UInt16, Int32, UInt32, Int64, UInt64, NInt, NUInt, Float, Double
    }

    internal static class Primitives
    {
        public static readonly PrimitiveKind[] All = (PrimitiveKind[])Enum.GetValues(typeof(PrimitiveKind));

        public static string SwiftName(PrimitiveKind k) => k switch
        {
            PrimitiveKind.Int8 => "Int8",
            PrimitiveKind.UInt8 => "UInt8",
            PrimitiveKind.Int16 => "Int16",
            PrimitiveKind.UInt16 => "UInt16",
            PrimitiveKind.Int32 => "Int32",
            PrimitiveKind.UInt32 => "UInt32",
            PrimitiveKind.Int64 => "Int64",
            PrimitiveKind.UInt64 => "UInt64",
            PrimitiveKind.NInt => "Int",
            PrimitiveKind.NUInt => "UInt",
            PrimitiveKind.Float => "Float",
            PrimitiveKind.Double => "Double",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        public static string CSharpName(PrimitiveKind k) => k switch
        {
            PrimitiveKind.Int8 => "sbyte",
            PrimitiveKind.UInt8 => "byte",
            PrimitiveKind.Int16 => "short",
            PrimitiveKind.UInt16 => "ushort",
            PrimitiveKind.Int32 => "int",
            PrimitiveKind.UInt32 => "uint",
            PrimitiveKind.Int64 => "long",
            PrimitiveKind.UInt64 => "ulong",
            PrimitiveKind.NInt => "nint",
            PrimitiveKind.NUInt => "nuint",
            PrimitiveKind.Float => "float",
            PrimitiveKind.Double => "double",
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        public static int Size(PrimitiveKind k) => k switch
        {
            PrimitiveKind.Int8 or PrimitiveKind.UInt8 => 1,
            PrimitiveKind.Int16 or PrimitiveKind.UInt16 => 2,
            PrimitiveKind.Int32 or PrimitiveKind.UInt32 or PrimitiveKind.Float => 4,
            _ => 8,
        };

        public static bool IsFloating(PrimitiveKind k) => k is PrimitiveKind.Float or PrimitiveKind.Double;

        public static bool IsSigned(PrimitiveKind k) =>
            k is PrimitiveKind.Int8 or PrimitiveKind.Int16 or PrimitiveKind.Int32 or PrimitiveKind.Int64 or PrimitiveKind.NInt;
    }

    /// <summary>
    /// A concrete value for one primitive leaf: literal text for both languages
    /// plus the little-endian in-memory bytes used for hashing.
    /// </summary>
    internal sealed class Value
    {
        public PrimitiveKind Kind { get; }
        public string SwiftLiteral { get; }
        public string CSharpLiteral { get; }
        public string CSharpTypedLiteral { get; }
        public byte[] HashBytes { get; }

        private Value(PrimitiveKind kind, string swiftLit, string csLit, string csTypedLit, byte[] hashBytes)
        {
            Kind = kind;
            SwiftLiteral = swiftLit;
            CSharpLiteral = csLit;
            CSharpTypedLiteral = csTypedLit;
            HashBytes = hashBytes;
        }

        public static Value Create(PrimitiveKind kind, SeededRandom rng)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            int size = Primitives.Size(kind);

            if (Primitives.IsFloating(kind))
            {
                // Integral-valued floats round-trip exactly through decimal
                // literals on both sides of the interop boundary.
                ulong max = kind == PrimitiveKind.Float ? 1UL << 24 : 1UL << 50;
                ulong m = NonZero(rng) % max;
                string lit = m.ToString(inv);
                byte[] bytes = new byte[size];
                if (kind == PrimitiveKind.Float)
                    BinaryPrimitives.WriteSingleLittleEndian(bytes, (float)m);
                else
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes, (double)m);
                return new Value(kind, lit, lit, $"({Primitives.CSharpName(kind)}){lit}", bytes);
            }

            ulong raw = NonZero(rng);
            byte[] le = new byte[8];
            if (Primitives.IsSigned(kind))
            {
                long v = size switch
                {
                    1 => (sbyte)raw,
                    2 => (short)raw,
                    4 => (int)raw,
                    _ => (long)raw,
                };
                // long.MinValue is not expressible as a plain literal in either
                // language without extra ceremony; avoid it.
                if (v == long.MinValue)
                    v++;
                BinaryPrimitives.WriteInt64LittleEndian(le, v);
                string lit = v.ToString(inv);
                // nint is a contextual keyword: (nint)-1 parses as a subtraction,
                // so negative values need their own parentheses (CS0075).
                string castable = v < 0 ? $"({lit})" : lit;
                string csLit = kind == PrimitiveKind.NInt ? $"unchecked((nint){castable})" : lit;
                string csTyped = kind == PrimitiveKind.NInt ? csLit : $"({Primitives.CSharpName(kind)}){lit}";
                return new Value(kind, lit, csLit, csTyped, le.AsSpan(0, size).ToArray());
            }
            else
            {
                ulong v = size switch
                {
                    1 => (byte)raw,
                    2 => (ushort)raw,
                    4 => (uint)raw,
                    _ => raw,
                };
                BinaryPrimitives.WriteUInt64LittleEndian(le, v);
                string lit = v.ToString(inv);
                string csLit = kind == PrimitiveKind.NUInt ? $"unchecked((nuint){lit})" : lit;
                string csTyped = kind == PrimitiveKind.NUInt ? csLit : $"({Primitives.CSharpName(kind)}){lit}";
                return new Value(kind, lit, csLit, csTyped, le.AsSpan(0, size).ToArray());
            }
        }

        private static ulong NonZero(SeededRandom rng)
        {
            ulong v = rng.NextUInt64();
            return v == 0 ? 1 : v;
        }
    }

    /// <summary>FNV-1a, bit-identical to the HasherFNV1a used by the Swift test sources.</summary>
    internal struct Fnv1aHasher
    {
        private ulong _hash;

        public static Fnv1aHasher Create() => new Fnv1aHasher { _hash = 14695981039346656037UL };

        public void Combine(byte[] bytes)
        {
            foreach (byte b in bytes)
            {
                _hash ^= b;
                _hash = unchecked(_hash * 1099511628211UL);
            }
        }

        public long Finish() => unchecked((long)_hash);
    }

    internal readonly record struct LeafSlot(int Offset, PrimitiveKind Kind, string SwiftPath, string CSharpPath);

    internal abstract class TypeRef
    {
        public abstract string SwiftName { get; }
        public abstract string CSharpName { get; }
        public abstract int Size { get; }
        public abstract int Alignment { get; }
        public abstract IEnumerable<LeafSlot> Leaves(int baseOffset, string swiftPath, string csharpPath);
    }

    internal sealed class PrimitiveTypeRef : TypeRef
    {
        public PrimitiveKind Kind { get; }

        public PrimitiveTypeRef(PrimitiveKind kind) => Kind = kind;

        public override string SwiftName => Primitives.SwiftName(Kind);
        public override string CSharpName => Primitives.CSharpName(Kind);
        public override int Size => Primitives.Size(Kind);
        public override int Alignment => Primitives.Size(Kind);

        public override IEnumerable<LeafSlot> Leaves(int baseOffset, string swiftPath, string csharpPath)
        {
            yield return new LeafSlot(baseOffset, Kind, swiftPath, csharpPath);
        }
    }

    /// <summary>
    /// A generated frozen Swift struct / C# blittable struct pair. Either a list
    /// of named fields (regular form) or a homogeneous tuple, which is emitted
    /// as a Swift tuple field and a C# [InlineArray].
    /// </summary>
    internal sealed class StructDecl : TypeRef
    {
        public string Name { get; }
        public bool IsInlineArray { get; }
        public PrimitiveKind ElementKind { get; }
        public int ElementCount { get; }
        public List<(string FieldName, TypeRef Type)> Fields { get; } = new();

        private int _size = -1;
        private int _alignment = -1;

        public StructDecl(string name) => Name = name;

        public StructDecl(string name, PrimitiveKind elementKind, int elementCount)
        {
            Name = name;
            IsInlineArray = true;
            ElementKind = elementKind;
            ElementCount = elementCount;
        }

        public override string SwiftName => Name;
        public override string CSharpName => Name;

        public override int Size
        {
            get { ComputeLayout(); return _size; }
        }

        public override int Alignment
        {
            get { ComputeLayout(); return _alignment; }
        }

        private void ComputeLayout()
        {
            if (_size >= 0)
                return;
            if (IsInlineArray)
            {
                _size = Primitives.Size(ElementKind) * ElementCount;
                _alignment = Primitives.Size(ElementKind);
                return;
            }
            // Swift universal layout for frozen structs: natural alignment,
            // sequential, size does not include tail padding. CLR sequential
            // layout with an explicit Size produces the same offsets.
            int offset = 0;
            int align = 1;
            foreach ((_, TypeRef type) in Fields)
            {
                offset = AlignUp(offset, type.Alignment);
                offset += type.Size;
                align = Math.Max(align, type.Alignment);
            }
            _size = offset;
            _alignment = align;
        }

        public override IEnumerable<LeafSlot> Leaves(int baseOffset, string swiftPath, string csharpPath)
        {
            if (IsInlineArray)
            {
                for (int i = 0; i < ElementCount; i++)
                {
                    yield return new LeafSlot(
                        baseOffset + i * Primitives.Size(ElementKind),
                        ElementKind,
                        $"{swiftPath}.elements.{i.ToString(CultureInfo.InvariantCulture)}",
                        $"{csharpPath}[{i.ToString(CultureInfo.InvariantCulture)}]");
                }
                yield break;
            }

            int offset = baseOffset;
            int index = 0;
            foreach ((string fieldName, TypeRef type) in Fields)
            {
                offset = AlignUp(offset, type.Alignment);
                string swift = swiftPath.Length == 0 ? fieldName : $"{swiftPath}.{fieldName}";
                string cs = csharpPath.Length == 0 ? CSharpFieldName(index) : $"{csharpPath}.{CSharpFieldName(index)}";
                foreach (LeafSlot leaf in type.Leaves(offset, swift, cs))
                    yield return leaf;
                offset += type.Size;
                index++;
            }
        }

        public static string CSharpFieldName(int index) => "F" + index.ToString(CultureInfo.InvariantCulture);

        /// <summary>All struct declarations reachable from this one, nested first (post-order).</summary>
        public IEnumerable<StructDecl> SelfAndNestedPostOrder()
        {
            foreach ((_, TypeRef type) in Fields)
            {
                if (type is StructDecl nested)
                {
                    foreach (StructDecl d in nested.SelfAndNestedPostOrder())
                        yield return d;
                }
            }
            yield return this;
        }

        internal static int AlignUp(int offset, int alignment) => (offset + alignment - 1) & ~(alignment - 1);
    }

    internal sealed class LoweringResult
    {
        public bool ByReference { get; init; }
        public int ElementCount { get; init; }

        /// <summary>Lowered elements as LLVM IR parameter type names, in order.</summary>
        public IReadOnlyList<string> IrElements { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Simulation of the normative Swift physical-lowering algorithm from
    /// docs/design/interop/swift/lowering.md, used to steer generated shapes
    /// toward interesting classes (at-cap, over-cap). Only natural sequential
    /// layouts are generated, so the misalignment and overlap rules never fire.
    /// </summary>
    internal static class SwiftLoweringSimulator
    {
        private enum Tag : byte { Empty, Opaque, Int64, Float, Double }

        public static LoweringResult Lower(StructDecl type)
        {
            int n = type.Size;
            if (n == 0)
                return new LoweringResult { ByReference = false, ElementCount = 0 };

            // Step 1: byte tagging.
            var tags = new Tag[n];
            foreach (LeafSlot leaf in type.Leaves(0, string.Empty, string.Empty))
            {
                int size = Primitives.Size(leaf.Kind);
                (Tag tag, int align) = leaf.Kind switch
                {
                    PrimitiveKind.Float => (Tag.Float, 4),
                    PrimitiveKind.Double => (Tag.Double, 8),
                    _ when size == 8 => (Tag.Int64, 8),
                    _ => (Tag.Opaque, 1),
                };
                if (leaf.Offset % align != 0)
                    tag = Tag.Opaque;
                for (int i = 0; i < size; i++)
                    tags[leaf.Offset + i] = tag;
            }

            // Step 2: interval formation.
            var intervals = new List<(int Start, int End, Tag Tag)>();
            for (int i = 0; i < n; i++)
            {
                Tag t = tags[i];
                if (t == Tag.Empty)
                    continue;
                bool newInterval = i == 0 || tags[i - 1] != t ||
                    (t == Tag.Float && i % 4 == 0) ||
                    ((t == Tag.Double || t == Tag.Int64) && i % 8 == 0);
                if (newInterval || intervals.Count == 0 || intervals[^1].End != i)
                    intervals.Add((i, i + 1, t));
                else
                    intervals[^1] = (intervals[^1].Start, i + 1, t);
            }

            // Step 3: opaque gap bridging ((A.end - 1) / 8 == B.start / 8).
            for (int i = 0; i + 1 < intervals.Count;)
            {
                var a = intervals[i];
                var b = intervals[i + 1];
                if (a.Tag == Tag.Opaque && b.Tag == Tag.Opaque && (a.End - 1) / 8 == b.Start / 8)
                {
                    intervals[i] = (a.Start, b.End, Tag.Opaque);
                    intervals.RemoveAt(i + 1);
                }
                else
                {
                    i++;
                }
            }

            // Step 4: element emission.
            var elements = new List<string>();
            foreach ((int start, int end, Tag tag) in intervals)
            {
                if (tag != Tag.Opaque)
                {
                    elements.Add(tag switch
                    {
                        Tag.Float => "float",
                        Tag.Double => "double",
                        _ => "i64",
                    });
                    continue;
                }
                int p = start;
                int r = end - start;
                while (r > 0)
                {
                    if (r > 4 && p % 8 == 0) { elements.Add("i64"); p += 8; r -= 8; }
                    else if (r > 2 && p % 4 == 0) { elements.Add("i32"); p += 4; r -= 4; }
                    else if (r > 1 && p % 2 == 0) { elements.Add("i16"); p += 2; r -= 2; }
                    else { elements.Add("i8"); p += 1; r -= 1; }
                }
            }

            // Step 5: by-reference decision (cap is inclusive).
            return new LoweringResult
            {
                ByReference = elements.Count > 4,
                ElementCount = elements.Count,
                IrElements = elements,
            };
        }
    }

    internal enum StructRecipe
    {
        Random,
        PaddingHeavy,
        FloatMix,
        AtCap,
        OverCap,
        HomogeneousFloat,
        InlineArrayTuple,
    }

    internal sealed class StructFactory
    {
        private const int MaxNestingDepth = 3;
        private readonly SeededRandom _rng;

        private static readonly PrimitiveKind[] s_smallInts =
        {
            PrimitiveKind.Int8, PrimitiveKind.UInt8, PrimitiveKind.Int16,
            PrimitiveKind.UInt16, PrimitiveKind.Int32, PrimitiveKind.UInt32,
        };

        private static readonly PrimitiveKind[] s_bigScalars =
        {
            PrimitiveKind.Int64, PrimitiveKind.UInt64, PrimitiveKind.Double,
            PrimitiveKind.NInt, PrimitiveKind.NUInt,
        };

        private static readonly PrimitiveKind[] s_floatMixPool =
        {
            PrimitiveKind.Float, PrimitiveKind.Double, PrimitiveKind.Int8,
            PrimitiveKind.Int16, PrimitiveKind.Int32, PrimitiveKind.UInt8,
            PrimitiveKind.UInt16, PrimitiveKind.UInt32,
        };

        private static readonly PrimitiveKind[] s_tupleElems =
        {
            PrimitiveKind.UInt8, PrimitiveKind.Int16, PrimitiveKind.Int32,
            PrimitiveKind.Int64, PrimitiveKind.Float, PrimitiveKind.Double,
        };

        public StructFactory(SeededRandom rng) => _rng = rng;

        public StructDecl Create(string name, StructRecipe recipe)
        {
            switch (recipe)
            {
                case StructRecipe.Random:
                    return CreateRandom(name, depth: 1, minFields: 1, maxFields: 8, allowNested: true);

                case StructRecipe.PaddingHeavy:
                {
                    // Small integer directly before a naturally aligned 8-byte
                    // scalar: guarantees interior padding that lowering must
                    // bridge into opaque integer chunks.
                    var s = new StructDecl(name);
                    int pairs = _rng.Next(1, 4);
                    for (int i = 0; i < pairs; i++)
                    {
                        s.Fields.Add(($"f{s.Fields.Count.ToString(CultureInfo.InvariantCulture)}", new PrimitiveTypeRef(_rng.Pick(s_smallInts))));
                        s.Fields.Add(($"f{s.Fields.Count.ToString(CultureInfo.InvariantCulture)}", new PrimitiveTypeRef(_rng.Pick(s_bigScalars))));
                    }
                    return s;
                }

                case StructRecipe.FloatMix:
                {
                    for (int attempt = 0; attempt < 32; attempt++)
                    {
                        var s = new StructDecl(name);
                        int fields = _rng.Next(2, 7);
                        for (int i = 0; i < fields; i++)
                            s.Fields.Add(($"f{i.ToString(CultureInfo.InvariantCulture)}", new PrimitiveTypeRef(_rng.Pick(s_floatMixPool))));
                        bool hasFloat = s.Fields.Any(f => Primitives.IsFloating(((PrimitiveTypeRef)f.Type).Kind));
                        bool hasInt = s.Fields.Any(f => !Primitives.IsFloating(((PrimitiveTypeRef)f.Type).Kind));
                        if (hasFloat && hasInt)
                            return s;
                    }
                    var fallback = new StructDecl(name);
                    fallback.Fields.Add(("f0", new PrimitiveTypeRef(PrimitiveKind.Float)));
                    fallback.Fields.Add(("f1", new PrimitiveTypeRef(PrimitiveKind.Int32)));
                    return fallback;
                }

                case StructRecipe.AtCap:
                {
                    // Exactly four lowered elements: direct, at the inclusive cap.
                    for (int attempt = 0; attempt < 64; attempt++)
                    {
                        StructDecl s = CreateRandom(name, depth: 1, minFields: 2, maxFields: 6, allowNested: true);
                        LoweringResult lowering = SwiftLoweringSimulator.Lower(s);
                        if (!lowering.ByReference && lowering.ElementCount == 4)
                            return s;
                    }
                    return CreateHomogeneous(name, PrimitiveKind.Int64, 4);
                }

                case StructRecipe.OverCap:
                {
                    // More than four lowered elements: passed by reference.
                    for (int attempt = 0; attempt < 64; attempt++)
                    {
                        StructDecl s = CreateRandom(name, depth: 1, minFields: 3, maxFields: 8, allowNested: true);
                        if (SwiftLoweringSimulator.Lower(s).ByReference)
                            return s;
                    }
                    return CreateHomogeneous(name, PrimitiveKind.Int64, 5);
                }

                case StructRecipe.HomogeneousFloat:
                {
                    PrimitiveKind kind = _rng.NextBool(0.5) ? PrimitiveKind.Float : PrimitiveKind.Double;
                    return CreateHomogeneous(name, kind, _rng.Next(2, 7));
                }

                case StructRecipe.InlineArrayTuple:
                {
                    PrimitiveKind elem = _rng.Pick(s_tupleElems);
                    int count = elem switch
                    {
                        PrimitiveKind.UInt8 => _rng.Next(4, 33),
                        PrimitiveKind.Int16 => _rng.Next(2, 17),
                        PrimitiveKind.Int32 or PrimitiveKind.Float => _rng.Next(2, 9),
                        _ => _rng.Next(2, 7),
                    };
                    return new StructDecl(name, elem, count);
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(recipe));
            }
        }

        /// <summary>A struct whose lowering is direct (at most 4 elements).</summary>
        public StructDecl CreateEnregistered(string name)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                StructDecl s = CreateRandom(name, depth: 1, minFields: 1, maxFields: 4, allowNested: true);
                if (!SwiftLoweringSimulator.Lower(s).ByReference)
                    return s;
            }
            return CreateHomogeneous(name, PrimitiveKind.Int64, 2);
        }

        private StructDecl CreateHomogeneous(string name, PrimitiveKind kind, int count)
        {
            var s = new StructDecl(name);
            for (int i = 0; i < count; i++)
                s.Fields.Add(($"f{i.ToString(CultureInfo.InvariantCulture)}", new PrimitiveTypeRef(kind)));
            return s;
        }

        private StructDecl CreateRandom(string name, int depth, int minFields, int maxFields, bool allowNested)
        {
            var s = new StructDecl(name);
            int fields = _rng.Next(minFields, maxFields + 1);
            int nestedIndex = 0;
            for (int i = 0; i < fields; i++)
            {
                string fieldName = $"f{i.ToString(CultureInfo.InvariantCulture)}";
                if (allowNested && depth < MaxNestingDepth && _rng.NextBool(0.15))
                {
                    StructDecl nested = CreateRandom(
                        $"{name}_S{nestedIndex.ToString(CultureInfo.InvariantCulture)}",
                        depth + 1, 1, 4, allowNested);
                    nestedIndex++;
                    s.Fields.Add((fieldName, nested));
                }
                else
                {
                    s.Fields.Add((fieldName, new PrimitiveTypeRef(_rng.Pick(Primitives.All))));
                }
            }
            return s;
        }
    }

    /// <summary>Concrete values for one instance of a type, shaped like the type.</summary>
    internal sealed class ValueTree
    {
        public TypeRef Type { get; }
        public Value? Primitive { get; }
        public List<ValueTree> Fields { get; } = new();
        public List<Value> Elements { get; } = new();

        private ValueTree(TypeRef type, Value? primitive)
        {
            Type = type;
            Primitive = primitive;
        }

        public static ValueTree Create(TypeRef type, SeededRandom rng)
        {
            if (type is PrimitiveTypeRef prim)
                return new ValueTree(type, Value.Create(prim.Kind, rng));

            var decl = (StructDecl)type;
            var tree = new ValueTree(type, null);
            if (decl.IsInlineArray)
            {
                for (int i = 0; i < decl.ElementCount; i++)
                    tree.Elements.Add(Value.Create(decl.ElementKind, rng));
            }
            else
            {
                foreach ((_, TypeRef fieldType) in decl.Fields)
                    tree.Fields.Add(Create(fieldType, rng));
            }
            return tree;
        }

        /// <summary>Leaf values in declaration order (matches TypeRef.Leaves order).</summary>
        public IEnumerable<Value> FlattenValues()
        {
            if (Primitive != null)
            {
                yield return Primitive;
                yield break;
            }
            foreach (Value v in Elements)
                yield return v;
            foreach (ValueTree field in Fields)
            {
                foreach (Value v in field.FlattenValues())
                    yield return v;
            }
        }
    }

    internal enum SuiteKind { Args, Returns, Callbacks, Calli, Invalid }

    internal enum FunctionKind
    {
        Args,
        Returns,
        CallbackNormal,
        CallbackSelfEnregistered,
        CallbackSelfByRef,
        Invalid,
    }

    internal enum InvalidKind
    {
        DupSwiftSelf,
        DupSwiftError,
        SwiftErrorByValue,
        SelfTNotLast,
        DupSelfAndSelfT,
    }

    internal sealed class ParamModel
    {
        public required string Name { get; init; }
        public required TypeRef Type { get; init; }
        public required ValueTree Values { get; init; }
    }

    internal sealed class FunctionModel
    {
        public required int Index { get; init; }
        public required FunctionKind Kind { get; init; }
        public required string SwiftFuncName { get; init; }
        public List<StructDecl> StructDecls { get; } = new();
        public List<ParamModel> Params { get; } = new();
        public ValueTree? ReturnTree { get; set; }
        public StructDecl? SelfStruct { get; set; }
        public ValueTree? SelfValues { get; set; }
        public PrimitiveKind CallbackReturnKind { get; set; }
        public Value? CallbackReturnValue { get; set; }
        public long ExpectedHash { get; set; }
        public string? MangledName { get; set; }
        public InvalidKind InvalidKind { get; set; }
        public bool HasReverseVariant { get; set; }
    }

    internal sealed class SuiteModel
    {
        public required SuiteKind Kind { get; init; }
        public required string Name { get; init; }
        public required int Seed { get; init; }
        public required int Count { get; init; }
        public required bool LibraryEvolution { get; init; }
        public required double SwiftSelfFraction { get; init; }
        public List<FunctionModel> Functions { get; } = new();

        public string CommandLine
        {
            get
            {
                CultureInfo inv = CultureInfo.InvariantCulture;
                string args = $"--seed {Seed.ToString(inv)} --count {Count.ToString(inv)} --suite {SuiteArg} --out <dir>";
                if (Kind == SuiteKind.Callbacks && Math.Abs(SwiftSelfFraction - SuiteBuilder.DefaultSwiftSelfFraction) > 0.0001)
                    args += $" --swiftself-fraction {SwiftSelfFraction.ToString("0.0###", inv)}";
                if (LibraryEvolution)
                    args += " --library-evolution";
                return args;
            }
        }

        public string SuiteArg => Kind switch
        {
            SuiteKind.Args => "args",
            SuiteKind.Returns => "returns",
            SuiteKind.Callbacks => "callbacks",
            SuiteKind.Calli => "calli",
            SuiteKind.Invalid => "invalid",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    internal static class SuiteBuilder
    {
        public const double DefaultSwiftSelfFraction = 0.25;

        private static readonly StructRecipe[] s_argRecipes =
        {
            StructRecipe.PaddingHeavy, StructRecipe.FloatMix, StructRecipe.AtCap,
            StructRecipe.OverCap, StructRecipe.HomogeneousFloat, StructRecipe.InlineArrayTuple,
        };

        private static readonly StructRecipe[] s_callbackRecipes =
        {
            StructRecipe.PaddingHeavy, StructRecipe.FloatMix, StructRecipe.AtCap,
            StructRecipe.OverCap, StructRecipe.HomogeneousFloat,
        };

        private static readonly StructRecipe[] s_returnRecipes =
        {
            StructRecipe.PaddingHeavy, StructRecipe.FloatMix, StructRecipe.AtCap,
            StructRecipe.OverCap, StructRecipe.HomogeneousFloat, StructRecipe.InlineArrayTuple,
            StructRecipe.Random,
        };

        private static readonly PrimitiveKind[] s_callbackReturnKinds =
        {
            PrimitiveKind.Int8, PrimitiveKind.UInt8, PrimitiveKind.Int16, PrimitiveKind.UInt16,
            PrimitiveKind.Int32, PrimitiveKind.UInt32, PrimitiveKind.Int64, PrimitiveKind.UInt64,
            PrimitiveKind.NInt, PrimitiveKind.NUInt, PrimitiveKind.Float, PrimitiveKind.Double,
        };

        public static SuiteModel Build(SuiteKind kind, string name, int seed, int count, bool libraryEvolution, double swiftSelfFraction)
        {
            var model = new SuiteModel
            {
                Kind = kind,
                Name = name,
                Seed = seed,
                Count = count,
                LibraryEvolution = libraryEvolution,
                SwiftSelfFraction = swiftSelfFraction,
            };
            var rng = new SeededRandom((ulong)seed * 0x2545F4914F6CDD1DUL + (ulong)kind);

            switch (kind)
            {
                case SuiteKind.Args:
                case SuiteKind.Calli:
                    for (int i = 0; i < count; i++)
                        model.Functions.Add(BuildArgsFunction(i, rng));
                    break;
                case SuiteKind.Returns:
                    for (int i = 0; i < count; i++)
                        model.Functions.Add(BuildReturnsFunction(i, rng));
                    break;
                case SuiteKind.Callbacks:
                    BuildCallbackFunctions(model, rng, count, swiftSelfFraction);
                    break;
                case SuiteKind.Invalid:
                    for (int i = 0; i < count; i++)
                        model.Functions.Add(BuildInvalidFunction(i, rng));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            return model;
        }

        private static FunctionModel BuildArgsFunction(int index, SeededRandom rng)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var fn = new FunctionModel
            {
                Index = index,
                Kind = FunctionKind.Args,
                SwiftFuncName = $"swiftFunc{index.ToString(inv)}",
            };
            var factory = new StructFactory(rng);

            int paramCount = rng.Next(1, 11);
            bool allPrimitive = index % 7 == 4;
            int designatedStructParam = allPrimitive ? -1 : rng.Next(0, paramCount);
            StructRecipe designatedRecipe = s_argRecipes[index % s_argRecipes.Length];

            int structIndex = 0;
            for (int p = 0; p < paramCount; p++)
            {
                string paramName = $"a{p.ToString(inv)}";
                TypeRef type;
                if (!allPrimitive && (p == designatedStructParam || rng.NextBool(0.3)))
                {
                    StructRecipe recipe = p == designatedStructParam ? designatedRecipe : StructRecipe.Random;
                    StructDecl decl = factory.Create($"F{index.ToString(inv)}_S{structIndex.ToString(inv)}", recipe);
                    structIndex++;
                    fn.StructDecls.AddRange(decl.SelfAndNestedPostOrder());
                    type = decl;
                }
                else
                {
                    type = new PrimitiveTypeRef(rng.Pick(Primitives.All));
                }
                fn.Params.Add(new ParamModel { Name = paramName, Type = type, Values = ValueTree.Create(type, rng) });
            }

            Fnv1aHasher hasher = Fnv1aHasher.Create();
            foreach (ParamModel param in fn.Params)
            {
                foreach (Value v in param.Values.FlattenValues())
                    hasher.Combine(v.HashBytes);
            }
            fn.ExpectedHash = hasher.Finish();
            return fn;
        }

        private static FunctionModel BuildReturnsFunction(int index, SeededRandom rng)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var fn = new FunctionModel
            {
                Index = index,
                Kind = FunctionKind.Returns,
                SwiftFuncName = $"swiftRetFunc{index.ToString(inv)}",
            };
            var factory = new StructFactory(rng);
            StructRecipe recipe = s_returnRecipes[index % s_returnRecipes.Length];
            StructDecl decl = factory.Create($"S{index.ToString(inv)}", recipe);
            fn.StructDecls.AddRange(decl.SelfAndNestedPostOrder());
            fn.ReturnTree = ValueTree.Create(decl, rng);
            return fn;
        }

        private static void BuildCallbackFunctions(SuiteModel model, SeededRandom rng, int count, double fraction)
        {
            int selfCount = (int)Math.Round(count * fraction, MidpointRounding.AwayFromZero);
            int selfOrdinal = 0;
            for (int i = 0; i < count; i++)
            {
                // Spread self-callbacks evenly across the suite, alternating
                // between enregistered and by-reference self values.
                bool isSelf = selfCount > 0 && ((i + 1) * selfCount / count) > (i * selfCount / count);
                model.Functions.Add(isSelf
                    ? BuildSelfCallbackFunction(i, rng, enregistered: selfOrdinal++ % 2 == 0)
                    : BuildNormalCallbackFunction(i, rng));
            }
        }

        private static FunctionModel BuildNormalCallbackFunction(int index, SeededRandom rng)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var fn = new FunctionModel
            {
                Index = index,
                Kind = FunctionKind.CallbackNormal,
                SwiftFuncName = $"swiftCallbackFunc{index.ToString(inv)}",
            };
            var factory = new StructFactory(rng);

            int paramCount = rng.Next(1, 11);
            bool allPrimitive = index % 7 == 5;
            int designatedStructParam = allPrimitive ? -1 : rng.Next(0, paramCount);
            StructRecipe designatedRecipe = s_callbackRecipes[index % s_callbackRecipes.Length];

            int structIndex = 0;
            for (int p = 0; p < paramCount; p++)
            {
                string paramName = $"a{p.ToString(inv)}";
                TypeRef type;
                if (!allPrimitive && (p == designatedStructParam || rng.NextBool(0.3)))
                {
                    StructRecipe recipe = p == designatedStructParam ? designatedRecipe : StructRecipe.Random;
                    StructDecl decl = factory.Create($"F{index.ToString(inv)}_S{structIndex.ToString(inv)}", recipe);
                    structIndex++;
                    fn.StructDecls.AddRange(decl.SelfAndNestedPostOrder());
                    type = decl;
                }
                else
                {
                    type = new PrimitiveTypeRef(rng.Pick(Primitives.All));
                }
                fn.Params.Add(new ParamModel { Name = paramName, Type = type, Values = ValueTree.Create(type, rng) });
            }

            fn.CallbackReturnKind = rng.Pick(s_callbackReturnKinds);
            fn.CallbackReturnValue = Value.Create(fn.CallbackReturnKind, rng);
            return fn;
        }

        private static FunctionModel BuildSelfCallbackFunction(int index, SeededRandom rng, bool enregistered)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            var fn = new FunctionModel
            {
                Index = index,
                Kind = enregistered ? FunctionKind.CallbackSelfEnregistered : FunctionKind.CallbackSelfByRef,
                SwiftFuncName = $"swiftCallbackFunc{index.ToString(inv)}",
            };
            var factory = new StructFactory(rng);

            int paramCount = rng.Next(1, 5);
            for (int p = 0; p < paramCount; p++)
            {
                var type = new PrimitiveTypeRef(rng.Pick(Primitives.All));
                fn.Params.Add(new ParamModel
                {
                    Name = $"a{p.ToString(inv)}",
                    Type = type,
                    Values = ValueTree.Create(type, rng),
                });
            }

            string structName = $"F{index.ToString(inv)}_S0";
            fn.SelfStruct = enregistered
                ? factory.CreateEnregistered(structName)
                : factory.Create(structName, StructRecipe.OverCap);
            fn.StructDecls.AddRange(fn.SelfStruct.SelfAndNestedPostOrder());
            fn.SelfValues = ValueTree.Create(fn.SelfStruct, rng);

            Fnv1aHasher hasher = Fnv1aHasher.Create();
            foreach (ParamModel param in fn.Params)
            {
                foreach (Value v in param.Values.FlattenValues())
                    hasher.Combine(v.HashBytes);
            }
            foreach (Value v in fn.SelfValues.FlattenValues())
                hasher.Combine(v.HashBytes);
            fn.ExpectedHash = hasher.Finish();
            return fn;
        }

        private static readonly InvalidKind[] s_invalidKinds =
        {
            InvalidKind.DupSwiftSelf, InvalidKind.DupSwiftError, InvalidKind.SwiftErrorByValue,
            InvalidKind.SelfTNotLast, InvalidKind.DupSelfAndSelfT,
        };

        // Reverse (UnmanagedCallersOnly) validation is only exercised for the
        // signature shapes SwiftInvalidCallConv proves are rejected when the
        // native entry stub is compiled.
        private static readonly InvalidKind[] s_reverseKinds =
        {
            InvalidKind.SelfTNotLast, InvalidKind.DupSelfAndSelfT,
        };

        private static FunctionModel BuildInvalidFunction(int index, SeededRandom rng)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            InvalidKind kind = s_invalidKinds[index % s_invalidKinds.Length];
            var fn = new FunctionModel
            {
                Index = index,
                Kind = FunctionKind.Invalid,
                SwiftFuncName = "simpleFunc",
                InvalidKind = kind,
                HasReverseVariant = Array.IndexOf(s_reverseKinds, kind) >= 0,
            };
            int fillers = rng.Next(0, 4);
            for (int p = 0; p < fillers; p++)
            {
                var type = new PrimitiveTypeRef(rng.Pick(Primitives.All));
                fn.Params.Add(new ParamModel
                {
                    Name = $"f{p.ToString(inv)}",
                    Type = type,
                    Values = ValueTree.Create(type, rng),
                });
            }
            return fn;
        }
    }
}
