using UnityEngine;

namespace Aelid.Rendering
{
    /// <summary>
    /// Glyph stabilizer for the game camera.
    ///
    /// A screen-fixed glyph grid shimmers when the world slides beneath it,
    /// because cells keep re-sampling slightly different content. This component
    /// snaps the camera so the world moves under the grid in whole cells only,
    /// then hands the leftover (sub-cell) offset to the ASCII pass, which shifts
    /// the finished image by whole pixels. Result: the view scrolls smoothly
    /// while every glyph stays locked to the world.
    ///
    /// Exact for orthographic cameras with fixed rotation and zoom.
    /// For perspective cameras it is exact on the focus plane only.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(10000)] // after every camera rig's LateUpdate
    [AddComponentMenu("Aelid/Rendering/ASCII Camera")]
    public sealed class AsciiCamera : MonoBehaviour
    {
        [SerializeField, Tooltip("Lock glyphs to the world while the camera moves.")]
        bool pixelSnap = true;

        [SerializeField, Tooltip("Perspective only: snapping is exact at this target's depth.")]
        Transform focusTarget;

        Camera m_Camera;

        public bool PixelSnap
        {
            get => pixelSnap;
            set => pixelSnap = value;
        }

        public Transform FocusTarget
        {
            get => focusTarget;
            set => focusTarget = value;
        }

        /// <summary>Whole-pixel offset (screen right / screen up) from the snapped to the desired camera.</summary>
        public Vector2Int SubCellOffsetPixels { get; private set; }

        /// <summary>World-anchored index of the cell grid origin; keeps dithering stable.</summary>
        public Vector2Int CellOrigin { get; private set; }

        /// <summary>Absolute camera position in pixels on the grid plane (diagnostics/tests).</summary>
        public Vector2Int PixelPosition { get; private set; }

        public Camera Camera => m_Camera != null ? m_Camera : m_Camera = GetComponent<Camera>();

        void LateUpdate() => ApplySnap();

        /// <summary>
        /// Snaps the camera transform for this frame. Call after the camera rig
        /// has written the desired transform (done automatically in LateUpdate).
        /// </summary>
        public void ApplySnap()
        {
            var cam = Camera;
            if (!pixelSnap || cam == null || cam.pixelHeight <= 0)
            {
                SubCellOffsetPixels = Vector2Int.zero;
                CellOrigin = Vector2Int.zero;
                return;
            }

            Vector2Int raster = AsciiGrid.RasterSize(cam);
            float worldHeight = WorldViewHeight(cam);
            if (worldHeight <= 0f)
                return;
            float unitsPerPixelX = worldHeight * cam.aspect / raster.x;
            float unitsPerPixelY = worldHeight / raster.y;

            int cellPx = AsciiGrid.ResolveCellSize(raster.y);
            Transform t = transform;
            Vector3 right = t.right;
            Vector3 up = t.up;
            Vector3 desired = t.position;

            // Camera position expressed in whole pixels on its own view plane.
            float px = Vector3.Dot(desired, right) / unitsPerPixelX;
            float py = Vector3.Dot(desired, up) / unitsPerPixelY;
            long pixelX = (long)System.Math.Round(px);
            long pixelY = (long)System.Math.Round(py);

            // Split into whole cells + remaining pixels.
            long cellX = FloorDiv(pixelX, cellPx);
            long cellY = FloorDiv(pixelY, cellPx);
            int remX = (int)(pixelX - cellX * cellPx);
            int remY = (int)(pixelY - cellY * cellPx);

            // Move the camera onto the cell lattice.
            float snappedX = cellX * cellPx * unitsPerPixelX;
            float snappedY = cellY * cellPx * unitsPerPixelY;
            Vector3 snapped = desired
                + right * (snappedX - Vector3.Dot(desired, right))
                + up * (snappedY - Vector3.Dot(desired, up));
            t.position = snapped;

            SubCellOffsetPixels = new Vector2Int(remX, remY);
            CellOrigin = new Vector2Int((int)cellX, (int)cellY);
            PixelPosition = new Vector2Int((int)pixelX, (int)pixelY);
        }

        float WorldViewHeight(Camera cam)
        {
            if (cam.orthographic)
                return 2f * cam.orthographicSize;

            float distance = focusTarget != null
                ? Vector3.Dot(focusTarget.position - transform.position, transform.forward)
                : 10f;
            distance = Mathf.Max(distance, cam.nearClipPlane);
            return 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        static long FloorDiv(long a, int b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0)))
                q--;
            return q;
        }
    }
}
