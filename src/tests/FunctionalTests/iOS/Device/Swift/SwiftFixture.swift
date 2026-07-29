// Swift fixture for the iOS DEVICE lane (arm64-apple-ios, real hardware).
// Exercises the same ABI shapes the desktop and simulator lanes validate —
// frozen-struct lowering, the self register, nested tail-packing, and a
// @MainActor call that only completes on a host that pumps the main queue.
//
// The device is arm64e (A17 Pro): even though managed app code is emitted as
// arm64, these calls cross into Swift/Foundation slices in the arm64e dyld
// shared cache, so success here is the real ptrauth/arm64e boundary check.
import Foundation

public struct Pair { public var a: Int64; public var b: Int64 }

public func addPair(_ p: Pair) -> Int64 { p.a &+ p.b }

public func selfSum(_ x: Int64, _ ctx: UnsafeRawPointer) -> Int64 {
    return x &+ ctx.load(as: Int64.self)
}

public struct Inner {
    public var a: Int64
    public var b: Int8
}

public struct Outer {
    public var inner: Inner
    public var c: Int8
}

public func sumOuter(_ v: Outer) -> Int64 {
    return v.inner.a &+ Int64(v.inner.b) &+ Int64(v.c)
}

public func makeOuter(a: Int64, b: Int8, c: Int8) -> Outer {
    return Outer(inner: Inner(a: a, b: b), c: c)
}

// @MainActor: completes only if the host services the main dispatch queue.
@MainActor
public func mainActorSum(_ a: Int64, _ b: Int64) async -> Int64 {
    return a &+ b
}

@_cdecl("fixture_call_mainactor")
public func callMainActor(_ context: UnsafeMutableRawPointer,
                          _ callback: @escaping @convention(c) (UnsafeMutableRawPointer, Int64) -> Void) {
    Task.detached {
        let r = await mainActorSum(40, 2)
        callback(context, r)
    }
}

// arm64e boundary: build a Foundation UUID (Foundation is an arm64e slice in
// the device's dyld shared cache) and hand its 16 bytes back by value. If
// authenticated-pointer handling were wrong at the interop boundary, resolving
// and calling this would fault rather than return.
public func uuidBytes(_ out: UnsafeMutablePointer<UInt8>) {
    let u = UUID()
    withUnsafeBytes(of: u.uuid) { raw in
        for i in 0..<16 { out[i] = raw[i] }
    }
    // Non-zero: a real UUID is never all-zero (version/variant bits are set).
}

// Swift Charts rendered to a bitmap ON DEVICE (arm64e). Same headless
// ImageRenderer path as desktop; the device app host pumps the main run loop,
// so the @MainActor render completes and reports back through the callback.
import Charts
import SwiftUI
import CoreGraphics

@available(iOS 16.0, macOS 13.0, tvOS 16.0, *)
@MainActor
func renderBarChart(_ values: [Int64]) -> (Int32, Int32, Int32)? {
    let data = values.enumerated().map { (String($0.offset), Int($0.element)) }
    let chart = Chart(data, id: \.0) { item in
        BarMark(x: .value("index", item.0), y: .value("value", item.1))
    }
    .frame(width: 240, height: 160)
    .background(Color.white)

    let renderer = ImageRenderer(content: chart)
    guard let cg = renderer.cgImage,
          let provider = cg.dataProvider?.data,
          let ptr = CFDataGetBytePtr(provider) else { return nil }
    let byteCount = CFDataGetLength(provider)
    let bytesPerPixel = max(1, cg.bitsPerPixel / 8)
    var nonBackground: Int32 = 0
    var i = 0
    while i + 2 < byteCount {
        if ptr[i] < 240 || ptr[i + 1] < 240 || ptr[i + 2] < 240 { nonBackground += 1 }
        i += bytesPerPixel
    }
    return (Int32(cg.width), Int32(cg.height), nonBackground)
}

@_cdecl("fixture_render_chart")
public func fixtureRenderChart(_ values: UnsafePointer<Int64>, _ count: Int64,
                               _ context: UnsafeMutableRawPointer,
                               _ callback: @escaping @convention(c) (UnsafeMutableRawPointer, Int32, Int32, Int32) -> Void) {
    let vals = Array(UnsafeBufferPointer(start: values, count: Int(count)))
    DispatchQueue.main.async {
        if #available(iOS 16.0, macOS 13.0, tvOS 16.0, *) {
            if let (w, h, nb) = renderBarChart(vals) { callback(context, w, h, nb) }
            else { callback(context, -1, 0, 0) }
        } else {
            callback(context, -2, 0, 0)  // Charts unavailable on this OS
        }
    }
}

// ---- Reverse interop (Phase 11) ON DEVICE: managed objects projected into
// Swift as closures and protocol conformers, with the GC<->ARC lifetime bridge.
final class ManagedHandleBox {
    let handle: UnsafeMutableRawPointer
    let free: @convention(c) (UnsafeMutableRawPointer) -> Void
    init(_ handle: UnsafeMutableRawPointer,
         _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) {
        self.handle = handle; self.free = free
    }
    deinit { free(handle) }
}

private var revStored: [(Int64) -> Int64] = []

