// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Resilient-value lifecycle fixture for the Swift projection support layer.
// This module is compiled WITH library evolution and WITHOUT @frozen, so
// ResilientValue has no fixed client-visible layout: clients must use the
// metadata accessor and value-witness operations.

import Foundation

private let counterLock = NSLock()
private var liveTrackerCount = 0

public final class Tracker {
    let payload: Int

    init(payload: Int) {
        self.payload = payload
        counterLock.lock()
        liveTrackerCount += 1
        counterLock.unlock()
    }

    deinit {
        counterLock.lock()
        liveTrackerCount -= 1
        counterLock.unlock()
    }
}

public func trackerLiveCount() -> Int {
    counterLock.lock()
    let count = liveTrackerCount
    counterLock.unlock()
    return count
}

public struct ResilientValue {
    var tracker: Tracker
    var a: Int
    var b: Double

    public init(seed: Int) {
        tracker = Tracker(payload: seed)
        a = seed
        b = Double(seed) * 1.5
    }

    public func sum() -> Double {
        return Double(a) + b + Double(tracker.payload)
    }
}

public func makeResilientValue(seed: Int) -> ResilientValue {
    return ResilientValue(seed: seed)
}

public func resilientValueSum(_ v: ResilientValue) -> Double {
    return v.sum()
}

public func makeTracker(payload: Int) -> Tracker {
    return Tracker(payload: payload)
}

public func trackerPayload(_ t: Tracker) -> Int {
    return t.payload
}

public struct TrackedError: Error {
    let tracker: Tracker

    public init(payload: Int) {
        tracker = Tracker(payload: payload)
    }

    public var payload: Int { tracker.payload }
}

public func throwTrackedError(payload: Int) throws {
    throw TrackedError(payload: payload)
}

public func trackedErrorPayload(_ e: Error) -> Int {
    guard let trackedError = e as? TrackedError else { return -1 }
    return trackedError.payload
}

public enum ResilientEnum {
    case empty
    case number(Int)
    case tracked(Tracker)
}

public func makeResilientEnum(kind: Int, payload: Int) -> ResilientEnum {
    switch kind {
    case 0: return .empty
    case 1: return .number(payload)
    default: return .tracked(Tracker(payload: payload))
    }
}

public func resilientEnumDescribe(_ e: ResilientEnum) -> Int {
    switch e {
    case .empty: return -1
    case .number(let n): return n
    case .tracked(let t): return t.payload
    }
}

public func scaleResilientValue(_ v: inout ResilientValue, by factor: Int) {
    v.a *= factor
    v.b *= Double(factor)
}

public struct NoncopyableResource: ~Copyable {
    var tracker: Tracker

    public init(payload: Int) {
        tracker = Tracker(payload: payload)
    }
}

public func makeNoncopyableResource(payload: Int) -> NoncopyableResource {
    return NoncopyableResource(payload: payload)
}

public protocol Describable {
    func describedValue() -> Int
}

extension ResilientValue: Describable {
    public func describedValue() -> Int {
        return a
    }
}
