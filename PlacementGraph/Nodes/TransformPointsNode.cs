using System;
using UnityEngine;

namespace UnityTools.Placement
{
    /// <summary>
    /// Modifier: random offset, rotation and scale per point. Rotation is added to the point's rotation or replaces
    /// it, optionally aligned to the surface normal; scale is uniform or per axis, and multiplies or replaces the
    /// point's scale.
    /// </summary>
    [Serializable]
    public class TransformPointsNode : PlacementNode
    {
        public enum RotationModes
        {
            /// <summary>The random rotation is applied on top of the point's rotation.</summary>
            Add,
            /// <summary>The random rotation replaces the point's rotation.</summary>
            Replace
        }

        public enum ScaleModes
        {
            /// <summary>The random scale multiplies the point's scale.</summary>
            Multiply,
            /// <summary>The random scale replaces the point's scale.</summary>
            Replace
        }

        [Tooltip("World-space offset range.")]
        public Vector3 OffsetMin = Vector3.zero;
        public Vector3 OffsetMax = Vector3.zero;
        [Tooltip("Euler angle range in degrees, applied after alignment.")]
        public Vector3 RotationMin = Vector3.zero;
        public Vector3 RotationMax = new Vector3(0f, 360f, 0f);
        [Tooltip("Uniform scale range, used with Uniform Scale on.")]
        public float ScaleMin = 1f;
        public float ScaleMax = 1f;
        [Tooltip("Per-axis scale range, used with Uniform Scale off.")]
        public Vector3 AxisScaleMin = Vector3.one;
        public Vector3 AxisScaleMax = Vector3.one;
        [Tooltip("Rotate each point so its up axis follows the surface normal, before the random rotation.")]
        public bool AlignToNormal;
        [Tooltip("Add: the random rotation goes on top of the point's rotation. Replace: it replaces it.")]
        public RotationModes RotationMode = RotationModes.Add;
        [Tooltip("On: one random scale for all axes, from Scale Min and Scale Max. Off: each axis from Axis Scale Min and Axis Scale Max.")]
        public bool UniformScale = true;
        [Tooltip("Multiply: the random scale multiplies the point's scale. Replace: it replaces it.")]
        public ScaleModes ScaleMode = ScaleModes.Multiply;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            PointData input = inputs[0];
            var result = PointData.Derive(input, input.Count);
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                uint s = point.Seed;
                var offset = new Vector3(
                    PlacementRandom.Range(s, seed, 1u, OffsetMin.x, OffsetMax.x),
                    PlacementRandom.Range(s, seed, 2u, OffsetMin.y, OffsetMax.y),
                    PlacementRandom.Range(s, seed, 3u, OffsetMin.z, OffsetMax.z));
                var euler = new Vector3(
                    PlacementRandom.Range(s, seed, 4u, RotationMin.x, RotationMax.x),
                    PlacementRandom.Range(s, seed, 5u, RotationMin.y, RotationMax.y),
                    PlacementRandom.Range(s, seed, 6u, RotationMin.z, RotationMax.z));
                // Channel 7 is the uniform scale, as before per-axis scale existed, so older graphs give the same result.
                Vector3 scale;
                if (UniformScale)
                {
                    float uniform = PlacementRandom.Range(s, seed, 7u, ScaleMin, ScaleMax);
                    scale = new Vector3(uniform, uniform, uniform);
                }
                else
                {
                    scale = new Vector3(
                        PlacementRandom.Range(s, seed, 7u, AxisScaleMin.x, AxisScaleMax.x),
                        PlacementRandom.Range(s, seed, 8u, AxisScaleMin.y, AxisScaleMax.y),
                        PlacementRandom.Range(s, seed, 9u, AxisScaleMin.z, AxisScaleMax.z));
                }

                Quaternion baseRotation;
                if (AlignToNormal) baseRotation = Quaternion.FromToRotation(Vector3.up, point.Normal);
                else baseRotation = RotationMode == RotationModes.Add ? point.Rotation : Quaternion.identity;

                PlacementPoint changed = point;
                changed.Position += offset;
                changed.Rotation = baseRotation * Quaternion.Euler(euler);
                changed.Scale = ScaleMode == ScaleModes.Multiply ? Vector3.Scale(point.Scale, scale) : scale;
                result.AddFrom(input, i, changed);
            }
            outputs[0] = result;
        }
    }
}