@_cdecl("fixture_store_closure")
public func fixtureStoreClosure(_ handle: UnsafeMutableRawPointer,
                                _ invoke: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64,
                                _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) -> Int64 {
    let box = ManagedHandleBox(handle, free)
    revStored.append({ x in invoke(x, box.handle) })
    return Int64(revStored.count - 1)
}

@_cdecl("fixture_call_closure")
public func fixtureCallClosure(_ index: Int64, _ x: Int64) -> Int64 { revStored[Int(index)](x) }

@_cdecl("fixture_release_closures")
public func fixtureReleaseClosures() { revStored.removeAll() }

protocol RevHandler { func handle(_ x: Int64) -> Int64; func label() -> Int64 }

final class RevManagedHandlerProxy: RevHandler {
    let box: ManagedHandleBox
    let handleFn: @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64
    let labelFn: @convention(c) (UnsafeMutableRawPointer) -> Int64
    init(_ box: ManagedHandleBox,
         _ h: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64,
         _ l: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64) {
        self.box = box; self.handleFn = h; self.labelFn = l
    }
    func handle(_ x: Int64) -> Int64 { handleFn(x, box.handle) }
    func label() -> Int64 { labelFn(box.handle) }
}

private var revHandlers: [RevHandler] = []

@_cdecl("fixture_store_handler")
public func fixtureStoreHandler(_ handle: UnsafeMutableRawPointer,
                                _ h: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64,
                                _ l: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64,
                                _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) -> Int64 {
    let box = ManagedHandleBox(handle, free)
    revHandlers.append(RevManagedHandlerProxy(box, h, l))
    return Int64(revHandlers.count - 1)
}

func revRunGeneric<H: RevHandler>(_ h: H, _ x: Int64) -> Int64 { h.handle(x) + h.label() }

@_cdecl("fixture_run_handler")
public func fixtureRunHandler(_ index: Int64, _ x: Int64) -> Int64 {
    let h: RevHandler = revHandlers[Int(index)]
    return revRunGeneric(h, x)
}

@_cdecl("fixture_release_handlers")
public func fixtureReleaseHandlers() { revHandlers.removeAll() }

// ---- Advanced reverse interop on device: open-class subclassing + associated types
open class RevShape {
    public init() {}
    open func area() -> Int64 { -1 }
    open func perimeter() -> Int64 { -1 }
    public final func report() -> Int64 { area() * 100 + perimeter() }
}
final class RevManagedShapeProxy: RevShape {
    let box: ManagedHandleBox
    let areaFn: @convention(c) (UnsafeMutableRawPointer) -> Int64
    let perimeterFn: @convention(c) (UnsafeMutableRawPointer) -> Int64
    init(_ b: ManagedHandleBox, _ a: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64,
         _ p: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64) { box = b; areaFn = a; perimeterFn = p; super.init() }
    override func area() -> Int64 { areaFn(box.handle) }
    override func perimeter() -> Int64 { perimeterFn(box.handle) }
}
private var revShapes: [RevShape] = []
@_cdecl("fixture_store_shape")
public func fixtureStoreShape(_ handle: UnsafeMutableRawPointer,
                              _ a: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64,
                              _ p: @escaping @convention(c) (UnsafeMutableRawPointer) -> Int64,
                              _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) -> Int64 {
    let box = ManagedHandleBox(handle, free); revShapes.append(RevManagedShapeProxy(box, a, p)); return Int64(revShapes.count - 1)
}
@_cdecl("fixture_report_shape")
public func fixtureReportShape(_ index: Int64) -> Int64 { revShapes[Int(index)].report() }
@_cdecl("fixture_release_shapes")
public func fixtureReleaseShapes() { revShapes.removeAll() }

public protocol RevProducer<Output> { associatedtype Output; func produce(_ seed: Int64) -> Output }
final class RevManagedProducerProxy: RevProducer {
    typealias Output = Int64
    let box: ManagedHandleBox
    let produceFn: @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64
    init(_ b: ManagedHandleBox, _ f: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64) { box = b; produceFn = f }
    func produce(_ seed: Int64) -> Int64 { produceFn(seed, box.handle) }
}
private var revProducers: [RevManagedProducerProxy] = []  // concrete type avoids the iOS16 parameterized-existential runtime gate; the generic function below still resolves P.Output
@_cdecl("fixture_store_producer")
public func fixtureStoreProducer(_ handle: UnsafeMutableRawPointer,
                                 _ f: @escaping @convention(c) (Int64, UnsafeMutableRawPointer) -> Int64,
                                 _ free: @escaping @convention(c) (UnsafeMutableRawPointer) -> Void) -> Int64 {
    let box = ManagedHandleBox(handle, free); revProducers.append(RevManagedProducerProxy(box, f)); return Int64(revProducers.count - 1)
}
func revGenericProduce<P: RevProducer>(_ p: P, _ seed: Int64) -> P.Output { p.produce(seed) }
@_cdecl("fixture_run_producer")
public func fixtureRunProducer(_ index: Int64, _ seed: Int64) -> Int64 { revGenericProduce(revProducers[Int(index)], seed) }
@_cdecl("fixture_release_producers")
public func fixtureReleaseProducers() { revProducers.removeAll() }
