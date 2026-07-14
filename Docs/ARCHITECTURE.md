# Runtime Architecture

Implementation updated: 2026-07-15; Play Mode validation pending

The current implementation is a reference-first vertical slice. Simulation state is separate
from rendered transforms so interpolation cannot feed presentation state back into the model.

```text
BasketballMatchController
  -> one shared BasketballRuntimeSettings profile
  -> Team roster (P1/P2 Team A, P3 Team B)
  -> active player / Tab switching / camera target
  -> teammate-only pass lock and pass-command latch
  -> atomic shared-ball possession and opponent steal arbitration
  -> per-player BasketballIntent override

BasketballKeyboardMouseInputProvider
  -> BasketballIntent
  -> BasketballNeuralController.Control             [10-30 Hz; 30 Hz reference]
  -> BasketballAgentState (61 samples, per-agent recurrent state)
  -> BasketballFeatureBuilder (864 floats)
  -> IBasketballInferenceBackend
       -> BasketballReferenceBackend (default correctness oracle)
       -> BasketballBurstBackend (synchronous Burst CPU, shared native model data)
       -> local fallback while Sentis is unavailable/warming
  -> BasketballSentisBatchScheduler (optional shared configurable clock)
       -> three prepared [864] rows
       -> official Sentis GPUCompute worker [3,864] -> [3,596]
       -> asynchronous GPU readback
       -> three independent 588-output state commits + 8 gating values each
  -> BasketballOutputDecoder (588 floats)
  -> previous/current BasketballPoseBuffer
  -> BasketballPoseApplicator (render Lerp/Slerp)
  -> BasketballSkeleton + BasketballBallController

Rendered Player Transform
  -> ThirdPersonOrbitCamera                        [render frame only]
  -> Camera-relative interpretation of WASD       [control input only]
```

## Ownership

- `BasketballModelAsset` owns the imported immutable model reference and validates all 58
  buffers. The reference backend reads the asset's shared managed arrays directly. Burst agents
  acquire one reference-counted persistent `NativeArray` copy per model asset through
  `BasketballBurstModelCache`; per-agent input, output, blended-weight and hidden-layer scratch
  buffers remain independent and preallocated.
- `BasketballAgentState` owns one agent's root trajectory, pose/velocity, dribble, ball,
  style, contact, and phase history. It is never shared between agents.
- `BasketballNeuralController` owns the fixed neural clock on Reference/Burst. When all three
  demo rigs select `SentisGpuBatch`, `BasketballSentisBatchScheduler` temporarily owns one shared
  profile-driven clock; each controller still owns its state, feature/output buffers and decode
  path. The 30 Hz setting is canonical; lower settings are experimental scheduling only.
- Every player owns an independent `BasketballAgentState`, backend work buffers, pose history,
  contacts, phase, and recurrent trajectory. All players consume the same runtime profile and
  therefore share one configured neural rate while retaining independent state.
- `BasketballRuntimeSettings` is the match-level source for neural rate, presentation
  interpolation, catch-up limit, inference backend, camera tuning, frame pacing, HUD sampling
  and the realtime-shadow budget. It replaces duplicated runtime fields on individual rigs.
- `BasketballTeamMember` stores stable player/team identity and references to that player's
  controller, input adapter, debug view, and target indicator.
- `BasketballMatchController` routes human/team commands, filters pass targets, latches pass/fake
  actions, and switches the active human. It does not own possession truth.
- `BasketballPossessionManager` is the single possession and ball-write authority. It owns Owner,
  passer/receiver, possession version, pass/shot/loose/contested/catch states, release, catch,
  interception and touch-before-secure arbitration.
- `BasketballPoseApplicator` is presentation-only. It applies interpolated poses but never
  writes rendered transforms back into `BasketballAgentState`.
- `ThirdPersonOrbitCamera` is also presentation-only. It reads the rendered player transform,
  resolves camera collision, and never writes camera orientation into recurrent neural state.
- Camera yaw becomes a control heading only while keyboard movement is non-zero; the controller
  converts heading error into the pretrained model's existing Turn/trajectory channels.
