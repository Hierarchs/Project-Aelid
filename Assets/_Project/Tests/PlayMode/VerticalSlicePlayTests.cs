using System.Collections;
using Aelid.Environment;
using Aelid.Gameplay;
using Aelid.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Aelid.Tests
{
    /// <summary>
    /// Plays the real vertical-slice scene with a virtual keyboard. Any error or
    /// exception logged while it runs (renderer feature included) fails the test.
    /// </summary>
    public class VerticalSlicePlayTests
    {
        const string ScenePath = "Assets/_Project/Scenes/VerticalSlice.unity";

        Keyboard m_Keyboard;
        InputSettings.BackgroundBehavior m_PreviousBackground;
        InputSettings.EditorInputBehaviorInPlayMode m_PreviousEditorBehavior;

        PlayerMotor m_Player;
        IsometricCameraRig m_Rig;
        AsciiCamera m_AsciiCamera;
        DayNightCycle m_DayNight;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Batch runs have no focused window; let input through regardless.
            m_PreviousBackground = InputSystem.settings.backgroundBehavior;
            m_PreviousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            m_Keyboard = InputSystem.AddDevice<Keyboard>("Aelid Test Keyboard");

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;
            yield return null;

            m_Player = Object.FindAnyObjectByType<PlayerMotor>();
            m_Rig = Object.FindAnyObjectByType<IsometricCameraRig>();
            m_AsciiCamera = Object.FindAnyObjectByType<AsciiCamera>();
            m_DayNight = Object.FindAnyObjectByType<DayNightCycle>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Release();
            if (m_Keyboard != null)
                InputSystem.RemoveDevice(m_Keyboard);
            InputSystem.settings.backgroundBehavior = m_PreviousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = m_PreviousEditorBehavior;
            yield return null;
        }

        void Hold(params Key[] keys)
        {
            InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState(keys));
        }

        void Release()
        {
            if (m_Keyboard != null)
                InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState());
        }

        [UnityTest]
        public IEnumerator Scene_ContainsCoreSystems()
        {
            Assert.IsNotNull(m_Player, "Player");
            Assert.IsNotNull(m_Rig, "Camera rig");
            Assert.IsNotNull(m_AsciiCamera, "ASCII camera");
            Assert.IsNotNull(m_DayNight, "Day/night cycle");
            Assert.IsTrue(m_Rig.cam.orthographic, "Default camera is the fixed isometric (orthographic) view.");
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTest]
        public IEnumerator HoldingW_WalksAwayFromCamera_AndCameraFollows()
        {
            Vector3 start = m_Player.transform.position;
            Hold(Key.W);
            yield return new WaitForSeconds(1.5f);
            Release();
            yield return new WaitForSeconds(0.6f); // let the follow smoothing settle

            Vector3 moved = m_Player.transform.position - start;
            Assert.Greater(moved.z, 2.5f, "W should move the player 'up' the screen (camera yaw 0 = +Z).");
            Assert.Less(Mathf.Abs(moved.x), 0.5f, "W should not drift sideways.");
            Assert.Greater(Vector3.Dot(m_Player.transform.forward, Vector3.forward), 0.95f, "Player should face the move direction.");

            // Camera centre ray should pass close to the player.
            Transform cam = m_Rig.transform;
            Vector3 toPlayer = m_Player.transform.position + m_Rig.targetOffset - cam.position;
            Vector3 lateral = toPlayer - cam.forward * Vector3.Dot(toPlayer, cam.forward);
            Assert.Less(lateral.magnitude, 1.0f, "Camera should keep the player centred.");
        }

        [UnityTest]
        public IEnumerator Sprinting_IsFasterThanWalking()
        {
            Hold(Key.D);
            yield return new WaitForSeconds(0.6f);
            float walk = m_Player.PlanarSpeed;
            Hold(Key.D, Key.LeftShift);
            yield return new WaitForSeconds(0.6f);
            float run = m_Player.PlanarSpeed;
            Release();

            Assert.That(walk, Is.EqualTo(m_Player.walkSpeed).Within(0.4f));
            Assert.That(run, Is.EqualTo(m_Player.runSpeed).Within(0.4f));
        }

        [UnityTest]
        public IEnumerator Player_StaysOnGround_WhileMoving()
        {
            var self = m_Player.GetComponent<CharacterController>();
            Hold(Key.A, Key.S);
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                float? ground = null;
                foreach (var hit in Physics.RaycastAll(m_Player.transform.position + Vector3.up * 2f, Vector3.down, 4f))
                    if (hit.collider != self && (ground == null || hit.point.y > ground))
                        ground = hit.point.y;
                Assert.IsTrue(ground.HasValue, "Ground must be under the player.");
                Assert.Less(Mathf.Abs(ground.Value - m_Player.transform.position.y), 0.35f, "Player should not float or sink.");
            }
            Release();
        }

        [UnityTest]
        public IEnumerator AsciiSnapping_KeepsOffsetsInsideOneCell()
        {
            Hold(Key.D, Key.W);
            int cell = AsciiGrid.ResolveCellSize(m_AsciiCamera.Camera.pixelHeight);
            for (int i = 0; i < 90; i++)
            {
                yield return null;
                Vector2Int offset = m_AsciiCamera.SubCellOffsetPixels;
                Assert.That(offset.x, Is.InRange(0, cell - 1));
                Assert.That(offset.y, Is.InRange(0, cell - 1));
            }
            Release();
        }

        [UnityTest]
        public IEnumerator DayNight_AdvancesAndCrossesIntoNight()
        {
            m_DayNight.SetTime(17.5f);
            m_DayNight.dayLengthMinutes = 24f;
            m_DayNight.timeScale = 60f; // 1 in-game hour per real second
            float start = m_DayNight.timeOfDay;
            yield return new WaitForSeconds(1.5f);
            Assert.That(m_DayNight.timeOfDay, Is.InRange(start + 1f, start + 3f), "Time should progress at the configured rate.");

            m_DayNight.SetTime(23f);
            yield return null;
            Assert.Greater(m_DayNight.NightFactor, 0.95f);
            Assert.Less(m_DayNight.celestialLight.intensity, 1.0f, "Moonlight should be dim.");

            m_DayNight.SetTime(12f);
            yield return null;
            Assert.Less(m_DayNight.NightFactor, 0.05f);
            Assert.Greater(m_DayNight.celestialLight.intensity, 1.5f, "Noon sun should be bright.");
        }

        [UnityTest]
        public IEnumerator Movement_KeepsFullSpeed_AtVeryHighFrameRates()
        {
            // Regression: CharacterController's default minMoveDistance dropped
            // sub-millimetre steps, so the player crawled at thousands of FPS.
            Assert.AreEqual(0f, m_Player.GetComponent<CharacterController>().minMoveDistance);
            Vector3 start = m_Player.transform.position;
            Hold(Key.S);
            yield return new WaitForSeconds(1.0f);
            Release();
            Assert.Greater(Vector3.Distance(start, m_Player.transform.position), 2.5f);
        }

        [UnityTest]
        public IEnumerator WadingThroughThePond_EmitsRipples()
        {
            var emitter = m_Player.GetComponent<WaterRippleEmitter>();
            Assert.IsNotNull(emitter, "Player should carry a WaterRippleEmitter.");
            Assert.IsTrue(Physics.Raycast(new Vector3(24.5f, 50f, -11f), Vector3.down, out var bed, 100f));
            m_Player.Teleport(bed.point + Vector3.up * 0.05f);
            WaterRipples.Clear();
            yield return null;

            Vector3 start = m_Player.transform.position;
            Hold(Key.A);
            yield return new WaitForSeconds(1.2f);
            Release();

            Assert.IsTrue(emitter.InWater, "Player should be standing in the pond.");
            Assert.Greater(Vector3.Distance(start, m_Player.transform.position), 2f, "Player should be able to wade.");
            Assert.GreaterOrEqual(WaterRipples.CountActive(2.6f), 2, "Entering and striding should leave ripples.");
        }

        [UnityTest]
        public IEnumerator DebugToggle_DisablesAndRestoresAscii()
        {
            Assert.IsTrue(AsciiRuntimeOverrides.Enabled);
            Hold(Key.F1);
            yield return null;
            Release();
            yield return null;
            Assert.IsFalse(AsciiRuntimeOverrides.Enabled, "F1 should toggle the ASCII pass off.");
            Hold(Key.F1);
            yield return null;
            Release();
            yield return null;
            Assert.IsTrue(AsciiRuntimeOverrides.Enabled, "F1 again should restore it.");
        }
    }
}
