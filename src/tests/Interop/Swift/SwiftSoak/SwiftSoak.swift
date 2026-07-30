// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Soak fixture (Audit follow-ups): deinit-tracked object churn, escaping
// closure boxes with exactly-once release, and string round-trips, all
// balance-checkable at scale.

import Foundation

public final class Tracked {
    nonisolated(unsafe) static var liveCount: Int64 = 0
    static let lock = NSLock()
    public init() {
        Tracked.lock.lock(); Tracked.liveCount &+= 1; Tracked.lock.unlock()
    }
    deinit {
        Tracked.lock.lock(); Tracked.liveCount &-= 1; Tracked.lock.unlock()
    }
}

public func makeTracked() -> Tracked { Tracked() }
public func trackedLive() -> Int64 {
    Tracked.lock.lock(); defer { Tracked.lock.unlock() }
    return Tracked.liveCount
}

// Escaping closure box: retains a context released exactly once via the
// release callback when the box deinits.
public final class ClosureBox {
    let invoke: @convention(c) (UnsafeMutableRawPointer, Int64) -> Int64
    let release: @convention(c) (UnsafeMutableRawPointer) -> Void
    let context: UnsafeMutableRawPointer
    public init(_ invoke: @escaping @convention(c) (UnsafeMutableRawPointer, Int64) -> Int64,
                _ release: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void,
                _ context: UnsafeMutableRawPointer) {
        self.invoke = invoke
        self.release = release
        self.context = context
    }
    deinit { release(context) }
}

public func makeClosureBox(
    _ invoke: @escaping @convention(c) (UnsafeMutableRawPointer, Int64) -> Int64,
    _ release: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void,
    _ context: UnsafeMutableRawPointer) -> ClosureBox {
    return ClosureBox(invoke, release, context)
}

public func invokeClosureBox(_ box: ClosureBox, _ arg: Int64) -> Int64 {
    return box.invoke(box.context, arg)
}

public func stringRoundTrip(_ s: String) -> Int64 {
    return Int64(s.utf8.count)
}
