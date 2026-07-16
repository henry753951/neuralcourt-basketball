# Player AI Integration Analysis

Last reviewed: 2026-07-15

This document records the current ten-player 5v5 reference architecture and the safe integration
boundary for future player, team, navigation, and network AI. The scene now contains a minimal
match coordinator, variable 1v1/3v3/5v5 active rosters, one shared ball, human player switching,
teammate-only passing, contact-validated opponent steals, team-aware shooting and a first
rule-based tactical baseline. It does not yet contain NavMesh path planning or polished tactics.

## Current runtime path

```text
Keyboard / mouse + Tab player selection
  -> BasketballMatchController
       -> active mask + human / mixed / Full Rule AI selection
       -> BasketballRuleBasedTeamAI for non-human active players
       -> team/pass/shot/possession rules
       -> one BasketballIntent override per player
  -> BasketballKeyboardMouseInputProvider
  -> BasketballIntent
  -> BasketballNeuralController.ApplyControl               shared neural tick
  -> BasketballAgentState recurrent series
  -> BasketballFeatureBuilder                              10 x 864 floats
  -> BasketballSentisBatchScheduler                        GPUCompute Batch10 MoE
  -> BasketballOutputDecoder                               588 floats
  -> previous/current BasketballPoseBuffer
  -> BasketballPoseApplicator + contact IK                 render frame
  -> canonical 26-bone rig

10 x BasketballAgentState <-> one BasketballBallController <-> Rigidbody
Rendered player pose  -> ThirdPersonOrbitCamera
BasketballAgentState  -> debug visualizer and UI Toolkit HUD
```

The camera and UI are presentation systems. Camera heading is converted into a control heading
only while interpreting keyboard movement; neither system writes directly to recurrent neural
state.

Every neural tick still follows the closed loop:

1. Shift the control history and apply the current intent.
2. Build the exact 864-value input in legacy channel order.
3. Evaluate the original normalized MoE model.
4. Decode all 588 outputs into root, future trajectory, posture, ball, contact, and phase state.
5. Capture the new simulation pose. Render frames interpolate without feeding transforms back
   into simulation.

The 30 Hz neural step remains the reference model rate and must be independent from the future
AI decision rate. Lower profile values are experimental and only alter scheduler cadence and
render interpolation; they do not retime the model equations.

## Current match and team layer

- P1-P5 use Team A; P6-P10 use Team B in 5v5. Match mode can activate 1, 3, 5 or
  custom counts per team while preserving ten GPU slots.
- `BasketballTeamMember` supplies stable player/team identity and per-agent references.
- `BasketballMatchController` selects the active human, routes independent intents, filters pass
  targets by team, and controls target indicators.
- `BasketballPossessionManager` is the single `IBasketballPossessionAuthority`. It owns the
  authoritative carrier, possession version, ball control mode and all acquire/release arbitration.
- Only the carrier applies the predicted ball pose to the one shared ball.
- A pass is a runtime state machine over the original Future Root facing and Hold style. It is
  not a new model label and never writes Ball Target/Pivot/Momentum. Release applies one computed
  projectile velocity and then uses pure Rigidbody flight.
- Steal is a separate runtime intent because Hold is not a steal label. A model-only proxy gives
  the defender pose context, but a real swept hand-ball touch must produce Loose/Contested first;
  possession can change only in a later secure arbitration.
- `BasketballRuleBasedTeamAI` selects world movement, facing, pass, shoot, catch and steal intents
  for AI-controlled active players. It uses the same low-level skills and authority APIs as the
  human path and cannot directly assign Owner or move the ball.

These are match rules around the pretrained model. Team ID, target selection and possession
authority are not added to the model's 864 inputs and do not change its 588 outputs.

## What replaced the original BasketballController responsibilities

| Original responsibility | Current implementation | Meaning for future AI |
| --- | --- | --- |
| Stand / Move / Sprint | `BasketballIntent.Move` and `Sprint`; `ApplyControl` derives stand from move magnitude | AI should request desired movement, not select animation clips |
| Turn | `BasketballIntent.Turn`, plus camera-relative heading conversion for keyboard control | AI needs an explicit world-facing adapter instead of camera dependence |
| Dribble | Derived as `Carrier && !Hold && !Shoot` | Dribble is currently a state-dependent default, not a separate command |
| Hold | `BasketballIntent.Hold`; a carrier stops movement and moves the ball toward the hold target | AI may hold this intent across neural ticks |
| Catch / reacquire | The same `Hold` intent while not carrier; proximity to either hand starts reacquisition | Catch must be requested before the hand/ball proximity condition occurs |
| Shoot | `BasketballIntent.Shoot`; valid only while carrier; release waits for ball height, speed change, and low hand contact | A one-frame AI pulse can be missed, so future actions need latching/acknowledgement |
| Steal | `BasketballIntent.Steal`; runtime-only contact intent, not a neural style | AI must approach so a real hand reaches the ball; Touch and Secure are separate |
| Spin | `BasketballIntent.Spin`; applied only while moving | Supported by the neural control path, but keyboard currently does not expose it directly |
| Horizontal ball control | `BasketballIntent.BallControl`, stored through pivot control | AI can target the legacy local ball-control disk after coordinate conversion |
| Ball height control | `BasketballIntent.BallHeight` and internal `BallHeightControl` | Supported by the control contract; keyboard currently does not expose it |
| Ball speed control | Internal `BallSpeedControl` derived from pivot height and vertical momentum | There is no direct AI speed field yet; do not add one without validating legacy behavior |
| Ball ownership/physics | `BasketballBallController` states: Controlled, Held, Released, FreePhysics, Reacquiring | Future match AI must request actions; it must not move the ball Rigidbody directly |

