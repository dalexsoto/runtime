// Reverse interop: a MANAGED object projected into Swift.
//
// Phase 11 foundation. The reverse-call ABI (Swift invoking a managed callback)
// already works; what this adds is the GC<->ARC LIFETIME bridge. A managed
// object is rooted by a GCHandle and handed to Swift inside a box whose ARC
// deinit frees the GCHandle — so the managed object stays alive exactly as long
// as Swift holds the closure, and is released deterministically when Swift lets
// go. That coordination across two garbage/refcount systems is the hard part.
import Foundation

// Owns a foreign (managed GCHandle) pointer; frees it via a managed callback
// when ARC releases the box. This is the bridge object.
final class ManagedHandleBox {
    let handle: UnsafeMutableRawPointer
    let free: @convention(c) (UnsafeMutableRawPointer) -> Void
    init(_ handle: UnsafeMutableRawPointer,
         _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) {
        self.handle = handle
        self.free = free
    }
    deinit { free(handle) }
}

private var stored: [(Int64) -> Int64] = []

// Build an @escaping Swift closure backed by a managed callback + handle, and
// store it Swift-side. The box (captured by the closure) ties the managed
// object's lifetime to the closure's ARC lifetime.
public func storeManagedClosure(_ handle: UnsafeMutableRawPointer,
                                _ invoke: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64,
                                _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) -> Int64 {
    let box = ManagedHandleBox(handle, free)
    stored.append({ x in invoke(x, box.handle) })
    return Int64(stored.count - 1)
}

// Invoke a stored closure — this is Swift calling back into managed code.
public func callStored(_ index: Int64, _ x: Int64) -> Int64 {
    return stored[Int(index)](x)
}

public func storedCount() -> Int64 { Int64(stored.count) }

// Drop all stored closures: releases the boxes, whose deinits free the managed
// handles. Deterministic GC-root release from the Swift side.
public func releaseAllStored() {
    stored.removeAll()
}

// ---- Protocol proxy: a managed object conforming to a Swift protocol ----
//
// A generated Swift proxy type conforms to the Swift protocol and forwards each
// requirement to a managed callback. Because the proxy conforms to the protocol,
// Swift's ordinary generic/existential dispatch (through the witness table)
// lands in managed code — a managed object IS a Swift protocol conformer.

public protocol Handler {
    func handle(_ x: Int64) -> Int64
    func label() -> Int64
}

// The proxy: conforms to Handler, forwards to managed callbacks, and ties the
// managed object's lifetime to its own via the box.
final class ManagedHandlerProxy: Handler {
    let box: ManagedHandleBox
    let handleFn: @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64
    let labelFn: @convention(c) (UnsafeMutableRawPointer) -> Int64
    init(_ box: ManagedHandleBox,
         _ handleFn: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64,
         _ labelFn: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64) {
        self.box = box; self.handleFn = handleFn; self.labelFn = labelFn
    }
    func handle(_ x: Int64) -> Int64 { handleFn(x, box.handle) }
    func label() -> Int64 { labelFn(box.handle) }
}

private var storedHandlers: [Handler] = []

public func storeManagedHandler(_ handle: UnsafeMutableRawPointer,
                                _ handleFn: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64,
                                _ labelFn: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64,
                                _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) -> Int64 {
    let box = ManagedHandleBox(handle, free)
    storedHandlers.append(ManagedHandlerProxy(box, handleFn, labelFn))
    return Int64(storedHandlers.count - 1)
}

// Consume the protocol GENERICALLY: this dispatches through the witness table,
// so a real Swift generic function ends up calling managed methods.
public func runHandlerGeneric<H: Handler>(_ h: H, _ x: Int64) -> Int64 {
    return h.handle(x) + h.label()
}

// Consume it as an EXISTENTIAL too (boxed protocol value, dynamic witness).
public func runStoredHandler(_ index: Int64, _ x: Int64) -> Int64 {
    let h: Handler = storedHandlers[Int(index)]
    return runHandlerGeneric(h, x)
}

