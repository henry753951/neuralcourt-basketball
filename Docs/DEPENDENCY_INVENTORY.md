# SIGGRAPH 2020 Basketball Dependency Inventory

Last verified: 2026-07-13

## Scope and method

This inventory targets the interactive scene at:

`Reference/AI4Animation-master/AI4Animation/SIGGRAPH_2020/Unity/Assets/Demo/Basketball/BasketballDemo.unity`

The Unity 2019 project uses binary asset serialization. The scene and `BasketballModel.asset` were inspected through their embedded Unity type trees. C# dependencies were located from the indexed code graph and then checked against the scene's external GUID table. This avoids treating filename matches as proof of a runtime dependency.

## Classification summary

| Area | Interactive Basketball | Generative Sampling | Quadruped | Training/editor pipeline |
|---|---:|---:|---:|---:|
| BasketballController and ball runtime | Required | Reused by generative scene | No | No |
| Canonical 26-bone actor and basketball series | Required | Required | Similar framework, different rig/model | Exporter constructs equivalent data |
| ExpertModel topology and BasketballModel buffers | Required | Required | Different model asset/buffers | Training produces buffers |
| GenerativeControl network/controller/model | No for reference demo | Required | No | Generative training only |
| UltimateIK | Required for reference-compatible contacts | Required | Controller-dependent | No |
| Quadruped controller/model/mocap scenes | No | No | Required | Quadruped export/training |
| DataProcessing, importers, exporters, Python models | No | No | No at runtime | Required only to reproduce training |
| Legacy post-processing and demo menu | No | No | No | No |

## Interactive scene composition

The scene contains 888 serialized objects: 355 GameObjects, 335 Transforms, 44 MonoBehaviours, 32 MeshRenderers, 32 MeshFilters, 14 CapsuleColliders, 13 Lights, five MeshColliders, five SphereColliders, nine BoxColliders, one Camera, and one Rigidbody.

Root objects:

| Root | Important contents |
|---|---|
| `World` | Ground/court, walls, primitive meshes, colliders, and lights |
| `Player` | Trigger CapsuleCollider, ExpertActivation, ExpertModel, Actor, BasketballController, PrimitiveCharacter |
| `Ball` | Built-in sphere mesh, Ball component, Rigidbody, SphereCollider, physics material |
| `Camera` | Original CameraController, FPS display, legacy post-processing |
| `Canvas` | Original keyboard/gamepad help and demo UI |
| `EventSystem` | Legacy uGUI input modules |

The basketball character is not an external skinned FBX dependency in this scene. The complete source transform hierarchy is embedded, and `PrimitiveCharacter` renders a simplified body from scene-local primitives. The basketball court is also scene-local primitive geometry. For the reference remake, preserve the transform hierarchy and proportions before considering a visual character replacement.

## Canonical actor skeleton

The serialized `Actor.Bones` array contains exactly 26 entries in this order:

| Index | Parent | Transform name |
|---:|---:|---|
| 0 | -1 | `Player 01:Hips` |
| 1 | 0 | `Player 01:LeftUpLeg` |
| 2 | 1 | `Player 01:LeftLeg` |
| 3 | 2 | `Player 01:LeftFoot` |
| 4 | 3 | `Player 01:LeftToeBase` |
| 5 | 4 | `Player 01:LeftFootEnd` |
| 6 | 0 | `Player 01:RightUpLeg` |
| 7 | 6 | `Player 01:RightLeg` |
| 8 | 7 | `Player 01:RightFoot` |
| 9 | 8 | `Player 01:RightToeBase` |
| 10 | 9 | `Player 01:RightFootEnd` |
| 11 | 0 | `Player 01:Spine` |
| 12 | 11 | `Player 01:Spine1` |
| 13 | 12 | `Player 01:Spine2` |
| 14 | 13 | `Player 01:Spine3` |
| 15 | 14 | `Player 01:LeftShoulder` |
| 16 | 15 | `Player 01:LeftArm` |
| 17 | 16 | `Player 01:LeftForeArm` |
| 18 | 17 | `Player 01:LeftHand` |
| 19 | 14 | `Player 01:Neck` |
| 20 | 19 | `Player 01:Neck1` |
| 21 | 20 | `Player 01:Head` |
| 22 | 14 | `Player 01:RightShoulder` |
| 23 | 22 | `Player 01:RightArm` |
| 24 | 23 | `Player 01:RightForeArm` |
| 25 | 24 | `Player 01:RightHand` |

Auxiliary transforms used by IK include `Player 01:LeftBallAux` and `Player 01:RightBallAux`. The full imported hierarchy also contains many face, twist, and helper transforms, but those are not entries in the model's 26-bone posture feature array.

## Required runtime source groups

Paths below are relative to the old SIGGRAPH 2020 Unity `Assets` directory. They are reference sources, not a license to copy their entire parent folders.

### Basketball runtime

