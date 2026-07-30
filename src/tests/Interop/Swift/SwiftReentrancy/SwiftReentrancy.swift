// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Three-level nesting: the managed test calls funcA, which invokes a managed
// callback, which calls funcB, which invokes another managed callback, which
// calls swiftLeaf. The arithmetic composes so the final value proves that
// every level ran.
public func funcA(f: (Int64) -> Int64) -> Int64 {
    return f(10) &+ 1
}

public func funcB(g: (Int64) -> Int64) -> Int64 {
    return g(20) &+ 2
}

public func swiftLeaf(x: Int64) -> Int64 {
    return x &* 3
}

// Recursion through the interop boundary: invokes the managed callback with
// n - 1 while n > 0 and accumulates n on the way out. The managed callback
// calls back into this function, so every recursion level crosses the
// boundary twice.
public func recurse(n: Int64, f: (Int64) -> Int64) -> Int64 {
    if n > 0 {
        return n &+ f(n - 1)
    }
    return 0
}

// Reentrancy with managed state: simply invokes the callback. The managed
// callback updates a static counter and re-enters through this function.
public func invokeCallback(x: Int64, f: (Int64) -> Int64) -> Int64 {
    return f(x)
}

public enum ReentrancyError: Error {
    case failure(code: Int64)
}

// Used by the inner managed callback to obtain a genuine Swift error box that
// it can propagate through the Swift error register.
public func throwReentrancyError(code: Int64) throws -> Int64 {
    throw ReentrancyError.failure(code: code)
}

// Middle Swift layer that observes an error thrown by the inner managed
// callback and propagates a flag value to the outermost (managed) caller.
public func middleObservesError(x: Int64, f: (Int64) throws -> Int64) -> Int64 {
    do {
        return try f(x)
    } catch let error as ReentrancyError {
        if case .failure(let code) = error {
            return 1000 &+ code
        }
        return -2
    } catch {
        return -1
    }
}
