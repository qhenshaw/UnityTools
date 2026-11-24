using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    /// <summary>The starting points the Placement Graph inspector's New button offers.</summary>
    public enum PlacementGraphTemplate
    {
        /// <summary>A graph with no nodes.</summary>
        Empty,
        /// <summary>Volume Sampler > Surface Sampler > Spawn Prefabs, with one Prefab Entry to fill in.</summary>
        ScatterOnSurface,
        /// <summary>Spline Sampler (Inside) > Surface Sampler > Spawn Prefabs. Needs the Splines package.</summary>
        FillSplineArea,
        /// <summary>
        /// Walls along the volume's edges: Subdivide (with a Wall Module) and Corner, merged into Spawn Prefabs, whose
        /// Prefab Entry is the corner post.
        /// </summary>
        ModularWall
    }

    /// <summary>Builds new .placegraph assets from <see cref="PlacementGraphTemplate"/>s with the authoring API.</summary>
    public static class PlacementGraphTemplates
    {
        /// <summary>The name shown in the New menu.</summary>
        public static string DisplayName(PlacementGraphTemplate template) => template switch
        {
            PlacementGraphTemplate.ScatterOnSurface => "Scatter on surface",
            PlacementGraphTemplate.FillSplineArea => "Fill spline area",
            PlacementGraphTemplate.ModularWall => "Modular wall along edges",
            _ => "Empty graph"
        };

        /// <summary>Whether the template can be built in this project (Fill spline area needs the Splines package).</summary>
        public static bool IsAvailable(PlacementGraphTemplate template)
        {
#if UNITYTOOLS_SPLINES
            return true;
#else
            return template != PlacementGraphTemplate.FillSplineArea;
#endif
        }

        /// <summary>Creates a graph at <paramref name="path"/> from <paramref name="template"/>, saves it and returns the compiled graph.</summary>
        public static PlacementGraphAsset Create(PlacementGraphTemplate template, string path)
        {
            if (!IsAvailable(template)) throw new InvalidOperationException($"{DisplayName(template)} needs the Splines package (com.unity.splines).");
            PlacementGraphHandle graph = PlacementGraphAuthoring.Create(path);
            switch (template)
            {
                case PlacementGraphTemplate.ScatterOnSurface:
                {
                    PlacementNodeHandle volume = graph.AddNode<Placement.VolumeSamplerNode>(new Vector2(0f, 0f));
                    PlacementNodeHandle surface = graph.AddNode<Placement.SurfaceSamplerNode>(new Vector2(300f, 0f));
                    PlacementNodeHandle spawn = graph.AddNode<Placement.SpawnPrefabsNode>(new Vector2(600f, 0f));
                    graph.Connect(volume, "Out", surface, "In");
                    graph.Connect(surface, "Out", spawn, "In");
                    graph.AddEntry(spawn, null);
                    break;
                }
#if UNITYTOOLS_SPLINES
                case PlacementGraphTemplate.FillSplineArea:
                {
                    PlacementNodeHandle spline = graph.AddNode<Placement.SplineSamplerNode>(new Vector2(0f, 0f)).Set("Mode", Placement.SplineSamplerNode.SampleMode.Inside);
                    PlacementNodeHandle surface = graph.AddNode<Placement.SurfaceSamplerNode>(new Vector2(300f, 0f));
                    PlacementNodeHandle spawn = graph.AddNode<Placement.SpawnPrefabsNode>(new Vector2(600f, 0f));
                    graph.Connect(spline, "Out", surface, "In");
                    graph.Connect(surface, "Out", spawn, "In");
                    graph.AddEntry(spawn, null);
                    break;
                }
#endif
                case PlacementGraphTemplate.ModularWall:
                {
                    PlacementNodeHandle subdivide = graph.AddNode<Placement.SubdivideNode>(new Vector2(0f, 0f))
                        .Set("Source", Subdivide.SourceMode.VolumeEdges).Set("Grammar", "[Wall]*");
                    PlacementNodeHandle module = graph.AddNode("Module", new Vector2(-300f, 220f)).Set("Symbol", "Wall");
                    graph.Connect(module, "Module", subdivide, "Modules");
                    PlacementNodeHandle corner = graph.AddNode<Placement.CornerNode>(new Vector2(0f, -220f)).Set("Source", Subdivide.SourceMode.VolumeEdges);
                    PlacementNodeHandle merge = graph.AddNode<Placement.MergeNode>(new Vector2(300f, 0f));
                    graph.Connect(subdivide, "Out", merge, "A");
                    graph.Connect(corner, "Out", merge, "B");
                    PlacementNodeHandle spawn = graph.AddNode<Placement.SpawnPrefabsNode>(new Vector2(600f, 0f));
                    graph.Connect(merge, "Out", spawn, "In");
                    // Wall slots carry their module's prefab; the entry is for the corner points, e.g. a post.
                    graph.AddEntry(spawn, null);
                    break;
                }
            }
            return graph.Save();
        }

        /// <summary>Copies the graph at <paramref name="source"/> to <paramref name="path"/> and returns the copy's compiled graph.</summary>
        public static PlacementGraphAsset Duplicate(string source, string path)
        {
            if (!AssetDatabase.CopyAsset(source, path)) throw new InvalidOperationException($"Couldn't copy {source} to {path}.");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<PlacementGraphAsset>(path);
        }

        /// <summary>A free path for a new graph named after <paramref name="name"/> in <paramref name="folder"/>.</summary>
        public static string SuggestPath(string folder, string name)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) folder = "Assets";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name)) name = "Placement Graph";
            return AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.{PlacementGraphDefinition.AssetExtension}");
        }
    }
}
