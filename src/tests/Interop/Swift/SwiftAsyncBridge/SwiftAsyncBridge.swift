// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Phase 8 generated async-thunk ABI v1 fixture: operation handles with an
// exactly-once terminal state machine, synchronous-completion coverage,
// result adoption via caller storage, MainActor hops, actor reentrancy,
// a pull-model AsyncSequence adapter, and an eager startup listener.

import Foundation

public func asyncthunk_abiVersion() -> Int64 {
    return 1
}

// Terminal states: 1 = completed, 2 = failed, 3 = cancelled.
private final class Operation {
    let lock = NSLock()
    var terminal: Int64 = 0
    var task: Task<Void, Never>?

    // Exactly-once terminal transition; returns false if already terminal.
    func tryTerminate(_ state: Int64) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        if terminal != 0 { return false }
        terminal = state
        return true
    }
}

public func asyncthunk_operationTerminalState(_ op: UnsafeMutableRawPointer) -> Int64 {
    let operation = Unmanaged<Operation>.fromOpaque(op).takeUnretainedValue()
    operation.lock.lock()
    defer { operation.lock.unlock() }
    return operation.terminal
}

/// begin: the completion (context, value, errorCode, terminalState) fires
/// exactly once. input == 0 completes synchronously BEFORE begin returns;
/// negative input fails with the input as the error code.
public func asyncthunk_beginFetch(
    _ input: Int64,
    _ context: UnsafeMutableRawPointer?,
    _ completion: @escaping @convention(c) (UnsafeMutableRawPointer?, Int64, Int64, Int64) -> Void
) -> UnsafeMutableRawPointer {
    let operation = Operation()
    let handle = Unmanaged.passRetained(operation).toOpaque()

    if input == 0 {
        // Synchronous completion before start returns.
        if operation.tryTerminate(1) {
            completion(context, 42, 0, 1)
        }
        return handle
    }

    operation.task = Task {
        do {
            try await Task.sleep(nanoseconds: 5_000_000)
            if Task.isCancelled {
                if operation.tryTerminate(3) { completion(context, 0, 0, 3) }
                return
            }
            if input < 0 {
                if operation.tryTerminate(2) { completion(context, 0, input, 2) }
            } else {
                if operation.tryTerminate(1) { completion(context, input &* 2, 0, 1) }
            }
        } catch {
            if operation.tryTerminate(3) { completion(context, 0, 0, 3) }
        }
    }
    return handle
}

/// Idempotent cancel: safe before/after completion and repeatedly.
public func asyncthunk_operationCancel(_ op: UnsafeMutableRawPointer) {
    let operation = Unmanaged<Operation>.fromOpaque(op).takeUnretainedValue()
    operation.task?.cancel()
}

/// Idempotent release of the begin reference.
public func asyncthunk_operationRelease(_ op: UnsafeMutableRawPointer) {
    Unmanaged<Operation>.fromOpaque(op).release()
}

// Result adoption through caller storage: the completion writes a resilient
// value into the caller-provided buffer (sized via the rooted metadata),
// transferring ownership to the caller.
public struct Bundle {
    public var total: Int64
    public var flag: Int8

    public init(total: Int64, flag: Int8) {
        self.total = total
        self.flag = flag
    }
}

public func asyncthunk_bundleMetadata() -> Any.Type {
    return Bundle.self
}

public func asyncthunk_beginFetchBundle(
    _ input: Int64,
    _ resultStorage: UnsafeMutableRawPointer,
    _ context: UnsafeMutableRawPointer?,
    _ completion: @escaping @convention(c) (UnsafeMutableRawPointer?, Int64) -> Void
) -> UnsafeMutableRawPointer {
    let operation = Operation()
    let handle = Unmanaged.passRetained(operation).toOpaque()
    operation.task = Task {
        try? await Task.sleep(nanoseconds: 2_000_000)
        if operation.tryTerminate(1) {
            resultStorage.assumingMemoryBound(to: Bundle.self)
                .initialize(to: Bundle(total: input &* 10, flag: 1))
            completion(context, 1)
        }
    }
    return handle
}

