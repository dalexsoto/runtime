// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Fixture for the R2R hardening suite: minimal functions whose managed
// wrappers can be attributed to a code source (R2R, JIT tier, interpreter)
// through runtime method-load events.

public func addValues(a: Int, b: Int) -> Int {
    return a + b
}

public func mulValues(a: Int, b: Int) -> Int {
    return a * b
}

public struct ProvenanceError: Error {
    let code: Int

    public init(code: Int) {
        self.code = code
    }
}

public func doubleOrThrow(value: Int) throws -> Int {
    if value < 0 {
        throw ProvenanceError(code: value)
    }
    return value * 2
}