| Source | Role | Reference import decision |
|---|---|---|
| `Demo/Basketball/Runtime/BasketballController.cs` | Control, exact Feed/Read order, recurrent state, ball/contact logic, IK calls | Required behavior reference; port deliberately |
| `Demo/Basketball/Runtime/Ball.cs` | Ball transform and Rigidbody velocity bridge | Required; replace with explicit authority state wrapper while preserving behavior |
| `Demo/Basketball/Runtime/ResolveCollision.cs` | Ball/body penetration correction | Required for reference behavior |
| `Demo/Basketball/Runtime/Ball.physicMaterial` | Ball collision response | Required values |
| `Demo/Basketball/Runtime/BasketballModel.asset` | Complete pretrained model, 58 embedded buffers | Required unchanged source data |
| `Demo/Basketball/BasketballDemo.unity` | Canonical hierarchy, court, ball, camera/UI reference | Required as extraction/visual reference; do not blindly make it the final architecture |

`AttachObject.cs` is present in the folder but is not directly referenced by the interactive scene and is not part of the neural loop.

### Animation state and control

| Source | Role |
|---|---|
| `Scripts/Animation/NeuralAnimation.cs` | Original Update-driven Feed/Predict/Read lifecycle and 30 fps setting |
| `Scripts/Animation/Actor.cs` | Actor and nested Bone representation, hierarchy utilities, pose/velocity state |
| `Scripts/Animation/Controller.cs` | Legacy gamepad/keyboard state and action logic |
| `Scripts/Animation/TimeSeries.cs` | 6 past keys, pivot, 6 future keys, resolution 5 |
| `Scripts/Animation/ComponentSeries.cs` | Base class for all derived runtime series |
| `Scripts/Animation/Series/RootSeries.cs` | Root transforms, directions, velocities, collision resolution |
| `Scripts/Animation/Series/DribbleSeries.cs` | Ball trajectory/control, carrier/interactor features |
| `Scripts/Animation/Series/StyleSeries.cs` | `Stand`, `Move`, `Dribble`, `Hold`, `Shoot` channels |
| `Scripts/Animation/Series/ContactSeries.cs` | `Left Foot`, `Right Foot`, `Left Hand`, `Right Hand`, `Ball` channels |
| `Scripts/Animation/Series/PhaseSeries.cs` | Same five local-phase channels and alignment features |

Sensor classes under `Scripts/Animation/Sensors` and `PID.cs` are not used by the interactive basketball scene.

### Inference and parameters

| Source | Role | Port decision |
|---|---|---|
| `Scripts/DeepLearning/NeuralNetwork.cs` | Input/output cursors and abstract network lifecycle | Contract reference |
| `Scripts/DeepLearning/Native/Parameters.cs` | ScriptableObject containing named float buffers | Import/data reference |
| `Scripts/DeepLearning/Native/Models/ExpertModel.cs` | Gating, Softmax, expert blend, dense layers, ELU, normalization | Required mathematical reference |
| `Scripts/DeepLearning/Native/NativeNetwork.cs` | Native matrix allocation/layer/blend wrappers | Historical source reference only |
| `Scripts/DeepLearning/Matrix.cs` | Native matrix handle wrapper | Historical source reference only |
| `Scripts/Plugins/Eigen/Eigen.cs` | P/Invoke declarations | Excluded from Unity 6 runtime |
| `Scripts/Plugins/Eigen/Eigen.dll` | Windows native Eigen implementation | Excluded from Unity 6 runtime |
| `AI4Animation/Plugins/Source/Eigen/Eigen.cpp` | Authoritative formulas and matrix operations | Mathematical reference outside Unity project |

The old Windows DLL is 69,120 bytes. Its Unity 2019 importer targets obsolete platforms and it is
not shipped. The accepted runtime model is the fixed ONNX consumed only by GPUCompute.

### IK, math, and debug support

| Source group | Interactive requirement |
|---|---|
| `Scripts/Tools/UltimateIK/UltimateIK.cs` | Required reference solver for body, feet, hands, and head |
| `Scripts/Tools/UltimateIK/GenericIK.cs` | Not used by the scene's BasketballController path |
| `Scripts/Extensions/ArrayExtensions.cs` | Original logic helpers (`First`, `Last`, Gaussian, append) |
| `Scripts/Extensions/Vector2Extensions.cs` | Phase/vector helpers |
| `Scripts/Extensions/Vector3Extensions.cs` | Root-relative transforms and XZ helpers |
| `Scripts/Extensions/QuaternionExtensions.cs` | Rotation helpers |
| `Scripts/Extensions/Matrix4x4Extensions.cs` | Position/rotation extraction and relative transforms |
| `Scripts/Extensions/ValueExtensions.cs` | Ratio, smoothing, activation curves |
| `Scripts/Extensions/LayerMaskExtensions.cs` | Layer-mask helpers where retained |
| `Scripts/Utility/Utility.cs` | Phase math, interpolation, timing/reference helpers |
| `Scripts/Utility/PrimitiveCharacter.cs` | Original simplified player rendering |
| `Scripts/Tools/UltiDraw/UltiDraw.cs` plus shader/font | Optional debug visualization; must allocate nothing when disabled |

