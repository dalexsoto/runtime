// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ABI fixture for ExtendedLayoutKind.SwiftStruct: the managed mirror uses
// tail-packed extended layout, so these shapes are byte-identical across
// the boundary — something C# sequential layout cannot express.

public struct Inner {
    public var a: Int64
    public var b: Int8

    public init(a: Int64, b: Int8) {
        self.a = a
        self.b = b
    }
}

public struct Outer {
    public var inner: Inner
    public var c: Int8

    public init(inner: Inner, c: Int8) {
        self.inner = inner
        self.c = c
    }
}

public func sumOuter(_ v: Outer) -> Int64 {
    return v.inner.a &+ Int64(v.inner.b) &+ Int64(v.c)
}

public func makeOuter(a: Int64, b: Int8, c: Int8) -> Outer {
    return Outer(inner: Inner(a: a, b: b), c: c)
}

public func outerFieldC(_ v: Outer) -> Int64 {
    return Int64(v.c)
}

// Frozen generic instantiation with tail-packing: Pair<Inner> places
// 'second' at offset 9 (Inner's unpadded size).
public struct GPair<T> {
    public var first: T
    public var second: Int8

    public init(first: T, second: Int8) {
        self.first = first
        self.second = second
    }
}

public func sumGPairInner(_ p: GPair<Inner>) -> Int64 {
    return p.first.a &+ Int64(p.first.b) &+ Int64(p.second)
}

public func makeGPairInner(_ a: Int64, _ b: Int8, _ c: Int8) -> GPair<Inner> {
    return GPair(first: Inner(a: a, b: b), second: c)
}
