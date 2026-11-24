using System.Collections.Generic;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Which Placement Graph inspector fields apply, from the compiled graph and the component's settings. Pure
    /// functions, so the rules are tested apart from the inspector.
    /// </summary>
    public static class PlacementGraphInspectorRules
    {
        /// <summary>Whether a node of the graph reads the component's Spline (Spline Sampler, or a modular node set to Spline Knots).</summary>
        public static bool UsesSpline(PlacementGraphAsset graph)
        {
            foreach (PlacementNode node in Nodes(graph))
            {
#if UNITYTOOLS_SPLINES
                if (node is Placement.SplineSamplerNode) return true;
#endif
                if (node is Placement.SubdivideNode subdivide && subdivide.Source == Subdivide.SourceMode.SplineKnots) return true;
                if (node is Placement.CornerNode corner && corner.Source == Subdivide.SourceMode.SplineKnots) return true;
                if (node is Placement.SpawnSplineMeshNode mesh && mesh.Source == Subdivide.SourceMode.SplineKnots) return true;
            }
            return false;
        }

        /// <summary>Whether a node of the graph reads the component's Markers (Child Markers).</summary>
        public static bool UsesMarkers(PlacementGraphAsset graph)
        {
            foreach (PlacementNode node in Nodes(graph))
            {
                if (node is Placement.ChildMarkersNode) return true;
            }
            return false;
        }

        /// <summary>A scene input field is shown when the graph uses it, or when something is assigned to it (never hidden silently).</summary>
        public static bool ShowSceneInput(bool used, bool assigned) => used || assigned;

        /// <summary>Load Radius, Unload Radius, Frame Budget and Use 3D Distance: Near Player only.</summary>
        public static bool ShowStreamingFields(PlacementGraph.GenerateMode mode) => mode == PlacementGraph.GenerateMode.NearPlayer;

        /// <summary>Track Scene Changes and Pooling: anything that generates at runtime.</summary>
        public static bool ShowRuntimeFields(PlacementGraph.GenerateMode mode) => mode != PlacementGraph.GenerateMode.EditorOnly;

        /// <summary>Max Pool Size: with Pooling on, at runtime.</summary>
        public static bool ShowMaxPoolSize(PlacementGraph.GenerateMode mode, bool pooling) => ShowRuntimeFields(mode) && pooling;

        /// <summary>Cell Size and Cell Overlap: only when the volume is partitioned.</summary>
        public static bool ShowCellFields(float cellSize) => cellSize > 0f;

        /// <summary>The cell size the Partition toggle restores: the last one used, or 20 m.</summary>
        public static float RestoredCellSize(float remembered) => remembered > 0f ? remembered : 20f;

        /// <summary>The collapsed Placement header's summary, e.g. "Seed 3 · 20×10×20 m · 20 m cells".</summary>
        public static string PlacementSummary(int seed, UnityEngine.Vector3 size, float cellSize) =>
            $"Seed {seed} · {size.x:0.##}×{size.y:0.##}×{size.z:0.##} m · " + (cellSize > 0f ? $"{cellSize:0.##} m cells" : "one volume");

        /// <summary>The status line under Generate, e.g. "120 objects · 4 cells · last run 3.2 ms".</summary>
        public static string Status(int objects, int cells, double milliseconds, int instanced, int drawCalls)
        {
            var parts = new List<string> { $"{objects} object{(objects == 1 ? "" : "s")}" };
            if (cells > 0) parts.Add($"{cells} cell{(cells == 1 ? "" : "s")}");
            if (instanced > 0) parts.Add($"{instanced} instanced ({drawCalls} draw call{(drawCalls == 1 ? "" : "s")})");
            parts.Add(milliseconds > 0 ? $"last run {milliseconds:0.0} ms" : "not run yet");
            return string.Join(" · ", parts);
        }

        private static IEnumerable<PlacementNode> Nodes(PlacementGraphAsset graph)
        {
            if (graph == null) yield break;
            foreach (PlacementNode node in graph.Nodes)
            {
                if (node != null) yield return node;
            }
        }
    }
}
