using UnityEngine;

namespace Aelid.Gameplay
{
    /// <summary>
    /// What the player wants to do this tick, independent of the device that
    /// produced it. Keeping input as plain data is what will let the server
    /// validate and replay movement later (authoritative multiplayer): clients
    /// send intents, not positions.
    /// </summary>
    public struct PlayerIntent
    {
        /// <summary>Desired move direction in world space (XZ), magnitude 0..1.</summary>
        public Vector3 moveWorld;
        public bool sprint;
    }
}
