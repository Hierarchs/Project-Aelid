using UnityEngine;

namespace Aelid.Gameplay
{
    /// <summary>
    /// Fixed-angle, elevated follow camera (Asciilands-style "above and behind").
    /// Orientation never changes; only position follows the target, plus a
    /// limited zoom. The rig keeps its own smoothed state and never reads the
    /// camera transform back, so post-steps such as ASCII pixel snapping can
    /// adjust the transform without feedback.
    /// </summary>
    [AddComponentMenu("Aelid/Gameplay/Isometric Camera Rig")]
    public sealed class IsometricCameraRig : MonoBehaviour
    {
        public Transform target;
        public Camera cam;
        public PlayerInputReader zoomInput;

        [Header("Angle (fixed)")]
        [Range(30f, 80f)] public float pitch = 52f;
        [Tooltip("0 = looking north (+Z), Asciilands-like. 45 = classic diagonal isometric.")]
        public float yaw = 0f;

        [Header("Framing")]
        public Vector3 targetOffset = new(0f, 1.0f, 0f);
        public float distance = 40f;
        public bool orthographic = true;
        [Tooltip("Perspective field of view (narrow keeps the isometric feel).")]
        [Range(10f, 60f)] public float fieldOfView = 24f;

        [Header("Zoom")]
        [Tooltip("Half the visible height in metres.")]
        public float viewHalfHeight = 8f;
        public float minViewHalfHeight = 5f;
        public float maxViewHalfHeight = 13f;
        public float zoomStep = 0.8f;
        public float zoomSmoothTime = 0.12f;

        [Header("Follow")]
        public float followSmoothTime = 0.14f;
        [Tooltip("Shift the view slightly ahead of movement (metres per m/s).")]
        public float lookAhead = 0.12f;

        Vector3 m_Focus;
        Vector3 m_FocusVelocity;
        Vector3 m_LastTargetPosition;
        float m_Zoom;
        float m_ZoomTarget;
        float m_ZoomVelocity;
        bool m_Initialized;

        void OnEnable()
        {
            if (cam == null) cam = GetComponent<Camera>();
            SnapToTarget();
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (target == null || dt <= 0f)
                return;
            if (!m_Initialized)
                SnapToTarget();

            if (zoomInput != null)
            {
                float z = zoomInput.Zoom;
                if (Mathf.Abs(z) > 0.01f)
                    m_ZoomTarget = Mathf.Clamp(m_ZoomTarget - z * zoomStep, minViewHalfHeight, maxViewHalfHeight);
            }
            m_Zoom = Mathf.SmoothDamp(m_Zoom, m_ZoomTarget, ref m_ZoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);

            Vector3 targetPosition = target.position;
            Vector3 targetVelocity = (targetPosition - m_LastTargetPosition) / dt;
            m_LastTargetPosition = targetPosition;
            Vector3 ahead = Vector3.ProjectOnPlane(targetVelocity, Vector3.up) * lookAhead;

            Vector3 desiredFocus = targetPosition + targetOffset + ahead;
            m_Focus = Vector3.SmoothDamp(m_Focus, desiredFocus, ref m_FocusVelocity, followSmoothTime, Mathf.Infinity, dt);
            ApplyTransform();
        }

        /// <summary>Jump straight to the target with no smoothing (spawn, teleport, captures).</summary>
        public void SnapToTarget()
        {
            if (target != null)
            {
                m_Focus = target.position + targetOffset;
                m_LastTargetPosition = target.position;
            }
            m_FocusVelocity = Vector3.zero;
            m_ZoomTarget = Mathf.Clamp(viewHalfHeight, minViewHalfHeight, maxViewHalfHeight);
            m_Zoom = m_ZoomTarget;
            m_ZoomVelocity = 0f;
            m_Initialized = target != null;
            ApplyTransform();
        }

        /// <summary>Places the camera so <paramref name="focus"/> is centred (editor tools).</summary>
        public void SetFocus(Vector3 focus)
        {
            m_Focus = focus;
            m_FocusVelocity = Vector3.zero;
            ApplyTransform();
        }

        public Quaternion Rotation => Quaternion.Euler(pitch, yaw, 0f);

        void ApplyTransform()
        {
            Quaternion rotation = Rotation;
            transform.SetPositionAndRotation(m_Focus - rotation * Vector3.forward * distance, rotation);

            if (cam == null)
                return;
            cam.orthographic = orthographic;
            if (orthographic)
            {
                cam.orthographicSize = m_Zoom;
            }
            else
            {
                cam.fieldOfView = fieldOfView;
                // Keep the same framing as ortho: half-height at the focus distance.
                float d = m_Zoom / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
                transform.position = m_Focus - rotation * Vector3.forward * d;
            }
        }
    }
}
