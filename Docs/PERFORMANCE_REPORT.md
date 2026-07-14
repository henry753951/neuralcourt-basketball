# Performance Report

Last updated: 2026-07-15

## Current reference baseline

| Configuration | Result |
|---|---|
| Unity Editor | `6000.5.3f1`, Windows, URP 17.5.0 |
| Agents | 3 functional agents; fixed batch-three GPU prototype added |
| Reference backend | Pure C# `BasketballReferenceBackend` correctness oracle |
| Optimized backends | Burst CPU and official Sentis 2.6.1 GPUCompute (owner validation pending) |
| Neural rate | 30 Hz reference; 20/15/10 Hz experimental profile settings |
| First sampled inference | approximately 9.6 ms in Editor |
| Historical pre-refactor EditMode tests | 8/8 passed |
| Historical pre-refactor PlayMode tests | 11/11 passed |

Closed-loop NaN regression (2026-07-13): fixed forward movement passed 600 ticks in the
automated PlayMode suite, 1,500 ticks in an Editor stress invocation, and 946 ticks during a
continuous real Play Mode run. The final Console check contained zero errors and warnings.

The 9.6 ms value is an Editor stopwatch sample, not a production benchmark. No CPU/GPU frame
timing, GC/frame, Player build, or percentile capture has been completed, so no optimization
claim is made yet.

The historical 2026-07-14 three-agent functional gate confirmed that all three independent recurrent agents
advance within the 27-33 tick window over one real-time second, keyboard Tab retargets player and
camera, Ctrl look-targeting locks a teammate, a short Space press fakes without release, a held
press commits and releases, pass targeting rejects the opponent, same-team players cannot steal,
opponent hand contact can transfer ownership through the controller path, and exactly one carrier
remains after transfer. This is correctness coverage, not a throughput benchmark.

The newer central possession, bounded ML pass release, Rival wiring and Touch-before-Secure
implementation supersedes that multiplayer result. Per project-owner direction, its current
Play Mode/manual validation is pending user verification; this report makes no pass claim for
the new multiplayer behavior yet.

## Performance instrumentation added

The current worktree adds an allocation-conscious UI Toolkit performance card and several
low-risk hot-path reductions. Runtime validation remains pending the project owner's manual
test, so the entries below describe implementation rather than measured improvement.

- FPS and frame time use a smoothed unscaled render-frame delta.
- Main-thread time and GC allocated per frame come from rolling `ProfilerRecorder` samples.
- GPU frame time comes from `FrameTimingManager`; unsupported Editor/platform combinations
  display `N/A` instead of a fabricated value.
- Inference, neural pipeline and animation time are sampled from the existing Basketball
  profiler markers. Marker results are rolling averages and the Unity Profiler remains the
  source of truth for captures.
- Actual neural ticks per second are measured from the currently selected agent, rather than
  displaying the configured 30 Hz as if it were observed throughput.
- `GPU INFER RTT` reports Sentis schedule-to-readback wall time. It is intentionally separate
  from the `INFERENCE` CPU marker, which measures dispatch work rather than GPU completion.
- `CAMERA SCRIPT` reports the `Basketball.Camera` marker separately from total frame/render cost.
- Performance text refreshes at 4 Hz, neural telemetry at 15 Hz, and the control disk at 30 Hz.
  Hidden telemetry no longer updates strings, gating bars or layout styles.
- `FrameTimingManager.CaptureFrameTimings` is profile-throttled (every four frames by default)
  instead of being requested on every render frame.
- Style/contact/expert bars only write UI styles when the displayed percentage changes.
- `ThirdPersonOrbitCamera` is cached once per neural controller instead of calling
  `GetComponent` every neural tick for every agent.

The HUD deliberately has a small measurement cost. For authoritative `GC Alloc / Frame` and
CPU captures, measure both HUD-on and HUD-off configurations and use a Development Player build
in addition to Editor measurements.

## Camera-orbit frame-drop investigation

Static inspection of `BasketballDemo.unity` found eight active lights affecting all layers. Two
directional lights and one point light cast realtime soft shadows. The current PC URP asset also
uses a 2048 main shadow map, four cascades, a 50-unit shadow distance and a 2048 additional-light
shadow atlas. This work is independent of player inference, so it remains when player objects are
disabled and can become visible while camera culling and shadow views change.

