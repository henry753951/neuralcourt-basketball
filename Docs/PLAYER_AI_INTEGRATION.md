# Player AI Integration Analysis

Last reviewed: 2026-07-14

This document records the current single-player control architecture and the safe integration
boundary for future player, team, navigation, and network AI. It is a design snapshot, not an
implementation claim: the project does not yet contain an AI intent provider or multi-agent
match coordinator.

## Current runtime path

```text
Keyboard / mouse
  -> BasketballKeyboardMouseInputProvider
  -> BasketballIntent
  -> BasketballNeuralController.ApplyControl               fixed 30 Hz
  -> BasketballAgentState recurrent series
  -> BasketballFeatureBuilder                              864 floats
  -> BasketballReferenceBackend                            original 8-expert MoE
  -> BasketballOutputDecoder                               588 floats
  -> previous/current BasketballPoseBuffer
  -> BasketballPoseApplicator + contact IK                 render frame
  -> canonical 26-bone rig

BasketballAgentState <-> BasketballBallController <-> Rigidbody
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

The 30 Hz neural step is part of the model contract and must remain independent from the future
AI decision rate.

## What replaced the original BasketballController responsibilities

| Original responsibility | Current implementation | Meaning for future AI |
| --- | --- | --- |
| Stand / Move / Sprint | `BasketballIntent.Move` and `Sprint`; `ApplyControl` derives stand from move magnitude | AI should request desired movement, not select animation clips |
| Turn | `BasketballIntent.Turn`, plus camera-relative heading conversion for keyboard control | AI needs an explicit world-facing adapter instead of camera dependence |
| Dribble | Derived as `Carrier && !Hold && !Shoot` | Dribble is currently a state-dependent default, not a separate command |
| Hold | `BasketballIntent.Hold`; a carrier stops movement and moves the ball toward the hold target | AI may hold this intent across neural ticks |
| Catch / reacquire | The same `Hold` intent while not carrier; proximity to either hand starts reacquisition | Catch must be requested before the hand/ball proximity condition occurs |
| Shoot | `BasketballIntent.Shoot`; valid only while carrier; release waits for ball height, speed change, and low hand contact | A one-frame AI pulse can be missed, so future actions need latching/acknowledgement |
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
    public bool IsGamepad;
}
```

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

Introduce multiple interchangeable intent sources:

```csharp
public interface IBasketballIntentSource
{
    void Sample(in BasketballObservation observation, ref BasketballIntent intent);
}
```

Expected implementations are `HumanIntentSource`, `AIIntentSource`,
`ScriptedScenarioIntentSource`, and eventually `NetworkIntentSource`. The current controller is
still wired to the concrete `BasketballKeyboardMouseInputProvider`, so introducing this
interface is the first behavior-preserving refactor required before player AI.

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

Expose a read-only, allocation-free observation snapshot rather than the mutable agent state:

- self root pose, facing, linear velocity, current style/action indicators;
- carrier flag and ball authority state;
- ball pose and velocity;
- hoop, court, teammate, and opponent relative observations;
- contact values and action readiness/cooldowns;
- current command result: pending, consumed, succeeded, failed, or expired.

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

- There is no `IBasketballIntentSource` yet; the neural controller references the concrete human
  input provider.
- Movement-space semantics are partially coupled to camera/gamepad compatibility.
- Keyboard control does not currently expose Spin or BallHeight, although the neural control
  path contains those channels.
- Ball-speed control is internal rather than an explicit intent value.
- Multi-agent ownership arbitration, team roles, navigation, avoidance, and passing protocols do
  not exist yet.
- Shared immutable model storage and stagger/batch scheduling still need multi-agent profiling.
- A phase-amplitude safety clamp protects the current closed loop from non-finite feedback; this
  is a documented safety difference while the remaining legacy feedback details are evaluated.

## Recommended next implementation order

1. Add `IBasketballIntentSource` and preserve the current human adapter bit-for-bit.
2. Add read-only `BasketballObservation` and a scripted intent source for deterministic tests.
3. Add a world-space `PlayerCommand` to legacy-intent adapter, including action latching.
4. Implement one AI-controlled player in the reference scene without changing model I/O.
5. Add a second agent with independent recurrent state and explicit ball ownership.
6. Only after parity and profiling, introduce shared weight storage, staggered scheduling, LOD,
   and optional batch inference.
