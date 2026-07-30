// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Direct generic Swift calls from closed, nongeneric managed entry points:
// opaque values by pointer, then type metadata, then witness tables in
// requirement order.

public protocol Doubler {
    func doubled() -> Int64
}

public protocol Tagger {
    func tag() -> Int64
}

public struct Marked: Doubler, Tagger {
    public var value: Int64

    public init(value: Int64) {
        self.value = value
    }

    public func doubled() -> Int64 {
        return value &* 2
    }

    public func tag() -> Int64 {
        return value &+ 1000
    }
}

public func applyDoubler<T: Doubler>(_ v: T) -> Int64 {
    return v.doubled()
}

public func combineRequirements<T: Doubler & Tagger>(_ v: T) -> Int64 {
    return v.doubled() &+ v.tag()
}

public func genericSum<T: AdditiveArithmetic>(_ a: T, _ b: T) -> T {
    return a + b
}

// Resilient generic value flow: an opaque value produced and consumed
// through generic entry points.
public struct ResilientCell {
    var stored: Int64
    var extra: Int8

    public init(stored: Int64, extra: Int8) {
        self.stored = stored
        self.extra = extra
    }
}

extension ResilientCell: Doubler {
    public func doubled() -> Int64 {
        return stored &* 2 &+ Int64(extra)
    }
}

public func resilientCellMetadata() -> Any.Type {
    return ResilientCell.self
}

public func makeResilientCellInto(_ p: UnsafeMutableRawPointer, stored: Int64, extra: Int8) {
    p.assumingMemoryBound(to: ResilientCell.self).initialize(to: ResilientCell(stored: stored, extra: extra))
}

public func int64Metadata() -> Any.Type {
    return Int64.self
}

public func doubleMetadata() -> Any.Type {
    return Double.self
}

// Conditional conformance: Box<T> is Doubler only when T is.
public struct Box<T> {
    public var value: T

    public init(value: T) {
        self.value = value
    }
}

extension Box: Doubler where T: Doubler {
    public func doubled() -> Int64 {
        return value.doubled() &+ 1
    }
}

public func boxedMarkedMetadata() -> Any.Type {
    return Box<Marked>.self
}

public func boxedIntMetadata() -> Any.Type {
    return Box<Int64>.self
}
