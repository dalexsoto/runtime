// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Layout edge cases: empty and zero-sized values, gap/padding bridging,
// misaligned fields, and inline-array (homogeneous tuple) shapes. Compiled
// WITHOUT library evolution so every struct lowers directly.

public struct Empty {
    public init() {}
}

public func takeEmptyBetween(a: Int64, e: Empty, b: Int64) -> Int64 {
    return a &+ b
}

public struct OnlyEmpties {
    var a: Empty
    var b: Empty

    public init() {
        a = Empty()
        b = Empty()
    }
}

public func takeOnlyEmpties(v: OnlyEmpties, x: Int64) -> Int64 {
    return x &* 3
}

public struct WrapsEmpty {
    var e: Empty
    var x: Int64

    public init(x: Int64) {
        e = Empty()
        self.x = x
    }
}

public func sumWrapsEmpty(_ v: WrapsEmpty) -> Int64 {
    return v.x &+ 1
}

// Byte-layout mirror of a C# explicit-layout struct with a gap:
// a@0, two padding words, b@12. The padding fields are ABI-visible here,
// while the C# side bridges its gap bytes as opaque data.
public struct GappedInts {
    var a: Int32
    var pad0: Int32
    var pad1: Int32
    var b: Int32

    public init(a: Int32, b: Int32) {
        self.a = a
        pad0 = 0
        pad1 = 0
        self.b = b
    }
}

public func sumGappedInts(_ v: GappedInts) -> Int64 {
    return Int64(v.a) &+ Int64(v.b)
}

// Byte-layout mirror of {int i; double-at-offset-4}: the C# double is
// misaligned so both sides handle the bytes opaquely; the callee
// reconstructs the double from its raw bits.
public struct MisalignedDouble {
    var i: Int32
    var dLo: UInt32
    var dHi: UInt32

    public init(i: Int32, dLo: UInt32, dHi: UInt32) {
        self.i = i
        self.dLo = dLo
        self.dHi = dHi
    }
}

public func sumMisalignedDouble(_ v: MisalignedDouble) -> Double {
    let bits = UInt64(v.dLo) | (UInt64(v.dHi) << 32)
    return Double(v.i) + Double(bitPattern: bits)
}

// Inline-array analogs: homogeneous tuples.
public struct FloatQuad {
    var f: (Float, Float, Float, Float)

    public init(_ a: Float, _ b: Float, _ c: Float, _ d: Float) {
        f = (a, b, c, d)
    }
}

public func sumFloatQuad(_ v: FloatQuad) -> Float {
    return v.f.0 + v.f.1 + v.f.2 + v.f.3
}

public struct ByteTriple {
    var b: (UInt8, UInt8, UInt8)

    public init(_ x: UInt8, _ y: UInt8, _ z: UInt8) {
        b = (x, y, z)
    }
}

public func sumByteTriple(_ v: ByteTriple) -> Int64 {
    return Int64(v.b.0) &+ Int64(v.b.1) &+ Int64(v.b.2)
}

public struct LongFive {
    var l: (Int64, Int64, Int64, Int64, Int64)

    public init(_ a: Int64, _ b: Int64, _ c: Int64, _ d: Int64, _ e: Int64) {
        l = (a, b, c, d, e)
    }
}

public func sumLongFive(_ v: LongFive) -> Int64 {
    return v.l.0 &+ v.l.1 &+ v.l.2 &+ v.l.3 &+ v.l.4
}

public func makeFloatQuad(_ a: Float, _ b: Float, _ c: Float, _ d: Float) -> FloatQuad {
    return FloatQuad(a, b, c, d)
}

// ABI mirror for the C# explicit-layout {a@0, gap, b@12} struct: typed
// fields around a pure gap stay separate lowered elements (lowering.md
// step 3 bridges only opaque intervals), so the physical signature is two
// Int32 register arguments.
public func sumGappedParts(_ a: Int32, _ b: Int32) -> Int64 {
    return Int64(a) &+ Int64(b)
}
