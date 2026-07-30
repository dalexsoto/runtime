// Swift fixture for the simulator lane: the same shapes the desktop suites
// validate (frozen struct lowering, self register, error register).
public struct Pair { public var a: Int64; public var b: Int64 }

public func addPair(_ p: Pair) -> Int64 { p.a &+ p.b }

public func selfSum(_ x: Int64, _ ctx: UnsafeRawPointer) -> Int64 {
    return x &+ ctx.load(as: Int64.self)
}

// Extended-layout (nested tail-packing) shapes: Outer packs `c` into Inner's
// trailing padding, so Swift's stride is 16 with c at offset 9 — the layout
// the managed ExtendedLayoutKind.SwiftStruct types must reproduce.
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
// A console host hangs here forever (abi-model.md); an APP host is claimed to
// be fine — this proves it rather than asserting it.
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