This is the leading cause based on scene/render configuration, not yet an owner-captured Profiler
proof. The shared runtime profile now defaults to a shadow budget of one directional shadow and
zero additional-light shadows. It disables only redundant realtime shadow maps at match startup;
the lights and their illumination remain enabled. `Basketball.Camera`, `.Input` and `.Collision`
markers plus the HUD `CAMERA SCRIPT` row allow the owner test to separate a cheap orbit script
from expensive render/shadow work. The profile also exposes occlusion culling, collision-query
interval, HUD refresh rates and FrameTiming capture interval for controlled A/B tests.

## Burst CPU backend implemented

The optional Burst path preserves the reference model's 864 input features, 588 outputs,
normalization, gating network, stable Softmax, eight-expert dynamic blending, ELU and layer
ordering. It uses strict/high-precision Burst compilation and preallocates all per-agent native
scratch buffers. A reference-counted cache stores only one persistent native copy of immutable
model weights for all agents using the same `BasketballModelAsset`.

The backend's public evaluation remains synchronous so simulation ordering and the 30 Hz closed
loop do not change. Internally, Gating completes first, the three large expert-weight blends run
as parallel jobs, and the dense network completes last. This parallelizes the dominant dynamic
blending work without yet scheduling complete agents independently.
`BasketballBurstBackendTests` compares deterministic inputs, outputs and gating weights against
the C# reference and checks repeated finite output. The project owner passed all three tests on
the initial scalar Burst implementation. Because the implementation has since been split into
parallel blend stages, the same tests must be rerun before accepting this revision.

## Owner A/B snapshot: three agents

The project owner ran the three EditMode parity tests successfully, then supplied these HUD
snapshots from `BasketballDemo.unity`. They are single visual samples rather than controlled
30-second percentile captures, but they exposed the first Burst implementation's frame-pacing
problem.

| Metric | Reference C# | Initial scalar Burst |
|---|---:|---:|
| FPS | 32 | 36 |
| Frame | 31.09 ms | 27.69 ms |
| Main CPU | 28.39 ms | 20.14 ms |
| GPU | 0.30 ms | 0.21 ms |
| GC / frame | 32.4 KB | 14.4 KB |
| Inference / invocation | 7.29 ms | 7.54 ms |
| Neural pipeline / tick | 7.59 ms | 7.86 ms |
| Animation | 0.03 ms | 0.03 ms |
| Observed neural rate | 30.0 Hz | 29.9 Hz |

The scalar Burst inference was not faster than Reference. With three 30 Hz agents aligned, a
render frame can absorb several roughly 7.5 ms inference calls before camera presentation, and
the owner reported visible mouse-orbit stutter. This result triggered the parallel expert-blend
implementation described above. New owner A/B measurements are pending; the table must not be
used as evidence for the parallel version's speed.

## Required measurement matrix

Future captures will record 1/5/10 agents, reference/optimized backend, 30/20/15/10 Hz, IK
on/off, and debug on/off. Each result must include CPU time, GPU time, FPS distribution, GC
allocation, inference time, pose/contact/ball quality metrics, and the exact build/hardware
configuration.

Current Profiler markers: `Basketball.Control`, `Basketball.BuildFeatures`,
`Basketball.Inference`, `Basketball.Decode`, `Basketball.Ball`, `Basketball.Contact`,
`Basketball.IK`, `Basketball.Interpolate`, and `Basketball.ApplyPose`.

## Official Sentis GPU batch implemented

The project now depends on Unity's official `com.unity.ai.inference@2.6.1` package; no third-party
ONNX Runtime package is present. `Tools/Onnx/export_basketball_onnx.py` converts the complete
legacy YAML weights offline into an exact fixed batch-three ONNX graph. The graph preserves the
864 input values, normalization, Gating/Softmax, eight dynamically blended experts, ELU and 588
decoded outputs. It appends eight telemetry-only gating values per row and exposes one
`[3,596]` output so a tick requires one GPU readback.

`BasketballSentisBatchScheduler` creates one `BackendType.GPUCompute` worker. On the current DX12
Editor this is the DirectML-capable Sentis path. Worker warm-up completes before switching away
from Burst. At Runtime the scheduler:

1. Prepares all three independent recurrent inputs at one shared profile tick boundary (30 Hz
   reference; lower values experimental).
2. Uploads `[3,864]` and schedules one worker without blocking the render frame.
3. Polls one asynchronous output readback.
4. Validates finiteness and commits three 588-output states together.
5. Starts no later tick until the previous closed-loop state has committed.

