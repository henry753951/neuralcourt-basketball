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
- Select `BasketballMatch` to choose `1v1`, `3v3`, `5v5` or custom active rosters. The GPU graph
  always keeps ten slots; players outside the selected mode are inactive gameplay slots.
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
- `U`: switch between the debug/telemetry HUD and the gameplay controls HUD.
- `P`: enter/leave Free Cam. While active all players use rule AI; use mouse + `WASD`,
  `Q`/`E` vertical movement and `Shift` boost.
- `WASD`: camera-relative movement; the character turns toward that heading while moving.
- Mouse: free third-person orbit; wheel zooms; `R` recenters.
- `Esc`: release/lock the cursor; left-click locks it again.
- `Shift`: sprint intent while moving forward.
- `Q` / `E`: left/right turn intent (matching the legacy keyboard controller).
- `Space`: shoot only.
- Carrier + `Ctrl`: teammate target-selection mode. Look at a teammate to lock the gold
  indicator; click left mouse once to submit the pass immediately. Target
  selection changes root facing only and never overwrites Ball Control/Ball Target.
- Non-carrier + left mouse: steal/tip intent. A touch only succeeds when a defender hand
  reaches the real shared ball. Short original-model Hold/reach pulses prepare the pose; there
  is no proxy ball or ownership-time suction. Clean contact transfers authority at the existing
  ball pose, while a weaker touch becomes physical Loose/Contested ball.
- Off-ball + teammate possession + hold `Ctrl`: call for pass. This improves the AI receiver
  score but range, passing lane, return-pass lockout and pass capability still decide whether
  the teammate passes. With a loose or in-flight ball, `Ctrl` remains Hold/Catch.
- Hold right mouse and move the mouse: ball-control intent.

The reference match contains P1-P5 on blue Team A and P6-P10 on red Team B. Pass locks reject
opponents. A cyan ground ring identifies the controlled player; a gold overhead ring identifies
the selected pass target.

Keyboard/mouse is the primary control path. The original gamepad adapter remains available as
secondary compatibility. The orbit camera is presentation-only and does not modify neural state.

## Model and data source

The model, scene-local primitive character, and reference behavior come from the
`facebookresearch/ai4animation` SIGGRAPH 2020 project, originally authored for Unity
`2019.3.0f3`. The untouched source archive is kept outside this Unity project. Only the
interactive demo's required data has been selectively brought into `Assets/`.

The FIBA court mesh, wood floor and basket geometry are selectively extracted from the sibling
`CrowdEyes-Twin` project. The clean `BasketballCourt.prefab` contains no dependency on that
project's cameras, synthetic-data scripts or render pipeline. It uses a 28 x 15 m Z-longitudinal
layout, 3.05 m rims, 32 physical rim colliders and project-owned URP materials.

The visible Humanoid athlete, skin textures, shoes, hair and team uniform meshes are selectively
copied from the sibling `CrowdEyes-Twin` project. Neural simulation still runs exclusively on the
canonical 26-bone Basketball rig; `BasketballHumanoidVisualRetargeter` applies its final render
pose to the Humanoid. Limb swing and arm roll are solved from canonical joint positions and the neural hand rotation is
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
3. Add `BasketballHumanoidHandContactSolver` beside the retargeter when the Humanoid contains
   upper/lower arm, hand and proximal finger mappings. It consumes the existing neural hand-contact
   channels and places a model-derived palm anchor on the physical ball surface. It never moves the
   ball or feeds the corrected pose back into simulation.
4. Add `BasketballPlayerAppearance` to the visual root. Assign body, jersey and shorts renderers,
   shared skin materials, the two team profile assets, and an optional
   `BasketballJerseyNumberDisplay`. The generated CrowdEyes-Twin prefab is a working reference.
5. Set `Team ID` and `Jersey Number` on the parent `BasketballTeamMember`. Team ID controls pass
   eligibility and profile selection; jersey number is presentation metadata and supports 0-99.
6. Keep gameplay, AI and movement commands on `BasketballIntent`. Never animate or move the visual
   root as the gameplay authority; the retargeter consumes the final interpolated canonical pose.

The orbit camera still references the rendered player root, but now filters that root through a
presentation-only camera target smoother before composing the orbit. Collision uses a non-allocating
sphere cast and ignores players and the shared ball, preventing 5v5 character colliders from causing
distance pops while orbiting. These camera transforms never feed back into recurrent neural state.
The GPU scheduler also keeps a separate presentation clock: simulation backlog decides when another
neural batch is due, while a freshly committed pose always begins interpolation at alpha zero. This
prevents asynchronous readback latency from snapping the rendered Root and therefore the camera.

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
In `SelectedPlayerOnly`, unselected players remain in true Stand unless they are the intended
receiver of a released pass. The current gameplay slice also includes team-aware hoops,
deterministic one-time ballistic
shot release, two/three-point scoring, post-score inbound reset, and a baseline allocation-free
rule AI. By default the Tab-selected player remains keyboard/mouse controlled while other active
players space, defend, pass, catch, shoot, contest and pursue loose balls through
`BasketballIntent`. `SelectedPlayerOnly` restores the earlier all-standby debug behavior, and
`FullRuleAI` leaves Tab as camera observation only.

High-level AI now crosses `IBasketballDecisionPolicy`: a fixed-capacity
`BasketballWorldObservation` enters the policy and a transactional `BasketballPlayerCommand`
comes back. Match validates pass requests against the current `PossessionVersion`; a bounded
`BasketballWorldEventStream` records pass, catch, interception, steal, shot and restart results for
debugging and future external policy/surrogate export.

The outer `BasketballMatch` now owns configurable core FIBA enforcement for out-of-bounds,
24/14-second shot clock, eight-second backcourt advance, backcourt return, offensive three
seconds, and conservative traveling/double-dribble detection. See
`Docs/RULES_AND_BALL_HANDLING.md`.

Not completed: polished tactical playbooks, screens, full foul/free-throw officiating,
substitutions, dedicated steal/tip animations, full 5v5 runtime allocation/profiler sign-off,
and low-frequency quality acceptance. The current rule AI is a deterministic integration baseline,
not a claim of finished basketball tactics. Play Mode and Test Runner validation remain with the
project owner.

See `Docs/PLAYER_AI_INTEGRATION.md` for the current control contract, the mapping from the
original controller/series, and the recommended boundary for future player and team AI.
See `Docs/MULTIPLAYER_BALL_SYSTEM_AUDIT.md` for the current possession/pass/steal data flow,
model-capability boundary, and the manual validation checklist.
See `Docs/COURT_SHOOTING_TEAM_AI.md` for court axes, scoring, match modes, AI behavior and the
current owner-run validation scenarios.
See `Docs/TEAM_PREFAB_AND_AI_TUNING.md` for the Home/Away prefab hierarchy,
per-player power limits, AI cadence tuning and body-contact model.
See `Docs/RULES_AND_BALL_HANDLING.md` for rule enforcement, immediate Shoot/Pass release,
protective dribble and steal-window tuning.

## Licensing

This remake retains the original project's research/education context and source attribution.
Review the upstream AI4Animation and motion-data licenses before redistribution or commercial
use; the original assets are not presented here as unrestricted commercial content. The visible
CrowdEyes-Twin athlete includes CC0 and CC BY components documented in its bundled third-party
notice and must retain the applicable attribution when redistributed.
