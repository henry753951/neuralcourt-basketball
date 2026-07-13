# AI4Animation Basketball Unity 6 Remake

This project is an in-progress Unity 6 remake of the SIGGRAPH 2020 Interactive Basketball
demo. The current reference path uses the original pretrained Basketball model and canonical
26-bone primitive rig; it does not use Animator clips, motion matching, retraining, or Python
at runtime.

## Open and run

- Unity version: `6000.5.3f1`.
- Open `Assets/Scenes/BasketballDemo.unity`.
- Enter Play Mode. The neural simulation advances at a fixed 30 Hz while the rendered pose is
  interpolated between previous/current neural states.
- Run tests from Test Runner or through MCP:
  - `CrowdEyes.AI4Animation.Tests.EditMode`
  - `CrowdEyes.AI4Animation.Tests.PlayMode`

## Current keyboard and mouse input

- `WASD`: camera-relative movement; the character turns toward that heading while moving.
- Mouse: free third-person orbit; wheel zooms; `R` recenters.
- `Esc`: release/lock the cursor; left-click locks it again.
- `Shift`: sprint intent while moving forward.
- `Q` / `E`: left/right turn intent (matching the legacy keyboard controller).
- `Ctrl`: hold/catch intent.
- `Space`: shoot intent.
- Hold right mouse and move the mouse: ball-control intent.

Keyboard/mouse is the primary control path. The original gamepad adapter remains available as
secondary compatibility. The orbit camera is presentation-only and does not modify neural state.

## Model and data source

The model, scene-local primitive character/court, and reference behavior come from the
`facebookresearch/ai4animation` SIGGRAPH 2020 project, originally authored for Unity
`2019.3.0f3`. The untouched source archive is kept outside this Unity project. Only the
interactive demo's required data has been selectively brought into `Assets/`.

The imported model contains the original 58 serialized float buffers. Its contract is
`864 -> MoE -> 588`, with an 8-way gating network and eight dynamically blended experts.
See `Docs/MODEL_IO_CONTRACT.md` for the exact channel order.

## Backends

- `BasketballReferenceBackend`: pure C#, row-major, behavior-oriented implementation of the
  original normalization, ELU, gating Softmax, expert blending, dense layers, and output
  denormalization. This is the only active backend.
- Optimized/Burst backend: not implemented yet. It will not become the default until numerical
  parity against the reference backend is demonstrated.

## Status

Completed and accepted as the Phase 1 reference: original model behavior, recurrent Feed/Read
ordering, canonical 26-bone rig, fixed 30 Hz neural scheduling, render interpolation, ball
authority transitions, contact/IK path, legacy UI/debug visualization, and automated long-run
rig-coherence gates.

Phase 2 adds a decoupled third-person orbit camera with mouse yaw/pitch, zoom, recenter, smoothing,
collision, cursor lock, camera-relative keyboard movement, and a dedicated right-mouse Ball
Control Mode. Keyboard/mouse is the default; gamepad remains secondary.

Not completed: AI path-planning input provider, multi-agent scenes, Burst/Jobs backend,
allocation/profiler sign-off, batch inference, and low-frequency experiments.

See `Docs/PLAYER_AI_INTEGRATION.md` for the current control contract, the mapping from the
original controller/series, and the recommended boundary for future player and team AI.

## Licensing

This remake retains the original project's research/education context and source attribution.
Review the upstream AI4Animation and motion-data licenses before redistribution or commercial
use; the original assets are not presented here as unrestricted commercial content.
