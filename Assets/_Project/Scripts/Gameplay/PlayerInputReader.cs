using UnityEngine;
using UnityEngine.InputSystem;

namespace Aelid.Gameplay
{
    /// <summary>
    /// Reads keyboard/mouse and gamepad through the Input System. Actions are
    /// defined in code for now (no asset to keep in sync); move them into an
    /// .inputactions asset when rebinding UI arrives.
    /// </summary>
    [AddComponentMenu("Aelid/Gameplay/Player Input Reader")]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        InputAction m_Move;
        InputAction m_Sprint;
        InputAction m_Zoom;

        public Vector2 Move => m_Move != null ? Vector2.ClampMagnitude(m_Move.ReadValue<Vector2>(), 1f) : Vector2.zero;
        public bool Sprint => m_Sprint != null && m_Sprint.IsPressed();

        /// <summary>Zoom steps this frame (+ = zoom in).</summary>
        public float Zoom
        {
            get
            {
                if (m_Zoom == null) return 0f;
                float v = m_Zoom.ReadValue<float>();
                // Mouse wheel reports ~120 per notch on Windows; normalize to notches.
                return Mathf.Abs(v) > 10f ? v / 120f : v;
            }
        }

        void Awake()
        {
            m_Move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            m_Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            m_Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            m_Move.AddBinding("<Gamepad>/leftStick");

            m_Sprint = new InputAction("Sprint", InputActionType.Button);
            m_Sprint.AddBinding("<Keyboard>/leftShift");
            m_Sprint.AddBinding("<Gamepad>/leftStickPress");
            m_Sprint.AddBinding("<Gamepad>/buttonEast");

            m_Zoom = new InputAction("Zoom", InputActionType.Value, expectedControlType: "Axis");
            m_Zoom.AddBinding("<Mouse>/scroll/y");
        }

        void OnEnable()
        {
            m_Move?.Enable();
            m_Sprint?.Enable();
            m_Zoom?.Enable();
        }

        void OnDisable()
        {
            m_Move?.Disable();
            m_Sprint?.Disable();
            m_Zoom?.Disable();
        }

        void OnDestroy()
        {
            m_Move?.Dispose();
            m_Sprint?.Dispose();
            m_Zoom?.Dispose();
        }
    }
}
