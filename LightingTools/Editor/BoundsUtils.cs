using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightingTools.Editor
{
    public static class BoundsUtils
    {
        public static bool TryGetSelectionBounds(out Bounds bounds, float padding = 2f)
        {
            bounds = new Bounds();

            GameObject[] selection = Selection.gameObjects;
            if (selection == null || selection.Length == 0)
            {
                Debug.LogWarning("Select one or more objects with renderers or terrains first.");
                return false;
            }

            var renderers = new List<Renderer>();
            var terrains = new List<Terrain>();
            foreach (GameObject selected in selection)
            {
                renderers.AddRange(selected.GetComponentsInChildren<MeshRenderer>());
                renderers.AddRange(selected.GetComponentsInChildren<SkinnedMeshRenderer>());
                terrains.AddRange(selected.GetComponentsInChildren<Terrain>());
            }

            if (!TryGetBounds(renderers, terrains, out bounds))
            {
                Debug.LogWarning("No renderers or terrains with data found in the selection.");
                return false;
            }

            bounds.size += Vector3.one * padding;
            return true;
        }

        /// <summary>Combined world bounds of renderers and terrains. Terrains without TerrainData are skipped with a warning.</summary>
        public static bool TryGetBounds(IEnumerable<Renderer> renderers, IEnumerable<Terrain> terrains, out Bounds bounds)
        {
            bounds = new Bounds();
            bool initialized = false;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                Encapsulate(ref bounds, ref initialized, renderer.bounds);
            }

            foreach (Terrain terrain in terrains)
            {
                if (terrain == null) continue;
                if (terrain.terrainData == null)
                {
                    Debug.LogWarning($"Terrain '{terrain.name}' has no TerrainData and was left out of the bounds.", terrain);
                    continue;
                }
                Encapsulate(ref bounds, ref initialized, GetTerrainWorldBounds(terrain));
            }

            return initialized;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool initialized, Bounds other)
        {
            if (!initialized)
            {
                bounds = other;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(other);
            }
        }

        public static Bounds GetTerrainWorldBounds(Terrain terrain)
        {
            Bounds bounds = terrain.terrainData.bounds;
            bounds.center += terrain.transform.position;
            return bounds;
        }
    }
}
