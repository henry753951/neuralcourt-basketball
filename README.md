# AI4Animation Basketball Unity 6 Remake

This project is an in-progress Unity 6 remake of the SIGGRAPH 2020 Interactive Basketball
demo. The current reference path uses the original pretrained Basketball model and canonical
26-bone primitive rig; it does not use Animator clips, motion matching, retraining, or Python
at runtime.

## Open and run

- Unity version: `6000.5.3f1`.
- Open `Assets/Scenes/BasketballDemo.unity`.
- Enter Play Mode. The default profile advances neural simulation at the canonical 30 Hz while
  the rendered pose is interpolated between previous/current neural states.
- Optional contract checks are in `CrowdEyes.AI4Animation.Tests.EditMode`. The GPU execution
  check requires a machine with compute-shader support.

## Shared runtime settings

Edit `Assets/AI4AnimationRemake/Resources/Settings/BasketballRuntimeSettings.asset` rather than
changing every player. The match shares this profile across all ten agents, the Sentis batch
scheduler, orbit camera, application frame pacing, HUD sampling and realtime-shadow budget.
`Neural Tick Rate = 30` is the Basketball 2020 reference. Values `20`, `15` and `10` are
experimental scheduling modes; `Linear` or `SmoothStep` interpolates only rendered root, bones
and ball presentation and never writes presentation state back into the recurrent model. Restart
Play Mode after changing startup settings.

## Current keyboard and mouse input

- `Tab`: cycle the controlled player; the orbit camera follows with a smooth transition.
- `WASD`: camera-relative movement; the character turns toward that heading while moving.
- Mouse: free third-person orbit; wheel zooms; `R` recenters.
- `Esc`: release/lock the cursor; left-click locks it again.
- `Shift`: sprint intent while moving forward.
- `Q` / `E`: left/right turn intent (matching the legacy keyboard controller).
- `Space`: shoot only.
- Carrier + `Ctrl`: teammate target-selection mode. Look at a teammate to lock the gold
  indicator; tap left mouse for a fake or hold it for at least 0.2 seconds to pass. Target
  selection changes root facing only and never overwrites Ball Control/Ball Target.
- Non-carrier + left mouse: steal/tip intent. A touch only succeeds when a defender hand
  reaches the shared ball; an invisible per-agent proxy helps the neural pose prepare without
  moving the shared ball. Touch and Secure remain separate.
- `Ctrl` without left mouse retains the original hold/catch control.
- Hold right mouse and move the mouse: ball-control intent.

The reference match contains P1-P5 on blue Team A and P6-P10 on red Team B. Pass locks reject
opponents. A cyan ground ring identifies the controlled player; a gold overhead ring identifies
the selected pass target.

Keyboard/mouse is the primary control path. The original gamepad adapter remains available as
secondary compatibility. The orbit camera is presentation-only and does not modify neural state.

## Model and data source

The model, scene-local primitive character/court, and reference behavior come from the
`facebookresearch/ai4animation` SIGGRAPH 2020 project, originally authored for Unity
`2019.3.0f3`. The untouched source archive is kept outside this Unity project. Only the
interactive demo's required data has been selectively brought into `Assets/`.

The visible Humanoid athlete, skin textures, shoes, hair and team uniform meshes are selectively
copied from the sibling `CrowdEyes-Twin` project. Neural simulation still runs exclusively on the
canonical 26-bone Basketball rig; `BasketballHumanoidVisualRetargeter` applies its final render
pose to the Humanoid. Limb swing is solved from canonical joint positions and wrist rotation is
bounded, avoiding the incompatible bone-axis roll that occurs with direct world-rotation copying.
If the visual Animator, Humanoid Avatar or required bones are missing, the visual is hidden and the
primitive canonical mannequin is restored automatically. `BasketballPlayerAppearance` reads the
team and jersey number from `BasketballTeamMember`; shared team profiles control jersey, shorts and
number colors without cloning materials. Single-digit front/back numbers use centered, slightly
smaller skinned patches. See
`Assets/AI4AnimationRemake/Characters/CrowdEyesTwin/THIRD_PARTY_NOTICES.md`.

## Using another Humanoid character

1. Keep the existing player root, `BasketballNeuralController`, `BasketballReferenceRig` and
   canonical 26-bone hierarchy. They remain the neural simulation and collision authority.
