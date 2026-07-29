// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

.assembly extern System.Runtime { .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A ) }

.assembly SwiftStructTypes { }

// ExtendedLayoutKind.SwiftStruct (2) is a non-shipping prototype value, so
// these types are declared in IL rather than through the public enum.

.class flags(0x18) public value beforefieldinit SwiftInner
{
    .custom instance void [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutAttribute::.ctor(valuetype [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutKind) = {
        int32(0x00000002) // SwiftStruct
    }

    .field public int64 A
    .field public int8 B
}

.class flags(0x18) public value beforefieldinit SwiftOuter
{
    .custom instance void [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutAttribute::.ctor(valuetype [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutKind) = {
        int32(0x00000002) // SwiftStruct
    }

    .field public valuetype SwiftInner Inner
    .field public int8 C
}

.class flags(0x18) public value beforefieldinit SwiftEmpty
{
    .custom instance void [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutAttribute::.ctor(valuetype [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutKind) = {
        int32(0x00000002) // SwiftStruct
    }
}

.class flags(0x18) public value beforefieldinit SwiftWrapsEmpty
{
    .custom instance void [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutAttribute::.ctor(valuetype [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutKind) = {
        int32(0x00000002) // SwiftStruct
    }

    .field public valuetype SwiftEmpty E
    .field public int8 X
}

// A sequential (non-Swift) nested struct keeps its full managed size, so no
// tail packing happens: the contrast case for SwiftOuter.
.class flags(0x18) public sequential value beforefieldinit PaddedSequentialInner
{
    .field public int64 A
    .field public int8 B
}

.class flags(0x18) public value beforefieldinit SwiftWithSequentialInner
{
    .custom instance void [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutAttribute::.ctor(valuetype [System.Runtime]System.Runtime.InteropServices.ExtendedLayoutKind) = {
        int32(0x00000002) // SwiftStruct
    }

    .field public valuetype PaddedSequentialInner Inner
    .field public int8 C
}
