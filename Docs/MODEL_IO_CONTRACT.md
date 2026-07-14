# Basketball 2020 Model I/O Contract

Last verified: 2026-07-13

This document is the compatibility contract for the SIGGRAPH 2020 Interactive Basketball pretrained model. Indices are zero-based and ranges are half-open unless shown as an inclusive table range.

## Fixed dimensions and topology

| Item | Value |
|---|---:|
| Full input feature count | 864 |
| Main motion feature count | 734 |
| Gating feature count | 130 |
| Output count | 588 |
| Experts | 8 |
| Gating topology | `130 -> 128 -> 128 -> 8` |
| Main topology per blended network | `734 -> 512 -> 512 -> 588` |
| Canonical posture bones | 26 |
| Styles | 5 |
| Contacts | 5 |
| Local phase channels | 5 |
| Runtime neural rate | 30 Hz |

Serialized ExpertModel component settings from the original interactive scene:

```text
Features = 864

Component 0:
  XDim = 130
  H1Dim = 128
  H2Dim = 128
  YDim = 8
  GatingPivot = 734

Component 1:
  XDim = 734
  H1Dim = 512
  H2Dim = 512
  YDim = 588
  GatingPivot = 0
```

## TimeSeries layout

The controller constructs:

```csharp
new TimeSeries(
    pastKeys: 6,
    futureKeys: 6,
    pastWindow: 1f,
    futureWindow: 1f,
    resolution: 5);
```

Consequences:

- KeyCount: 13.
- PivotKey: 6.
- Past samples: 30.
- Pivot sample index: 30.
- Future samples: 30.
- Total samples: 61.
- Model keys occur at sample indices `0, 5, 10, ..., 60`.
- Key timestamps are `-1, -5/6, -4/6, -3/6, -2/6, -1/6, 0, 1/6, 2/6, 3/6, 4/6, 5/6, 1` seconds.

The model is advanced once per 1/30-second neural tick. Do not substitute render `Time.deltaTime` into formulas that use `Framerate`, and do not infer a 10 Hz runtime from newer AI4AnimationPy examples.

## Ordered input contract

### Summary

| Inclusive indices | Count | Block |
|---:|---:|---|
| 0–220 | 221 | Root/dribble/style key series |
| 221–532 | 312 | Current 26-bone posture and velocities |
| 533–581 | 49 | Past-to-current ball control series |
| 582–616 | 35 | Past-to-current contacts |
| 617–629 | 13 | Rival/interactor weights |
| 630–707 | 78 | Rival gradients/directions/velocities |
| 708–733 | 26 | Rival bone distances |
| 734–863 | 130 | Local-phase gating alignment |
| **Total** | **864** | |

### 0–220: root, dribble, and style keys

For each of 13 keys in chronological order, append 17 floats:

| Per-key offset | Count | Values |
|---:|---:|---|
| 0 | 2 | Root position XZ relative to the current actor root |
| 2 | 2 | Root forward direction XZ relative to the current actor root |
| 4 | 2 | Root velocity XZ relative to the current actor root |
| 6 | 3 | Dribble pivot XYZ |
| 9 | 3 | Dribble momentum XYZ |
| 12 | 5 | Styles in exact order below |

Style channel order:

1. `Stand`
2. `Move`
3. `Dribble`
4. `Hold`
5. `Shoot`

### 221–532: current posture

For each of the 26 bones in canonical order, append 12 root-relative floats:

1. Position XYZ.
2. Forward XYZ.
3. Up XYZ.
4. Velocity XYZ.

Canonical bone order is defined in `DEPENDENCY_INVENTORY.md`. Do not use Animator humanoid ordering or hierarchy traversal order as a substitute.

### 533–581: ball history/control

For keys 0 through PivotKey inclusive (seven keys), append seven floats:

1. Ball control weight.
2. Weighted ball position XYZ, converted relative to the current actor root.
3. Weighted ball velocity XYZ, converted relative to the current actor root.

### 582–616: contact history

For the same seven past-to-current keys, append five contacts in exact order:

1. `Left Foot`
2. `Right Foot`
3. `Left Hand`
4. `Right Hand`
5. `Ball`

### 617–733: rival/interactor block

The interactive single-player demo does not instantiate a rival, but it still feeds the full block. Removing it changes every later index.

1. 13 interactor weights: indices 617–629.
2. For each of 13 keys, append interactor gradient XZ, direction XZ, and velocity XZ: indices 630–707.
3. One interactor bone-distance scalar per canonical bone: indices 708–733.

The original first loop calls `GetInteractorWeight(i)` directly, while the other time-series loops generally convert keys with `GetKey(i).Index`. The reference implementation must preserve observed behavior until a numerical test proves a correction is safe.

