using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aelid.World
{
    /// <summary>
    /// The object list for a region, modelled on the Objects tab of Oblivion's
    /// Region Editor. Rules run top to bottom, and each child runs straight after
    /// its parent, so earlier rules claim ground first.
    /// </summary>
    [CreateAssetMenu(menuName = "Aelid/World/Region Rule Set", fileName = "RegionRules")]
    public sealed class RegionRuleSet : ScriptableObject
    {
        [Tooltip("Two 'Is A Tree' objects must stand this many times their combined radii apart, so crowns don't " +
                 "merge, while smaller objects can still sit close to the trunk. Oblivion kept this in a hidden game setting.")]
        [Min(1f)] public float treeSpacing = 3f;

        [Tooltip("Generated top to bottom. A rule with a Parent spawns in a ring around each placed parent.")]
        public List<RegionObjectRule> rules = new();

        public int IndexOf(string ruleName)
        {
            for (int i = 0; i < rules.Count; i++)
                if (rules[i] != null && rules[i].name == ruleName)
                    return i;
            return -1;
        }
    }

    [Serializable]
    public sealed class RegionObjectRule
    {
        public string name = "New Rule";

        [Tooltip("Name of the parent rule. Empty means top level.")]
        public string parent = "";

        [Tooltip("Prefabs to choose from at random, like several SpeedTree seeds of one species.")]
        public GameObject[] variants = Array.Empty<GameObject>();

        [Header("Distribution")]
        [Tooltip("Percent chance per block. Top-level rules split the region into blocks of 2 x Radius; children split the ring around their parent the same way.")]
        [Range(0f, 100f)] public float density = 5f;

        [Tooltip("Footprint radius in metres. Objects never overlap footprints. Smaller radii make more, smaller blocks.")]
        [Min(0.05f)] public float radius = 0.5f;

        [Tooltip("Children only: width of the ring outside the parent's radius where this object can appear.")]
        [Min(0f)] public float radiusFromParent = 2f;

        [Tooltip("Percent chance that an object shifts next to the nearest one of its kind. Higher values make clumps and groves.")]
        [Range(0f, 100f)] public float clustering;

        [Tooltip("Aelid addition: modulates density with broad noise so the rule forms patches and glades. 0 = even.")]
        [Range(0f, 1f)] public float patchiness;

        [Tooltip("Typical patch width in metres.")]
        [Min(1f)] public float patchSize = 16f;

        [Tooltip("Uses the rule set's Tree Spacing against other trees.")]
        public bool isTree;

        [Header("Terrain limits")]
        [Range(0f, 90f)] public float minSlope;
        [Range(0f, 90f)] public float maxSlope = 90f;
        [Tooltip("Ground height in world metres.")]
        public float minHeight = -1000f;
        public float maxHeight = 1000f;

        [Header("Placement")]
        [Tooltip("Tilt the object to the ground's slope before adding angle variance.")]
        public bool conformToSlope;
        [Tooltip("Random turn about the vertical axis, +/- degrees.")]
        [Range(0f, 180f)] public float yawVariance = 180f;
        [Tooltip("Random lean in any direction, up to this many degrees.")]
        [Range(0f, 45f)] public float tiltVariance;
        [Min(0.01f)] public float scale = 1f;
        [Tooltip("Random scale, +/- this fraction of Scale.")]
        [Range(0f, 0.9f)] public float sizeVariance = 0.15f;
        [Tooltip("Metres to lower the object into the ground. Negative raises it.")]
        public float sink;
        [Min(0f)] public float sinkVariance;
    }
}
