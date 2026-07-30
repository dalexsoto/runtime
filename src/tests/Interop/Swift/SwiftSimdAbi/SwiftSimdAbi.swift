// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Swift SIMD physical-lowering fixtures. Empirical rules recorded in
// docs/design/interop/swift/lowering.md "SIMD status": each SIMD value of 16
// bytes or less is one hardware-vector element in one V register, vector
// chunks count one element each toward the 4-element cap, and vectors share
// the sequential V-register file with scalar floating-point values.

@frozen
public struct VecPair
{
    public let v : SIMD4<Float>;
    public let x : Int64;

    public init(v: SIMD4<Float>, x: Int64) {
        self.v = v
        self.x = x
    }
}

@frozen
public struct TwoVec
{
    public let a : SIMD2<Double>;
    public let b : SIMD2<Double>;
}

@frozen
public struct FourVec
{
    public let c0 : SIMD4<Float>;
    public let c1 : SIMD4<Float>;
    public let c2 : SIMD4<Float>;
    public let c3 : SIMD4<Float>;
}

@frozen
public struct FiveVec
{
    public let c0 : SIMD4<Float>;
    public let c1 : SIMD4<Float>;
    public let c2 : SIMD4<Float>;
    public let c3 : SIMD4<Float>;
    public let c4 : SIMD4<Float>;
}

public func sumSimd4Float(_ v: SIMD4<Float>) -> Float {
    return v.x + v.y + v.z + v.w
}

public func sumSimd2Float(_ v: SIMD2<Float>) -> Float {
    return v.x + v.y
}

public func sumSimd2Double(_ v: SIMD2<Double>) -> Double {
    return v.x + v.y
}

public func sumSimd4Int32(_ v: SIMD4<Int32>) -> Int32 {
    return v.x &+ v.y &+ v.z &+ v.w
}

public func makeSimd4Float(_ a: Float, _ b: Float, _ c: Float, _ d: Float) -> SIMD4<Float> {
    return SIMD4<Float>(a, b, c, d)
}

public func sumVecPair(_ p: VecPair) -> Float {
    return p.v.x + p.v.y + p.v.z + p.v.w + Float(p.x)
}

public func makeVecPair() -> VecPair {
    return VecPair(v: SIMD4<Float>(1.5, 2.5, 3.5, 4.5), x: 42)
}

public func sumTwoVec(_ t: TwoVec) -> Double {
    return t.a.x + t.a.y + t.b.x + t.b.y
}

public func sumFourVec(_ m: FourVec) -> Float {
    return (m.c0 + m.c1 + m.c2 + m.c3).sum()
}

public func sumFiveVec(_ m: FiveVec) -> Float {
    return (m.c0 + m.c1 + m.c2 + m.c3 + m.c4).sum()
}

// Scalar FP values and vectors share one sequential V-register assignment.
public func sumSimdMix(_ a: Float, _ v: SIMD4<Float>, _ b: Double, _ w: SIMD2<Double>) -> Double {
    return Double(a) + Double(v.x + v.y + v.z + v.w) + b + w.x + w.y
}

// Reverse direction: managed callback receiving a vector argument.
public func invokeSimdCallback(f: (SIMD4<Float>, Int64) -> Float) -> Float {
    return f(SIMD4<Float>(10, 20, 30, 40), 5)
}