This introduces readback latency but avoids the previous three large synchronous CPU inference
calls landing in the same camera frame. If model import, worker warm-up, schedule or readback
fails, every player remains on or returns to the Burst fallback; mixed authority is disallowed.
The current implementation deliberately does not pipeline multiple recurrent ticks.

Official references:

- https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/index.html
- https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/create-an-engine.html
- https://docs.unity3d.com/Packages/com.unity.ai.inference@2.6/manual/profile-a-model.html
- https://docs.unity3d.com/6000.0/Documentation/Manual/class-ComputeShader-run.html
- https://docs.unity3d.com/current/Documentation/Manual/script-compilation-burst.html

The generated ONNX is approximately 30 MB and is imported as a Sentis `ModelAsset` under
`Assets/AI4AnimationRemake/Resources/Models`. Python remains authoring-only. Static Runtime,
Editor and EditMode test assemblies compile successfully. Per project-owner direction, Codex did
not run Test Runner or Play Mode, so neither numerical parity nor a speedup is claimed yet.

Remaining acceptance order:

1. Run the three Sentis EditMode tests: import contract, CPU batch parity and GPUCompute batch
   parity. Rerun the Burst tests after its parallel-blend revision.
2. Manually exercise the same dribble, turn, sprint, pass, catch, interception and steal script
   on Reference, Burst and Sentis.
3. Capture controlled 30-second Timeline/GC samples after a 10-second warm-up, including HUD on
   and off. Compare frame pacing and mouse orbit, not only average FPS.
4. Reject Sentis as a default if output drift, readback latency or closed-loop quality is worse;
   retain Reference as oracle and Burst as fallback.
5. Only after batch-three acceptance, generalize model assets/scheduling for 5 and 10 agents or
   investigate persistent GPU-resident post-processing.

GPU acceptance requires matching input/output dimensions and ordering, no model retraining,
an unchanged 30 Hz reference mode, bounded single-tick numeric error, and a closed-loop quality
comparison covering root, pose, contact, phase and ball behavior. A faster backend is not
accepted if its output only looks approximately correct for a short clip.

## Manual profiling checklist

For the next owner-run test in `BasketballDemo.unity`:

1. Set `Inference Backend = Reference` on the shared `BasketballRuntimeSettings` profile. Open the UI Toolkit HUD
   and confirm its heading says `REFERENCE C#`, then confirm FPS, frame, main CPU, GPU, GC,
   inference, neural pipe,
   animation and neural rate values update. `GPU = N/A` is valid in unsupported Editor modes.
2. In Test Runner, run `BasketballBurstBackendTests` and all three
   `BasketballSentisBatchTests`. The GPU test is categorized `GPU`.
3. In Unity Profiler, enable CPU Usage, GPU Usage, Memory and Rendering; use Timeline view.
4. After 10 seconds of warm-up, record at least 30 seconds with HUD on, then HUD off.
5. Stop Play Mode, set the shared profile to `Burst`, restart, and confirm the heading says
   `BURST CPU` rather than `BURST DISABLED`. Repeat the identical warm-up and capture.
6. Stop Play Mode, set the shared profile to `SentisGpuBatch`, restart, and wait for the heading to
   change from `BURST CPU FALLBACK` during warm-up to `SENTIS DML BATCH 3`. Confirm
   `GPU INFER RTT` becomes numeric and repeat the same capture. If it stays on fallback, read the
   Console warning rather than treating the result as GPU.
7. Repeat with IK on/off and debug drawing on/off. For neural-rate quality, test 30 Hz Linear,
   then 20/15/10 Hz with SmoothStep and record dribble/contact/phase regressions.
8. Record Editor and Development Player results separately; do not compare them as equivalent.
9. Verify `Basketball.Inference`, `Basketball.Inference.Burst`,
   `Basketball.Inference.Sentis.Schedule`, `Basketball.Inference.Sentis.Readback`,
   `Basketball.BuildFeatures`,
   `Basketball.Decode`, `Basketball.Camera`, `Basketball.Camera.Input`,
   `Basketball.Camera.Collision`,
   `Basketball.Contact`, `Basketball.IK` and `Basketball.ApplyPose` appear in Timeline.
10. Exercise dribble, turning, shoot, pass, catch and steal on all three paths. Report
   average/95th-percentile frame time, inference time per call, GC bytes/frame and any
   visual regression in dribble, pass, catch, steal, turning or ball ownership.
