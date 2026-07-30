// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Static fixture: built as a static archive and linked INTO the NativeAOT
// binary; symbols (including the metadata accessor) resolve at link time.
// Kept free of Foundation so its only autolink dependency is libswiftCore.

public func staticAdd(a: Int, b: Int) -> Int {
    return a &+ b
}

public struct StaticValue {
    var x: Int
    var y: Int

    public init(x: Int, y: Int) {
        self.x = x
        self.y = y
    }
}

public func makeStaticValue(x: Int, y: Int) -> StaticValue {
    return StaticValue(x: x, y: y)
}

public func staticValueSum(_ v: StaticValue) -> Int {
    return v.x &+ v.y
}

public final class StaticBox {
    public let payload: Int

    public init(payload: Int) {
        self.payload = payload
    }
}

public func makeStaticBox(payload: Int) -> StaticBox {
    return StaticBox(payload: payload)
}

public func staticBoxPayload(_ b: StaticBox) -> Int {
    return b.payload
}
