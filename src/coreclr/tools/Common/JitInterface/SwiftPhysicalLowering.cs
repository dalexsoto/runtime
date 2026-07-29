// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Internal.TypeSystem;

namespace Internal.JitInterface
{
    public static class SwiftPhysicalLowering
    {
        // This implementation deliberately mirrors the CoreCLR VM implementation
        // (MethodTable::GetNativeSwiftPhysicalLowering and helpers in
        // src/coreclr/vm/methodtable.cpp) byte for byte, including its treatment
        // of shapes that Swift itself cannot express (explicit-layout overlaps
        // and gaps). The two implementations must produce identical
        // CORINFO_SWIFT_LOWERING results for every type; the normative
        // specification is docs/design/interop/swift/lowering.md.
        private enum LoweredType : byte
        {
            Empty,
            Opaque,
            Int64,
            Float,
            Double,
        }

        private static int GetAlignment(LoweredType tag)
            => tag switch
            {
                LoweredType.Int64 => 8,
                LoweredType.Float => 4,
                LoweredType.Double => 8,
                _ => 1,
            };

        private static void SetLoweringRange(LoweredType[] bytes, int start, int size, LoweredType tag)
        {
            bool forceOpaque = false;

            if (start % GetAlignment(tag) != 0)
            {
                // If the start of the range is not aligned, we need to force the entire range to be opaque.
                forceOpaque = true;
            }

            // Check if any of the range is non-empty.
            // If so, we need to force this range to be opaque
            // and extend the range to the natural alignment of the first
            // conflicting tag we encounter, exactly as the VM does.
            for (int i = 0; i < size; i++)
            {
                LoweredType currentTag = bytes[start + i];
                if (currentTag != LoweredType.Empty && currentTag != tag)
                {
                    forceOpaque = true;

                    int alignment = GetAlignment(currentTag);
                    start = AlignDown(start, alignment);
                    size = AlignUp(size + start, alignment) - start;
                    break;
                }
            }

            if (forceOpaque)
            {
                tag = LoweredType.Opaque;
            }

            bytes.AsSpan(start, size).Fill(tag);

            static int AlignDown(int value, int alignment) => value - (value % alignment);
            static int AlignUp(int value, int alignment) => AlignDown(value + alignment - 1, alignment);
        }

        private static int GetFieldSize(TypeDesc fieldType)
        {
            if (fieldType.IsPointer || fieldType.IsFunctionPointer)
            {
                return fieldType.Context.Target.PointerSize;
            }

            Debug.Assert(fieldType.IsValueType);
            return ((DefType)fieldType).InstanceByteCountUnaligned.AsInt;
        }

        private static void AddTypeToLowering(LoweredType[] bytes, TypeDesc type, int offset)
        {
            if (type is MetadataType { IsInlineArray: true } inlineArrayType)
            {
                type = new TypeWithRepeatedFields(inlineArrayType);
            }

            foreach (FieldDesc field in type.GetFields())
            {
                if (field.IsStatic)
                    continue;

                AddFieldToLowering(bytes, field.FieldType, offset + field.Offset.AsInt);
            }
        }

        private static void AddFieldToLowering(LoweredType[] bytes, TypeDesc fieldType, int offset)
        {
            // Enums lower as their underlying primitive type.
            fieldType = fieldType.UnderlyingType;

            switch (fieldType.Category)
            {
                case TypeFlags.Single:
                    SetLoweringRange(bytes, offset, 4, LoweredType.Float);
                    break;
                case TypeFlags.Double:
                    SetLoweringRange(bytes, offset, 8, LoweredType.Double);
                    break;
                case TypeFlags.Int64:
                case TypeFlags.UInt64:
                    SetLoweringRange(bytes, offset, 8, LoweredType.Int64);
                    break;
                default:
                    if (fieldType.IsValueType && !fieldType.IsPrimitiveNumeric)
                    {
                        AddTypeToLowering(bytes, fieldType, offset);
                    }
                    else
                    {
                        // Everything else — small integers, bool, char, native
                        // ints, pointers, and function pointers — lowers as
                        // opaque bytes, mirroring the VM. A naturally aligned
                        // pointer-sized opaque range re-emits as an 8-byte
                        // integer, so pointers still lower to CORINFO_TYPE_LONG,
                        // but they participate in opaque-range merging exactly
                        // like the VM's ranges do.
                        SetLoweringRange(bytes, offset, GetFieldSize(fieldType), LoweredType.Opaque);
                    }
                    break;
            }
        }

