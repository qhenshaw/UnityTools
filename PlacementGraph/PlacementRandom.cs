using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Stateless, deterministic random numbers. Every value is a hash of the graph seed, the node and the point,
    /// so a result never depends on the order things ran in or on <see cref="UnityEngine.Random"/>'s global state.
    /// </summary>
    public static class PlacementRandom
    {
        /// <summary>Mixes values into one well-distributed hash (a variant of the lowbias32 finalizer).</summary>
        public static uint Hash(uint a)
        {
            a ^= a >> 16;
            a *= 0x7feb352dU;
            a ^= a >> 15;
            a *= 0x846ca68bU;
            a ^= a >> 16;
            return a;
        }

        public static uint Hash(uint a, uint b) => Hash(a ^ Hash(b + 0x9e3779b9U));
        public static uint Hash(uint a, uint b, uint c) => Hash(Hash(a, b), c);
        public static uint Hash(uint a, uint b, uint c, uint d) => Hash(Hash(a, b, c), d);

        /// <summary>A float in [0, 1) from a hash.</summary>
        public static float Value(uint hash) => (hash >> 8) * (1f / 16777216f);

        /// <summary>A float in [min, max) for a point, a node and a channel (use a different channel per value).</summary>
        public static float Range(uint pointSeed, uint nodeSeed, uint channel, float min, float max)
            => Mathf.Lerp(min, max, Value(Hash(pointSeed, nodeSeed, channel)));
    }
}
