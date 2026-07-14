# Runtime Architecture

Implementation updated: 2026-07-15; Play Mode validation pending

The current implementation is a reference-first vertical slice. Simulation state is separate
from rendered transforms so interpolation cannot feed presentation state back into the model.

```text
BasketballMatchController
  -> Team roster (P1/P2 Team A, P3 Team B)
  -> active player / Tab switching / camera target
  -> teammate-only pass lock and pass-command latch
  -> atomic shared-ball possession and opponent steal arbitration
  -> per-player BasketballIntent override

BasketballKeyboardMouseInputProvider
  -> BasketballIntent
  -> BasketballNeuralController.Control                    [30 Hz]
  -> BasketballAgentState (61 samples, per-agent recurrent state)
  -> BasketballFeatureBuilder (864 floats)
  -> BasketballReferenceBackend (shared model data, 8-expert MoE)
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
  buffers. Backend initialization reads these shared arrays; per-tick inference uses
  preallocated work buffers.
- `BasketballAgentState` owns one agent's root trajectory, pose/velocity, dribble, ball,
  style, contact, and phase history. It is never shared between agents.
- `BasketballNeuralController` owns the fixed neural clock. The shipping reference rate is
  hard-validated at 30 Hz.
- Every player owns an independent `BasketballAgentState`, backend work buffers, pose history,
  contacts, phase, and recurrent trajectory. Three players currently run at the same 30 Hz
  reference rate.
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

## Profiler markers present

- `Basketball.Control`
- `Basketball.BuildFeatures`
- `Basketball.Inference`
- `Basketball.Decode`
- `Basketball.Ball`
- `Basketball.Interpolate`
- `Basketball.ApplyPose`

Contact/IK markers will be attached when those processing stages are implemented.
