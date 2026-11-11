using UnityEngine;

namespace Aelid.Gameplay
{
    /// <summary>
    /// Local-player glue: device input -> camera-relative PlayerIntent -> motor.
    /// Because the camera never rotates, "up" on the stick always means the
    /// same world direction (camera forward flattened onto the ground).
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    [AddComponentMenu("Aelid/Gameplay/Player Controller")]
    public sealed class PlayerController : MonoBehaviour
    {
        public PlayerInputReader input;
        [Tooltip("Camera whose yaw defines 'forward'. Defaults to Camera.main.")]
        public Transform viewTransform;

        PlayerMotor m_Motor;

        public PlayerIntent LastIntent { get; private set; }

        void Awake()
        {
            m_Motor = GetComponent<PlayerMotor>();
            if (input == null) input = GetComponent<PlayerInputReader>();
        }

        void Update()
        {
            if (viewTransform == null && Camera.main != null)
                viewTransform = Camera.main.transform;

            Vector2 move = input != null ? input.Move : Vector2.zero;
            var intent = new PlayerIntent
            {
                moveWorld = ToWorld(move),
                sprint = input != null && input.Sprint,
            };
            LastIntent = intent;
            m_Motor.Tick(intent, Time.deltaTime);
        }

        Vector3 ToWorld(Vector2 move)
        {
            Vector3 forward = Vector3.forward;
            if (viewTransform != null)
            {
                forward = Vector3.ProjectOnPlane(viewTransform.forward, Vector3.up);
                if (forward.sqrMagnitude < 1e-4f)
                    forward = Vector3.ProjectOnPlane(viewTransform.up, Vector3.up);
                forward.Normalize();
            }
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return forward * move.y + right * move.x;
        }
    }
}
