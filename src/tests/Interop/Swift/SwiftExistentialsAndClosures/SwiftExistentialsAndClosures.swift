// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Phase 7 existentials + closures fixture: opaque and class existential
// containers, protocol compositions, PAT dispatch through a closed generic
// thunk, returned Swift closures, and escaping managed callbacks boxed with
// exactly-once release semantics.

public protocol Speaker {
    func speak() -> Int64
}

public protocol Louder {
    func loudness() -> Int64
}

public protocol RefSpeaker: AnyObject {
    func speak() -> Int64
}

public struct SmallThing: Speaker, Louder {
    public var x: Int64

    public func speak() -> Int64 { return x }
    public func loudness() -> Int64 { return x &* 10 }
}

public struct BigThing: Speaker {
    public var a: Int64, b: Int64, c: Int64, d: Int64

    public func speak() -> Int64 { return a &+ b &+ c &+ d }
}

public final class RefThing: RefSpeaker {
    public static nonisolated(unsafe) var live: Int64 = 0
    let x: Int64

    public init(x: Int64) {
        self.x = x
        RefThing.live &+= 1
    }

    deinit {
        RefThing.live &-= 1
    }

    public func speak() -> Int64 { return x &* 2 }
}

public func liveRefThings() -> Int64 { return RefThing.live }

public func speakerExistentialMetadata() -> Any.Type { return (any Speaker).self }
public func comboExistentialMetadata() -> Any.Type { return (any Speaker & Louder).self }
public func refSpeakerExistentialMetadata() -> Any.Type { return (any RefSpeaker).self }

public func makeSmallSpeakerInto(_ p: UnsafeMutableRawPointer, _ x: Int64) {
    p.assumingMemoryBound(to: (any Speaker).self).initialize(to: SmallThing(x: x))
}

public func makeBigSpeakerInto(_ p: UnsafeMutableRawPointer, _ seed: Int64) {
    p.assumingMemoryBound(to: (any Speaker).self)
        .initialize(to: BigThing(a: seed, b: seed &+ 1, c: seed &+ 2, d: seed &+ 3))
}

public func makeComboInto(_ p: UnsafeMutableRawPointer, _ x: Int64) {
    p.assumingMemoryBound(to: (any Speaker & Louder).self).initialize(to: SmallThing(x: x))
}

public func makeRefSpeakerInto(_ p: UnsafeMutableRawPointer, _ x: Int64) {
    p.assumingMemoryBound(to: (any RefSpeaker).self).initialize(to: RefThing(x: x))
}

// Thunk-routed dispatch (the resilient/conditional dispatch path): the
// thunk opens the existential; managed code never guesses box layouts.
public func skthunk_speak(_ p: UnsafeRawPointer) -> Int64 {
    return p.assumingMemoryBound(to: (any Speaker).self).pointee.speak()
}

public func skthunk_comboSum(_ p: UnsafeRawPointer) -> Int64 {
    let v = p.assumingMemoryBound(to: (any Speaker & Louder).self).pointee
    return v.speak() &+ v.loudness()
}

public func skthunk_refSpeak(_ p: UnsafeRawPointer) -> Int64 {
    return p.assumingMemoryBound(to: (any RefSpeaker).self).pointee.speak()
}

// Protocol with associated type: no existential; a closed generic thunk
// projects the concrete conformer (the generated-generic-API pattern).
public protocol Producer {
    associatedtype Output
    func produce() -> Output
}

public struct IntProducer: Producer {
    public var seed: Int64

    public init(seed: Int64) {
        self.seed = seed
    }

    public func produce() -> Int64 {
        return seed &* 7
    }
}

public func skthunk_runIntProducer(_ seed: Int64) -> Int64 {
    func run<T: Producer>(_ producer: T) -> T.Output {
        return producer.produce()
    }
    return run(IntProducer(seed: seed))
}

// Closures.
public func makeAdder(_ base: Int64) -> (Int64) -> Int64 {
    return { $0 &+ base }
}

public func callNonescaping(_ f: (Int64) -> Int64) -> Int64 {
    return f(9)
}

// Escaping managed callback boxed with exactly-once release: the box owns
// the managed context and releases it in deinit — once, regardless of how
// many times the closure ran or on which thread the last reference died.
private final class CallbackBox {
    let context: UnsafeMutableRawPointer
    let invoke: @convention(c) (UnsafeMutableRawPointer, Int64) -> Int64
    let onRelease: @convention(c) (UnsafeMutableRawPointer) -> Void

    init(
        _ context: UnsafeMutableRawPointer,
        _ invoke: @escaping @convention(c) (UnsafeMutableRawPointer, Int64) -> Int64,
        _ onRelease: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void
    ) {
        self.context = context
        self.invoke = invoke
        self.onRelease = onRelease
    }

    deinit {
        onRelease(context)
    }
}

private nonisolated(unsafe) var storedClosure: ((Int64) -> Int64)?

public func skthunk_storeEscaping(
    _ context: UnsafeMutableRawPointer,
    _ invoke: @escaping @convention(c) (UnsafeMutableRawPointer, Int64) -> Int64,
    _ onRelease: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void
) {
    let box = CallbackBox(context, invoke, onRelease)
    storedClosure = { box.invoke(box.context, $0) }
}

public func skthunk_invokeStored(_ value: Int64) -> Int64 {
    return storedClosure?(value) ?? -1
}

public func skthunk_clearStored() {
    storedClosure = nil
}