With no rival assigned, the original `DribbleSeries` feeds zero weights and zero
gradient/direction/velocity features, followed by `5.0` for each rival bone distance
(`2 * AreaOfInterest`, where `AreaOfInterest = 2.5`).

### 734–863: gating alignment

For each of 13 keys, then each of five phase channels, append the phase vector X and Y:

```text
phaseVector = amplitude * (sin(2*pi*phase), cos(2*pi*phase))
```

Inactive channels append `(0, 0)`.

Phase channel order:

1. `Left Foot`
2. `Right Foot`
3. `Left Hand`
4. `Right Hand`
5. `Ball`

This final 130-float slice is consumed by the gating network. The main expert network consumes only indices 0–733.

## Ordered output contract

### Summary

| Inclusive indices | Count | Block |
|---:|---:|---|
| 0–15 | 16 | Current root, dribble, and style state |
| 16–117 | 102 | Six future key states |
| 118–429 | 312 | 26-bone posture and velocities |
| 430–442 | 13 | Ball control/pose/velocity/orientation |
| 443–447 | 5 | Contacts |
| 448–587 | 140 | Current-plus-future local phase updates/states |
| **Total** | **588** | |

### 0–15: current state

| Inclusive indices | Count | Meaning |
|---:|---:|---|
| 0–2 | 3 | Root local offset: X displacement, yaw angle, Z displacement |
| 3–4 | 2 | Root velocity XZ |
| 5–7 | 3 | Current dribble pivot XYZ |
| 8–10 | 3 | Current dribble momentum XYZ |
| 11–15 | 5 | Styles: Stand, Move, Dribble, Hold, Shoot |

The root yaw value is passed to `Quaternion.AngleAxis(offset.y, Vector3.up)` and therefore follows Unity's degree convention in the legacy decoder.

### 16–117: six future keys

For each future key, append 17 floats:

1. Root position XZ relative to the newly decoded current root.
2. Root direction XZ relative to that root.
3. Root velocity XZ relative to that root.
4. Dribble pivot XYZ.
5. Dribble momentum XYZ.
6. Five style values in canonical order.

### 118–429: posture

For each canonical bone, append 12 floats:

1. Position XYZ relative to decoded root.
2. Forward XYZ relative to decoded root.
3. Up XYZ relative to decoded root.
4. Velocity XYZ relative to decoded root.

Legacy position post-processing is:

```text
position = Lerp(
    previousWorldPosition + decodedWorldVelocity / 30,
    decodedWorldPosition,
    0.5)
```

Forward and up are normalized before conversion to world space. Bone rotation is reconstructed with `Quaternion.LookRotation(forward, up)`, followed by the legacy single-child twist correction.

### 430–442: ball

| Inclusive indices | Count | Meaning |
|---:|---:|---|
| 430 | 1 | Ball control weight |
| 431–433 | 3 | Weighted ball position XYZ |
| 434–436 | 3 | Weighted ball velocity XYZ |
| 437–439 | 3 | Weighted ball forward XYZ |
| 440–442 | 3 | Weighted ball up XYZ |

When the character is the carrier and control weight is positive, position, velocity, forward, and up are divided by control weight before root-space conversion/orientation construction. Hold style blends predicted ball rotation toward identity relative to the prior ball transform.

### 443–447: contacts

Five values are clamped to `[0, 1]` and post-processed in canonical contact order. The ball channel uses a lower threshold than body channels in the legacy controller.

### 448–587: local phases

The decoder processes seven keys: current PivotKey through the six future keys. For every key and each of five phase channels, read four floats:

1. Update vector X.
2. Update vector Y.
3. State vector X.
4. State vector Y.

The update magnitude becomes amplitude. Phase is reconstructed from a stability-weighted blend between an incremental phase update and a predicted state phase. Internal phase state advances only on neural ticks; render interpolation must never feed interpolated phase back into this loop.

## Normalization and ExpertModel math

The model asset includes:

- `Xmean`: 864 floats.
- `Xstd`: 864 floats.
- `Ymean`: 588 floats.
- `Ystd`: 588 floats.

Reference formulas:

```text
Xn[i] = (X[i] - Xmean[i]) / Xstd[i]
Y[i]  = Yn[i] * Ystd[i] + Ymean[i]
```

Gating component:

```text
g0 = ELU(GW0 * Xn[734:864] + Gb0)
g1 = ELU(GW1 * g0          + Gb1)
g  = Softmax(GW2 * g1      + Gb2)
```

Main component:

