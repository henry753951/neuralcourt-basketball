# Rules, Ball Handling, Passing, and Steals

Implementation snapshot: 2026-07-16. Play Mode acceptance remains with the project owner.

## Single authority

`BasketballRulesManager` lives on the outer `BasketballMatch` object. It never writes a Player's
`Carrier` flag directly. Every violation selects one legal opposing player and calls
`BasketballPossessionManager.ResetPossession`, which clears stale pass, catch, shot, contested and
steal state before publishing the violation and restart events.

The same component and values are used by 1v1, 3v3, 5v5, and custom active masks. The ten Player
prefab instances do not carry independent clocks or rule settings.

## Enforced core rules

- out of bounds: the team that last controlled or touched the ball loses possession;
- shot clock: 24 seconds after a change of team control, 14 seconds after an offensive rebound;
- backcourt: eight seconds to establish frontcourt control, then no return across the center line;
- offensive three seconds: each active attacking player has an independent paint timer;
- traveling: a conservative call only after explicit `Hold` remains active and the controlled
  root moves beyond the configured distance;
- double dribble: a conservative call only when explicit Hold has ended the dribble and the ball
  ground-contact channel later crosses the configured contact threshold again.

Travel and double-dribble deliberately use conservative intent/contact gates. The SIGGRAPH 2020
model does not output referee labels or foot-step counts, so aggressively inferring violations
from pose alone would create false calls during valid neural dribbles.

The current rule layer does not claim full FIBA officiating. Personal/shooting fouls, free throws,
team-foul bonus, jump balls, substitutions and game-clock periods still require dedicated match
systems. Player capsules and recurrent-root separation prevent body overlap now, and torso
occlusion rejects a steal hand path through the carrier, but ordinary legal contact is not
automatically called a foul.

## Shoot and pass input

- `Space` latches one Shoot immediately. The neural pose and facing continue during a short
  release window, but there is no separate pre-commit wait. Natural contact release is preferred;
  a configurable deadline releases once so the ball cannot remain stuck in the hand.
- `Ctrl + left click` on a locked teammate requests one pass immediately. The former short-click
  fake and hold-to-confirm states are removed.
- pass preparation uses short Gather, Align, Push and ReleasePending phases. At release the
  planned velocity is assigned once and Rigidbody owns the complete flight.
- Chest/Lead passes use the low `Direct Pass Apex Clearance`; only an explicit Lob uses the lob
  clearance. Ball Control/Ball Target is not repurposed to steer a pass.

Tune these shared values in
`Assets/AI4AnimationRemake/Resources/Settings/BasketballRuntimeSettings.asset`. They apply to all
players and do not change the pretrained model I/O contract.

## Protective dribble and steal difficulty

When a defender enters `Protect Ball Pressure Distance`, the rule AI feeds the original model a
local Ball Control target on the opposite side. It changes the protected side when the defender
crosses over or the hand-switch interval expires. This asks the pretrained model for a different
dribble; it never teleports or guides the real shared ball.

AI Steal is now a short pulse with a cooldown. A pulse requires:

- the assigned on-ball defender is inside the attempt distance;
- the ball is in front of the defender;
- the carrier's ball is laterally/vertically exposed or has low hand contact.

The possession resolver still requires a real swept hand-ball distance, a non-occluded path,
minimum exposure, minimum hand approach and a higher aggregate contact score. Touch releases the
ball to physics; Secure has a separate delay and higher quality threshold. This preserves possible
failed tips, contested balls and loose balls instead of turning every overlap into possession.

## Owner Play checklist

1. Select `BasketballMatch > Basketball Rules` and keep all core toggles enabled.
2. In Full Rule AI, watch the shot clock reach a turnover when no shot is taken.
3. Keep a team in its backcourt for eight seconds, then separately cross center and return.
4. Observe a Cutter leave the paint before three seconds; temporarily disable its exit behavior
   only if you intentionally want to verify the violation restart.
5. Tap `Ctrl + left click`: the pass should commit once without a fake or hold delay.
6. Tap `Space`: the shot should release once and never remain suspended in PassPreparing/Possessed.
7. Pressure a carrier from its ball side and verify the AI changes the protected dribble side.
8. A defender should make one short steal attempt, recover, then wait before trying again. A body
   overlap or a hand path through the torso must not immediately transfer ownership.
9. Repeat in 1v1, 3v3 and 5v5; clocks and violation restarts must only use active players.
