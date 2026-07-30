// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public class OrderingLibrary {
    public var seed: Int64
    public static let shared = OrderingLibrary(seed: 1000)

    private init(seed: Int64) {
        self.seed = seed
    }

    public static func getInstance() -> UnsafeMutableRawPointer {
        return Unmanaged.passUnretained(shared).toOpaque()
    }

    // Instance method with several ordinary arguments; self is carried in the
    // Swift self register, so managed declarations may place SwiftSelf at any
    // parameter position.
    public func combine(a: Int64, b: Int64, c: Int64) -> Int64 {
        return seed &+ a &* 2 &+ b &* 3 &+ c &* 5
    }

    // Throwing instance method exercising SwiftSelf and SwiftError* together.
    public func conditionallyThrow(shouldThrow: Int32, value: Int64) throws -> Int64 {
        if shouldThrow != 0 {
            throw OrderingError.failure(code: value)
        }
        return seed &+ value
    }

    // Throwing instance method returning a non-frozen struct: exercises
    // SwiftIndirectResult, SwiftSelf, and SwiftError* all in one signature.
    public func makeOrThrowLargeStruct(shouldThrow: Int32, base: Int64) throws -> LargeNonFrozenStruct {
        if shouldThrow != 0 {
            throw OrderingError.failure(code: base)
        }
        return LargeNonFrozenStruct(a: seed &+ base, b: base &* 2, c: base &* 3, d: base &* 4, e: base &* 5)
    }
}

public enum OrderingError: Error {
    case failure(code: Int64)
}

public func getErrorCode(from error: Error) -> Int64 {
    if let orderingError = error as? OrderingError, case .failure(let code) = orderingError {
        return code
    }
    return -1
}

// Throwing free function for SwiftError* position coverage.
public func conditionallyThrow(shouldThrow: Int32, a: Int64, b: Int64) throws -> Int64 {
    if shouldThrow != 0 {
        throw OrderingError.failure(code: a &+ b)
    }
    return a &* 10 &+ b
}

// Returned indirectly (via the indirect-result register) because the struct is
// not frozen and the library is built with library evolution enabled.
public struct LargeNonFrozenStruct {
    public let a: Int64
    public let b: Int64
    public let c: Int64
    public let d: Int64
    public let e: Int64
}

public func makeLargeStruct(base: Int64, scale: Int64) -> LargeNonFrozenStruct {
    return LargeNonFrozenStruct(a: base, b: base &+ scale, c: base &+ 2 &* scale, d: base &+ 3 &* scale, e: base &+ 4 &* scale)
}

// Used by managed callbacks to obtain a genuine Swift error box that they can
// propagate through the Swift error register.
public func throwOrderingError(code: Int64) throws -> Int64 {
    throw OrderingError.failure(code: code)
}

// Reverse: invokes the managed callback as a closure. The funcContext value
// supplied by the managed caller rides in the Swift self/context register, so
// the managed callback may declare SwiftSelf at any parameter position.
public func invokeWithContext(a: Int64, b: Int64, f: (Int64, Int64) -> Int64) -> Int64 {
    return f(a, b)
}

// Reverse: the managed callback may set the Swift error register; this
// function observes the thrown error and folds its code into the return value.
public func invokeThrowingCallback(value: Int64, f: (Int64) throws -> Int64) -> Int64 {
    do {
        return try f(value)
    } catch {
        return getErrorCode(from: error) &+ 5000
    }
}
