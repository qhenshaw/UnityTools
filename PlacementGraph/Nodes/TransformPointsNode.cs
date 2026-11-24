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

        public enum ApplyModes
        {
            /// <summary>Every point is transformed.</summary>
            All,
            /// <summary>Only points whose Prefab attribute is <see cref="Prefab"/>.</summary>
            OnlyPrefab,
            /// <summary>Every point except those whose Prefab attribute is <see cref="Prefab"/>.</summary>
            AllExceptPrefab
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
        [Tooltip("Optional. A Vector3 attribute added to each point's random offset (e.g. EntryOffset).")]
        public string OffsetAttribute = "";
        [Tooltip("Optional. A Vector3 attribute of Euler degrees added to each point's random rotation (e.g. EntryRotation).")]
        public string RotationAttribute = "";
        [Tooltip("Optional. A Float or Vector3 attribute multiplied into each point's random scale (e.g. EntryScale).")]
        public string ScaleAttribute = "";
        [Tooltip("All: every point. Only Prefab / All Except Prefab: filter by the point's Prefab attribute (from Select Prefab); other points pass through unchanged.")]
        public ApplyModes ApplyTo = ApplyModes.All;
        [Tooltip("The prefab Apply To compares the point's Prefab attribute with.")]
        public GameObject Prefab;

        public override int InputCount => 1;
        public override int OutputCount => 1;

        public override void Execute(PlacementContext context, PointData[] inputs, PointData[] outputs)
        {
            uint seed = PlacementRandom.Hash(context.SeedHash, NodeSeed);
            PointData input = inputs[0];
            var result = PointData.Derive(input, input.Count);
            PointAttribute<Vector3> offsetAttribute = Named<Vector3>(input, OffsetAttribute);
            PointAttribute<Vector3> rotationAttribute = Named<Vector3>(input, RotationAttribute);
            PointAttribute<float> scaleFloat = Named<float>(input, ScaleAttribute);
            PointAttribute<Vector3> scaleVector = Named<Vector3>(input, ScaleAttribute);
            PointAttribute<UnityEngine.Object> prefabs = input.GetAttribute<UnityEngine.Object>(PointAttributeNames.Prefab);
            if (ApplyTo != ApplyModes.All && prefabs == null)
                context.Warn(this, "Transform Points filters by prefab, but no Select Prefab runs before it");
            for (int i = 0; i < input.Count; i++)
            {
                PlacementPoint point = input.Points[i];
                if (ApplyTo != ApplyModes.All)
                {
                    // A point without a Prefab attribute never matches. Skipped points keep their place and attributes.
                    bool matches = prefabs != null && prefabs.Get(i) != null && prefabs.Get(i) == Prefab;
                    if (matches != (ApplyTo == ApplyModes.OnlyPrefab))
                    {
                        result.AddFrom(input, i);
                        continue;
                    }
                }
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
                if (offsetAttribute != null) offset += offsetAttribute.Get(i);
                if (rotationAttribute != null) euler += rotationAttribute.Get(i);
                if (scaleFloat != null) scale *= scaleFloat.Get(i);
                if (scaleVector != null) scale = Vector3.Scale(scale, scaleVector.Get(i));
                changed.Position += offset;
                changed.Rotation = baseRotation * Quaternion.Euler(euler);
                changed.Scale = ScaleMode == ScaleModes.Multiply ? Vector3.Scale(point.Scale, scale) : scale;
                result.AddFrom(input, i, changed);
            }
            outputs[0] = result;
        }

        private static PointAttribute<T> Named<T>(PointData data, string name) =>
            string.IsNullOrEmpty(name) ? null : data.GetAttribute<T>(name);
    }
}
