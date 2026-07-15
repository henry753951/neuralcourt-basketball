# Known Differences from SIGGRAPH 2020 Basketball

Implementation updated: 2026-07-15; Play Mode validation pending

The `BasketballDemo.unity` Phase 1 reference was accepted after direct keyboard/mouse play
comparison on 2026-07-13. The original model, recurrent data layout, control curves, ball/contact
path, IK, UI, and debug visualization are the behavior baseline for subsequent phases.

## Open differences

- Phase 2 uses the custom Unity 6 third-person orbit camera rather than the original fixed camera;
  the superseded legacy camera component has been removed.
- Keyboard/mouse is now the default and gamepad is secondary compatibility. Holding right mouse
  enters Ball Control Mode so normal mouse movement can orbit the camera.
- A future AI route controller will provide `BasketballIntent` directly; no path planner is part
  of Phase 2.
- The 5v5 team/pass/steal layer is reconstructed because the supplied SIGGRAPH 2020
  project does not include a multiplayer match controller. Team filtering, central possession,
  physical pass flight, contested/loose states and arbitration are new match-level rules.
- The model has no Pass or Steal style labels. Passing uses Hold/root-facing preparation without
  changing Ball Target, then performs a one-shot ballistic physical release. Steal gives the
  defender a model-only proxy ball for pose context, but success still uses real swept hand-ball
  distance and Touch-before-Secure arbitration; it does not claim a learned poke animation.
- Unselected players intentionally receive Stand rather than Hold. Only an intended receiver in
  `PassFlight` receives catch guidance; this prevents a remote ball from pulling idle hands/body.
- The 5v5 demo now uses the official Unity Inference Engine 2.6.1 GPUCompute batch path
  exclusively. It retains one 30 Hz closed-loop state per player. The Reference/Burst CPU
  evaluators and fallback have been removed; GPU failure now stops simulation visibly.
- Neural scheduling can now be selected from one shared profile. The 30 Hz mode remains the
  formal reference. Lower 20/15/10 Hz modes use render-time pose interpolation but do not retime
  the 2020 model's trained equations, so they remain experimental until dribble, contact, phase,
  shoot/release and foot-slide quality are accepted.
- The Unity 6 remake applies a configurable realtime-shadow budget at match startup. This keeps
  scene lights enabled while suppressing duplicate shadow maps that are not part of the original
  neural behavior.

## Intentional differences

- The Unity 2019 Eigen native plugin and later pure-C#/Burst compatibility evaluators are not
  shipped. The deployed ONNX is the sole runtime model representation.
- Legacy Post Processing Stack v2 was not imported. The reference scene uses URP materials and a
  clean Unity 6 camera.
- Cinemachine is not installed; the Phase 2 orbit camera is a small project-owned component to
  avoid a package dependency for this reference scene.

## Tooling-only warning

MCP for Unity may log `[WebSocket] Unexpected receive error: WebSocket is not initialised` after
domain reload while HTTP MCP remains healthy. This originates in the installed MCP package and
is tracked separately from gameplay Console results.
