// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Stand-in for a generated native binding module: version and identity
// exports (the only native calls permitted before validation succeeds) and
// one gated entry point that counts its invocations so tests can prove
// rejected bindings never reach Swift code.

private var entryCallCount = 0

public func bindingNativeAbiVersion() -> Int {
    return 2
}

public func bindingIdentityWord0() -> UInt64 {
    return 0x1122_3344_5566_7788
}

public func bindingIdentityWord1() -> UInt64 {
    return 0x99aa_bbcc_ddee_ff00
}

public func bindingEntryAdd(a: Int, b: Int) -> Int {
    entryCallCount += 1
    return a &+ b
}

public func bindingEntryCallCount() -> Int {
    return entryCallCount
}

/// A trivial CallConvSwift target used to PROBE whether the running runtime
/// actually implements the Swift calling convention — the difference between
/// "the marker types exist" and "the convention works".
public func probeAdds(_ a: Int64, _ b: Int64) -> Int64 {
    return a &+ b
}
