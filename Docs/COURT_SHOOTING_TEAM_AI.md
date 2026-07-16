# Court, Shooting, Match Modes, and Rule AI

Implementation snapshot: 2026-07-16. Play Mode acceptance remains with the project owner.

## Court integration

`Assets/AI4AnimationRemake/Court/CrowdEyesTwin/BasketballCourt.prefab` is a clean extraction of
the required CrowdEyes-Twin court geometry. The source environment prefab is not a Runtime
dependency. The target prefab contains project-owned URP materials, the wood-floor texture,
required generated meshes, two `BasketballHoop` components and a `BasketballScoreTracker`.

- court: 28 x 15 m;
- longitudinal gameplay axis: Z;
- Team A / Team 0 attacks +Z;
- Team B / Team 1 attacks -Z;
- rim centers: approximately `(0, 3.05, +/-12.463)`;
- rim radius: 0.225 m;
- 16 capsule colliders per rim, 32 total;
- four invisible `BasketballCourtBoundary` walls keep both the physical ball and neural-root
  players inside the 28 x 15 m playing area; the default wall height is 12 m and all dimensions
  can be adjusted from the bilingual Inspector;
- third-person camera collision explicitly ignores these invisible walls, so approaching a
  sideline does not force the orbit camera to zoom into the player;
- decoded root collision correction translates the complete canonical pose and a controlled
  ball together, preventing a stopped trajectory from leaving its rendered skeleton outside;
- backboard target boxes use a dedicated dark matte, non-emissive URP material; they no longer
  share the glossy orange rim material;
- no copied CrowdEyes-Twin cameras, synthetic-data scripts, HDRP materials or gameplay managers.

## Shot and score flow

```text
Space or AI Shoot decision
  -> WorldFacing aligns the neural root toward the team's attack hoop
  -> original pretrained Shoot style remains latched until model release conditions
  -> BasketballShotPlanner solves one projectile velocity from the actual model ball pose
  -> BasketballPossessionManager sets ShotFlight and releases the Rigidbody once
  -> no homing, per-frame velocity rewrite or target tracking during flight
  -> BasketballScoreTracker accepts only an above-to-below rim-plane crossing
  -> points use the stored release position and FIBA arc/corner geometry
  -> HUD score update
  -> delayed inbound possession to the conceding team through ResetPossession
```

The restart clears stale pass, receiver, shot, steal and contested state and seeds each agent's
ball history with the same authoritative inbound pose. A missed shot remains physical and is
handled by the existing catch, contested and loose-ball arbitration.

## Match modes and fixed GPU capacity

`BasketballMatchController` exposes `OneOnOne`, `ThreeOnThree`, `FiveOnFive`, and `Custom`.
The deployed ONNX remains Batch10 in every mode. Active roster filtering is gameplay-side:

- active players are valid Tab, pass, Rival, catch, steal, defense and loose-ball candidates;
- inactive slots have inactive GameObjects, so their renderers, colliders and Update methods do
  not affect the court;
- the shared GPU scheduler can still call their controller buffers directly to preserve the
  fixed ten-row tensor contract;
- modes do not duplicate ball, score, pass, shot or possession systems.

The Inspector can also apply a mode during Play Mode. The request waits for any in-flight GPU
readback, blocks the scheduler from dispatching a stale follow-up batch, then atomically resets the
active mask, deterministic formation, per-agent recurrent pose/contact/phase state, possession,
camera focus and optional score. The shared Batch10 model and weights remain alive.

## Control modes

- `SelectedPlayerOnly`: old debug behavior; Tab player is human, others stand by except required
  catch preparation.
- `SelectedPlayerWithRuleAI`: default; Tab player is keyboard/mouse controlled, every other active
  player uses rule AI.
- `FullRuleAI`: all active players use rule AI; Tab changes only camera/HUD focus.

## Current rule AI

`BasketballRuleBasedTeamAI` is deliberately small and allocation-free during decisions. It uses
simulation state rather than interpolated visual transforms and can:

- advance the carrier toward the correct hoop;
- align and request the existing Shoot skill;
- choose a teammate and request the existing Lead Pass skill;
- reject heavily blocked passing lanes and choose one defender to move toward a predicted
  interception point during PassFlight;
- move the intended receiver to the predicted catch point and enter Hold only when catchable;
- place off-ball attackers across deterministic spacing targets;
- reserve one off-ball attacker as a lane Cutter and one as Transition Safety in 3v3/5v5;
- assign defenders by active team rank and position them between matchup and defended hoop;
- reserve one Help Defender in three-player-or-larger modes instead of sending every defender;
- request Steal only in a short, cooled-down window when the assigned carrier's real ball is
  close, exposed and in front of the defender;
- protect the live dribble on the side opposite the nearest defender and change hand when that
  defender crosses the protected side; this is an original-model Ball Control input, never a
  direct ball Transform write;