```text
W0 = sum(e=0..7, g[e] * W0[e])
b0 = sum(e=0..7, g[e] * b0[e])
W1 = sum(e=0..7, g[e] * W1[e])
b1 = sum(e=0..7, g[e] * b1[e])
W2 = sum(e=0..7, g[e] * W2[e])
b2 = sum(e=0..7, g[e] * b2[e])

h0 = ELU(W0 * Xn[0:734] + b0)
h1 = ELU(W1 * h0          + b1)
Yn = W2 * h1 + b2
```

ELU is exactly:

```text
ELU(x) = max(x, 0) + exp(min(x, 0)) - 1
```

The native reference Softmax exponentiates each of the eight logits directly, sums them, and divides by the sum. It does not subtract the maximum logit. The pure C# backend uses the mathematically equivalent `Softmax(x - max(x))` formulation after long-running closed-loop tests demonstrated float overflow in the direct form. Golden-vector parity tests remain mandatory.

## Buffer names, counts, and storage order

`BasketballModel.asset` stores 58 named buffers:

- Six gating buffers: `wc000_w`, `wc000_b`, `wc010_w`, `wc010_b`, `wc020_w`, `wc020_b`.
- Forty-eight main expert buffers: for each expert `0..7`, `wc10e_w/b`, `wc11e_w/b`, and `wc12e_w/b` using the source naming convention.
- Four normalization buffers: `Xmean`, `Xstd`, `Ymean`, `Ystd`.

Important sizes:

| Buffer group | Shape per buffer |
|---|---|
| Gating layer 0 weights/bias | `128 x 130`, `128` |
| Gating layer 1 weights/bias | `128 x 128`, `128` |
| Gating layer 2 weights/bias | `8 x 128`, `8` |
| Main expert layer 0 weights/bias | `512 x 734`, `512` |
| Main expert layer 1 weights/bias | `512 x 512`, `512` |
| Main expert layer 2 weights/bias | `588 x 512`, `588` |

The original loader assigns `buffer[row * cols + col]` to matrix `(row, col)`. The compatibility backend must therefore interpret serialized arrays as row-major logical matrices even if an optimized backend later transposes or packs them internally.

The separately extracted `DeepLearning/Weights/BasketballController/*.bin` files are Git LFS pointer files in this archive, but their declared sizes agree with the embedded asset. Runtime import must use the complete float values embedded in `BasketballModel.asset`, not the pointer text.

## Sentis batch packaging

`Tools/Onnx/export_basketball_onnx.py` is an offline authoring step that reads the complete
`BasketballModel.Legacy.asset`. It validates all 58 names and lengths, then emits
`Assets/AI4AnimationRemake/Resources/Models/BasketballMoEBatch3.onnx` for Unity Sentis 2.6.1.
There is no Python Runtime dependency and no retraining, quantization, channel reordering or
model approximation.

The imported graph has a fixed input shape `[3,864]`. Each row independently executes the same
normalization, 130-feature gating network, Softmax, dynamic eight-expert blending, ELU layers and
denormalization described above. Its single `[3,596]` transport output packs:

```text
row[0..587]   = original Basketball model output
row[588..595] = the same eight gating weights, for HUD/debug telemetry only
```

Only the first 588 values enter `BasketballOutputDecoder`; the extra eight values never become
recurrent model channels. The packed layout exists solely to perform one GPU readback instead of
two. `BasketballSentisBatchTests` compares three deterministic Sentis rows against three
independent Reference evaluations on CPU and GPUCompute; execution of those tests remains an
owner-run validation step.

## Closed-loop state contract

Before inference, the feature builder reads current root, pose, velocities, ball, contacts, phase, and future series. After inference, the decoder:

1. Shifts past ball/contact/phase state.
2. Updates current root, root velocity, pivot, momentum, and styles.
3. Updates six future trajectory/style keys.
4. Updates all 26 bone poses and velocities.
5. Updates ball prediction and Rigidbody velocity when neural control is authoritative.
6. Updates contact values.
7. Updates current/future local phase states.
8. Interpolates non-key samples in the time series.
9. Applies pose and twist correction.
10. Resolves trajectory, ball, contact, and IK post-processing.

The resulting state becomes the next tick's input. Tests must therefore include multi-tick replay, not only one isolated network evaluation.

## Required numerical validation

Before declaring the reference backend complete:

- Validate all 58 buffer names and element counts.
- Compute and record deterministic hashes for imported float bytes.
- Compare normalization and renormalization against the legacy formula.
- Compare gating logits, Softmax weights, and verify their sum.
- Compare all six blended matrices/biases for a known input.
- Compare hidden layers, normalized output, and final 588 outputs.
- Run at least 300 recurrent ticks from an identical captured state and report drift.
- Keep tolerances explicit. Visual similarity alone is not numerical parity.

No golden-vector parity result has been claimed yet; this is the next Phase 1 validation task.
