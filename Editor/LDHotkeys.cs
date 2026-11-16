using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EditorTools
{
    public class LDHotkeys : MonoBehaviour
    {
        private const float CastPadding = 0.5f;

        [MenuItem("Tools/LD Shortcuts/Snap to Floor _End")]
        private static void SnapToFloor()
        {
            SnapToFloor(Selection.gameObjects);
        }

        /// <summary>
        /// Moves each object down (or up, if it's sunk in) so its lowest point rests on the first solid surface below
        /// it. Triggers, the object's own colliders and the other objects being snapped are ignored. One Undo step.
        /// Returns how many objects moved.
        /// </summary>
        internal static int SnapToFloor(IReadOnlyList<GameObject> objects)
        {
            GameObject[] targets = objects.Where(go => go != null)
                // A selected child of another selected object moves with its parent.
                .Where(go => !objects.Any(other => other != null && other != go && go.transform.IsChildOf(other.transform)))
                .Distinct().ToArray();
            if (targets.Length == 0) return 0;

            Physics.SyncTransforms();
            var moves = new List<(Transform transform, float distance)>();
            foreach (GameObject target in targets)
            {
                if (TryGetDropDistance(target, targets, out float distance)) moves.Add((target.transform, distance));
            }
            if (moves.Count == 0) return 0;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.RecordObjects(moves.Select(m => (Object)m.transform).ToArray(), "Snap to Floor");
            foreach ((Transform transform, float distance) in moves)
            {
                transform.position += Vector3.down * distance;
            }
            Undo.SetCurrentGroupName("Snap to Floor");
            Undo.CollapseUndoOperations(group);
            return moves.Count;
        }

        /// <summary>How far the object has to move down (negative: up) to rest on the surface below it.</summary>
        internal static bool TryGetDropDistance(GameObject target, IReadOnlyList<GameObject> ignore, out float distance)
        {
            distance = 0f;
            Bounds bounds = GetBounds(target, out bool hasBounds);
            float lowest = hasBounds ? bounds.min.y : target.transform.position.y;
            Vector3 center = hasBounds ? bounds.center : target.transform.position;

            // Cast from just above the top, so an object that's sunk into the floor still finds the floor's surface.
            float top = (hasBounds ? bounds.max.y : lowest) + CastPadding;
            var origin = new Vector3(center.x, top, center.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearest = Mathf.Infinity;
            foreach (RaycastHit hit in hits)
            {
                Transform hitTransform = hit.collider.transform;
                if (ignore.Any(go => go != null && hitTransform.IsChildOf(go.transform))) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    distance = lowest - hit.point.y;
                }
            }
            return nearest < Mathf.Infinity;
        }

        private static Bounds GetBounds(GameObject target, out bool hasBounds)
        {
            hasBounds = false;
            var bounds = new Bounds();
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return bounds;
        }
    }
}
