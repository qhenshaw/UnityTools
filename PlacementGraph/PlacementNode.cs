using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// The runtime half of a graph node: its settings and what it does. The editor's Graph Toolkit node compiles
    /// into one of these on import, so nothing here depends on Graph Toolkit and it runs in players too.
    /// </summary>
    [Serializable]
    public abstract class PlacementNode
    {
        [SerializeField, HideInInspector] private string _name;
        [SerializeField, HideInInspector] private uint _nodeSeed;

        /// <summary>Display name, used in logs and the debug view.</summary>
        public string Name { get => _name; internal set => _name = value; }

        /// <summary>Seed mixed into every random value this node draws, so nodes don't share random streams.</summary>
        public uint NodeSeed { get => _nodeSeed; internal set => _nodeSeed = value; }

        /// <summary>Number of point inputs. Several wires into one input are merged in wire order.</summary>
        public abstract int InputCount { get; }

        /// <summary>Number of point outputs.</summary>
        public abstract int OutputCount { get; }

        /// <summary>
        /// Runs the node. <paramref name="inputs"/> has <see cref="InputCount"/> entries (empty sets when unconnected);
        /// fill <paramref name="outputs"/>, which has <see cref="OutputCount"/> slots. Don't modify the input lists.
        /// </summary>
        public abstract void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs);
    }

    /// <summary>A wire in a compiled graph: from an output of one node to an input of another (indices into the node list).</summary>
    [Serializable]
    public struct PlacementEdge
    {
        public int FromNode;
        public int FromPort;
        public int ToNode;
        public int ToPort;

        public PlacementEdge(int fromNode, int fromPort, int toNode, int toPort)
        {
            FromNode = fromNode;
            FromPort = fromPort;
            ToNode = toNode;
            ToPort = toPort;
        }
    }
}
