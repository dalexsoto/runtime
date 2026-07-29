// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// @MainActor async functions require the MAIN DISPATCH QUEUE to be drained by
// the process. A host that never services the main queue hangs forever on such
// a call — silently, with no error. StoreKit's Product.purchase(options:) is
// @MainActor async, so this is load-bearing for the whole product target.
import Foundation

@MainActor
public func mainActorSum(_ a: Int64, _ b: Int64) async -> Int64 {
    return a &+ b
}

public func nonisolatedSum(_ a: Int64, _ b: Int64) async -> Int64 {
    return a &+ b
}

@_cdecl("swiftmainactor_call_mainactor")
public func callMainActor(_ context: UnsafeMutableRawPointer,
                          _ callback: @escaping @convention(c) (UnsafeMutableRawPointer, Int64) -> Void) {
    Task.detached {
        let r = await mainActorSum(40, 2)
        callback(context, r)
    }
}

@_cdecl("swiftmainactor_call_nonisolated")
public func callNonisolated(_ context: UnsafeMutableRawPointer,
                            _ callback: @escaping @convention(c) (UnsafeMutableRawPointer, Int64) -> Void) {
    Task.detached {
        let r = await nonisolatedSum(40, 2)
        callback(context, r)
    }
}
