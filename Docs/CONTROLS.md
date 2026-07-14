# Controls

Implementation updated: 2026-07-15; Play Mode validation pending

## Keyboard and mouse (primary)

| Input | Action |
|---|---|
| `Tab` | Cycle P1 -> P2 -> P3; camera smoothly retargets |
| `WASD` | Camera-relative movement; character turns toward that heading while moving |
| Mouse movement | Orbit camera yaw/pitch |
| Mouse wheel | Zoom camera distance |
| `R` | Recenter camera behind the character |
| `Esc` | Release/lock cursor; left-click locks it again |
| `Shift` | Sprint while moving forward |
| `Q` / `E` | Original keyboard turn control |
| `Space` | Shoot only |
| Carrier + `Ctrl` | Open teammate-only pass targeting; look at a teammate to lock |
| Carrier + `Ctrl` + tap left mouse | Pass fake; release remains blocked |
| Carrier + `Ctrl` + hold left mouse | Commit a targeted pass after 0.2 seconds |
| Non-carrier + left mouse | Steal/tip intent; continue holding to secure a loose ball |
| `Ctrl` without left mouse | Original Hold/Catch control |
| Hold right mouse + mouse movement | Ball Control Mode; camera orbit pauses while held |

P1 and P2 are Team A; P3 is Team B. The cyan ground ring marks the controlled player and the
gold overhead ring marks a valid same-team pass target. Opponents are never valid pass targets.
Steal is a separate runtime intent because the pretrained model has no learned Steal label.
A defender's neural state uses an invisible reach-clamped proxy ball while left mouse is held,
but the real hand must still reach the real ball. Touch and Secure remain separate; clean contact
can stay near the defender for attachment, while a weak tip may become `Loose`. Pass targeting
never writes the Ball Control target. Unselected players stand idle unless they are the intended
receiver of a pass being prepared or already in flight.

The intended receiver begins moving toward the predicted catch point during pass preparation.
It turns toward the passer and enters a CatchReady Hold-style stance without targeting the
distant ball. Direct Hold/Catch starts after the real ball is in flight and approaching. During
an interception contest, the receiver follows the live loose ball; unresolved contests become
Loose Ball instead of remaining stuck.

The third-person camera reads only the rendered player `Transform`. It never writes camera
rotation or interpolation state back into the 30 Hz neural simulation. Camera rotation alone
does nothing to the model; a non-zero WASD intent converts the heading error into the model's
original Turn/trajectory controls.

## Gamepad (secondary compatibility)

The original two-stick mapping remains selectable from the on-screen UI, but keyboard/mouse is
the default. Gamepad is not the primary Phase 2 workflow.

## Future AI control

Gameplay consumes `BasketballIntent`, not raw key codes. A future path-planning AI should
provide intents (or use the existing controller override seam) while keeping its own route state
outside the pretrained neural model. It must not impersonate keyboard input or reuse an agent's
recurrent animation state.
