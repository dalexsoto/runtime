// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public class SelfLibrary {
    public var number: Int
    public static let shared = SelfLibrary(number: 42)

    private init(number: Int) {
        self.number = number
    }

    public func getMagicNumber() -> Int {
        return self.number
    }

    public static func getInstance() -> UnsafeMutableRawPointer {
        let unmanagedInstance = Unmanaged.passUnretained(shared)
        let pointer = unmanagedInstance.toOpaque()
        return pointer
    }
}

@frozen
public struct FrozenEnregisteredStruct
{
    let a : Int64;
    let b : Int64;

    public func sum() -> Int64 {
        return a + b
    }

    public func sumWithExtraArgs(c: Float, d: Float) -> Float {
        return Float(a + b) + c + d
    }
}

@frozen
public struct FrozenNonEnregisteredStruct {
    let a : Int64;
    let b : Int64;
    let c : Int64;
    let d : Int64;
    let e : Int64;

    public func sum() -> Int64 {
        return a + b + c + d + e
    }

    public func sumWithExtraArgs(f: Float, g: Float) -> Float {
        return Float(a + b + c + d + e) + f + g
    }
}

// Reverse direction: Swift invokes a managed callback as a closure. Calling a
// Swift closure places the closure context in the self register, which is how
// a managed UnmanagedCallersOnly function receives a by-reference SwiftSelf<T>
// (the caller passes a pointer to the self value as the context).
public func sumFrozenNonEnregisteredStructCallback(f: (Int64, Int64) -> Int64) -> Int64 {
    return f(11, 22)
}

// Reverse direction with a directly-lowered self: the struct is passed as a
// trailing ordinary argument, which is physically identical to Swift passing a
// directly-lowered self value.
public func sumFrozenEnregisteredStructCallback(f: (Int64, Int64, FrozenEnregisteredStruct) -> Int64) -> Int64 {
    return f(1000, 2000, FrozenEnregisteredStruct(a: 30, b: 40))
}