- `BasketballKeyboardMouseInputProvider` is the primary human input adapter. Holding right mouse
  reserves mouse delta for ball control; otherwise the same delta belongs to the orbit camera.
- Future route-planning AI should be another `BasketballIntent` producer. The controller's intent
  seam avoids coupling planning, human input, and recurrent model state.
- `BasketballBallController` explicitly distinguishes `Controlled`, `Held`, `Released`,
  `FreePhysics`, and `Reacquiring`. Only the carrier's pose applicator may write the shared
  controlled ball; released/free states remain Rigidbody-authoritative.

## Reconstructed team interactions

The upstream 2020 source archive contains the single-player neural control path but no reusable
multiplayer pass/steal coordinator. The remake keeps the original 864/588 contract and drives a
pass preparation through existing Future Root facing and Hold style only. It deliberately leaves
Ball Target, Pivot, Momentum and BallHeight untouched. After the one computed release impulse,
Rigidbody flight has no homing or per-frame correction. Steal is a separate runtime intent, not
a fabricated model label. A non-owner can receive a reach-clamped model-only proxy ball for pose
context, while real swept hand-ball contact creates `Loose` or `Contested`; a later arbitration
step alone can secure possession.

## Allocation policy

Inference, feature construction, output decode, pose capture, and pose application use
preallocated arrays. The current code contains no LINQ or per-tick Tasks. A Profiler capture is
still required before claiming zero GC allocation for the complete frame.

## Inference execution

`BasketballRuntimeSettings.InferenceBackend` chooses the execution path before agent
initialization. Reference
remains the correctness oracle. Burst and Reference implement the same per-agent
`IBasketballInferenceBackend` contract and expose the most recent eight gating weights. The
current shared profile explicitly selects `SentisGpuBatch`.

The Burst implementation uses strict/high-precision jobs in three ordered stages: normalization
and Gating; three parallel expert-weight blends plus a small bias blend; then the dense network
and output denormalization. `Evaluate` waits for those stages before decode, so this does not
change the 30 Hz closed loop or add one-frame latency. It does not yet run complete agents in
parallel or stagger their ticks.

The Sentis path is a project-owned conversion of the same math, not a replacement model. Offline
Python authoring reads the complete legacy YAML asset and emits a fixed batch-three ONNX graph;
Python is never part of the Unity runtime. One worker schedules the three normalized MoE rows on
`BackendType.GPUCompute`. Its packed output combines the original 588 values with eight duplicate
gating values used only by the HUD, reducing GPU-to-CPU transfer to one readback. No second tick
is prepared until readback completes, so autoregressive state ordering remains intact.

Worker warm-up runs while the controllers continue on Burst. After warm-up, the scheduler splits
each neural tick into prepare, batch schedule, readback poll, and commit. Render interpolation
uses scheduler time but never writes presentation state into simulation. Initialization or
schedule failure detaches all three controllers together and retains their Burst backends; mixed
GPU/CPU ownership is not allowed.

Previous/current pose buffers always stay at neural-tick boundaries. Render frames use position
`Lerp` and quaternion `Slerp`; the profile can shape alpha linearly or with SmoothStep. Disabling
interpolation snaps presentation to current state. Changing neural rate never changes feature
dimensions, Feed/Read order, normalization or the model's trained fixed-step formulas.

## Profiler markers present

- `Basketball.Control`
- `Basketball.BuildFeatures`
- `Basketball.Inference`
- `Basketball.Decode`
- `Basketball.Ball`
- `Basketball.Interpolate`
- `Basketball.ApplyPose`
- `Basketball.Contact`
- `Basketball.IK`
- `Basketball.Camera`
- `Basketball.Camera.Input`
- `Basketball.Camera.Collision`

The Burst path also emits nested `Basketball.Inference.Burst`, `.Gating`, `.Blend`, and `.Dense`
samples beneath the common `Basketball.Inference` marker.

The Sentis path adds `Basketball.Inference.Sentis.Schedule` and
`Basketball.Inference.Sentis.Readback`. HUD `GPU INFER RTT` measures wall-clock schedule-to-readback
latency; it is distinct from the CPU duration of `Basketball.Inference`.
