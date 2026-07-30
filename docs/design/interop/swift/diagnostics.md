# Mixed-stack diagnostics for Swift interop

**Status (2026-07-13):** the recorded story for debugging and crash
reporting across managed/Swift frames, written against what is
verified on this branch; device-side symbolication rides the device
lanes.

## Stack walking and crash shapes

- **Synchronous CallConvSwift frames** walk normally: Swift functions
  use standard AAPCS64 frames, so lldb, `createdump`, and Apple crash
  logs interleave managed and Swift frames correctly. Managed frames
  symbolicate through the usual .NET mechanisms (createdump/dotnet-dump
  for CoreCLR; dSYMs for NativeAOT); Swift frames through their dylib
  symbols and `swift-demangle`.
- **Swift async frames** set fp bit 60 (abi-model.md); Apple's
  unwinders and Instruments understand the marker, but naive
  fp-chain walkers must mask bit 60 or misread the chain. Resumes run
  on executor threads whose stacks bottom out in `swift_job_run` — a
  crash inside a resume shows a SHORT native stack plus the async
  context chain, not the logical await chain (typically a flat
  one-frame backtrace, e.g. at `_swift_release_dealloc`).
- **Stitched logical async stacks** (managed await chain + Swift
  continuation chain as one trace) exist in NEITHER model (recorded in
  async-direct.md); this is a shared diagnosability gap with generated
  thunks, tracked as future EventPipe/debugger work.

## Fail-closed presentations (what operators will actually see)

- **Capability handshake refusals** (SWIFT0001-0006) throw managed
  `NotSupportedException`-family errors before any native call — they
  present as ordinary managed exceptions with the SWIFT code in the
  message, never as native crashes.
- **Signature validation** (duplicate/byref/combination markers)
  presents as `InvalidProgramException` under JIT/interpreter and as a
  BUILD failure under ILC.
- **The ptrauth fail-closed paths** are compile-time (`#error`) for the
  helper/generated thunks on arm64e; the runtime-side discriminator
  table refusal is a managed exception (handshake). None of the
  fail-closed paths abort the process.
- **Real native crashes** in the Swift runtime (the classes this work
  hit: `swift_release` of a non-object, `braa` auth failure shapes on
  arm64e, resume-convention mismatches) produce EXC_BAD_ACCESS /
  SIGBUS Apple crash logs. Triage recipe: symbolicate the Swift side
  (`atos`/`swift-demangle`), read the async-context registers per
  abi-model.md (x22 chain, x20 error/self), and check the
  crashed-thread name — Swift executor threads are named
  (`Task N`, cooperative queues), which immediately distinguishes
  resume-path crashes from call-path crashes.

## Debugger workflow

- lldb attaches to `corerun` and breaks in Swift dylibs, runtime
  helpers (`Load_/Store_Swift*` routines), and JITted code
  (`DOTNET_JitDisasm` for codegen inspection; SOS for managed state).
- Register-level triage: `register read x20 x21 x22` at Swift
  boundaries, `image lookup -a` for symbolication, and watch the
  interpreter's transition-block save area (`[fp,#16..]` in
  `InterpreterStub`) when special registers look clobbered.
- For interpreter issues, `Load_/Store_` routine breakpoints show
  exactly which stub routine ran and in what order.

## Crash dumps

- CoreCLR: `createdump` fires on managed crashes as usual; frames in
  CallConvSwift P/Invokes appear as native frames with their dylib
  symbols. `DOTNET_DbgEnableMiniDump=1` works unchanged.
- NativeAOT: standard Apple crash logs plus dSYMs; the Swift interop
  frames need no special handling.
- Known gap: a dump taken while a thread executes a Swift async
  RESUME shows only the physical stack; recovering the logical chain
  requires walking async contexts from x22 manually (abi-model.md
  documents the layout). A dump-analyzer helper for this is future
  work, tracked with the stitched-stacks gap.

## SWIFT0007 — host does not service the main dispatch queue

**Raised by:** the support-layer guard emitted ahead of any
`@MainActor`-isolated binding.

**Means:** the process does not drain its main dispatch queue, so a
`@MainActor` Swift call would never complete — not slowly, never, with no
error (abi-model.md). The guard converts that permanent hang into this
catchable error, naming the declaration.

**Fix:** run a main run loop. An app host (iOS/tvOS/Catalyst/macOS
bundle) already does. A console-style host must service the main run loop
(`CFRunLoopRunInMode` on the main thread) while the call is outstanding.

**Do not** "fix" this by removing the guard: without it the call hangs
silently, which in a purchase flow is the worst available outcome.