        public static CORINFO_SWIFT_LOWERING LowerTypeForSwiftSignature(TypeDesc type)
        {
            if (!type.IsValueType || type is DefType { ContainsGCPointers: true })
            {
                Debug.Fail("Non-unmanaged types should not be passed directly to a Swift function.");
                return new() { byReference = true };
            }

            Debug.Assert(type.Context.Target.PointerSize == 8, "Swift interop is only supported on 64-bit platforms.");
            const int PointerSize = 8;

            // We'll build the intervals by scanning the fields byte-by-byte and then calculate
            // the lowering intervals from that information.
            LoweredType[] loweredBytes = new LoweredType[((DefType)type).InstanceByteCountUnaligned.AsInt];
            AddTypeToLowering(loweredBytes, type, 0);

            // Build intervals from the byte sequence.
            List<(int Offset, int Size, LoweredType Tag)> intervals = new();
            for (int i = 0; i < loweredBytes.Length; i++)
            {
                // Don't create an interval for empty bytes.
                if (loweredBytes[i] == LoweredType.Empty)
                {
                    continue;
                }

                bool startNewInterval =
                    // We're at the start of the type
                    i == 0
                    // We're starting a new float (as we're aligned)
                    || (i % 4 == 0 && loweredBytes[i] == LoweredType.Float)
                    // We're starting a new double or int64_t (as we're aligned)
                    || (i % 8 == 0 && loweredBytes[i] is LoweredType.Double or LoweredType.Int64)
                    // We've changed interval types
                    || loweredBytes[i] != loweredBytes[i - 1];

                if (startNewInterval)
                {
                    intervals.Add((i, 1, loweredBytes[i]));
                }
                else
                {
                    var last = intervals[^1];
                    intervals[^1] = (last.Offset, last.Size + 1, last.Tag);
                }
            }

            // Merge opaque intervals that are in the same pointer-sized block.
            // Note: like the VM, the merge condition compares against the
            // original previous interval (using its end sentinel), not the
            // merged accumulator.
            List<(int Offset, int Size, LoweredType Tag)> mergedIntervals = new();
            for (int i = 0; i < intervals.Count; i++)
            {
                var interval = intervals[i];

                if (i != 0 && interval.Tag == LoweredType.Opaque)
                {
                    var prevInterval = intervals[i - 1];
                    if (prevInterval.Tag == LoweredType.Opaque
                        && (prevInterval.Offset + prevInterval.Size) / PointerSize == interval.Offset / PointerSize)
                    {
                        var last = mergedIntervals[^1];
                        mergedIntervals[^1] = (last.Offset, interval.Offset + interval.Size - last.Offset, last.Tag);
                        continue;
                    }
                }

                mergedIntervals.Add(interval);
            }

            // Now we have the intervals, we can calculate the lowering.
            List<(CorInfoType Type, int Offset)> loweredTypes = new();
            foreach (var interval in mergedIntervals)
            {
                switch (interval.Tag)
                {
                    case LoweredType.Int64:
                        loweredTypes.Add((CorInfoType.CORINFO_TYPE_LONG, interval.Offset));
                        break;
                    case LoweredType.Float:
                        loweredTypes.Add((CorInfoType.CORINFO_TYPE_FLOAT, interval.Offset));
                        break;
                    case LoweredType.Double:
                        loweredTypes.Add((CorInfoType.CORINFO_TYPE_DOUBLE, interval.Offset));
                        break;
                    case LoweredType.Opaque:
                    {
                        // We need to split the opaque ranges into integer parameters.
                        // As part of this splitting, we must ensure that we don't introduce alignment padding.
                        // This lowering algorithm should produce a lowered type sequence that would have the same
                        // padding for a naturally-aligned struct with the lowered fields as the original type has.
                        // This algorithm intends to split the opaque range into the least number of lowered elements
                        // that covers the entire range. The lowered range is allowed to extend past the end of the
                        // opaque range (including past the end of the struct), but not into the next non-empty interval.
                        // However, due to the properties of the lowering (the only non-8 byte elements of the lowering
                        // are 4-byte floats), we'll never encounter a scenario where we need would need to account
                        // for a correctly-aligned opaque range of > 4 bytes that we must not pad to 8 bytes.
                        int opaqueIntervalStart = interval.Offset;
                        // The remaining size here may become negative, so use a signed type.
                        int remainingIntervalSize = interval.Size;
                        while (remainingIntervalSize > 0)
                        {
                            if (remainingIntervalSize > 4 && opaqueIntervalStart % 8 == 0)
                            {
                                loweredTypes.Add((CorInfoType.CORINFO_TYPE_LONG, opaqueIntervalStart));
                                opaqueIntervalStart += 8;
                                remainingIntervalSize -= 8;
                            }
                            else if (remainingIntervalSize > 2 && opaqueIntervalStart % 4 == 0)
                            {
                                loweredTypes.Add((CorInfoType.CORINFO_TYPE_INT, opaqueIntervalStart));
                                opaqueIntervalStart += 4;
                                remainingIntervalSize -= 4;
                            }
                            else if (remainingIntervalSize > 1 && opaqueIntervalStart % 2 == 0)
                            {
                                loweredTypes.Add((CorInfoType.CORINFO_TYPE_SHORT, opaqueIntervalStart));
                                opaqueIntervalStart += 2;
                                remainingIntervalSize -= 2;
                            }
                            else
                            {
                                loweredTypes.Add((CorInfoType.CORINFO_TYPE_BYTE, opaqueIntervalStart));
                                opaqueIntervalStart++;
                                remainingIntervalSize--;
                            }
                        }
                        break;
                    }
                    default:
                        Debug.Fail("Empty intervals should have been dropped during interval construction");
                        break;
                }
            }

            // If a type has a primitive sequence with more than 4 elements, Swift passes it by reference.
            if (loweredTypes.Count > 4)
            {
                return new() { byReference = true };
            }

            CORINFO_SWIFT_LOWERING lowering = new()
            {
                byReference = false,
                numLoweredElements = loweredTypes.Count
            };

            for (int i = 0; i < loweredTypes.Count; i++)
            {
                lowering.LoweredElements[i] = loweredTypes[i].Type;
                lowering.Offsets[i] = (uint)loweredTypes[i].Offset;
            }

            return lowering;
        }
    }
}
