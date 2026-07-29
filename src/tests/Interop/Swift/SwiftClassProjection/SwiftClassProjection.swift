// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Class projection fixture, compiled WITH library evolution so overridable
// members get exported dispatch thunks (`…Tj`).

private var liveCounters: Int64 = 0

public func liveCounterInstances() -> Int64 {
    return liveCounters
}

open class Counter {
    public private(set) var total: Int64

    public init(start: Int64) {
        total = start
        liveCounters &+= 1
    }

    public init?(validated start: Int64) {
        if start < 0 {
            return nil
        }
        total = start
        liveCounters &+= 1
    }

    deinit {
        liveCounters &-= 1
    }

    public func increment(by amount: Int64) {
        total &+= amount
    }

    public final func snapshot() -> Int64 {
        return total
    }

    open func describe() -> Int64 {
        return total &* 10
    }

    public static func defaultStart() -> Int64 {
        return 7
    }

    public var doubledTotal: Int64 {
        return total &* 2
    }

    public subscript(index: Int64) -> Int64 {
        return total &+ index
    }
}

public class LoudCounter: Counter {
    public override func describe() -> Int64 {
        return total &* 100
    }
}

public func makeLoudCounterAsBase(start: Int64) -> Counter {
    return LoudCounter(start: start)
}
