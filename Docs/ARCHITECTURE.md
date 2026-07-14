# Runtime Architecture

Implementation updated: 2026-07-15; GPU-only owner Play Mode validation pending.

The runtime keeps neural simulation state separate from rendered transforms. Render interpolation
never writes presentation state back into the autoregressive model.

```text
BasketballMatchController
  -> shared BasketballRuntimeSettings
  -> team roster, Tab switching, pass/steal intent and possession arbitration
  -> one BasketballIntent per player

BasketballKeyboardMouseInputProvider
  -> BasketballNeuralController.Control
  -> BasketballAgentState (61 samples, independent per player)
  -> BasketballFeatureBuilder (864 floats per player)
  -> BasketballSentisBatchScheduler
       -> one official GPUCompute Worker
       -> [3,864] ONNX input
       -> asynchronous [3,596] readback
       -> 588 model outputs + 8 HUD gating values per player
  -> BasketballOutputDecoder
  -> previous/current BasketballPoseBuffer
  -> BasketballPoseApplicator (render Lerp/Slerp and contact IK)
  -> BasketballSkeleton + authoritative BasketballBallController

Rendered player Transform
  -> ThirdPersonOrbitCamera
  -> camera-relative WASD interpretation only
```

## GPU-only inference ownership

- `BasketballModelContract` defines the immutable dimensions: 864 inputs, 588 model outputs,
  eight experts and 596 packed GPU outputs.
- `BasketballMoEBatch3.onnx` is the only deployed model/weight asset. The old 58-buffer serialized
  CPU asset and Reference/Burst evaluators are not part of the runtime.
- `BasketballSentisBatchScheduler` owns the neural clock and the only `Worker` and input tensor.
  Exactly one recurrent batch may be in flight. All three output rows are committed together.
- Every `BasketballNeuralController` owns its recurrent state, preallocated feature/output data,
  pose history, contacts and phase. Players never share recurrent state.
- The scheduler uses `BackendType.GPUCompute` only. Initialization, schedule or readback failure
  changes the HUD to `SENTIS GPU ERROR` and stops neural simulation; there is no CPU fallback.
- The Worker and tensor are disposed from disable, destroy and failure paths. Readback clones are
  scoped with `using`; `PeekOutput` remains Worker-owned according to the package contract.

Unity Inference Engine may bring Burst/Collections as package internals. The project runtime
assembly does not reference those APIs and contains no project-owned CPU MoE implementation.

## Timing and interpolation

`BasketballRuntimeSettings` supplies the shared neural rate, interpolation mode, catch-up limit,
camera tuning, frame pacing, HUD sampling and shadow budget. 30 Hz is the canonical Basketball
2020 rate; 20/15/10 Hz remain experimental. Changing the scheduler rate does not alter feature
dimensions, Feed/Read order, normalization or fixed-step model formulas.

The scheduler accumulates render time, prepares the next batch only when no GPU readback is
pending, and commits the completed state before beginning another tick. Previous/current pose
buffers remain neural-tick snapshots. Render frames apply position Lerp and quaternion Slerp;
interpolation never becomes simulation input.

## Match and ball ownership

- `BasketballMatchController` routes human/team commands and does not own possession truth.
- `BasketballPossessionManager` is the single owner of possession version, owner,
  pass/shot/loose/contested/catch states and touch-before-secure arbitration.
- Only the owner may apply a predicted controlled ball pose. Released/free flight is Rigidbody
  authoritative and receives no in-flight homing correction.
- `BasketballTeamMember` stores stable identity/team data and player references.
- `ThirdPersonOrbitCamera` and UI Toolkit are presentation systems. Camera heading enters control
  only while interpreting keyboard movement.
- Future route-planning AI should produce `BasketballIntent`; it must not write recurrent state,
  player transforms or ball Rigidbody state directly.

## Allocation and lifetime policy

Feature building, output decode, pose capture and pose application use preallocated arrays. The
runtime contains no per-tick LINQ or Tasks. `BasketballPerformanceMonitor` stops and disposes all
`ProfilerRecorder` instances on disable/dispose. UI Toolkit callbacks are removed when unbound.
A long owner-run Profiler capture is still required before claiming complete-frame 0 B GC.

## Profiler markers

- `Basketball.Control`
- `Basketball.BuildFeatures`
- `Basketball.Inference`
- `Basketball.Inference.Sentis.Schedule`
- `Basketball.Inference.Sentis.Readback`
- `Basketball.Decode`
- `Basketball.Ball`
- `Basketball.Interpolate`
- `Basketball.ApplyPose`
- `Basketball.Contact`
- `Basketball.IK`
- `Basketball.Camera`
- `Basketball.Camera.Input`
- `Basketball.Camera.Collision`

HUD `GPU INFER RTT` measures wall-clock schedule-to-readback latency. The `INFERENCE` profiler
sample measures CPU dispatch/readback work and is not GPU execution duration.
