using UnityEngine;

namespace Aelid.Gameplay
{
    /// <summary>
    /// Deterministic character movement driven purely by PlayerIntent + dt.
    /// No input or camera access here, so the same code can later run on the
    /// server for validation and on the client for prediction.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Aelid/Gameplay/Player Motor")]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Speed (m/s)")]
        public float walkSpeed = 3.4f;
        public float runSpeed = 6.2f;

        [Header("Responsiveness")]
        [Tooltip("m/s^2 when speeding up.")] public float acceleration = 28f;
        [Tooltip("m/s^2 when slowing down.")] public float deceleration = 36f;
        [Tooltip("Degrees per second.")] public float turnSpeed = 720f;

        [Header("Gravity")]
        public float gravity = -25f;
        public float groundedStick = -2f;

        CharacterController m_Controller;
        Vector3 m_PlanarVelocity;
        float m_VerticalSpeed;

        public Vector3 Velocity => m_PlanarVelocity + Vector3.up * m_VerticalSpeed;
        public float PlanarSpeed => m_PlanarVelocity.magnitude;

        /// <summary>0 idle, 0.5 walking, 1 running.</summary>
        public float LocomotionBlend => runSpeed > 0f ? Mathf.Clamp01(PlanarSpeed / runSpeed) : 0f;

        public bool IsGrounded => m_Controller != null && m_Controller.isGrounded;

        void Awake() => m_Controller = GetComponent<CharacterController>();

        public void Tick(in PlayerIntent intent, float dt)
        {
            if (m_Controller == null || dt <= 0f)
                return;

            Vector3 wish = Vector3.ClampMagnitude(new Vector3(intent.moveWorld.x, 0f, intent.moveWorld.z), 1f);
            float targetSpeed = (intent.sprint ? runSpeed : walkSpeed) * wish.magnitude;
            Vector3 targetVelocity = wish.sqrMagnitude > 1e-6f ? wish.normalized * targetSpeed : Vector3.zero;

            float rate = targetVelocity.sqrMagnitude > m_PlanarVelocity.sqrMagnitude ? acceleration : deceleration;
            m_PlanarVelocity = Vector3.MoveTowards(m_PlanarVelocity, targetVelocity, rate * dt);

            if (wish.sqrMagnitude > 1e-4f)
            {
                Quaternion face = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, face, turnSpeed * dt);
            }

            if (m_Controller.isGrounded && m_VerticalSpeed < 0f)
                m_VerticalSpeed = groundedStick;
            else
                m_VerticalSpeed += gravity * dt;

            m_Controller.Move((m_PlanarVelocity + Vector3.up * m_VerticalSpeed) * dt);
        }

        /// <summary>Instantly places the character (spawn, teleport, server correction).</summary>
        public void Teleport(Vector3 position)
        {
            if (m_Controller == null) m_Controller = GetComponent<CharacterController>();
            m_Controller.enabled = false;
            transform.position = position;
            m_Controller.enabled = true;
            m_PlanarVelocity = Vector3.zero;
            m_VerticalSpeed = 0f;
        }
    }
}
