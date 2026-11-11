using Aelid.Environment;
using Aelid.Gameplay;
using Aelid.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aelid.Debugging
{
    /// <summary>
    /// Development HUD and hotkeys for evaluating the vertical slice.
    ///   F1  toggle ASCII          F2/F3  glyph density -/+
    ///   F4  toggle glyph snapping F5     toggle orthographic/perspective
    ///   T   fast time (x60)       [ / ]  time -1h / +1h
    ///   H   hide this panel
    /// </summary>
    [AddComponentMenu("Aelid/Debugging/Dev Overlay")]
    public sealed class DevOverlay : MonoBehaviour
    {
        public DayNightCycle dayNight;
        public IsometricCameraRig cameraRig;
        public AsciiCamera asciiCamera;
        public PlayerMotor player;

        bool m_Visible = true;
        bool m_FastTime;
        float m_SmoothedDelta = 1f / 60f;
        GUIStyle m_Style;

        void Update()
        {
            m_SmoothedDelta = Mathf.Lerp(m_SmoothedDelta, Time.unscaledDeltaTime, 0.05f);
            var kb = Keyboard.current;
            if (kb == null)
                return;

            if (kb.f1Key.wasPressedThisFrame) AsciiRuntimeOverrides.Enabled = !AsciiRuntimeOverrides.Enabled;
            if (kb.f2Key.wasPressedThisFrame) AsciiRuntimeOverrides.CellSizeOffset = Mathf.Max(AsciiRuntimeOverrides.CellSizeOffset - 2, -4);
            if (kb.f3Key.wasPressedThisFrame) AsciiRuntimeOverrides.CellSizeOffset = Mathf.Min(AsciiRuntimeOverrides.CellSizeOffset + 2, 16);
            if (kb.f4Key.wasPressedThisFrame && asciiCamera != null) asciiCamera.PixelSnap = !asciiCamera.PixelSnap;
            if (kb.f5Key.wasPressedThisFrame && cameraRig != null) cameraRig.orthographic = !cameraRig.orthographic;
            if (kb.hKey.wasPressedThisFrame) m_Visible = !m_Visible;

            if (dayNight != null)
            {
                if (kb.tKey.wasPressedThisFrame)
                {
                    m_FastTime = !m_FastTime;
                    dayNight.timeScale = m_FastTime ? 60f : 1f;
                }
                if (kb.leftBracketKey.wasPressedThisFrame) dayNight.SetTime(dayNight.timeOfDay - 1f);
                if (kb.rightBracketKey.wasPressedThisFrame) dayNight.SetTime(dayNight.timeOfDay + 1f);
            }
        }

        void OnGUI()
        {
            if (!m_Visible)
                return;
            m_Style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };

            float hours = dayNight != null ? dayNight.timeOfDay : 0f;
            int h = Mathf.FloorToInt(hours);
            int m = Mathf.FloorToInt((hours - h) * 60f);
            int cell = AsciiGrid.ResolveCellSize(Screen.height);
            string text =
                $"<b>AELID vertical slice</b>   {1f / Mathf.Max(m_SmoothedDelta, 1e-4f):0} fps\n" +
                $"Time {h:00}:{m:00}{(m_FastTime ? "  (x60)" : "")}   night {(dayNight != null ? dayNight.NightFactor : 0f):0.00}\n" +
                $"ASCII {(AsciiRuntimeOverrides.Enabled ? "on" : "off")}  cell {cell}px  snap {(asciiCamera != null && asciiCamera.PixelSnap ? "on" : "off")}  " +
                $"{(cameraRig != null && cameraRig.orthographic ? "ortho" : "persp")}\n" +
                $"Speed {(player != null ? player.PlanarSpeed : 0f):0.0} m/s\n" +
                "<color=#aaaaaa>WASD move  Shift run  Wheel zoom\nF1 ascii  F2/F3 density  F4 snap  F5 projection\nT fast time  [ ] hour  H hide</color>";

            var rect = new Rect(12, 12, 420, 140);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(rect.x - 6, rect.y - 4, rect.width, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect, text, m_Style);
        }
    }
}
