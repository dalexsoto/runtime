// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

.assembly extern System.Runtime { .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A ) }

.assembly SwiftExtendedLayoutTypes { }

// Managed mirrors of the SwiftExtendedLayoutAbi fixture using the
// non-shipping ExtendedLayoutKind.SwiftStruct (2): C tail-packs to offset 9.

.class flags(0x18) public value beforefieldinit ExtInner
{
    .custom instance void [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutAttribute::.ctor(valuetype [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutKind) = {
        int32(0x00000002) // SwiftStruct
    }

    .field public int64 A
    .field public int8 B
}

.class flags(0x18) public value beforefieldinit ExtOuter
{
    .custom instance void [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutAttribute::.ctor(valuetype [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutKind) = {
        int32(0x00000002) // SwiftStruct
    }

    .field public valuetype ExtInner Inner
    .field public int8 C
}

// Mirror of GPair<Inner>: nested SwiftStruct tail-packing composes with a
// frozen generic instantiation (Second at offset 9).
.class flags(0x18) public value beforefieldinit ExtGPairInner
{
    .custom instance void [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutAttribute::.ctor(valuetype [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutKind) = {
        int32(0x00000002) // SwiftStruct
    }

    .field public valuetype ExtInner First
    .field public int8 Second
}
