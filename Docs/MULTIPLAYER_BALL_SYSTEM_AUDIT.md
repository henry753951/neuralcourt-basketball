# Multiplayer Ball System Audit

Last updated: 2026-07-15

## Confirmed current data flow

- Each player owns an independent `BasketballAgentState` containing Root, 26-bone pose and
  velocity, Pivot/Momentum dribble control, Style, Contact, Phase and ball history.
- `BasketballFeatureBuilder` still writes exactly 864 model inputs. The legacy Rival block is
  restored in its original order: 13 interactor weights, 13 root gradient/direction/velocity
  groups, then 26 rival bone distances.
- `BasketballOutputDecoder` consumes the unchanged 588 outputs. Only the current owner may apply
  decoded ball position/rotation/velocity to the shared visual ball.
- `BasketballPossessionManager` is the sole owner and ball-write authority. Per-player `Carrier`
  is only a mirrored recurrent-state flag.
- `PassFlight`, `ShotFlight`, `Loose` and `Contested` are Rigidbody-authoritative. No player may
  write or guide the ball during flight.

## Root causes removed

The previous targeted pass used view direction to overwrite Ball Target/Pivot/Momentum and then
waited for a bounded ML velocity candidate. This both changed the hold pose and could leave the
ball permanently attached. Pass preparation now uses only the existing Hold style and Future
Root facing. It never writes `BallControl`, Pivot, Momentum or BallHeight. After a short facing
window, the ballistic velocity is recomputed from the actual release position and applied once;
there is no in-flight homing or per-frame correction.

The previous steal equated first contact with ownership and reused Hold as if it were a learned
steal label. The current runtime `Steal` intent is separate. While attempting a steal, that
non-owner receives an invisible, reach-clamped proxy ball only inside its own recurrent model
state so the pose can react; the proxy is never rendered and cannot move the shared ball. Actual
touch still requires the real hand to intersect the real ball, including the hand's swept path
over one neural tick. A clean touch keeps the ball near the defender for a later Secure step;
a weaker touch applies a bounded tip and produces `Loose` or `Contested`.

The previous standby logic forced `Hold=true` on every non-selected player. For a non-carrier,
that enables horizontal, height and speed control toward the shared ball, causing remote hands
and upper body to sway or rise with it. Standby players now receive a true default Stand intent.
Only the intended receiver receives pass preparation/catch guidance. Rival input is connected
only while an explicit steal interaction is active, so other idle players are not perturbed by
an unrequested opponent interaction.

The intended receiver now starts root movement and facing during `PassPreparing`, before the
ball leaves the passer. `CatchReady` activates the original Hold style during preparation without
enabling Pivot/Momentum ball targeting, so a distant held ball cannot pull the receiver's arms.
During `PassFlight`, direct Hold activates only when the real ball is within 2.4 m or the expected
arrival is within 0.5 s. Catch still requires real hand/body range;
only the intended receiver receives a modestly larger hand/contact allowance. Because the
pretrained Hold pose does not always place a hand within the strict threshold before a short
pass arrives, the intended receiver also has a 0.70 m upper-body catch volume centered on the
live chest bone. Hand and chest tests use the real ball's swept path over the latest physics
step, preventing a fast pass from tunnelling between render frames. The volume is valid only
after release, while Catch intent is active, and while the real ball is entering it.

If an opponent and receiver both qualify, the ball enters `Contested` and remains Rigidbody
controlled. The intended receiver keeps CatchReady/Hold guidance toward the live ball while an
opponent may continue a Steal/Interception attempt. One candidate can win through the normal
quality arbitration; otherwise `Contested` expires to `Loose` after 0.8 s. Catch, interception
and Loose transitions clear the old passer/receiver/pass-plan references so stale pass state
cannot modify the new possession.

## Current input contract

- `Space`: Shoot only.
- `Ctrl` + left mouse: select teammate and immediately commit one pass.
- Left mouse while defending: Steal/Tip; continue holding to attempt Secure after a loose touch.
- `Ctrl` alone: original Hold/Catch.
- `Tab`: switch controlled player.

## Model capability boundary

The pretrained model has Stand, Move, Dribble, Hold and Shoot styles, but no Pass or Steal label.
The gameplay possession/physics system therefore does not claim a learned chest-pass or poke
animation. The pass pose uses Hold/root-facing preparation, then a match-level one-shot physical
release. Steal uses a model-only proxy for pose context plus real contact arbitration. Neither
system uses remote suction or flight guidance.

## User validation requested

1. Leave P1 selected for 20 seconds while P2/P3 are visible. Confirm idle hands and torso do not
   follow the ball or lift without a pass/steal interaction.
2. Press `Space` and confirm it only shoots; it must not enter teammate targeting.
3. Hold `Ctrl`, look at P2, click left mouse once, and confirm a single pass request.
4. Confirm the ball releases toward P2 after the shortened preparation window and that target
   selection does not move the Ball Control disk/pose.
5. During flight, move the camera and receiver. The airborne trajectory must not steer.
6. Watch P2 before release: P2 should move toward the predicted catch point and face the passer,
   then raise its catch posture only as the real ball approaches.
7. Confirm the catch changes ownership only when the real ball reaches P2's hands or chest area;
   a ball passing outside the upper body must remain physical.
8. Switch to P3, approach from front and both dribble sides, then hold left mouse. The pose may
   prepare toward the model-only proxy, but the real ball must react only after real contact.
9. A strong close touch should remain near P3 and attach only after the short Secure delay. A
   weaker edge touch may still roll loose; it must not instantly assign ownership.
10. Repeat the steal from several approach angles and report which side/height still fails to
   produce real hand-ball contact.
