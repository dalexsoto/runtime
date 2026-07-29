// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Verification helpers for the managed SwiftString: content is observed
// through Swift itself (sums, comparisons, concatenation) so no managed
// UTF-8 readback is required by the walking skeleton.

public func utf8Sum(_ s: String) -> Int64 {
    var sum: Int64 = 0
    for b in s.utf8 {
        sum &+= Int64(b)
    }
    return sum
}

public func concat(_ a: String, _ b: String) -> String {
    return a + b
}

public func equalsAlphabet(_ s: String) -> Int64 {
    return s == "abcdefghijklmnopqrstuvwxyz0123456789" ? 1 : 0
}

public func stringsEqual(_ a: String, _ b: String) -> Int64 {
    return a == b ? 1 : 0
}
