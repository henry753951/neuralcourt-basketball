using System.Collections;
using CrowdEyes.AI4Animation.Basketball;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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

            BasketballMatchController match =
                Object.FindAnyObjectByType<BasketballMatchController>();
            Assert.That(match, Is.Not.Null);
            match.InitializeMatch();
            BasketballNeuralController[] controllers =
                Object.FindObjectsByType<BasketballNeuralController>();
            Assert.That(controllers, Has.Length.EqualTo(3));
            var startTicks = new int[controllers.Length];
            for (int index = 0; index < controllers.Length; index++)
            {
                Assert.That(controllers[index].IsInitialized, Is.True);
                startTicks[index] = controllers[index].SimulationTickCount;
            }

            yield return new WaitForSecondsRealtime(1f);

            for (int index = 0; index < controllers.Length; index++)
            {
                int delta = controllers[index].SimulationTickCount - startTicks[index];
                Assert.That(delta, Is.InRange(27, 33),
                    $"Player {index + 1} neural simulation must remain at 30 Hz.");
                BasketballAgentState state = controllers[index].State;
                for (int bone = 0; bone < BasketballSkeleton.BoneCount; bone++)
                {
                    Assert.That(float.IsFinite(state.BonePositions[bone].x), Is.True);
                    Assert.That(float.IsFinite(state.BonePositions[bone].y), Is.True);
                    Assert.That(float.IsFinite(state.BonePositions[bone].z), Is.True);
                }
            }
        }

        [UnityTest]
        public IEnumerator ReferenceScene_ForwardIntentAdvancesRoot()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballNeuralController controller = GetPrimaryController(disableMatch: true);
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

            BasketballNeuralController controller = GetPrimaryController(disableMatch: true);
            Assert.That(controller, Is.Not.Null);
            BasketballSkeleton skeleton = controller.GetComponent<BasketballSkeleton>();
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

            BasketballNeuralController controller = GetPrimaryController(disableMatch: true);
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
            BasketballMatchController match =
                Object.FindAnyObjectByType<BasketballMatchController>();
            match.InitializeMatch();
            BasketballKeyboardMouseInputProvider input = match.ActivePlayer.InputProvider;
            BasketballNeuralController controller = match.ActivePlayer.Controller;

            Assert.That(orbit, Is.Not.Null);
            Assert.That(orbit.enabled, Is.True);
            Assert.That(orbit.Target, Is.Not.Null);
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
            BasketballNeuralController controller = GetPrimaryController(disableMatch: true);
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

        [UnityTest]
        public IEnumerator BasketballDemo_ThreePlayersUseTeamsSharedBallAndAtomicPossession()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballMatchController match =
                Object.FindAnyObjectByType<BasketballMatchController>();
            BasketballTeamMember[] members =
                Object.FindObjectsByType<BasketballTeamMember>();
            BasketballBallController[] balls =
                Object.FindObjectsByType<BasketballBallController>();
            Assert.That(match, Is.Not.Null);
            match.InitializeMatch();
            Assert.That(members, Has.Length.EqualTo(3));
            Assert.That(balls, Has.Length.EqualTo(1));
            Assert.That(match.PlayerCount, Is.EqualTo(3));

            BasketballTeamMember player1 = match.ActivePlayer;
            BasketballTeamMember player2 = FindPlayer(members, 1);
            BasketballTeamMember player3 = FindPlayer(members, 2);
            Assert.That(player1.TeamId, Is.EqualTo(0));
            Assert.That(player2.TeamId, Is.EqualTo(0));
            Assert.That(player3.TeamId, Is.EqualTo(1));
            Assert.That(match.Owner, Is.EqualTo(player1));
            Assert.That(match.IsValidPassTarget(player1, player2), Is.True);
            Assert.That(match.IsValidPassTarget(player1, player3), Is.False,
                "Pass locking must reject opponents.");

            BasketballPossessionManager possession = match.PossessionManager;
            Assert.That(possession, Is.Not.Null);
            Assert.That(possession.HasBall(player1.Controller), Is.True);
            Assert.That(possession.CanWriteBall(player1.Controller), Is.True);
            Assert.That(possession.HasBall(player2.Controller), Is.False);
            Assert.That(possession.CanWriteBall(player2.Controller), Is.False);
            Assert.That(possession.HasBall(player3.Controller), Is.False);
            Assert.That(possession.CanWriteBall(player3.Controller), Is.False);
            int carrierCount = 0;
            for (int index = 0; index < members.Length; index++)
            {
                if (members[index].Controller.IsCarrier)
                {
                    carrierCount += 1;
                }
            }
            Assert.That(carrierCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BasketballDemo_PlayerSelectionRetargetsOrbitCamera()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballMatchController match =
                Object.FindAnyObjectByType<BasketballMatchController>();
            ThirdPersonOrbitCamera orbit =
                Object.FindAnyObjectByType<ThirdPersonOrbitCamera>();
            Keyboard keyboard = Keyboard.current;
            match.InitializeMatch();
            match.SelectPlayer(0);
            Transform previousTarget = orbit.Target;
            Vector3 cameraBefore = orbit.transform.position;

            Assert.That(keyboard, Is.Not.Null);
            QueueKeyboard(keyboard, Key.Tab);
            yield return null;
            Assert.That(match.ActivePlayerIndex, Is.EqualTo(1));
            Assert.That(orbit.Target, Is.EqualTo(match.ActivePlayer.transform));
            Assert.That(ReferenceEquals(orbit.Target, previousTarget), Is.False);
            Assert.That(match.ActivePlayer.Indicator.State,
                Is.EqualTo(BasketballTargetIndicatorState.ActivePlayer));

            QueueKeyboard(keyboard);
            yield return null;
            Assert.That(Vector3.Distance(cameraBefore, orbit.transform.position),
                Is.GreaterThan(0.001f), "Camera did not begin its smooth target transition.");
        }

        [UnityTest]
        public IEnumerator BasketballDemo_KeyboardTargetingFakesThenCommitsTeammatePass()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballMatchController match =
                Object.FindAnyObjectByType<BasketballMatchController>();
            BasketballTeamMember[] members =
                Object.FindObjectsByType<BasketballTeamMember>();
            ThirdPersonOrbitCamera orbit =
                Object.FindAnyObjectByType<ThirdPersonOrbitCamera>();
            Camera viewCamera = Camera.main;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Assert.That(match, Is.Not.Null);
            Assert.That(orbit, Is.Not.Null);
            Assert.That(viewCamera, Is.Not.Null);
            Assert.That(keyboard, Is.Not.Null);
            Assert.That(mouse, Is.Not.Null);
            match.InitializeMatch();

            BasketballTeamMember passer = FindPlayer(members, 0);
            BasketballTeamMember teammate = FindPlayer(members, 1);
            orbit.enabled = false;
            viewCamera.transform.LookAt(teammate.AimPoint);

            QueueKeyboard(keyboard, Key.LeftCtrl);
            yield return null;
            Assert.That(match.IsTargetSelectionActive, Is.True);
            Assert.That(match.LockedTarget, Is.SameAs(teammate));
            Assert.That(teammate.Indicator.State,
                Is.EqualTo(BasketballTargetIndicatorState.PassLocked));

            QueueKeyboard(keyboard, Key.LeftCtrl);
            QueueMouse(mouse, true);
            yield return null;
            QueueMouse(mouse, false);
            QueueKeyboard(keyboard, Key.LeftCtrl);
            yield return null;
            Assert.That(match.Owner, Is.SameAs(passer), "A short pass press must remain a fake.");
            Assert.That(match.IsPassCommitted, Is.False);
            Assert.That(match.IsPassFakeActive, Is.True,
                "The pass fake was not latched across neural ticks.");

            float fakeDeadline = Time.realtimeSinceStartup + 0.35f;
            while (match.IsPassFakeActive && Time.realtimeSinceStartup < fakeDeadline)
            {
                yield return null;
            }
            Assert.That(match.IsPassFakeActive, Is.False);

            viewCamera.transform.LookAt(teammate.AimPoint);
            QueueKeyboard(keyboard, Key.LeftCtrl);
            yield return null;
            Assert.That(match.LockedTarget, Is.SameAs(teammate));
            QueueKeyboard(keyboard, Key.LeftCtrl);
            QueueMouse(mouse, true);
            float commitDeadline = Time.realtimeSinceStartup + 0.6f;
            while (!match.IsPassCommitted && Time.realtimeSinceStartup < commitDeadline)
            {
                yield return null;
            }
            Assert.That(match.IsPassCommitted, Is.True,
                "Holding Ctrl+LeftMouse did not commit the targeted pass.");

            QueueMouse(mouse, false);
            QueueKeyboard(keyboard);
            float releaseDeadline = Time.realtimeSinceStartup + 1.6f;
            while (match.Owner == passer && Time.realtimeSinceStartup < releaseDeadline)
            {
                yield return null;
            }
            Assert.That(match.Owner, Is.Not.SameAs(passer),
                "The committed pass never released the shared ball.");

            QueueKeyboard(keyboard);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BasketballDemo_OpponentStealContactKnocksBallLooseBeforeSecure()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("BasketballDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            BasketballMatchController match =
                Object.FindAnyObjectByType<BasketballMatchController>();
            BasketballTeamMember[] members =
                Object.FindObjectsByType<BasketballTeamMember>();
            BasketballBallController ball =
                Object.FindAnyObjectByType<BasketballBallController>();
            match.InitializeMatch();
            BasketballTeamMember opponent = FindPlayer(members, 2);
            match.enabled = false;
            opponent.Controller.SetIntentOverride(new BasketballIntent { Steal = true });

            float deadline = Time.realtimeSinceStartup + 0.6f;
            while (match.Owner != null && Time.realtimeSinceStartup < deadline)
            {
                opponent.Controller.State.BonePositions[18] = ball.transform.position;
                opponent.Controller.State.BonePositions[25] = ball.transform.position;
                yield return null;
            }

            Assert.That(match.Owner, Is.Null,
                "A steal touch must first knock the ball loose instead of transferring Owner.");
            Assert.That(opponent.Controller.IsCarrier, Is.False);
            Assert.That(match.PossessionManager.BallState,
                Is.EqualTo(BasketballPossessionState.Loose).Or
                    .EqualTo(BasketballPossessionState.Contested));
        }

        private static void QueueKeyboard(Keyboard keyboard, params Key[] pressedKeys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressedKeys));
        }

        private static void QueueMouse(Mouse mouse, bool pressed)
        {
            InputSystem.QueueDeltaStateEvent(mouse.leftButton, pressed ? 1f : 0f);
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

        private static BasketballNeuralController GetPrimaryController(bool disableMatch)
        {
            BasketballMatchController match =
                Object.FindAnyObjectByType<BasketballMatchController>();
            Assert.That(match, Is.Not.Null);
            match.InitializeMatch();
            BasketballNeuralController controller = match.ActivePlayer.Controller;
            if (disableMatch)
            {
                match.enabled = false;
            }
            return controller;
        }

        private static BasketballTeamMember FindPlayer(
            BasketballTeamMember[] members,
            int playerIndex)
        {
            for (int index = 0; index < members.Length; index++)
            {
                if (members[index].PlayerIndex == playerIndex)
                {
                    return members[index];
                }
            }
            Assert.Fail($"Player {playerIndex + 1} was not found.");
            return null;
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
