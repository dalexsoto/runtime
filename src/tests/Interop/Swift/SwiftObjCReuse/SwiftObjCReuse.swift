// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// An @objc Swift class: the same instance must be usable through both the
// Swift ABI and the Objective-C runtime (objc_msgSend), so existing
// Objective-C interop wrappers can be reused for imported/@objc classes.

import Foundation

@objc public class Beacon: NSObject {
    @objc public private(set) var level: Int64

    @objc public init(level: Int64) {
        self.level = level
    }

    @objc public func boost(_ amount: Int64) -> Int64 {
        level += amount
        return level
    }
}

public func makeBeacon(level: Int64) -> Beacon {
    return Beacon(level: level)
}

public func beaconLevel(_ b: Beacon) -> Int64 {
    return b.level
}
