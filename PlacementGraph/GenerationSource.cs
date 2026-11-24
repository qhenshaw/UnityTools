using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Marks an object (usually the player or the camera) that Placement Graphs set to Generate On: Near Player
    /// generate around at runtime. With none in the scene they use the main camera.
    /// </summary>
    [AddComponentMenu("UnityTools/Generation Source")]
    public class GenerationSource : MonoBehaviour
    {
        private static readonly List<GenerationSource> Enabled = new List<GenerationSource>();
        private static readonly List<Vector3> Buffer = new List<Vector3>();

        private void OnEnable() => Enabled.Add(this);
        private void OnDisable() => Enabled.Remove(this);

        /// <summary>The positions of every enabled source, or the main camera's when there are none.</summary>
        public static IReadOnlyList<Vector3> Positions()
        {
            Buffer.Clear();
            foreach (GenerationSource source in Enabled) Buffer.Add(source.transform.position);
            if (Buffer.Count == 0 && Camera.main != null) Buffer.Add(Camera.main.transform.position);
            return Buffer;
        }
    }
}