// MainActor hop: the completion runs ON the main actor; in a headless host
// nothing delivers it until the main queue is pumped.
public func asyncthunk_beginMainActorFetch(
    _ input: Int64,
    _ context: UnsafeMutableRawPointer?,
    _ completion: @escaping @convention(c) (UnsafeMutableRawPointer?, Int64) -> Void
) -> UnsafeMutableRawPointer {
    let operation = Operation()
    let handle = Unmanaged.passRetained(operation).toOpaque()
    operation.task = Task {
        let value = input &+ 1
        await MainActor.run {
            if operation.tryTerminate(1) {
                completion(context, value)
            }
        }
    }
    return handle
}

// Actor reentrancy: awaiting inside the actor lets a second call interleave.
public actor Ledger {
    private var entries: Int64 = 0
    private var maxObservedDuringAwait: Int64 = 0

    public init() {}

    public func slowAdd(_ amount: Int64) async -> Int64 {
        entries &+= amount
        let before = entries
        try? await Task.sleep(nanoseconds: 10_000_000) // reentrancy window
        maxObservedDuringAwait = max(maxObservedDuringAwait, entries &- before)
        return entries
    }

    public func interleavedGrowth() -> Int64 {
        return maxObservedDuringAwait
    }
}

private nonisolated(unsafe) var sharedLedger = Ledger()

public func asyncthunk_ledgerSlowAdd(
    _ amount: Int64,
    _ context: UnsafeMutableRawPointer?,
    _ completion: @escaping @convention(c) (UnsafeMutableRawPointer?, Int64) -> Void
) {
    Task {
        let total = await sharedLedger.slowAdd(amount)
        completion(context, total)
    }
}

public func asyncthunk_ledgerInterleavedGrowth(
    _ context: UnsafeMutableRawPointer?,
    _ completion: @escaping @convention(c) (UnsafeMutableRawPointer?, Int64) -> Void
) {
    Task {
        let growth = await sharedLedger.interleavedGrowth()
        completion(context, growth)
    }
}

// Pull-model AsyncSequence adapter: exactly one in-flight next() per call.
private final class StreamBox {
    var iterator: AsyncStream<Int64>.Iterator
    var stopped = false
    let lock = NSLock()

    init(_ count: Int64) {
        var i: Int64 = 0
        let stream = AsyncStream<Int64> { continuation in
            Task {
                while i < count {
                    try? await Task.sleep(nanoseconds: 1_000_000)
                    continuation.yield(i &* 3)
                    i &+= 1
                }
                continuation.finish()
            }
        }
        iterator = stream.makeAsyncIterator()
    }
}

public func asyncthunk_streamOpen(_ count: Int64) -> UnsafeMutableRawPointer {
    return Unmanaged.passRetained(StreamBox(count)).toOpaque()
}

/// Runs exactly one next(): onItem(context, hasValue, value).
public func asyncthunk_streamNext(
    _ handle: UnsafeMutableRawPointer,
    _ context: UnsafeMutableRawPointer?,
    _ onItem: @escaping @convention(c) (UnsafeMutableRawPointer?, Int64, Int64) -> Void
) {
    let box = Unmanaged<StreamBox>.fromOpaque(handle).takeUnretainedValue()
    Task {
        box.lock.lock()
        let stopped = box.stopped
        box.lock.unlock()
        if stopped {
            onItem(context, 0, 0)
            return
        }
        if let value = await box.iterator.next() {
            onItem(context, 1, value)
        } else {
            onItem(context, 0, 0)
        }
    }
}

public func asyncthunk_streamClose(_ handle: UnsafeMutableRawPointer) {
    let box = Unmanaged<StreamBox>.fromOpaque(handle).takeUnretainedValue()
    box.lock.lock()
    box.stopped = true
    box.lock.unlock()
}

public func asyncthunk_streamRelease(_ handle: UnsafeMutableRawPointer) {
    Unmanaged<StreamBox>.fromOpaque(handle).release()
}

// Fail-closed storefront predicate: unknown/unconfigured means "no".
private nonisolated(unsafe) var storefrontConfigured = false

public func asyncthunk_setStorefrontConfigured(_ value: Int64) {
    storefrontConfigured = value != 0
}

public func asyncthunk_canMakePayments() -> Int64 {
    return storefrontConfigured ? 1 : 0
}
