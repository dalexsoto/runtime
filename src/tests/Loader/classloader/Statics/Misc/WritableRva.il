// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

.assembly extern mscorlib {}
.assembly extern xunit.core {}
.assembly WritableRva {}

.data InitialValue = int32(41)

.class public auto ansi beforefieldinit WritableRva
       extends [mscorlib]System.Object
{
    .field private static int32 Value at InitialValue

    .method public hidebysig static int32 Main() cil managed
    {
        .custom instance void [xunit.core]Xunit.FactAttribute::.ctor() = (01 00 00 00)
        .entrypoint
        .maxstack 2

        ldsfld int32 WritableRva::Value
        ldc.i4.s 41
        bne.un.s INITIAL_VALUE_FAILURE

        ldsfld int32 WritableRva::Value
        ldc.i4.1
        add
        stsfld int32 WritableRva::Value

        ldsfld int32 WritableRva::Value
        ldc.i4.s 42
        bne.un.s WRITE_FAILURE

        ldc.i4.s 100
        ret

    INITIAL_VALUE_FAILURE:
        ldc.i4.s 101
        ret

    WRITE_FAILURE:
        ldc.i4.s 102
        ret
    }
}