Only methods actually used by runtime code should be ported into the maintained assemblies. Editor-only utilities and old debug GUI dependencies should not force large folder imports.

## Direct external assets referenced by BasketballDemo.unity

Custom scripts:

- `Demo/Basketball/Runtime/ResolveCollision.cs`
- `Scripts/ExpertActivation.cs`
- `Scripts/DeepLearning/Native/Models/ExpertModel.cs`
- `Scripts/Animation/Actor.cs`
- `Demo/Basketball/Runtime/BasketballController.cs`
- `Scripts/Utility/PrimitiveCharacter.cs`
- `Scripts/Utility/Stick.cs`
- `Demo/UI/BasketballDemo_UI.cs`
- `Demo/UI/SceneLoader.cs`
- `Scripts/Camera/CameraController.cs`
- `Scripts/Utility/FPS.cs`
- `Resources/PostProcessing/PostProcessing/Runtime/PostProcessingBehaviour.cs`
- `Demo/Basketball/Runtime/Ball.cs`

Data and presentation assets:

- `Demo/Basketball/Runtime/BasketballModel.asset`
- `Demo/Basketball/Runtime/Ball.physicMaterial`
- `Resources/Materials/Grey.mat`
- `Resources/Materials/Black.mat`
- `Resources/Materials/Checkerboard.mat`
- `Resources/Materials/Gold.mat`
- `Resources/PostProcessing.asset`
- `Scripts/Tools/UltiDraw/Resources/Fonts/Coolvetica.ttf`
- Unity built-in meshes/resources and legacy uGUI scripts.

For Unity 6, the post-processing profile and legacy demo UI are presentation-only. The required geometry/material values will be recreated with URP-compatible materials rather than importing the complete legacy post-processing package.

## Ball reference values

- `Ball.Radius`: `0.125 m`
- SphereCollider radius: `0.11322755 m`
- Rigidbody mass: `0.62369 kg`
- Gravity: enabled
- Kinematic: false
- Interpolation: enabled
- Collision detection: `Continuous` (Unity 2019 serialized enum value `1`)
- Player root CapsuleCollider: radius `0.6666667`, height `5`, trigger enabled

The original controller directly writes ball transform while also writing Rigidbody velocity. The remake must reproduce the visible result through explicit authority states so that later fixed-step physics does not fight neural control.

## Generative Sampling only

Exclude from the first interactive remake:

- `Demo/Basketball/GenerativeDemo.unity`
- `Demo/Basketball/Runtime/GenerativeController.cs`
- `Demo/Basketball/Runtime/GenerativeModel.asset`
- `Scripts/DeepLearning/Native/Models/GenerativeControl.cs`
- `DeepLearning/Weights/GenerativeController/*`
- Generative training/model code under the SIGGRAPH 2020 `DeepLearning` tree

The `BasketballController` source contains an optional `GenerativeControl` branch, but the inspected interactive scene has `GenerativeControl = false` and no generative model component. The maintained reference controller should not require this optional backend to compile or run.

## Quadruped only

Exclude from Basketball runtime:

- `Unity/Assets/Demo/Quadruped/**`
- `QuadrupedController.cs` and `QuadrupedModel.asset`
- Quadruped motion-capture scenes/assets
- `DeepLearning/Weights/QuadrupedController/**`
- Quadruped exporter/training setup

The shared concepts (`TimeSeries`, phase channels, ExpertModel) do not make the quadruped rig, model, or data a basketball dependency.

## Editor training/data pipeline only

Exclude from runtime assemblies and player builds:

- `Unity/Assets/Scripts/DataProcessing/**`
- MotionEditor, MotionProcessor, importers, exporters, modules, and editor windows
- `DeepLearning/Models/**`, datasets, training scripts, and optimizers
- Raw/processed motion-capture assets used only to export training features
- Socket inference code

The original repository notes that basketball motion-capture data is not distributed. This does not block runtime recreation because the complete pretrained `BasketballModel.asset` buffers are present.

## Licensing and provenance

The source repository states that the project is for research or education and is not freely available for commercial use or redistribution. Motion-capture data is identified as CC BY-NC 4.0. All imported code/data must retain provenance, and the final README must repeat the original restrictions. Third-party assets such as legacy post-processing have separate license files and should be avoided unless actually required.

## Import strategy

Do not copy the old `Assets` tree. Import in this order:

1. Model data and numerical tests.
2. Canonical skeleton and actor metadata.
3. Minimal runtime series/control/ball behavior.
4. Minimal court and URP-compatible presentation.
5. Legacy-compatible IK.
6. Optional debug visuals and UI.

Every import batch must preserve a reviewable diff and pass Unity compile/Console/Play Mode validation before the next batch.
