// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public struct Payload {
    public var a: Int64
    public var b: Double

    public init(a: Int64, b: Double) {
        self.a = a
        self.b = b
    }
}

public func addPayload(_ p: Payload, _ delta: Int64) -> Int64 {
    return p.a &+ Int64(p.b) &+ delta
}

public func makePayload(a: Int64, b: Double) -> Payload {
    return Payload(a: a, b: b)
}

public struct ImportError: Error {
    let code: Int64

    public init(code: Int64) {
        self.code = code
    }
}

public func doubleOrFail(_ value: Int64) throws -> Int64 {
    if value < 0 {
        throw ImportError(code: value)
    }
    return value &* 2
}

public func sumRawBuffer(_ buffer: UnsafeRawBufferPointer) -> Int64 {
    var sum: Int64 = 0
    for b in buffer {
        sum &+= Int64(b)
    }
    return sum
}

public func fillBuffer(_ buffer: UnsafeMutableRawBufferPointer, seed: UInt8) {
    for i in 0..<buffer.count {
        buffer[i] = seed &+ UInt8(truncatingIfNeeded: i)
    }
}

public func advancePointer(_ p: UnsafeRawPointer, by offset: Int64) -> UnsafeRawPointer {
    return p.advanced(by: Int(offset))
}
