# Runtime Architecture

Last verified: 2026-07-13

The current implementation is a reference-first vertical slice. Simulation state is separate
from rendered transforms so interpolation cannot feed presentation state back into the model.

```text
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
  `FreePhysics`, and `Reacquiring`; full transition conditions remain under implementation.

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
