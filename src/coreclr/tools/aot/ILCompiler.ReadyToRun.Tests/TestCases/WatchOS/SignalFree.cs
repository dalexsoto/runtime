// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading;

public interface IWatchValue
{
    int Read();
}

public class WatchValue : IWatchValue
{
    public int Number;
    public virtual int Read() => Number;
    public virtual int Read<T>() => Number;
}

[StructLayout(LayoutKind.Explicit, Size = 8192)]
public class LargeWatchValue
{
    [FieldOffset(4096)]
    public int Number;
}

public struct WatchPayload
{
    public object Reference;
    public long First;
    public long Second;
}

public static class WatchCodegen
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int EntryOnly(int value) => value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadField(WatchValue value) => value.Number;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteField(WatchValue value, int number) => value.Number = number;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadLargeField(LargeWatchValue value) => value.Number;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ArrayLength(int[] value) => value.Length;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ref int ArrayAddress(int[] value, int index) => ref value[index];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int InterfaceCall(IWatchValue value) => value.Read();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int GenericVirtualCall(WatchValue value) => value.Read<object>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Func<int> BindVirtualMethod(WatchValue value) => value.Read;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void InvokeDelegate(Action value) => value();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int AtomicAccess(ref int value) => Interlocked.CompareExchange(ref value, 1, 0);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Vector128<int> VectorLoad(ref int value) => Vector128.LoadUnsafe(ref value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CopyStruct(ref WatchPayload destination, ref WatchPayload source) => destination = source;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SelfLoop()
    {
    Loop:
        goto Loop;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ConditionalLoop(ref int stop)
    {
        while (Volatile.Read(ref stop) == 0)
        {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void LeaveLoop(ref int stop, ref int progress)
    {
    Loop:
        try
        {
            if (Volatile.Read(ref stop) == 0)
                goto Loop;
        }
        finally
        {
            Volatile.Write(ref progress, 1);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string CatchEntry(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught.Message;
        }
    }
}