public func releaseHandlers() { storedHandlers.removeAll() }

// ---- Subclassing an open Swift class from managed code ----
//
// A generated Swift subclass of an OPEN Swift class overrides its methods to
// forward to managed callbacks. Because the Swift compiler builds the subclass
// vtable, Swift's ordinary VIRTUAL DISPATCH — including a base-class `final`
// method that calls the overridden virtuals — lands in managed code. A managed
// object IS a subclass of a Swift class.

open class Shape {
    public init() {}
    open func area() -> Int64 { -1 }
    open func perimeter() -> Int64 { -1 }
    // A base method that uses virtual dispatch internally: proves the vtable,
    // not a direct call, reaches the managed overrides.
    public final func report() -> Int64 { area() * 100 + perimeter() }
}

final class ManagedShapeProxy: Shape {
    let box: ManagedHandleBox
    let areaFn: @convention(c) (UnsafeMutableRawPointer) -> Int64
    let perimeterFn: @convention(c) (UnsafeMutableRawPointer) -> Int64
    init(_ box: ManagedHandleBox,
         _ areaFn: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64,
         _ perimeterFn: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64) {
        self.box = box; self.areaFn = areaFn; self.perimeterFn = perimeterFn
        super.init()
    }
    override func area() -> Int64 { areaFn(box.handle) }
    override func perimeter() -> Int64 { perimeterFn(box.handle) }
}

private var shapes: [Shape] = []

public func storeManagedShape(_ handle: UnsafeMutableRawPointer,
                              _ areaFn: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64,
                              _ perimeterFn: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64,
                              _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) -> Int64 {
    let box = ManagedHandleBox(handle, free)
    shapes.append(ManagedShapeProxy(box, areaFn, perimeterFn))
    return Int64(shapes.count - 1)
}

// Virtual dispatch through a base `final` method -> managed overrides.
public func reportShape(_ index: Int64) -> Int64 { shapes[Int(index)].report() }

// Polymorphic dispatch through the base type -> managed override.
public func areaOfShape(_ index: Int64) -> Int64 {
    let s: Shape = shapes[Int(index)]
    return s.area()
}

public func releaseShapes() { shapes.removeAll() }

// ---- Associated-type proxy: a managed conformer binding a protocol's
// associated type, dispatched generically ----
//
// The protocol has an associated type; the generated proxy binds it to a
// concrete type; a Swift GENERIC function resolves `P.Output` and dispatches
// into managed. This exercises associated-type witness resolution, not just
// plain protocol dispatch.

public protocol Producer<Output> {
    associatedtype Output
    func produce(_ seed: Int64) -> Output
}

final class ManagedProducerProxy: Producer {
    typealias Output = Int64
    let box: ManagedHandleBox
    let produceFn: @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64
    init(_ box: ManagedHandleBox,
         _ produceFn: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64) {
        self.box = box; self.produceFn = produceFn
    }
    func produce(_ seed: Int64) -> Int64 { produceFn(seed, box.handle) }
}

private var producers: [any Producer<Int64>] = []

public func storeManagedProducer(_ handle: UnsafeMutableRawPointer,
                                 _ produceFn: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64,
                                 _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) -> Int64 {
    let box = ManagedHandleBox(handle, free)
    producers.append(ManagedProducerProxy(box, produceFn))
    return Int64(producers.count - 1)
}

// Generic over P: resolves P.Output through the associated-type witness.
func genericProduce<P: Producer>(_ p: P, _ seed: Int64) -> P.Output { p.produce(seed) }

public func runProducer(_ index: Int64, _ seed: Int64) -> Int64 {
    let p = producers[Int(index)]           // any Producer<Int64>
    return genericProduce(p, seed)          // implicit opening; P.Output == Int64
}

public func releaseProducers() { producers.removeAll() }
