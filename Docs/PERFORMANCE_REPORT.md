# Performance Report

Last updated: 2026-07-15

## Current configuration

| Item | Configuration |
|---|---|
| Unity Editor | `6000.5.3f1`, Windows, URP 17.5.0 |
| Agents | Fixed three-player batch |
| Inference | Unity Inference Engine 2.6.1 `BackendType.GPUCompute` only |
| GPU graph | `[3,864] -> [3,596]` |
| Neural rate | 30 Hz reference; 20/15/10 Hz experimental |
| CPU inference fallback | None |
| Owner Play Mode validation after GPU-only cleanup | Pending |

The previous pure-C# and Burst CPU evaluators, their raw 58-buffer model asset, fallback logic and
backend comparison tests have been removed. Older CPU measurements are therefore historical and
are no longer runtime configurations.

## Resource-lifetime audit

Static audit completed before the GPU-only deletion found no unpaired long-lived resource:

- The scheduler disposes its Unity Inference Engine `Worker` and input tensor on disable,
  destroy and failure.
- Readback clones are scoped with `using`; `PeekOutput` remains Worker-owned.
- `BasketballPerformanceMonitor` stops/disposes all `ProfilerRecorder` instances.
- UI Toolkit event and `generateVisualContent` callbacks are removed during unbind.
- Fixed arrays and pose/model buffers are bounded and reused rather than appended indefinitely.

This does not replace a long-run memory capture. Native/GPU driver allocations, package-internal
caches and complete-frame managed allocation must still be verified in the Unity Profiler and a
Development Player.

## Current scheduling path

```text
render Update
  -> accumulate shared neural time
  -> prepare three 864-float rows only when no batch is in flight
  -> schedule one GPUCompute Worker
  -> poll asynchronous readback on later render frames
  -> commit all three 588-output recurrent states together
  -> interpolate previous/current poses at render rate
```

The one-batch-in-flight rule preserves closed-loop ordering. It also means GPU round-trip latency
can limit achieved neural rate if a readback frequently exceeds the configured tick interval.
`GPU INFER RTT` must therefore be evaluated together with `NEURAL RATE`, frame time and readback
markers.

## Profiling interpretation

- `Basketball.Inference` is CPU-side dispatch/readback bookkeeping.
- `Basketball.Inference.Sentis.Schedule` is CPU schedule cost, not total GPU duration.
- `Basketball.Inference.Sentis.Readback` is CPU-side completion/readback processing.
- HUD `GPU INFER RTT` is wall-clock schedule-to-readback latency.
- Use the GPU Profiler/Frame Debugger for actual GPU workload attribution.
- Camera hitch analysis should compare `Basketball.Camera`, `.Input`, `.Collision`, URP rendering,
  UI Toolkit generation and Editor overhead independently from neural inference.

## Owner manual validation

No Play Mode or Test Runner execution was performed during the GPU-only cleanup, by request.
Please validate in this order:

1. Open `Assets/Scenes/BasketballDemo.unity` and confirm the Console has no compile error.
2. Enter Play Mode and wait for the HUD to show `SENTIS DML BATCH 3` on DX12, or
   `SENTIS GPU BATCH 3` on another supported GPU API. `SENTIS GPU ERROR` is a hard failure;
   it must never fall back to CPU.
3. Let the three-player scene run for at least five minutes. In Memory Profiler/Profiler, check
   Total Used Memory, Graphics/Driver memory, GC Alloc/frame and native allocation trend after
   warm-up. Stable plateaus are expected; monotonic growth is not.
4. Record FPS, frame/main-thread/GPU time, GPU inference RTT and achieved neural rate at 30 Hz.
5. Rotate the camera continuously with all player renderers active, then repeat with the player
   GameObjects disabled. Compare Camera markers, Physics raycasts, UI Toolkit and URP render work.
6. Exercise Tab switching, movement, dribble, shoot, pass, catch, interception and steal. Confirm
   ball authority and recurrent animation continue after prolonged play.
7. Only after 30 Hz passes, compare 20/15/10 Hz for dribble period, contact, phase continuity,
   shoot release, hand-ball distance and foot sliding.
8. Repeat the representative capture in a Development Player; do not treat Editor timing as a
   shipping benchmark.

## Acceptance targets

- No compile errors or `SENTIS GPU ERROR`.
- No monotonic managed/native/GPU memory growth after warm-up.
- Stable 30 Hz neural rate under the target three-player workload.
- No recurring per-frame allocations attributable to basketball runtime code.
- Camera rotation does not introduce periodic main-thread spikes.
- GPU-only output retains accepted movement, ball, pass/catch and possession behavior.
