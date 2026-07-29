// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace System.Runtime.Intrinsics
{
    // Minimal mirrors of the hardware vector types, shaped like the real
    // CoreLib definitions, for type-system tests (Swift lowering).
    [StructLayout(LayoutKind.Sequential, Size = 8)]
    public readonly struct Vector64<T>
    {
        private readonly ulong _00;
    }

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public readonly struct Vector128<T>
    {
        private readonly Vector64<T> _lower;
        private readonly Vector64<T> _upper;
    }

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    public readonly struct Vector256<T>
    {
        private readonly Vector128<T> _lower;
        private readonly Vector128<T> _upper;
    }
}