2. Import the visual model as `Humanoid`, make it a child presentation object, and add
   `BasketballHumanoidVisualRetargeter` to that visual root. Assign the model Animator; leave its
   Animator Controller empty and Root Motion off.
3. Add `BasketballPlayerAppearance` to the visual root. Assign body, jersey and shorts renderers,
   shared skin materials, the two team profile assets, and an optional
   `BasketballJerseyNumberDisplay`. The generated CrowdEyes-Twin prefab is a working reference.
4. Set `Team ID` and `Jersey Number` on the parent `BasketballTeamMember`. Team ID controls pass
   eligibility and profile selection; jersey number is presentation metadata and supports 0-99.
5. Keep gameplay, AI and movement commands on `BasketballIntent`. Never animate or move the visual
   root as the gameplay authority; the retargeter consumes the final interpolated canonical pose.

The orbit camera still references the rendered player root, but now filters that root through a
presentation-only camera target smoother before composing the orbit. Collision uses a non-allocating
sphere cast and ignores players and the shared ball, preventing 5v5 character colliders from causing
distance pops while orbiting. These camera transforms never feed back into recurrent neural state.

The deployed ONNX contains the original pretrained MoE math and weights. Its contract is
`864 -> MoE -> 588`, with an 8-way gating network and eight dynamically blended experts. The
GPU batch graph packs eight telemetry-only gating values after the 588 model outputs, producing
`[10,864] -> [10,596]`. The old serialized 58-buffer CPU model asset is not shipped in this
runtime. See `Docs/MODEL_IO_CONTRACT.md` for the exact channel order.

## GPU-only inference

`BasketballSentisBatchScheduler` is the only inference implementation. It uses the official
Unity Inference Engine 2.6.1 `GPUCompute` worker for a fixed ten-player (5v5) batch. On DX12 the HUD
reports `SENTIS DML BATCH 10`; other supported graphics APIs report `SENTIS GPU BATCH 10`.
The scheduler warms the worker, allows only one recurrent tick in flight, polls readback without
blocking the camera frame, and commits the ten independent agent states together. `GPU INFER
RTT` is the schedule-to-readback round trip; `INFERENCE` is CPU dispatch/readback bookkeeping.

There is no CPU/reference/Burst inference fallback. If the ONNX contract, GPU worker, scheduling,
or readback fails, the neural simulation stops and the HUD/Console reports `SENTIS GPU ERROR`
instead of silently changing numerical backends.

## Status

Completed and accepted as the Phase 1 reference: original model behavior, recurrent Feed/Read
ordering, canonical 26-bone rig, canonical 30 Hz neural scheduling, render interpolation, ball
authority transitions, contact/IK path, legacy UI/debug visualization, and automated long-run
rig-coherence gates.

Phase 2 adds a decoupled third-person orbit camera with mouse yaw/pitch, zoom, recenter, smoothing,
collision, cursor lock, camera-relative keyboard movement, and a dedicated right-mouse Ball
Control Mode. Keyboard/mouse is the default; gamepad remains secondary. The current match slice
also runs ten independent 30 Hz agents in a 5v5 roster, one authoritative shared
ball, Tab player switching, teammate-only targeted passes, and contact-validated opponent steals.
Pass flight starts with one ballistic release velocity and remains pure Rigidbody flight.
Unselected players now remain in true Stand unless they are the intended receiver of a released
pass; they no longer receive Hold input merely because another player owns the ball.

Not completed: AI path planning and defensive navigation, dedicated steal/tip animations,
5v5 runtime allocation/profiler sign-off, and low-frequency quality acceptance. Runtime and
Test Runner validation remain with the project owner.

See `Docs/PLAYER_AI_INTEGRATION.md` for the current control contract, the mapping from the
original controller/series, and the recommended boundary for future player and team AI.
See `Docs/MULTIPLAYER_BALL_SYSTEM_AUDIT.md` for the current possession/pass/steal data flow,
model-capability boundary, and the manual validation checklist.

## Licensing

This remake retains the original project's research/education context and source attribution.
Review the upstream AI4Animation and motion-data licenses before redistribution or commercial
use; the original assets are not presented here as unrestricted commercial content. The visible
CrowdEyes-Twin athlete includes CC0 and CC BY components documented in its bundled third-party
notice and must retain the applicable attribution when redistributed.
