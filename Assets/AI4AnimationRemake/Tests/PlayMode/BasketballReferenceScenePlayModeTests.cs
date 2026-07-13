using System.Collections;
using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace CrowdEyes.AI4Animation.Tests
{
    public sealed class BasketballReferenceScenePlayModeTests
    {
        [UnityTest]
        public IEnumerator ReferenceScene_RunsFiniteClosedLoopAtThirtyHertz()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballNeuralController controller =
                Object.FindAnyObjectByType<BasketballNeuralController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsInitialized, Is.True);
            int startTick = controller.SimulationTickCount;

            yield return new WaitForSecondsRealtime(1f);

            int delta = controller.SimulationTickCount - startTick;
            Assert.That(delta, Is.InRange(27, 33), "Reference neural simulation must remain at 30 Hz.");
            BasketballAgentState state = controller.State;
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                Assert.That(float.IsFinite(state.BonePositions[bone].x), Is.True);
                Assert.That(float.IsFinite(state.BonePositions[bone].y), Is.True);
                Assert.That(float.IsFinite(state.BonePositions[bone].z), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator ReferenceScene_ForwardIntentAdvancesRoot()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballNeuralController controller =
                Object.FindAnyObjectByType<BasketballNeuralController>();
            Vector3 start = controller.State.ActorRootPosition;
            controller.SetIntentOverride(new BasketballIntent { Move = Vector2.up });

            yield return new WaitForSecondsRealtime(1f);

            Vector3 end = controller.State.ActorRootPosition;
            controller.ClearIntentOverride();
            Assert.That(Vector3.Distance(start, end), Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator BasketballDemo_SprintRemainsFiniteConnectedAndInsideCourtForSixHundredTicks()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballNeuralController controller =
                Object.FindAnyObjectByType<BasketballNeuralController>();
            Assert.That(controller, Is.Not.Null);
            BasketballSkeleton skeleton = Object.FindAnyObjectByType<BasketballSkeleton>();
            Assert.That(skeleton, Is.Not.Null);
            float[] referenceLengths = CaptureCanonicalLengths(skeleton);
            Vector3 initialRoot = controller.State.ActorRootPosition;
            Physics.SyncTransforms();
            controller.SetIntentOverride(new BasketballIntent
            {
                Move = Vector2.up,
                Sprint = true
            });

            float worstSegmentError = 0f;
            int worstSegment = 1;
            int worstSegmentTick = 0;

            for (int tick = 0; tick < 600; tick++)
            {
                controller.SimulateTick();
                Assert.That(controller.enabled, Is.True, $"Simulation guard fired at stress tick {tick}.");
                float error = MeasureCanonicalRigError(
                    controller.State,
                    referenceLengths,
                    out int segment);
                if (error > worstSegmentError)
                {
                    worstSegmentError = error;
                    worstSegment = segment;
                    worstSegmentTick = tick;
                }
                if (tick % 30 == 29)
                {
                    yield return null;
                }
            }

            BasketballAgentState state = controller.State;
            Vector3 finalRoot = state.ActorRootPosition;
            Assert.That(Mathf.Abs(finalRoot.y - initialRoot.y), Is.LessThan(0.01f),
                "The actor root must remain on the demo ground plane.");
            Assert.That(Vector3.Distance(initialRoot, finalRoot), Is.LessThan(30f),
                "The original court collision volume must contain a continuous sprint.");
            Assert.That(
                worstSegmentError,
                Is.LessThan(0.12f),
                $"Canonical segment {BasketballSkeleton.CanonicalParents[worstSegment]}->" +
                $"{worstSegment} reached {worstSegmentError:F5}m length error at neural tick " +
                $"{worstSegmentTick}.");
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                Assert.That(float.IsFinite(state.BonePositions[bone].x), Is.True);
                Assert.That(float.IsFinite(state.BonePositions[bone].y), Is.True);
                Assert.That(float.IsFinite(state.BonePositions[bone].z), Is.True);
                Assert.That(Vector3.Distance(state.BonePositions[bone], finalRoot),
                    Is.LessThan(3f),
                    $"Canonical bone {bone} escaped the actor root volume.");
            }
        }

        [UnityTest]
        public IEnumerator BasketballDemo_TurnIntentChangesHeadingAndStaysFinite()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballNeuralController controller =
                Object.FindAnyObjectByType<BasketballNeuralController>();
            Assert.That(controller, Is.Not.Null);
            Quaternion start = controller.State.ActorRootRotation;
            controller.SetIntentOverride(new BasketballIntent
            {
                Move = Vector2.up,
                Turn = 0.8f
            });

            for (int tick = 0; tick < 90; tick++)
            {
                controller.SimulateTick();
                if (tick % 30 == 29)
                {
                    yield return null;
                }
            }

            Quaternion end = controller.State.ActorRootRotation;
            Assert.That(Quaternion.Angle(start, end), Is.GreaterThan(10f));
            Assert.That(controller.enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator BasketballDemo_ThirdPersonCameraFollowsAndAvoidsObstacles()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            ThirdPersonOrbitCamera orbit =
                Object.FindAnyObjectByType<ThirdPersonOrbitCamera>();
            BasketballLegacyCamera legacy =
                Object.FindAnyObjectByType<BasketballLegacyCamera>(FindObjectsInactive.Include);
            BasketballKeyboardMouseInputProvider input =
                Object.FindAnyObjectByType<BasketballKeyboardMouseInputProvider>();
            BasketballNeuralController controller =
                Object.FindAnyObjectByType<BasketballNeuralController>();

            Assert.That(orbit, Is.Not.Null);
            Assert.That(orbit.enabled, Is.True);
            Assert.That(orbit.Target, Is.Not.Null);
            Assert.That(legacy, Is.Not.Null);
            Assert.That(legacy.enabled, Is.False, "Reference camera must remain available but inactive.");
            Assert.That(input.Mode, Is.EqualTo(BasketballKeyboardMouseInputProvider.InputMode.Keyboard));

            controller.enabled = false;
            Vector3 cameraStart = orbit.transform.position;
            orbit.Target.position += Vector3.right;
            for (int frame = 0; frame < 12; frame++)
            {
                yield return null;
            }
            Assert.That(orbit.transform.position.x - cameraStart.x, Is.GreaterThan(0.5f),
                "Orbit camera did not follow the rendered player transform.");

            float unobstructedDistance = orbit.CurrentDistance;
            Vector3 pivot = orbit.Target.position + new Vector3(0f, 1.35f, 0f);
            Vector3 cameraDirection = (orbit.transform.position - pivot).normalized;
            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "CameraCollisionProbe";
            obstacle.transform.SetPositionAndRotation(
                pivot + cameraDirection * 1.1f,
                Quaternion.LookRotation(cameraDirection, Vector3.up));
            obstacle.transform.localScale = new Vector3(2f, 2f, 0.25f);
            Physics.SyncTransforms();

            for (int frame = 0; frame < 8; frame++)
            {
                yield return null;
            }

            Assert.That(orbit.CurrentDistance, Is.LessThan(unobstructedDistance - 0.5f),
                "Camera collision did not pull the camera in front of the obstacle.");
            Object.Destroy(obstacle);
        }

        [UnityTest]
        public IEnumerator BasketballDemo_CameraHeadingSteersKeyboardMovementAndCharacterFacing()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            ThirdPersonOrbitCamera orbit =
                Object.FindAnyObjectByType<ThirdPersonOrbitCamera>();
            BasketballNeuralController controller =
                Object.FindAnyObjectByType<BasketballNeuralController>();
            Assert.That(orbit, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);

            Quaternion startFacing = controller.State.ActorRootRotation;
            orbit.SetHeading(90f, true);
            controller.SetIntentOverride(new BasketballIntent
            {
                Move = Vector2.up,
                IsGamepad = false
            });

            for (int tick = 0; tick < 120; tick++)
            {
                controller.SimulateTick();
                if (tick % 30 == 29)
                {
                    yield return null;
                }
            }

            controller.ClearIntentOverride();
            Vector3 actorForward = controller.State.ActorRootRotation * Vector3.forward;
            Assert.That(Quaternion.Angle(startFacing, controller.State.ActorRootRotation),
                Is.GreaterThan(25f),
                "Keyboard movement did not turn the character toward the camera heading.");
            Assert.That(Vector3.Dot(actorForward.normalized, orbit.PlanarForward.normalized),
                Is.GreaterThan(0.45f),
                "Character facing did not converge toward the camera-relative movement direction.");
        }

        [UnityTest]
        public IEnumerator BasketballDemo_UsesUIToolkitHudAndOriginalStyleControlDisk()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            UIDocument document = Object.FindAnyObjectByType<UIDocument>();
            BasketballUIToolkitController uiController =
                Object.FindAnyObjectByType<BasketballUIToolkitController>();
            BasketballDebugHUD legacyHud =
                Object.FindAnyObjectByType<BasketballDebugHUD>(FindObjectsInactive.Include);
            Canvas legacyCanvas =
                Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);

            Assert.That(document, Is.Not.Null);
            Assert.That(document.visualTreeAsset, Is.Not.Null);
            Assert.That(document.panelSettings, Is.Not.Null);
            Assert.That(uiController, Is.Not.Null);
            Assert.That(uiController.IsBound, Is.True);
            Assert.That(document.rootVisualElement.Q<VisualElement>("control-disk"), Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<VisualElement>("expert-card"), Is.Not.Null);
            Assert.That(legacyHud, Is.Null, "Legacy OnGUI HUD must be replaced by UI Toolkit.");
            Assert.That(legacyCanvas, Is.Null, "Legacy Canvas UI must be replaced by UIDocument.");
        }

        private static float[] CaptureCanonicalLengths(BasketballSkeleton skeleton)
        {
            float[] lengths = new float[BasketballSkeleton.BoneCount];
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                int parent = BasketballSkeleton.CanonicalParents[bone];
                if (parent >= 0)
                {
                    lengths[bone] = Vector3.Distance(
                        skeleton.GetBone(parent).position,
                        skeleton.GetBone(bone).position);
                }
            }
            return lengths;
        }

        private static float MeasureCanonicalRigError(
            BasketballAgentState state,
            float[] referenceLengths,
            out int worstSegment)
        {
            float worstError = 0f;
            worstSegment = 1;
            for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
            {
                int parent = BasketballSkeleton.CanonicalParents[bone];
                if (parent < 0)
                {
                    continue;
                }

                float actualLength = Vector3.Distance(
                    state.BonePositions[parent],
                    state.BonePositions[bone]);
                float error = Mathf.Abs(actualLength - referenceLengths[bone]);
                if (error > worstError)
                {
                    worstError = error;
                    worstSegment = bone;
                }
            }
            return worstError;
        }
    }
}
