using UnityEngine;

namespace Aelid.Rendering
{
    /// <summary>
    /// Tags this object's renderers (and children, unless a child has its own
    /// AsciiSurface) with an ASCII surface class via Rendering Layers.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Aelid/Rendering/ASCII Surface")]
    public sealed class AsciiSurface : MonoBehaviour
    {
        [SerializeField] AsciiSurfaceClass surfaceClass = AsciiSurfaceClass.Default;

        public AsciiSurfaceClass SurfaceClass
        {
            get => surfaceClass;
            set
            {
                surfaceClass = value;
                Apply();
            }
        }

        void OnEnable() => Apply();

        void OnValidate() => Apply();

        public void Apply()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                // A nested AsciiSurface owns its own subtree.
                if (r.GetComponentInParent<AsciiSurface>(true) != this)
                    continue;
                r.renderingLayerMask = AsciiSurfaceLayers.Apply(r.renderingLayerMask, surfaceClass);
            }
        }
    }
}
