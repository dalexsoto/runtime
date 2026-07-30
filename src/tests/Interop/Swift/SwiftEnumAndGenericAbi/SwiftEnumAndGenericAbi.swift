// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Fixture for frozen generic struct instantiations and the enum matrix
// (Optional, single-payload, multi-payload, nested, generic).

public struct Pair<T> {
    public var first: T
    public var second: Int8

    public init(first: T, second: Int8) {
        self.first = first
        self.second = second
    }
}

public func sumPairInt(_ p: Pair<Int64>) -> Int64 {
    return p.first &+ Int64(p.second)
}

public func sumPairDouble(_ p: Pair<Double>) -> Double {
    return p.first + Double(p.second)
}

public func makePairInt(_ a: Int64, _ b: Int8) -> Pair<Int64> {
    return Pair(first: a, second: b)
}

// Metadata for instantiated generics, returned as Any.Type (a metadata
// pointer in x0), sidestepping instantiated-accessor symbol lookup.
public func pairIntMetadata() -> Any.Type {
    return Pair<Int64>.self
}

public func optionalIntMetadata() -> Any.Type {
    return Optional<Int64>.self
}

public func nestedOptionalIntMetadata() -> Any.Type {
    return Optional<Optional<Int64>>.self
}

public enum MultiPayload {
    case a(Int64)
    case b(Double)
    case c
    case d
}

public func multiPayloadMetadata() -> Any.Type {
    return MultiPayload.self
}

public func makeMultiPayloadInto(_ p: UnsafeMutableRawPointer, kind: Int, payload: Int64) {
    let e: MultiPayload
    switch kind {
    case 0: e = .a(payload)
    case 1: e = .b(Double(payload) * 0.5)
    case 2: e = .c
    default: e = .d
    }
    p.assumingMemoryBound(to: MultiPayload.self).initialize(to: e)
}

public func makeOptionalIntInto(_ p: UnsafeMutableRawPointer, hasValue: Bool, payload: Int64) {
    let v: Int64? = hasValue ? payload : nil
    p.assumingMemoryBound(to: Int64?.self).initialize(to: v)
}

public enum GenericBox<T> {
    case empty
    case boxed(T)
}

public func genericBoxIntMetadata() -> Any.Type {
    return GenericBox<Int64>.self
}

public func makeGenericBoxIntInto(_ p: UnsafeMutableRawPointer, hasValue: Bool, payload: Int64) {
    let v: GenericBox<Int64> = hasValue ? .boxed(payload) : .empty
    p.assumingMemoryBound(to: GenericBox<Int64>.self).initialize(to: v)
}
