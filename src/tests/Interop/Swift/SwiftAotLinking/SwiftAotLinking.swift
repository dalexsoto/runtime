// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Dynamic fixture: built as a dylib and bound eagerly at link time through
// DirectPInvoke under NativeAOT (lazily via dlopen everywhere else).

public func dylibAdd(a: Int, b: Int) -> Int {
    return a &+ b
}

public struct DylibValue {
    var x: Int
    var y: Double

    public init(x: Int, y: Double) {
        self.x = x
        self.y = y
    }
}

public func makeDylibValue(x: Int, y: Double) -> DylibValue {
    return DylibValue(x: x, y: y)
}

public func dylibValueSum(_ v: DylibValue) -> Double {
    return Double(v.x) + v.y
}
