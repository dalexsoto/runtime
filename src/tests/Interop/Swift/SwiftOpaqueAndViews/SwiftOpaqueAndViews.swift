// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Phase 10 fixture: opaque results (`some P`/`some View`), result-builder
// parameters, and headless SwiftUI view-value construction (StoreKit views
// included). Rendering is app/device-gated; value construction, metadata,
// and lifecycle are not.

import Foundation
import SwiftUI
import StoreKit

public protocol Describer {
    func describe() -> Int64
}

public struct Small: Describer {
    public var x: Int64
    public func describe() -> Int64 { x }
}

public struct Large: Describer {
    public var a, b, c, d, e: Int64
    public func describe() -> Int64 { a &+ e }
}

@inline(never)
public func makeSmall(_ x: Int64) -> some Describer { Small(x: x) }

@inline(never)
public func makeLarge(_ x: Int64) -> some Describer { Large(a: x, b: 1, c: 2, d: 3, e: 4) }

@resultBuilder
public struct SumBuilder {
    public static func buildBlock(_ parts: Int64...) -> Int64 {
        var total: Int64 = 0
        for p in parts { total &+= p }
        return total
    }
}

@inline(never)
public func withBuilder(@SumBuilder _ body: () -> Int64) -> Int64 { body() }

// Headless SwiftUI: view VALUES are plain structs.
@inline(never)
public func makeTextView(_ n: Int64) -> some View {
    Text("value \(n)")
}

@available(macOS 14.0, *)
@inline(never)
public func makeProductView(_ id: String) -> some View {
    ProductView(id: id)
}

@available(macOS 14.0, *)
@inline(never)
public func makeProductViewUtf8(_ ptr: UnsafePointer<UInt8>, _ len: Int64) -> some View {
    ProductView(id: String(decoding: UnsafeBufferPointer(start: ptr, count: Int(len)), as: UTF8.self))
}

// Parameter-pack API plus the fixed-arity erasure thunk SHAPE the generator
// will emit for it (hand-written here; emission is an open roadmap item —
// classification SWIFTGEN007 routes callers to this shape).
@inline(never)
public func packSum<each T: BinaryInteger>(_ ts: repeat each T) -> Int64 {
    var total: Int64 = 0
    for t in repeat each ts { total &+= Int64(t) }
    return total
}

@inline(never)
public func packSum2(_ a: Int64, _ b: Int64) -> Int64 {
    return packSum(a, b)
}

@inline(never)
public func packSum3(_ a: Int64, _ b: Int64, _ c: Int64) -> Int64 {
    return packSum(a, b, c)
}

// Explicit ownership fixture (roadmap Phase 10): borrowing (+0 guaranteed),
// consuming (+1 owned), and sending parameters over a deinit-tracked class.
public final class Tracked {
    nonisolated(unsafe) static var liveCount: Int64 = 0
    public init() { Tracked.liveCount &+= 1 }
    deinit { Tracked.liveCount &-= 1 }
}

public func makeTracked() -> Tracked { Tracked() }

public func trackedLive() -> Int64 { Tracked.liveCount }

@inline(never)
public func borrowTracked(_ t: borrowing Tracked) -> Int64 { Tracked.liveCount }

@inline(never)
public func consumeTracked(_ t: consuming Tracked) -> Int64 { Tracked.liveCount }

@inline(never)
public func sendTracked(_ t: sending Tracked) -> Int64 { Tracked.liveCount }

// Remaining StoreKit views for headless construction.
@available(macOS 15.0, *)
@inline(never)
public func makeStoreView() -> some View {
    StoreView(ids: ["com.example.a", "com.example.b"])
}

@available(macOS 15.0, *)
@inline(never)
public func makeSubscriptionStoreView() -> some View {
    SubscriptionStoreView(groupID: "example-group")
}

// Environment-action invocation round-trip (Audit follow-ups): an
// OpenURLAction whose handler is a managed callback. Returning .handled
// keeps invocation headless-safe (SwiftUI performs no system open).
@inline(never)
public func makeHandledOpenURLAction(_ token: Int64,
    _ cb: @escaping @convention(c) (Int64) -> Void) -> OpenURLAction {
    return OpenURLAction { _ in
        cb(token)
        return .handled
    }
}

public func urlStride() -> Int64 { Int64(MemoryLayout<URL>.stride) }

public func makeURL(_ ptr: UnsafePointer<UInt8>, _ len: Int64, _ out: UnsafeMutableRawPointer) {
    let s = String(decoding: UnsafeBufferPointer(start: ptr, count: Int(len)), as: UTF8.self)
    out.assumingMemoryBound(to: URL.self).initialize(to: URL(string: s)!)
}

public func destroyURL(_ p: UnsafeMutableRawPointer) {
    p.assumingMemoryBound(to: URL.self).deinitialize(count: 1)
}
