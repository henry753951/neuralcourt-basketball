# Controls

Implementation updated: 2026-07-16; Play Mode validation pending

## Keyboard and mouse (primary)

| Input | Action |
|---|---|
| `Tab` | Cycle active on-court players; camera smoothly retargets |
| `U` | Toggle between the telemetry/debug HUD and the aligned gameplay controls HUD |
| `P` | Toggle Free Cam; while active every on-court player is controlled by rule AI |
| `WASD` | Camera-relative walk: W forward, A/D strafe, S backpedal; facing stays aligned to the camera view |
| Mouse movement | Orbit camera yaw/pitch |
| Mouse wheel | Zoom camera distance |
| `R` | Recenter camera behind the character |
| `Esc` | Release/lock cursor; left-click locks it again |
| Hold `Shift` + `WASD` | Sprint in the requested camera-relative direction and turn the character toward that running direction |
| `Q` / `E` | Original keyboard turn control |
| `Space` | Shoot only |
| Carrier + `Ctrl` | Open teammate-only pass targeting; look at a teammate to lock |
| Carrier + `Ctrl` + left click | Immediately commit one pass to the locked teammate |
| Opponent has ball + left mouse | Repeated short neural Hold/reach pulses; only real hand-ball contact can steal or tip |
| Teammate has ball + hold `Ctrl` | Call for pass; raises AI target preference but does not force a pass |
| Loose/in-flight ball + hold `Ctrl` | Original Hold/Catch control for pickup or reception |
| Hold right mouse + mouse movement | Ball Control Mode; camera orbit pauses while held |

P1-P5 are Team A and P6-P10 are Team B in 5v5. The cyan ground ring marks the controlled player and the
gold overhead ring marks a valid same-team pass target. Opponents are never valid pass targets.
Steal is a separate runtime intent because the pretrained model has no learned Steal label.
The defender receives the real shared ball pose and a short pulse of the original Hold-style
input. There is no proxy ball and no distant attachment. A clean hand-ball contact changes
authority at the ball's existing world pose; a weaker touch gives physics one finite deflection
and becomes `Loose` or `Contested`. Pass targeting never writes the Ball Control target. With the default `SelectedPlayerWithRuleAI` mode, unselected
on-court players are controlled by the rule AI. `SelectedPlayerOnly` restores the old standby
behavior; `FullRuleAI` makes Tab camera-only and ignores gameplay keys.

In Free Cam, mouse controls view direction, `WASD` flies, `Q`/`E` moves down/up and `Shift`
increases speed. Press `P` again to smoothly return to the selected player. The Free Cam HUD
replaces both normal HUD styles while this mode is active.

The intended receiver begins moving toward the predicted catch point during pass preparation.
It turns toward the passer and enters a CatchReady Hold-style stance without targeting the
distant ball. Direct Hold/Catch starts after the real ball is in flight and approaching. During
an interception contest, the receiver follows the live loose ball; unresolved contests become
Loose Ball instead of remaining stuck.

The third-person camera reads only the rendered player `Transform`. It never writes camera
rotation or interpolation state back into the 30 Hz neural simulation. In walk mode, camera
heading becomes the model-facing target while WASD remains an independent local strafe/backpedal
trajectory. Sprint mode instead uses the requested movement heading as the model-facing target.
Both paths still use the original Turn/root-trajectory controls rather than rotating the rendered
character Transform directly.

Shoot is a latched one-shot command: once accepted, the central possession manager carries the
release window through a missed input/neural tick and commits exactly one physics release. A
committed shot cannot be converted into an instant secure steal before release. Rule violations
enter a short visible dead-ball pause before the opponent restart, so a whistle no longer appears
as an unexplained mid-dribble teleport to another player.

## Gamepad (secondary compatibility)

The original two-stick mapping remains selectable from the on-screen UI, but keyboard/mouse is
the default. Gamepad is not the primary Phase 2 workflow.

## Match and AI modes

Set these fields on `BasketballMatchController` before entering Play Mode:

| Setting | Meaning |
|---|---|
| `Match Mode = OneOnOne` | One active GPU/gameplay slot per team |
| `Match Mode = ThreeOnThree` | Three active slots per team |
| `Match Mode = FiveOnFive` | Five active slots per team |
| `Match Mode = Custom` | Independently choose Team A/B active counts |
| `SelectedPlayerOnly` | Current Tab player uses keyboard/mouse; everyone else waits except catch preparation |
| `SelectedPlayerWithRuleAI` | Current Tab player is human; all other active players use rule AI |
| `FullRuleAI` | All active players use rule AI; Tab only changes the observed player |

All modes still execute the fixed ten-row GPU model. An inactive slot is disabled in scene
gameplay and excluded from pass, Rival, catch, steal and loose-ball arbitration; it is retained
only to preserve the deployed Batch10 tensor shape. Rule AI produces `BasketballIntent` and calls
the existing pass/possession APIs. It does not write bones, recurrent model output, Owner, ball
Transform or Rigidbody flight.
