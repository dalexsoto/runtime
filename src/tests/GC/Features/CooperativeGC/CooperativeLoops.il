// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

.assembly extern mscorlib { }
.assembly CooperativeLoops { }

.class public abstract sealed auto ansi CooperativeLoops extends [mscorlib]System.Object
{
    .method public hidebysig static int32 SwitchLoop(int32& stop, int32& ready, int32& currentValue) cil managed noinlining
    {
        .maxstack 2
    Loop:
        ldarg.1
        ldc.i4.1
        volatile.
        stind.i4
        ldarg.0
        volatile.
        ldind.i4
        switch (Loop)
        ldarg.2
        volatile.
        ldind.i4
        ret
    }

    .method public hidebysig static int32 LeaveLoop(int32& stop, int32& ready, int32& currentValue) cil managed noinlining
    {
        .maxstack 2
    Loop:
        nop
        .try
        {
            ldarg.0
            volatile.
            ldind.i4
            brtrue.s Exit
            leave.s Loop
        Exit:
            leave.s Done
        }
        finally
        {
            ldarg.1
            ldc.i4.1
            volatile.
            stind.i4
            endfinally
        }
    Done:
        ldarg.2
        volatile.
        ldind.i4
        ret
    }

    .method public hidebysig static void SelfLoop(int32& ready) cil managed noinlining
    {
        .maxstack 2
        ldarg.0
        ldc.i4.1
        volatile.
        stind.i4
    Loop:
        br.s Loop
    }
}
