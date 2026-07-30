// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Generic metadata instantiation fixture: direct-argument accessors,
// the >3-parameter array form, and an Array<Int64> skeleton.

public struct Quad<A, B, C, D> {
    public var a: A
    public var b: B
    public var c: C
    public var d: D
}

public func quadMetadata() -> Any.Type {
    return Quad<Int64, Double, Int8, Int32>.self
}

public func int64Metadata() -> Any.Type {
    return Int64.self
}

public func doubleMetadata() -> Any.Type {
    return Double.self
}

public func int8Metadata() -> Any.Type {
    return Int8.self
}

public func int32Metadata() -> Any.Type {
    return Int32.self
}

public func optionalIntMetadata() -> Any.Type {
    return Optional<Int64>.self
}

public func arrayIntMetadata() -> Any.Type {
    return [Int64].self
}

public func makeIntArray(count: Int64) -> [Int64] {
    var result: [Int64] = []
    for i in 0..<count {
        result.append(i &* 3)
    }
    return result
}

public func arraySum(_ a: [Int64]) -> Int64 {
    var sum: Int64 = 0
    for v in a {
        sum &+= v
    }
    return sum
}

public func appendToArray(_ a: inout [Int64], _ value: Int64) {
    a.append(value)
}

// Default arguments: literal defaults are serialized into the interface
// and re-emitted by callers (no exported symbol); defaults that reference
// non-serializable state get an exported per-parameter generator
// (fA_, fA0_, ...).
public var configuredCount: Int64 = 3

public func setConfiguredCount(_ value: Int64) {
    configuredCount = value
}

public func scaledCount(count: Int64 = configuredCount, scale: Int64 = 10, bias: Int64) -> Int64 {
    return count &* scale &+ bias
}

public func optionalSum(_ v: Int64?) -> Int64 {
    return v ?? -1
}

public func makeIntSet(count: Int64) -> Set<Int64> {
    var result: Set<Int64> = []
    for i in 0..<count {
        result.insert(i &* 5)
    }
    return result
}

public func setContains(_ s: Set<Int64>, _ value: Int64) -> Int64 {
    return s.contains(value) ? 1 : 0
}

public func setIntMetadata() -> Any.Type {
    return Set<Int64>.self
}

public func makeIntDict(count: Int64) -> [Int64: Int64] {
    var result: [Int64: Int64] = [:]
    for i in 0..<count {
        result[i] = i &* 7
    }
    return result
}

public func dictLookup(_ d: [Int64: Int64], _ key: Int64) -> Int64 {
    return d[key] ?? -1
}

public func dictIntMetadata() -> Any.Type {
    return [Int64: Int64].self
}