- read the authoritative shot clock and leave the attacking paint before a three-second call;
- build one shared team plan per render frame;
- select at most one loose-ball/rebound chaser per team while others transition;
- predict the next court contact from the live Rigidbody position, velocity and gravity, then
  choose rebound/loose-ball responsibility by estimated arrival instead of current ball position;
- recompute that recovery point every decision frame, so rim/backboard deflections replace the
  previous prediction without steering the physical ball;
- assign one baseline box-out defender during ShotFlight;
- clamp policy targets inside the FIBA court and apply allocation-free local player avoidance to
  ordinary movement while preserving real contact for catches, steals and loose balls;
- reserve only one defender as a pass interceptor during flight, while the intended receiver keeps
  exclusive receiving responsibility on offense; the interceptor must now reach a sampled point
  on the live ballistic trajectory within the configured reaction/slack window.

It does not move a ball or skeleton, guarantee catches/steals, or bypass `PossessionVersion`.
Pass and shot flight remain pure Rigidbody flight.

The rule AI now implements `IBasketballDecisionPolicy`. It reads a fixed-capacity
`BasketballWorldObservation` and returns `BasketballPlayerCommand`; it no longer calls
`RequestPass` itself. Match validation checks the actor, target and `PossessionVersion` before the
central possession manager sees the request. `BasketballWorldEventStream` records the resulting
pass, catch, interception, steal, shot and restart outcomes in a bounded ring buffer.
External policies may also implement `IBasketballWorldEventObserver` to receive those delayed
outcomes once in sequence order before their next decision frame.
The built-in rule policy uses the same path to discard stale pass, shot and possession caches
after physical outcomes instead of waiting for its previous cooldown to expire.
Every player snapshot includes a `BasketballActionMask`; for example, 1v1 owners cannot request an
illegal pass while 3v3/5v5 owners can. `BasketballSkillTelemetryRecorder` can optionally persist
events as versioned JSONL under `Application.persistentDataPath/BasketballTelemetry`; it is off by
default and never participates in gameplay. Schema v2 records both actual ball state and the
planned catch/hoop target plus pass variant for offline skill-surrogate analysis.
Assign another `MonoBehaviour` implementing `IBasketballDecisionPolicy` to BasketballMatch's
`Decision Policy Behaviour` field to replace the built-in policy without changing neural control.

## Known limits

- no screens, picks, polished playbook, personal/shooting fouls, free throws or substitutions;
- core out-of-bounds, 24/14-second shot clock, eight-second advance, return-to-backcourt,
  offensive three seconds and conservative traveling/double-dribble calls are implemented by
  `BasketballRulesManager`;
- help defense, recovery assignment and box-out are deterministic baselines; the landing predictor
  uses current free-flight gravity and is deliberately recalculated after rim/backboard collisions,
  but it does not predict a future collision before that collision occurs;
- switch communication, screens and picks remain unimplemented;
- runtime formation reset is deterministic rather than a substitution system; it always starts
  Team A on negative court Z, Team B on positive court Z, and gives Team A the reset possession;
- spacing is deterministic rather than NavMesh/path-planned;
- pass and shot choices are heuristic, not learned; the new policy interface is data-compatible
  but an external transport/serializer has not been added;
- the pretrained 2020 model still limits the exact look of pass, catch and steal poses;
- dedicated Play Mode stability, quality and Profiler acceptance has not been run by Codex.

## Owner Play Mode checklist

1. Open `Assets/Scenes/BasketballDemo.unity` and select `BasketballMatch`.
2. Start with `FiveOnFive + SelectedPlayerWithRuleAI`.
3. Confirm Tab still switches only active players and camera transitions remain smooth.
4. Confirm unselected teammates spread out and defenders approach matchups without everyone
   chasing the ball.
5. Shoot from both teams and confirm each faces the opposite hoop.
6. Confirm ball flight is not guided after release and can collide with rim/backboard.
7. Confirm made baskets update `A score : score B`, then grant the conceding team possession.
8. Confirm misses remain physical and the selected rebounders run toward the changing predicted
   landing point rather than directly under the airborne ball; after a rim/backboard bounce, their
   target should visibly update without changing the ball trajectory.
9. During a pass, confirm only a defender who can plausibly reach the actual arc attempts an
   interception; a distant defender should keep the normal defensive assignment.
10. Test `OneOnOne`, `ThreeOnThree`, and `FiveOnFive` in separate Play Mode runs; inactive athletes
   must be invisible, non-colliding and unavailable as targets.
11. Test `FullRuleAI` for at least five minutes and report any stuck PassPreparing, ShotFlight,
    CatchBlend, Contested, repeated possession swap, non-finite pose, or GPU readback error.

EditMode contract tests include court/team mapping, FIBA two/three-point geometry, shot target
reconstruction and recovery/interception trajectory math. The project owner runs Test Runner.
