// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Phase 7 errors + enums fixture: typed-throws normalization, NSError
// bridging, managed-exception containment in reverse callbacks, Swift-side
// translation of managed failures, unknown-case preservation, and
// borrow-safe payload extraction.

import Foundation

public struct CatalogError: Error {
    public let code: Int64

    public init(code: Int64) {
        self.code = code
    }
}

// The NSError bridge surfaces a meaningful code/domain only through
// CustomNSError; without it, bridged Swift errors report code 1.
extension CatalogError: CustomNSError {
    public static var errorDomain: String {
        return "SwiftErrorsAndEnums.CatalogError"
    }

    public var errorCode: Int {
        return Int(code)
    }
}

public func fetchOrThrow(_ value: Int64) throws(CatalogError) -> Int64 {
    if value < 0 {
        throw CatalogError(code: value)
    }
    return value &* 2
}

// Typed-throws normalization thunk: typed throws becomes a plain result
// code plus output value; the thrown type never crosses raw.
public func skthunk_fetchNormalized(_ value: Int64, _ out: UnsafeMutablePointer<Int64>) -> Int64 {
    do {
        out.pointee = try fetchOrThrow(value)
        return 0
    } catch {
        return error.code
    }
}

public func throwCatalogError(_ code: Int64) throws {
    throw CatalogError(code: code)
}

// The optional NSError bridge lives in the thunk: a raw error box's lazy
// ObjC accessors only work once Foundation's bridging hooks are installed,
// which a pure host process cannot rely on. Bridging explicitly via
// `as NSError` is deterministic; the returned object (+1) answers
// code/domain through objc_msgSend.
public func skthunk_bridgeToNSError(_ box: UnsafeMutableRawPointer) -> UnsafeMutableRawPointer {
    let anyObject = Unmanaged<AnyObject>.fromOpaque(box).takeUnretainedValue()
    let error = anyObject as! Error
    let bridged = error as NSError
    return Unmanaged.passRetained(bridged).toOpaque()
}

// Swift-side translation of managed failures: the reverse callback reports
// failure through its return code; the thunk translates it into a real
// Swift throw so Swift callers observe an ordinary error.
public func skthunk_translateManagedFailure(
    _ context: UnsafeMutableRawPointer?,
    _ callback: @convention(c) (UnsafeMutableRawPointer?) -> Int64
) -> Int64 {
    func run() throws -> Int64 {
        let code = callback(context)
        if code != 0 {
            throw CatalogError(code: code)
        }
        return 42
    }

    do {
        return try run()
    } catch let error as CatalogError {
        return -error.code // observed as a Swift error, returned as evidence
    } catch {
        return -999
    }
}

// A resilient enum the client compiles against BEFORE lateAdded existed:
// unknown cases must be preserved and round-tripped, never switched into
// oblivion.
public enum Channel {
    case alpha
    case beta(Int64)
    case lateAdded(Int64)
}

public func makeChannelInto(_ p: UnsafeMutableRawPointer, _ kind: Int64, _ payload: Int64) {
    let value: Channel
    switch kind {
    case 0: value = .alpha
    case 1: value = .beta(payload)
    default: value = .lateAdded(payload)
    }
    p.assumingMemoryBound(to: Channel.self).initialize(to: value)
}

public func channelDescribe(_ p: UnsafeRawPointer) -> Int64 {
    switch p.assumingMemoryBound(to: Channel.self).pointee {
    case .alpha: return -1
    case .beta(let v): return v
    case .lateAdded(let v): return v &* 1000
    }
}

public func channelMetadata() -> Any.Type {
    return Channel.self
}

// A payload case with ARC content, for borrow-safety proof.
public final class Counter {
    public static nonisolated(unsafe) var live: Int64 = 0

    public let value: Int64

    public init(value: Int64) {
        self.value = value
        Counter.live &+= 1
    }

    deinit {
        Counter.live &-= 1
    }
}

public func counterLive() -> Int64 {
    return Counter.live
}

public enum Tracked {
    case empty
    case holding(Counter)
}

public func makeTrackedInto(_ p: UnsafeMutableRawPointer, _ value: Int64) {
    p.assumingMemoryBound(to: Tracked.self).initialize(to: .holding(Counter(value: value)))
}

public func trackedValue(_ p: UnsafeRawPointer) -> Int64 {
    switch p.assumingMemoryBound(to: Tracked.self).pointee {
    case .empty: return -1
    case .holding(let counter): return counter.value
    }
}

public func trackedMetadata() -> Any.Type {
    return Tracked.self
}
