// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace ILCompiler.Compiler.Tests.Assets.SwiftValidationParity;

// Signature inventory for the VM-vs-ILC marshalling/blittability parity
// test (SwiftValidationParityTests). Each method's Swift-signature
// marshalling requirement must match the CoreCLR VM's decision for the
// same shape; see src/tests/Interop/Swift/SwiftInvalidCallConv for the
// runtime half.
public static class Signatures
{
    [DllImport("noop")]
    public static extern void PrimitivesOnly(long a, double b, int c, float d);

    public struct BlittableStruct
    {
        public long X;
        public float Y;
    }

    [DllImport("noop")]
    public static extern void TakesBlittableStruct(BlittableStruct s);

    [DllImport("noop")]
    public static extern void TakesPointer(System.IntPtr p);

    [DllImport("noop")]
    public static extern void TakesObject(object o);

    [DllImport("noop")]
    public static extern void TakesBool(bool b);

    [DllImport("noop")]
    public static extern void TakesVector64(Vector64<float> v);

    [DllImport("noop")]
    public static extern void TakesVector128(Vector128<float> v);

    [DllImport("noop")]
    public static extern void TakesVector256(Vector256<float> v);
}
