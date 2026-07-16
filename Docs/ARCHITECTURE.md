# Runtime Architecture

Implementation updated: 2026-07-15; GPU-only owner Play Mode validation pending.

The runtime keeps neural simulation state separate from rendered transforms. Render interpolation
never writes presentation state back into the autoregressive model.

```text
BasketballMatchController
  -> shared BasketballRuntimeSettings
  -> 1v1 / 3v3 / 5v5 active mask over ten fixed GPU slots
  -> human / mixed / full-rule-AI control selection
  -> team roster, Tab switching, pass/steal intent and possession arbitration
  -> BasketballRuleBasedTeamAI
       -> spacing, matchup, pass, shot, steal, catch and loose-ball decisions
       -> BasketballIntent and legal BasketballPossessionManager requests only
  -> one BasketballIntent per player

BasketballCourt
  -> team-aware +Z / -Z BasketballHoop targets
  -> FIBA two/three-point geometry
  -> BasketballShotPlanner one-time ballistic release
  -> BasketballScoreTracker downward plane crossing
  -> post-score possession restart through BasketballPossessionManager

BasketballKeyboardMouseInputProvider
  -> BasketballNeuralController.Control
  -> BasketballAgentState (61 samples, independent per player)
  -> BasketballFeatureBuilder (864 floats per player)
  -> BasketballSentisBatchScheduler
       -> one official GPUCompute Worker
       -> [10,864] ONNX input
       -> asynchronous [10,596] readback
       -> 588 model outputs + 8 HUD gating values per player
  -> BasketballOutputDecoder
  -> previous/current BasketballPoseBuffer
  -> BasketballPoseApplicator (render Lerp/Slerp and contact IK)
  -> BasketballSkeleton + authoritative BasketballBallController

Rendered player Transform
  -> presentation-only target smoothing
  -> ThirdPersonOrbitCamera
  -> camera-relative WASD interpretation only

Canonical interpolated pose
  -> BasketballHumanoidVisualRetargeter
       -> position-driven limb swing + arm-plane roll + neural hand rotation
       -> Humanoid visual skeleton only
  -> BasketballHumanoidHandContactSolver
       -> neural left/right hand Contact weights
       -> palm anchor to physical ball surface
       -> presentation-only two-bone arm correction + relaxed/contact finger curl
  -> BasketballPlayerAppearance
       -> shared team profiles + MaterialPropertyBlock colors
       -> centered skinned jersey-number patches
```

## GPU-only inference ownership

- `BasketballModelContract` defines the immutable dimensions: 864 inputs, 588 model outputs,
  eight experts and 596 packed GPU outputs.
- `BasketballMoEBatch10.onnx` is the deployed 5v5 model/weight asset. It preserves the same shared
  MoE weights and operators while changing only the fixed batch dimension and three blend-weight
  reshape tensors from 3 to 10. The old 58-buffer serialized
  CPU asset and Reference/Burst evaluators are not part of the runtime.
- `BasketballSentisBatchScheduler` owns the neural clock and the only `Worker` and input tensor.
  Exactly one recurrent batch may be in flight. All ten output rows are committed together.
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

The scheduler accumulates simulation time, prepares the next batch only when no GPU readback is
pending, and commits the completed state before beginning another tick. A separate presentation
clock resets to zero whenever a new previous/current pair is committed; GPU backlog therefore
cannot force the new pose to alpha one on the same frame. Previous/current pose buffers remain
neural-tick snapshots. Render frames apply position Lerp and quaternion Slerp; interpolation never
becomes simulation input.

## Match and ball ownership

- `BasketballMatchController` routes human/team commands and does not own possession truth.
- `IBasketballDecisionPolicy` is the replaceable high-level boundary. Policies consume a
  fixed-capacity `BasketballWorldObservation` plus per-player snapshots and return
  `BasketballPlayerCommand` values. Match validates transactional skill requests before they
  reach possession.
- `BasketballMatchController.decisionPolicyBehaviour` accepts any `MonoBehaviour` implementing
  that interface and falls back to `BasketballRuleBasedTeamAI` when none is assigned.
- `BasketballRuleBasedTeamAI` is the first deterministic policy implementation. It never writes
  pose, recurrent state, player transforms, Owner, or ball physics. It consumes world-event
  outcomes to invalidate stale tactical/action caches after possession and skill transitions.
- `BasketballWorldEventStream` is a bounded, allocation-free-after-initialization ring buffer for
  possession and skill outcomes. It is the current capture seam for Debug and future RL/surrogate
  export. Policies may additionally implement `IBasketballWorldEventObserver`; Match then delivers
  each retained event once, in sequence order, before the next decision frame. A lagging consumer
  resumes from the oldest still-retained event instead of replaying stale data.
- `BasketballPlayerObservation.ActionMask` exposes mode- and possession-aware legal high-level
  actions without changing the 864-float neural model input. Inactive slots always expose `None`.
- `BasketballSkillTelemetryRecorder` is an optional, default-off JSONL sink for world events. File
  I/O is isolated from control and can be disabled for performance acceptance runs. Schema v2
  includes the actual event pose/velocity plus a skill target position and variant, so pass type,
  predicted catch point and shot target remain available to offline surrogate tooling.
- `BasketballPossessionManager` is the single owner of possession version, owner,
  pass/shot/loose/contested/catch states and touch-before-secure arbitration.
- Only the owner may apply a predicted controlled ball pose. Released/free flight is Rigidbody
  authoritative and receives no in-flight homing correction.
- `BasketballCourt` maps Team 0 to +Z and Team 1 to -Z. Shot release solves one projectile
  velocity to that hoop; scoring requires a downward center-plane crossing and uses the stored
  release position for two/three-point classification.
- `BasketballTeamMember` stores stable player index, team ID, jersey number and player references.
- Match modes preserve the deployed `[10,864]` graph. Inactive slots are deactivated gameplay
  objects and excluded from Tab, pass, Rival, catch, steal and loose-ball candidate loops.
- `BasketballHumanoidVisualRetargeter` is presentation-only. It restores the canonical mannequin
  whenever the optional Humanoid visual cannot be validated, so a broken skin cannot hide the
  neural reference rig.
- `ThirdPersonOrbitCamera` and UI Toolkit are presentation systems. Camera heading enters control
  only while interpreting keyboard movement. Its pivot is smoothed independently from the neural
  clock, and collision ignores dynamic player/ball colliders.
- Future route-planning or RL AI should preserve the current `BasketballIntent` and possession
  request boundary; it must not write recurrent state, player transforms or ball Rigidbody state.

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
