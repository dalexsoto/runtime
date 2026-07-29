// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Fixture for shutdown-lifetime coverage: native Swift objects held by
// managed SafeHandles that are still alive (some mid-use on another thread)
// when the process exits.

public final class Resource {
    public let payload: Int

    public init(payload: Int) {
        self.payload = payload
    }
}

public func makeResource(payload: Int) -> Resource {
    return Resource(payload: payload)
}

public func resourcePayload(_ r: Resource) -> Int {
    return r.payload
}
