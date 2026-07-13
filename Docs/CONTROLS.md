# Controls

Last verified: 2026-07-13

## Keyboard and mouse (primary)

| Input | Action |
|---|---|
| `WASD` | Camera-relative movement; character turns toward that heading while moving |
| Mouse movement | Orbit camera yaw/pitch |
| Mouse wheel | Zoom camera distance |
| `R` | Recenter camera behind the character |
| `Esc` | Release/lock cursor; left-click locks it again |
| `Shift` | Sprint while moving forward |
| `Q` / `E` | Original keyboard turn control |
| `Space` | Shoot |
| `Ctrl` | Hold/catch |
| Hold right mouse + mouse movement | Ball Control Mode; camera orbit pauses while held |

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
