using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace UnityTools.Placement.Editor
{
    /// <summary>
    /// Converts graphs that use the obsolete Prefab Palette asset: every Select Prefab and Spawn Prefabs node that
    /// references a palette (directly or through a Blackboard variable) gets one Prefab Entry node per palette entry,
    /// with all its values and attributes, and stops referencing the palette. Palette variables nothing uses any more
    /// are removed. The palette assets themselves are left alone.
    /// </summary>
    public static class PlacementPaletteConverter
    {
        [MenuItem("Tools/UnityTools/Convert Placement Graph Palettes")]
        private static void ConvertAllFromMenu()
        {
            List<string> report = ConvertAll();
            string text = report.Count > 0 ? string.Join("\n", report) : "No placement graph uses a Prefab Palette.";
            Debug.Log("Convert Placement Graph Palettes:\n" + text);
            EditorUtility.DisplayDialog("Convert Placement Graph Palettes", text.Length > 1500 ? text.Substring(0, 1500) + "\n..." : text, "OK");
        }

        /// <summary>Converts every .placegraph under Assets. Returns one line per change.</summary>
        public static List<string> ConvertAll()
        {
            var report = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets($"glob:\"*.{PlacementGraphAuthoring.Extension}\"", new[] { "Assets" }))
                report.AddRange(ConvertGraph(AssetDatabase.GUIDToAssetPath(guid)));
            return report;
        }

        /// <summary>Converts one graph and saves it if anything changed. Returns one line per change.</summary>
        public static List<string> ConvertGraph(string path)
        {
            var report = new List<string>();
            PlacementGraphHandle graph = PlacementGraphAuthoring.Open(path);
            var usedVariables = new HashSet<IVariable>();
            foreach (PlacementNodeHandle node in graph.Nodes.ToList())
            {
                if (node.Node is not (SelectPrefabNode or SpawnPrefabsNode)) continue;
                IPort port = node.Node.GetInputPortByName("Palette");
                if (port == null) continue;

                PrefabPalette palette = null;
                if (port.IsConnected)
                {
                    IPort source = port.FirstConnectedPort;
                    if (source?.GetNode() is IVariableNode variableNode)
                    {
                        variableNode.Variable.TryGetDefaultValue(out palette);
                        usedVariables.Add(variableNode.Variable);
                        report.Add($"{path}: {node.Title} read palette variable '{variableNode.Variable.Name}' ({(palette != null ? palette.name : "empty")}); its per-component overrides no longer apply.");
                    }
                    graph.Graph.Disconnect(source, port);
                }
                else
                {
                    port.TryGetValue(out palette);
                }
                if (palette == null) continue;

                graph.AddEntries(node, palette.Entries);
                node.Set("Palette", null);
                report.Add($"{path}: {node.Title} now has {palette.Entries.Count} Prefab Entry nodes copied from {palette.name}.");
            }

            foreach (IVariable variable in usedVariables)
            {
                var nodes = new List<IVariableNode>();
                variable.GetNodes(nodes);
                if (nodes.Any(n => n.GetOutputPorts().Any(p => p.IsConnected))) continue;
                report.Add($"{path}: removed the palette variable '{variable.Name}', which nothing uses now.");
                graph.Graph.RemoveVariable(variable, true);
            }

            if (report.Count > 0) graph.Save();
            return report;
        }
    }
}
