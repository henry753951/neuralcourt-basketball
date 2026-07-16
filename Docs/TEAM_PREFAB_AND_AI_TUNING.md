# Team Prefab and AI Tuning

## Reproducible hierarchy

`BasketballDemo.unity` now installs one outer prefab:

```text
Teams (BasketballTeams.prefab)
├─ Home (BasketballTeamGroup)
│  ├─ Player 1 (BasketballPlayer.prefab)
│  └─ Player 2 ... Player 5
└─ Away (BasketballTeamGroup)
   ├─ Player 6 (BasketballPlayer.prefab)
   └─ Player 7 ... Player 10
```

Rebuild it with `Tools > AI4Animation > Build CrowdEyes-Twin 5v5 Basketball Demo`.

The builder creates or updates:

- `BasketballPlayerVisual.prefab`: Humanoid presentation only.
- `BasketballPlayer.prefab`: canonical neural rig, input, debug, Humanoid visual and body contact.
- `BasketballTeams.prefab`: Home/Away groups and ten nested player prefab instances.

Scene-only Ball and Camera references are deliberately absent from the reusable
player prefab. The builder wires them after installing the Teams prefab.

## Where to tune values

Select `Teams/Home` or `Teams/Away` to edit shared team limits:

- base maximum pass release speed;
- base maximum shot release speed;
- AI maximum effective pass distance;
- AI maximum effective shot distance.

Select an individual Player only for values that are genuinely different:

- Player Index;
- Team ID;
- Jersey Number;
- Maximum Power.

`Maximum Power = 1.0` uses the team values unchanged. A value of `0.85` gives
that player 85% of the team's pass/shot release speed and effective range. It
does not modify the neural network or feature layout.

Team AI cadence and tactics remain on `BasketballMatch > Rule-based Team AI`,
because they are match-wide policy settings. Pass trajectory settings remain in
the shared `BasketballRuntimeSettings` asset.

## Contact model

Each player prefab uses a 1.72 m non-trigger body capsule and a kinematic
Rigidbody. Neural roots receive a bounded recurrent-state correction so players
cannot occupy the same torso space. The torso also collides with a free ball and
rejects steals whose hand-to-ball path crosses the carrier body. This avoids an
unstable per-limb Rigidbody ragdoll on top of the neural skeleton.

## Manual acceptance checks

1. Confirm the hierarchy above in `BasketballDemo.unity`.
2. In Full Rule AI, verify players run to spacing, defense and loose-ball targets.
3. Let one AI catch a pass and confirm it does not immediately return the ball.
4. Lower one player's Maximum Power and repeat the same long pass and shot.
5. Verify normal AI passes are Chest/Lead and visibly flatter; Lob is explicit only.
6. Drive two opponents together and confirm their torso roots do not overlap.
7. Try stealing through the carrier torso, then from the exposed-ball side.
