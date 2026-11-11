using UnityEngine;

/// <summary>
/// Optional scene-wide wind for the Oblivion foliage pack.
/// Add it to any GameObject. When a WindZone is assigned it follows that zone's direction and Main strength,
/// otherwise it uses the values below. Without this component each material uses its own wind settings.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("Oblivion Foliage/Foliage Wind")]
public class OblivionFoliageWind : MonoBehaviour
{
    [Tooltip("Optional. When set, direction and strength come from this WindZone (Main and Turbulence).")]
    public WindZone windZone;

    [Tooltip("Direction the wind blows toward (world space, Y ignored). Used when no WindZone is set.")]
    public Vector3 direction = new Vector3(1f, 0f, 0.35f);

    [Tooltip("Multiplies each material's Bend Strength. 0 = calm, 1 = material default, 3 = storm.")]
    [Range(0f, 4f)] public float strength = 1f;

    [Tooltip("Multiplies each material's Bend Speed.")]
    [Range(0.1f, 4f)] public float speed = 1f;

    static readonly int GlobalWind = Shader.PropertyToID("_OF_GlobalWind");
    static readonly int GlobalSpeed = Shader.PropertyToID("_OF_GlobalWindSpeed");

    void OnEnable() => Push();
    void Update() => Push();

    void OnDisable()
    {
        Shader.SetGlobalVector(GlobalWind, Vector4.zero);
        Shader.SetGlobalFloat(GlobalSpeed, 1f);
    }

    void Push()
    {
        Vector3 dir = direction;
        float s = strength;
        float sp = speed;
        if (windZone != null)
        {
            dir = windZone.transform.forward;
            s *= windZone.windMain;
            sp *= 1f + windZone.windTurbulence;
        }
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
        dir.Normalize();
        Shader.SetGlobalVector(GlobalWind, new Vector4(dir.x * s, 0f, dir.z * s, 1f));
        Shader.SetGlobalFloat(GlobalSpeed, sp);
    }
}
