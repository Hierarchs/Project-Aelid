using UnityEngine;

namespace Aelid.Gameplay
{
    /// <summary>
    /// Placeholder locomotion animation for the primitive-built character:
    /// limb swing, body bob, lean and idle breathing, all driven by the motor's
    /// speed. Swap for an Animator + real rig once character art exists; the
    /// motor/controller do not depend on this component.
    /// </summary>
    [AddComponentMenu("Aelid/Gameplay/Procedural Humanoid Animator")]
    public sealed class ProceduralHumanoidAnimator : MonoBehaviour
    {
        public PlayerMotor motor;

        [Header("Rig (pivots)")]
        public Transform body;
        public Transform head;
        public Transform leftArm;
        public Transform rightArm;
        public Transform leftLeg;
        public Transform rightLeg;

        [Header("Gait")]
        [Tooltip("Steps per metre travelled.")] public float stepsPerMetre = 0.85f;
        public float walkSwing = 32f;
        public float runSwing = 55f;
        public float bobHeight = 0.06f;
        public float runLean = 12f;

        float m_Phase;
        float m_Blend;
        Vector3 m_BodyBase;

        void Awake()
        {
            if (motor == null) motor = GetComponentInParent<PlayerMotor>();
            if (body != null) m_BodyBase = body.localPosition;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float speed = motor != null ? motor.PlanarSpeed : 0f;
            float blend = motor != null ? motor.LocomotionBlend : 0f;
            m_Blend = Mathf.Lerp(m_Blend, blend, 1f - Mathf.Exp(-12f * dt));

            m_Phase += speed * stepsPerMetre * dt * Mathf.PI;
            float moving = Mathf.Clamp01(speed / 0.5f);
            float swing = Mathf.Lerp(walkSwing, runSwing, Mathf.InverseLerp(0.5f, 1f, m_Blend)) * moving;
            float s = Mathf.Sin(m_Phase);

            SetPitch(leftLeg, s * swing);
            SetPitch(rightLeg, -s * swing);
            SetPitch(leftArm, -s * swing * 0.8f);
            SetPitch(rightArm, s * swing * 0.8f);

            if (body != null)
            {
                float breathe = Mathf.Sin(Time.time * 2.2f) * 0.012f * (1f - moving);
                float bob = Mathf.Abs(Mathf.Cos(m_Phase)) * bobHeight * moving;
                body.localPosition = m_BodyBase + Vector3.up * (bob + breathe);
                body.localRotation = Quaternion.Euler(runLean * Mathf.InverseLerp(0.55f, 1f, m_Blend), 0f, 0f);
            }

            if (head != null)
                head.localRotation = Quaternion.Euler(-Mathf.Sin(m_Phase * 2f) * 2f * moving, 0f, 0f);
        }

        static void SetPitch(Transform t, float degrees)
        {
            if (t != null)
                t.localRotation = Quaternion.Euler(degrees, 0f, 0f);
        }
    }
}