The current human input payload is:

```csharp
public struct BasketballIntent
{
    public Vector2 Move;
    public Vector2 BallControl;
    public float Turn;
    public float Spin;
    public float BallHeight;
    public bool Sprint;
    public bool Hold;
    public bool Shoot;
    public bool CatchReady;
    public bool Steal;
    public bool PassTargeting;
    public bool CommitBallRelease;
    public bool PassControl;
    public float PassHoldStyle;
    public float PassShootStyle;
    public bool UseWorldMove;
    public Vector3 WorldMove;
    public bool UseWorldFacing;
    public Vector3 WorldFacing;
    public bool IsGamepad;
}
```

The pass preparation fields reuse only the original Hold-style and root-facing controls. Pass
direction is never written into Ball Target, Pivot or Momentum. These fields add no model
inputs/outputs and do not alter the 864/588 contract.

For the current teammate receiver, route/facing preparation begins in `PassPreparing`. The
receiver sets `CatchReady`, which activates Hold style but deliberately leaves direct Hold and
ball targeting disabled. It enables direct Hold only during `PassFlight` when the real ball is
close or its expected arrival is imminent. This keeps inactive players stable while still giving
the 30 Hz model several ticks to form a catch pose. Catch arbitration uses the real ball's swept
physics path against hands and an intended-receiver-only live chest volume, preventing fast-ball
tunnelling; the physical ball must cross one of those regions.

An interception does not write ownership directly from the defender animation. Receiver catch
and defender interception enter the same candidate arbitration. Similar candidates produce a
Rigidbody-authoritative `Contested` state; a winner enters `CatchBlend`, while an unresolved
contest becomes `Loose` after its timeout. Future team AI should react to these states rather
than assuming every requested pass reaches its intended receiver.

`IsGamepad` is a compatibility flag, not a semantic AI property. It currently affects analog
ball-control normalization and movement interpretation. Future AI work should replace this
implicit branch with an explicit control-space/precision policy while preserving the existing
keyboard and gamepad behavior.

## Where the original series live now

The legacy series have not been removed from model behavior. They are currently consolidated
inside one per-agent `BasketballAgentState` so the first remake keeps Feed/Read order obvious.

| Legacy concept | Current per-agent state |
| --- | --- |
| `RootSeries` | `RootPositions`, `RootRotations`, `RootVelocities`, actor root pose |
| `DribbleSeries` | `Pivots`, `Momentums`, ball position/rotation/velocity history and control flags |
| `StyleSeries` | 61 samples x 5 `Styles` channels |
| `ContactSeries` | 61 samples x 5 `Contacts` channels |
| `PhaseSeries` | 61 samples x 5 `Phases` and `Amplitudes` channels |
| Actor / posture state | 26 bone positions, rotations, and velocities |

These arrays are recurrent model state. A planner, behavior tree, utility AI, NavMesh agent, or
network client must never mutate them directly. Later modular series classes may expose typed
views, but their sample layout and model serialization order must remain unchanged.

## Recommended player-AI boundary

The intended future path is:

```text
Team / coach AI
  -> player decision AI (role, target, action choice)
  -> navigation and local steering (world target, avoidance, desired facing)
  -> legacy intent adapter
  -> unchanged 30 Hz BasketballNeuralController and pretrained model
```

The runtime now exposes a first interchangeable policy boundary:

```csharp
public interface IBasketballDecisionPolicy
{
    void BeginDecisionFrame(BasketballWorldObservation observation);
    BasketballPlayerCommand Decide(in BasketballPlayerObservation player);
    void ReportCommandResult(
        in BasketballPlayerCommand command,
        BasketballSkillCommandResult result);
}

// Optional: receive delayed, multi-tick outcomes before the next decision frame.
public interface IBasketballWorldEventObserver
{
    void ObserveEvent(in BasketballWorldEvent worldEvent);
}
```

`BasketballRuleBasedTeamAI` is the first implementation. It outputs a
`BasketballPlayerCommand`; `BasketballMatchController` validates the actor, possession version,
target team and current ball state before forwarding a pass request to
`BasketballPossessionManager`. A rejected or stale command is acknowledged back to the policy and
cannot mutate possession. Human keyboard/mouse input intentionally remains a direct input source
for the selected player.

An AI planner should preferably emit a higher-level command in world space:

- desired world position or velocity;
- desired facing or look target;
- action request: hold/catch, shoot, or spin;
- optional local ball target and height;
- urgency or expiry, plus a command identifier for acknowledgement.

The adapter, not the planner, converts that command into legacy `Move`, `Turn`, `Spin`, and ball
control channels. This keeps tactical/navigation code independent from the pretrained model's
unusual control semantics.

## Observation contract needed by AI

`BasketballWorldObservation` is a fixed-capacity, allocation-free snapshot rather than a mutable
view of recurrent agent state. It currently exposes:

- self root pose, facing, linear velocity, current style/action indicators;
- carrier flag and ball authority state;
- ball pose and velocity;
- hoop, court, teammate, and opponent relative observations;
- contact values and action readiness/cooldowns;
- fixed roster slots with Active flags, stable player/team IDs, root pose and velocity;
- owner, previous owner, intended receiver and possession version;
- ball state, control mode, pose and velocity;
- both team attack targets, active counts, match mode and score.

The policy receives one `BasketballPlayerObservation` plus the shared world snapshot. Future
external bridges can serialize this data-only surface without exposing Unity Transforms or model
buffers. Pass skill requests already carry a stable command ID, possession transaction version and
expiry. Immediate validation is returned through `ReportCommandResult`; delayed physical outcomes
are available through the optional `IBasketballWorldEventObserver` interface.

`BasketballWorldEventStream` adds a fixed-size ring buffer for possession, pass request/release,
pass catch/failure/interception, shot release/make/miss, steal touch, contested/loose ball, secure
and match restart events. It overwrites the oldest record instead of growing a managed list.
Observer delivery is ordered and once-only. If a consumer falls behind beyond ring capacity, it
continues at `OldestSequence`; overwritten events remain available from optional JSONL telemetry,
not from the runtime ring.

The JSONL `basketball-world-event-v2` record also carries `targetPosition` and `skillVariant`.
Pass records use these for the predicted catch point and `BasketballPassType`; shot records use the
resolved hoop target. The event `position` and `velocity` remain the actual physical state, so the
offline dataset can measure planning error without treating a desired target as ground truth.

The single-agent feature builder currently leaves legacy interaction blocks zero-filled. Adding
opponents to tactical observations does not automatically mean they should be written into the
neural model input. Any change to those 864 channels requires separate parity analysis against
the original demo contract.

## Scheduling and event rules

- Keep neural simulation at 30 Hz for every active reference agent.
- Team decisions can run at roughly 2-5 Hz and player decisions/navigation at roughly 5-15 Hz,
  while the last intent is held and sampled by every neural tick.
- Shoot, catch, hold, spin, possession changes, and sharp direction changes need latched commands
  or explicit acknowledgement. Do not rely on a render-frame-only boolean pulse.
- Rendering interpolation remains presentation-only and must not influence AI observations used
  for deterministic decisions; observations should use simulation state.
- Every agent owns independent input/output buffers, pose, trajectory, contact, phase, and ball
  intent state. Immutable model data may be shared.
- One match ball must have exactly one authority/owner. Multiple independent test balls must use
  explicit IDs and separate authority state.

## Current limitations relevant to future AI

- Human and policy routing are still coordinated by `BasketballMatchController`, but rule AI now
  crosses an explicit data snapshot/command interface. Human input-source unification remains
  future work.
- Movement-space semantics are partially coupled to camera/gamepad compatibility.
- Keyboard control does not currently expose Spin or BallHeight, although the neural control
  path contains those channels.
- Ball-speed control is internal rather than an explicit intent value.
- The baseline AI includes deterministic spacing, rank-based matchups, pass evaluation, defensive
  pursuit, one shared team plan and at most one loose-ball/rebound chaser per team. It does not yet
  contain path planning, screens, rotations,
  fouls, clock rules, substitutions or polished playbooks.
- The current opponent steal has contact-validated Touch/Loose/Secure gameplay but no dedicated
  learned poke animation. `SelectedPlayerOnly` remains available for the original standby
  comparison; mixed/full modes explicitly supply AI movement, catch and steal intents.
- Batch10 GPU scheduling is deployed, but full-match allocation and long-duration profiling still
  need owner validation.
- A phase-amplitude safety clamp protects the current closed loop from non-finite feedback; this
  is a documented safety difference while the remaining legacy feedback details are evaluated.

## Recommended next implementation order

1. Extend multi-tick skill-result acknowledgement to shot/steal/movement as well as pass.
2. Add screen/pick timing, defensive switch communication and rebound landing prediction on top
   of the current Cutter, Safety, Help Defender, box-out and local-avoidance baseline.
3. Add a transport adapter for an external RL policy; action masks and optional JSONL skill-event
   export are already available without a Python runtime dependency.
4. Complete long 1v1/3v3/5v5 quality, memory and Profiler acceptance before expanding tactics.

Runtime roster changes now use deterministic court-local formations and a GPU readback barrier.
Every player's closed-loop root, bone, ball, contact, phase and intent state is re-seeded before a
new Batch10 inference can be dispatched.
