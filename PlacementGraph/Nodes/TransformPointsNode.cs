using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>Modifier: random offset, rotation and uniform scale per point, optionally aligned to the surface normal.</summary>
    [Serializable]
    public class TransformPointsNode : PlacementNode
    {
        [Tooltip("World-space offset range.")]
        public Vector3 OffsetMin = Vector3.zero;
        public Vector3 OffsetMax = Vector3.zero;
        [Tooltip("Euler angle range in degrees, applied after alignment.")]
        public Vector3 RotationMin = Vector3.zero;
        public Vector3 RotationMax = new Vector3(0f, 360f, 0f);
        [Tooltip("Uniform scale range, multiplied into the point's scale.")]
        public float ScaleMin = 1f;
        public float ScaleMax = 1f;
        [Tooltip("Rotate each point so its up axis follows the surface normal.")]
        public bool AlignToNormal;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            var result = new PointData(inputs[0].Count);
            foreach (PlacementPoint point in inputs[0].Points)
            {
                uint s = point.Seed;
                var offset = new Vector3(
                    PlacementRandom.Range(s, seed, 1u, OffsetMin.x, OffsetMax.x),
                    PlacementRandom.Range(s, seed, 2u, OffsetMin.y, OffsetMax.y),
                    PlacementRandom.Range(s, seed, 3u, OffsetMin.z, OffsetMax.z));
                var euler = new Vector3(
                    PlacementRandom.Range(s, seed, 4u, RotationMin.x, RotationMax.x),
                    PlacementRandom.Range(s, seed, 5u, RotationMin.y, RotationMax.y),
                    PlacementRandom.Range(s, seed, 6u, RotationMin.z, RotationMax.z));
                float scale = PlacementRandom.Range(s, seed, 7u, ScaleMin, ScaleMax);

                PlacementPoint changed = point;
                Quaternion baseRotation = AlignToNormal ? Quaternion.FromToRotation(Vector3.up, point.Normal) : point.Rotation;
                changed.Position += offset;
                changed.Rotation = baseRotation * Quaternion.Euler(euler);
                changed.Scale = point.Scale * scale;
                result.Points.Add(changed);
            }
            outputs[0] = result;
        }
    }
}
