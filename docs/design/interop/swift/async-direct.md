# Direct Swift async: verified ABI and candidate design

**Status (2026-07-12): track COMPLETE; decision recorded below.** This was
the roadmap's "Parallel research track - Direct Swift async"; all 19
items are closed and the exit-gate decision is in the "Decision"
section. Everything in "Verified ABI" below is
measured from `swiftc -emit-ir`/`-S` output (Swift 6.4, arm64/arm64e) and
proven executable by a foreign-code harness
(`swift-abi-probe/sources/async-direct/`, `HARNESS PASSED`, six legs).
Everything in "Candidate design" is definitional and non-shipping: the
`SwiftInterop.AsyncDirect1` profile stays reserved until the exit-gate
comparison against generated async thunks (abi-model.md "Generated
async-thunk ABI") produces an explicit go decision.

## Verified ABI (ARM64)

### The `Tu` async-function-pointer record

Every async function `F` exports `F + "Tu"`: an 8-byte
`%swift.async_func_pointer` in `__TEXT,__const`:

| offset | type | meaning |
|---|---|---|
| 0 | i32 | relative pointer to the function entry (`entry = &Tu + value`) |
| 4 | u32 | required async context size in bytes |

The compiler itself consumes Tu records dynamically (generic
reabstraction thunks load both fields at run time exactly as a binder
would), so parsing them is supported usage, not an implementation leak.
Observed sizes range 16 (leaf) to 96+; the record is the only
authoritative source — sizes change with function-body changes and must
never be baked into bindings.

### Async context and register contract

Calling convention `swifttailcc`; the context parameter carries the
LLVM `swiftasync` attribute, which pins it to **x22**. The context is
task-allocated; its first two words are ABI, the rest is the callee's
private frame:

| context offset | meaning |
|---|---|
| 0 | parent (caller) context pointer |
| 8 | resume function pointer (the caller's continuation) |
| 16.. | callee frame storage |

Register roles at the three transfer points:

| transfer | registers |
|---|---|
| call | x22 = callee context (freshly `swift_task_alloc`ed, header installed by caller); ordinary arguments in x0.. per the standard Swift physical lowering; **indirect results are a leading ordinary pointer argument (x0), never x8** |
| resume (return) | callee loads `resume` from its own context and tail-branches with **x22 still = the callee's context**; direct results arrive as ordinary arguments (x0, x1, ...) of the resume function; for throwing functions the error box arrives in **x20** (`swiftself` position; null = success) with the result registers undefined on the error path |
| suspension | `swift_task_switch(ctx, resumeFn, executorLo, executorHi)` — a `swifttailcc` tail call; the callee hops to its own executor at entry when isolated (and nonisolated functions may hop to the generic executor); the *caller's continuation therefore runs on whatever executor the callee finished on* |

Frame discipline (all measured):

- The caller **pops its entire native stack frame before the tail
  branch** (`add sp; b target`). Every value live across an async call
  must live in the caller's *async context frame*, including
  indirect-result buffers — a stack-allocated result buffer is a
  use-after-free by construction.
- Async frames set **bit 60 of fp** (`orr x29, x29, #1<<60`) so
  unwinders/profilers can recognize the extended async frame chain.
- The continuation is entered with x22 = the *completed callee's*
  context; it recovers its own context via `ctx[0]` (parent), then
  `swift_task_dealloc(callee ctx)`. Allocation and deallocation are
  strictly LIFO per task.
- `bl` is only used for ordinary (non-async) calls made from within an
  async function (e.g. `swift_task_alloc`); every async-to-async
  transfer is `b`/`br`.

### Task origination from synchronous foreign code

`swift_task_create` (libswift_Concurrency, `swiftcc`, callable from
sync code) originates a task:

```
{task, initialContext} swift_task_create(
    u64 taskCreateFlags,      // bit 12 = enqueue on the global executor;
                              // bits 0..7 priority (0 = unspecified)
    ptr options,              // null for a plain detached task
    ptr futureResultTypeMetadata,
    ptr taskFunctionTu,       // a Tu record pointer — hand-authored works
    ptr closureContext)       // OWNERSHIP: runtime-consumed, see below
```

Verified contracts (each one violated first, then fixed, in the
harness):

- `taskFunctionTu` may be a **hand-authored** Tu record naming a
  foreign entry with a self-declared context size; the runtime
  allocates the initial context from it and invokes the entry with
  x22 = initial context and x20 = `closureContext`.
- `closureContext` is **runtime-owned**: non-null values select the
  `completeTaskWithClosure` completion functlet, which
  `swift_release`s the context when the task completes. Passing
  foreign non-heap state there crashes at completion; foreign state
  must travel out of band (or in a genuine Swift-heap box).
- The entry completes the task by tail-calling the resume from its
  *initial* context header with x22 = its own context and the error
  in x20 (null on success), matching the throwing-closure shape that
  `swift_task_create` always reabstracts to.
- The returned task handle is +1 to the creator; release only after
  the task reaches its terminal state (the completion functlet runs
  *after* any foreign resume observes the result).
- Runtime-private state (`AsyncContextPrefix`: entry point, closure
  context, indirect-result slot) lives at negative offsets of the
  initial context. It is written and read only by the runtime; a
  foreign entry must treat everything below its context pointer as
  opaque.

### clang mapping (usable for a C-hosted support layer)

`__attribute__((swiftasynccall))` = `swifttailcc`;
`__attribute__((swift_async_context))` = x22;
`__attribute__((swift_context))` = x20. Calls to `swiftasynccall`
functions in tail position from `swiftasynccall` functions are
guaranteed tail calls — the entire call plan is expressible in C
without hand-written assembly (the harness is plain C).

### ptrauth (arm64e) schema

Measured from arm64e codegen of the same probes; constants match
Swift's `SpecialPointerAuthDiscriminators`:

| value | key | diversity | discriminator |
|---|---|---|---|
| resume function pointer in async context | IA | address + constant | 0xD707 |
| parent context pointer in async context | DA | address + constant | 0xBDA2 |
| spilled async context (saved x22 in the extended frame slot) | DB | address + constant | 0xC31A |
| return addresses | IB | sp | — |

Corresponding entries live in
[ptrauth-discriminators.json](ptrauth-discriminators.json). On arm64
all four are no-ops; arm64e *execution* remains hardware-gated, but any
future x22 codegen must emit `pacia`/`autda`-shaped sequences per this
table, and a binder storing a foreign resume pointer into a context
must sign it (IA, address-diversified, 0xD707) or the callee's `braa`
faults.

## Candidate design (non-shipping)

### `CallConvSwiftAsync` semantics

A candidate unmanaged calling-convention modifier, applicable only
alongside `CallConvSwift`, only on ARM64 Apple targets, and only for
runtimes in this repository:

1. The P/Invoke signature returns `void`; results are delivered to a
   managed-provided resume stub (the managed side always supplies the
   context header, so the "return" is a call into managed code).
2. Exactly one parameter carries the `SwiftAsyncContext` marker; the
   JIT pins it to x22.
3. Calls are mandatory tail transfers: the JIT must guarantee the
   native frame of the transition stub is gone before the branch, or
   route the call through a one-instruction shim (`mov x22, arg; br`)
   — prototyping which of the two is viable is the "X22 register
   support"/"tail-call and stack-pop" work item.
4. Reverse direction (managed continuations resumed by Swift) enters
   through `UnmanagedCallersOnly` stubs whose first action is to
   re-establish the managed context from `ctx[0]`; the stub itself is
   an ordinary managed method — Swift's tail-branch INTO it imposes no
   constraint on its own frame.

### `SwiftAsyncContext` marker

`System.Runtime.InteropServices.Swift.SwiftAsyncContext` mirroring
`SwiftSelf`/`SwiftError`: a struct wrapping `void*`, recognized
positionally anywhere in the signature, lowered to x22. Internal (not
public API) until an API review explicitly approves an ARM64,
in-scope-runtime-only contract; the capability handshake already
reserves the `SwiftInterop.AsyncDirect1` profile name and fails closed
on it.

### Prohibited combinations

- `MethodImplOptions.Async` (managed runtime-async) and any managed
  async method variant MUST NOT combine with `CallConvSwiftAsync` on
  the same method. The two continuation models (managed suspension
  state machines vs Swift task-allocated contexts) have no defined
  composition; the loader rejects the combination.
- Managed runtime-async continuation registers, spill conventions, and
  R2R fixup kinds are NOT reused for Swift async. x22 handling is a
  Swift-interop-only contract; sharing register-allocation plumbing
  with the managed async feature would couple two independently
  evolving ABIs (and x22 is not part of the managed async contract on
  ARM64 anyway).

### Managed `Task` integration (no private layout inspection)

The harness demonstrates the full integration shape without reading a
single private Swift structure: foreign code only ever dereferences
(a) the two public context-header words it wrote itself, (b) Tu
records, and (c) documented runtime entry points (`swift_task_create`,
`swift_task_alloc/dealloc`, `swift_task_switch`, error retain/release).
A managed integration therefore:

1. allocates its own context frames (their layout is entirely ours),
2. parses Tu records for entry + size,
3. bridges completion to `TaskCompletionSource` from the resume stub —
   the same terminal-state machine as the generated-thunk ABI v1, with
   the operation-handle layer replaced by context frames.

Ownership rules from the generated-thunk ABI carry over unchanged
(exactly-once terminal callback, results adopted via VWT, errors
consume the box).

## The slice-host contract (what JIT support actually requires)

Probe leg 7 ("slice host") proves the load-bearing fact for RyuJIT
support: an *ordinary* frame that `bl`s into an async entry with x22
set regains control through the normal return path at the callee's
first suspension (`swift_task_switch` enqueues the continuation and
returns), before the resume has run; the task continues independently
and resumes through the installed continuation later. Consequences:

- **No mandatory tail calls in managed code.** A managed caller acts
  as a slice host — exactly what `swift_job_run` callers do — with an
  ordinary prolog/epilog; its stack pops normally when the slice ends.
- The caller's frame must contribute nothing to async-world state: the
  child context and all cross-suspension values are task-allocated.
  (The x22 save/restore around the call keeps AAPCS for the caller's
  own callees.)
- What RyuJIT actually needs is therefore *only* x22 argument/parameter
  placement (forward: P/Invoke with a SwiftAsyncContext argument;
  reverse: UnmanagedCallersOnly stub receiving the context), plus
  treating x22 as call-clobbered around such calls despite being
  callee-saved in AAPCS.

## x22 support: implemented (prototype)

The candidate register contract is now implemented and test-verified in
this branch (non-shipping, per the marker policy):

- **Corelib**: `SwiftAsyncContext` marker struct
  (`System.Runtime.InteropServices.Swift`, `[Intrinsic]`, documented
  non-shipping).
- **RyuJIT** (all compilation modes — JIT, R2R via crossgen2, NativeAOT
  via ILC): forward, a `SwiftAsyncContext` argument of a
  `CallConvSwift` P/Invoke is pinned to x22
  (`WellKnownArg::SwiftAsyncContext`, `GetCustomRegister`, ABI
  classifier); reverse, a `CallConvSwift` `UnmanagedCallersOnly` method
  receives x22 as a parameter. x22 is treated as killed by calls that
  pass it (a Swift async slice does not preserve it), while normal
  callee-saved discipline is kept for the managed caller's own frame.
- **CoreCLR interpreter**: the Swift call-stub generator excludes the
  marker from the rewritten signature and routes it with
  `Load_SwiftAsyncContext`/`Store_SwiftAsyncContext` routines at the
  argument's original position. The reverse routine reads the original
  x22 from the transition block (`[fp, #40]`) because the interpreter
  entry stub uses x22 as scratch across its thread lookup — the same
  pattern as `Store_SwiftSelf` with x20.

The `SwiftAsyncDirect` suite's `ManagedSliceHostForwardAndReverseX22`
leg proves both directions with no C resume functlet in the path: a
managed planner (the slice host, an ordinary JIT-compiled frame)
allocates the child context, installs a managed `CallConvSwift` UCO as
the resume, and calls the Swift async entry directly; Swift later
tail-branches into the managed stub with x22 = the completed context.
Verified codegen: `stp x22, x1, [x0]` (header install),
`mov x22, x21` + `blr` (the call). The suite passes in all four
execution modes, and the pre-existing Swift suites plus the 37 ILC unit
tests show no regression.

## Managed prototype (SwiftAsyncDirect)

`src/tests/Interop/Swift/SwiftAsyncDirect` executes the integration
design above: `libSwiftAsyncDirectShim.c` (the stand-in for JIT x22
support — its contents are exactly what generated code would do)
performs direct calls through the fixture's Tu records, and managed
code completes `TaskCompletionSource`s from `UnmanagedCallersOnly`
resume stubs. Eight tests cover scalar/suspending/throwing-both-ways/
multi-register/indirect legs, cooperative cancellation
(`swift_task_cancel` from managed, mapped to `TaskCanceledException`
via the caller's cancel-requested flag), 16-way concurrent operation
pairing, and exactly-once terminal accounting with GCHandle release in
the terminal stub. The suite passes under CoreCLR JIT, the CoreCLR
interpreter, R2R, and NativeAOT.

Mode findings (the fallback story):

- The shim representation is *mode-agnostic by construction*: managed
  code sees ordinary P/Invokes and UCO reverse calls, so R2R needs no
  new fixup kinds, NativeAOT needs no new ILC support (the shim's
  resume functlets are literally pre-generated continuation thunks,
  and the UCO stubs are AOT-rooted), and the interpreter path is the
  existing P/Invoke + reverse-entry machinery. This is the universal
  fallback every compilation mode retains if JIT-inlined x22 codegen
  ships for some modes only.
- Operation state travels through a self-contained descriptor stack in
  the shim because `swift_task_create`'s context argument is
  runtime-owned; any (descriptor, task-entry) pairing is valid, which
  the 16-way concurrency test exercises.

## Runtime-async scheduling and diagnostics: reuse evaluation

Evaluated against the managed runtime-async feature
(`MethodImplOptions.Async` state machines):

- **Continuation dispatch: no reuse.** Managed runtime-async
  continuations are managed frames resumed by the scheduler; Swift
  continuations are task-allocated contexts resumed by executors via
  tail transfer. There is no shared representation to exploit, and the
  prohibited-combinations section above forbids pretending otherwise.
  The boundary object remains a completed-callback (`TaskCompletionSource`
  with `RunContinuationsAsynchronously`), identical to the generated
  thunk ABI — meaning *the two models already compose at the Task
  level* and nothing register-level is gained by deeper coupling.
- **Scheduling: reuse the Task layer as-is.** Swift resumes arrive on
  Swift executor threads; the TCS hop moves managed continuations onto
  the thread pool, which both preserves the never-block-a-Swift-executor
  policy and picks up managed async diagnostics for free.
- **Diagnostics: partial reuse.** Task-level tracing (TplEventSource,
  Activity flow) works unchanged because the integration surface IS a
  Task. Native-side visibility comes from the async frame-pointer bit
  (bit 60) that Apple's unwinders already understand. Cross-boundary
  *stitched* async stacks (one logical stack across managed and Swift
  frames) would require the debugger/EventPipe to walk Swift async
  contexts — out of scope; recorded as a diagnostics gap in both
  models (generated thunks share it).

## Comparison against generated thunks (exit gate)

`src/tests/Interop/Swift/SwiftAsyncBench` runs identical async targets
(leaf add; one-yield add) through three models: the generated
async-thunk ABI v1 shape (operation handle, `Task {}` wrapper, C
completion), the direct call via the C shim, and the direct call via
managed x22 slice hosting. Timing assertions are never made — the
suite asserts correctness and prints measurements.

Performance (osx-arm64 M-series, Checked runtime, settled runs; all
paths share the same managed completion plumbing):

| shape | thunk | direct (shim) | direct (managed x22) |
|---|---|---|---|
| leaf, no suspension | ~6.6 µs/op | ~5.4 µs/op (~19% faster) | ~5.4 µs/op (~19% faster) |
| one suspension | ~7.7 µs/op | ~8.6 µs/op (~12% slower) | ~8.4 µs/op (~10% slower) |
| managed allocation | ~121 B/op | ~121 B/op | ~121 B/op |

Cold runs vary by 2x; every path is dominated by task creation and
executor scheduling, not by the bridging model. The stable signal:
direct wins the no-suspension path (no `Operation` allocation, no
closure box — the task *is* the call) and loses the suspension path by
roughly the cost of its extra managed/native transitions per
completion.

Size (per-API marginal native bytes, measured from fixture symbols):

| model | per-API native code | fixed shared native | per-API toolchain/asset |
|---|---|---|---|
| generated thunk | ~1.0 KB (entry + closures + operation machinery) | — | Swift compilation, per-target dylib asset, re-signing/notarization |
| direct | 0 bytes | ~51 KB shim dylib (or in-runtime support; no shipped asset) | none (managed metadata only) |

## Decision (exit gate): no-go as shipping default; capability retained

The gate criterion was "continue only if it materially reduces
generated code or improves performance without weakening
compatibility". Findings:

- **Performance: a wash.** Mixed by shape (±10-20%) at µs scale,
  dominated by task machinery both models share. Not material.
- **Generated code: materially reduced, but only for the async
  share.** Per-API native thunks and their Swift-compilation/asset
  pipeline disappear; the binding dylib itself remains for the
  non-async surface, so the asset shrinks rather than disappears.
- **Compatibility: widened surface.** The direct model makes this
  runtime re-implement swiftc's caller-side async lowering and track
  Swift concurrency-runtime evolution (task creation flags, context
  prefix behavior), and its arm64e story is codegen-verified but not
  hardware-verified.

**Decision: the generated async-thunk ABI v1 remains the shipping
vehicle for Phases 8/9. Direct Swift async is retained as a complete,
tested, non-shipping capability behind the reserved
`SwiftInterop.AsyncDirect1` profile (handshake fails closed; the
`SwiftAsyncContext` marker carries `[Experimental("SYSLIB5100")]`).**
Reopen conditions, any of: arm64e hardware validation lands; a
workload where the no-suspension latency edge or full asset
elimination is decisive; API review approves the marker types for a
public contract.

## Evidence## Evidence

- `swift-abi-probe/sources/async-direct/AsyncCallPlans.swift` +
  `build/async-direct/AsyncCallPlans.{ll,s,arm64e.s}` — call plans,
  Tu records, resume conventions, hops, ptrauth sequences.
- `swift-abi-probe/sources/async-direct/AsyncDirectFixture.swift` +
  `direct_async_harness.c` — the executing proof (six legs: leaf,
  suspending, throwing failure/success, multi-register result,
  indirect result in the caller's async frame).
