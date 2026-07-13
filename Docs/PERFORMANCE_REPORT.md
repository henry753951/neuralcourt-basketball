# Performance Report

Last verified: 2026-07-13

## Current reference baseline

| Configuration | Result |
|---|---|
| Unity Editor | `6000.5.3f1`, Windows, URP 17.5.0 |
| Agents | 1 |
| Backend | Pure C# `BasketballReferenceBackend` |
| Neural rate | 30 Hz fixed reference |
| First sampled inference | approximately 9.6 ms in Editor |
| EditMode tests | 8/8 passed |
| PlayMode 30 Hz/finite-state + forward-motion/stability tests | 3/3 passed |

Closed-loop NaN regression (2026-07-13): fixed forward movement passed 600 ticks in the
automated PlayMode suite, 1,500 ticks in an Editor stress invocation, and 946 ticks during a
continuous real Play Mode run. The final Console check contained zero errors and warnings.

The 9.6 ms value is an Editor stopwatch sample, not a production benchmark. No CPU/GPU frame
timing, GC/frame, Player build, or percentile capture has been completed, so no optimization
claim is made yet.

## Required measurement matrix

Future captures will record 1/5/10 agents, reference/optimized backend, 30/20/15/10 Hz, IK
on/off, and debug on/off. Each result must include CPU time, GPU time, FPS distribution, GC
allocation, inference time, pose/contact/ball quality metrics, and the exact build/hardware
configuration.

Current Profiler markers: `Basketball.Control`, `Basketball.BuildFeatures`,
`Basketball.Inference`, `Basketball.Decode`, `Basketball.Ball`, `Basketball.Interpolate`, and
`Basketball.ApplyPose`.
