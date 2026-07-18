using UnityEngine;
using InspectorAttributes;
using UnityEngine.Search;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ScatterTool
{
    [ExecuteAlways]
    public class ProjectionScatter : MonoBehaviour
    {
#if UNITY_EDITOR

        private class DistanceComparer : IComparer<RaycastHit>
        {
            public static readonly DistanceComparer Instance = new DistanceComparer();

            public int Compare(RaycastHit x, RaycastHit y)
            {
                return x.distance.CompareTo(y.distance);
            }
        }

        [System.Serializable]
        private class NormalFilter
        {
            public enum NormalFilterMode
            {
                None,
                Projector,
                Custom
            }

            public NormalFilterMode Mode = NormalFilterMode.Custom;
            public Vector3 CustomDirection = new Vector3(0f, 1f, 0f);
            public float AngleThreshold = 45f;

            public bool Filter(Vector3 normal, Vector3 projectorForward)
            {
                switch (Mode)
                {
                    case NormalFilterMode.None:
                        return true;
                    case NormalFilterMode.Projector:
                        return Vector3.Angle(normal, -projectorForward) <= AngleThreshold;
                    case NormalFilterMode.Custom:
                        return Vector3.Angle(normal, CustomDirection.normalized) <= AngleThreshold;
                    default:
                        return true;
                }
            }
        }

        [System.Serializable]
        private class RotationRange
        {
            public enum RotationMode
            {
                Surface,
                World
            }

            public RotationMode Mode = RotationMode.Surface;
            public Vector2 RangeX = new Vector2(0f, 0f);
            public Vector2 RangeY = new Vector2(0f, 360f);
            public Vector2 RangeZ = new Vector2(0f, 0f);

            public Quaternion GetRotation(Vector3 normal, Vector3 tangent)
            {
                Quaternion baseRotation = Quaternion.identity;
                switch (Mode)
                {
                    case RotationMode.Surface:
                        baseRotation = Quaternion.LookRotation(tangent, normal);
                        break;
                    case RotationMode.World:
                        baseRotation = Quaternion.identity;
                        break;
                }
                float rotX = Random.Range(RangeX.x, RangeX.y);
                float rotY = Random.Range(RangeY.x, RangeY.y);
                float rotZ = Random.Range(RangeZ.x, RangeZ.y);
                Quaternion randomRotation = Quaternion.Euler(rotX, rotY, rotZ);
                return baseRotation * randomRotation;
            }
        }

        private struct ScatterPoint
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector3 Tangent;
            public float Weight;
        }

        [System.Serializable]
        private class ValueRange
        {
            public float Min;
            public float Max;
            public bool BasedOnWeight;
            public AnimationCurve WeightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            [Button("Reset Curve", allowEditMode: true), SerializeField]
            public string _resetCurve = nameof(ResetCurve);

            public void ResetCurve()
            {
                WeightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }

            public ValueRange(float min, float max, bool basedOnWeight)
            {
                Min = min;
                Max = max;
                BasedOnWeight = basedOnWeight;
            }

            public float GetValue(float weight)
            {
                if (BasedOnWeight)
                {
                    float curved = WeightCurve.Evaluate(weight);
                    return Mathf.Lerp(Min, Max, curved);
                }
                else
                {
                    return Random.Range(Min, Max);
                }
            }
        }

        [SerializeField] private Texture2D _scatterMap;
        [SerializeField] private int _seed = 0;
        [SerializeField] private bool _randomSeed = false;
        [SerializeField] private Vector2 _size = new Vector2(5f, 5f);
        [SerializeField] private float _projectionDistance = 10f;
        [SerializeField] private LayerMask _projectionMask;
        [SerializeField] private float _jitter = 0.4f;
        [SerializeField] private float _updateRate = 5f;
        [SerializeField] private float _weightCutoff = 0.1f;
        [SerializeField] private bool _autoBake;
        [SerializeField, Range(0.1f, 8f)] private float _density = 1f;
        [SerializeField] private NormalFilter _normalFilter;
        [SerializeField] private RotationRange _rotationRange;
        [SerializeField] private ValueRange _scaleRange = new ValueRange(1f, 2f, true);
        [SerializeField, SearchContext("p: t:Prefab")] private GameObject[] _prefabs;

        [Button("Clear", allowEditMode: true), SerializeField]
        private string _clear = nameof(Clear);

        [Button("Bake", allowEditMode: true), SerializeField]
        private string _bake = nameof(Bake);

        private Mesh _quadMesh;
        private Color32[] _pixels;
        private float[] _weights;
        private ScatterPoint[] _scatterPoints;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _previousScale;
        private bool _isDirty;
        private float _lastUpdateTime;
        private RaycastHit[] _raycastHits = new RaycastHit[32];

        private void Reset()
        {
            _projectionDistance = LayerMask.NameToLayer("Default");
        }

        private void OnValidate()
        {
            _isDirty = true;
        }

        private void Update()
        {
            if (!Application.isEditor || Application.isPlaying) return;

            if (transform.position != _previousPosition || 
                transform.rotation != _previousRotation ||
                transform.localScale != _previousScale)
            {
                _isDirty = true;
                _previousPosition = transform.position;
                _previousRotation = transform.rotation;
                _previousScale = transform.localScale;
            }

            if(_isDirty && Time.time >= _lastUpdateTime + 1f / _updateRate)
            {
                UpdatePoints();
                _isDirty = false;
                _lastUpdateTime = Time.time;
            }
        }

        private void UpdatePoints()
        {
            if (!Application.isEditor || Application.isPlaying) return;
            if (_scatterMap == null) return;

            for (int i = 0; i < _prefabs.Length; i++)
            {
                if (_prefabs[i] == null) return;
            }

            int total = _scatterMap.width * _scatterMap.height;
            if(_density <= 0f) _density = 0.1f;
            int safeDensity = Mathf.Max(1, Mathf.RoundToInt(_density));
            int densityTotal = Mathf.RoundToInt(total * safeDensity);
            if (_pixels == null || _pixels.Length != total) _pixels = new Color32[total];
            if (_weights == null || _weights.Length != total) _weights = new float[total];
            if (_scatterPoints == null || _scatterPoints.Length != densityTotal) _scatterPoints = new ScatterPoint[densityTotal];

            _pixels = _scatterMap.GetPixels32();
            for (int i = 0; i < _pixels.Length; i++)
            {
                _weights[i] = _pixels[i].r / 255f;
            }

            if (_randomSeed) _seed = Random.Range(int.MinValue, int.MaxValue);
            Random.InitState(_seed);

            for (int i = 0; i < _scatterPoints.Length; i++)
            {
                _scatterPoints[i].Weight = 0f;
            }

            int scatterCount = 0;
            Vector2 spreadMax = new Vector2(1f / _scatterMap.width, 1f / _scatterMap.height);
            for (int i = 0; i < _weights.Length; i++)
            {
                float weight = _weights[i];
                if (weight > _weightCutoff)
                {
                    Vector2 normalizedCoord = LinearToNormalizedV2Coord(i, _scatterMap.width, _scatterMap.height);
                    for (int j = 0; j < safeDensity; j++)
                    {
                        if(_density < 1f && Random.value > _density) continue;
                        Vector2 spread = new Vector2(Random.Range(-_jitter, _jitter) * spreadMax.x, Random.Range(-_jitter, _jitter) * spreadMax.y);
                        Vector2 jitterCoord = normalizedCoord + spread;
                        Vector3 worldPos = NormalizedV2CoordToWorld(jitterCoord);
                        Vector3 direction = transform.forward;
                        Vector3 hitLocation = worldPos;

                        int hitCount = Physics.RaycastNonAlloc(worldPos, direction, _raycastHits, _projectionDistance, _projectionMask);
                        if (hitCount >= 1)
                        {
                            if(!GetClosestNonChildHit(_raycastHits, hitCount, transform, out RaycastHit hit)) continue;
                            if (_normalFilter.Filter(hit.normal, transform.forward))
                            {
                                Vector3 cross = Vector3.Cross(hit.normal, direction);
                                if(cross.magnitude < 0.1f)
                                {
                                    cross = (cross + new Vector3(0f, 0f, 0.01f)).normalized;
                                }
                                ScatterPoint scatterPoint = new ScatterPoint
                                {
                                    Position = hit.point,
                                    Normal = hit.normal,
                                    Tangent = cross,
                                    Weight = weight
                                };
                                _scatterPoints[scatterCount++] = scatterPoint;
                            }
                        }
                    }
                }
            }

            if(_autoBake) Bake();
        }

        private bool GetClosestNonChildHit(RaycastHit[] hits, int hitCount, Transform parent, out RaycastHit validHit)
        {
            Array.Sort(hits, 0, hitCount, DistanceComparer.Instance);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider != null && !hit.collider.transform.IsChildOf(parent))
                {
                    validHit = hit;
                    return true;
                }
            }

            validHit = new RaycastHit();
            return false;
        }

        public void Clear()
        {
            List<Transform> children = new List<Transform>();
            foreach (Transform child in transform)
            {
                children.Add(child);
            }
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] == transform) continue;
                DestroyImmediate(children[i].gameObject);
            }
        }

        public void Bake()
        {
            Clear();
            for (int i = 0; i < _scatterPoints.Length; i++)
            {
                ScatterPoint point = _scatterPoints[i];
                if (point.Weight > _weightCutoff && _prefabs.Length > 0)
                {
                    int prefabIndex = Random.Range(0, _prefabs.Length);
                    GameObject prefab = _prefabs[prefabIndex];
                    if (prefab != null)
                    {
                        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, transform) as GameObject;
                        instance.transform.position = point.Position;
                        instance.transform.rotation = _rotationRange.GetRotation(point.Normal, point.Tangent);
                        instance.transform.localScale = new Vector3(1f / transform.localScale.x, 1f / transform.localScale.y, 1f / transform.localScale.z);
                        instance.transform.localScale *= _scaleRange.GetValue(point.Weight);
                    }
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireMesh(GetQuadMesh(), transform.position, transform.rotation, new Vector3(_size.x * transform.localScale.x, _size.y * transform.localScale.y, 1f));
            Gizmos.DrawWireMesh(GetQuadMesh(), transform.position + transform.forward * _projectionDistance, transform.rotation, new Vector3(_size.x * transform.localScale.x, _size.y * transform.localScale.y, 1f));
            Gizmos.DrawRay(NormalizedV2CoordToWorld(new Vector2(0f, 0f)), transform.forward * _projectionDistance);
            Gizmos.DrawRay(NormalizedV2CoordToWorld(new Vector2(1f, 0f)), transform.forward * _projectionDistance);
            Gizmos.DrawRay(NormalizedV2CoordToWorld(new Vector2(0f, 1f)), transform.forward * _projectionDistance);
            Gizmos.DrawRay(NormalizedV2CoordToWorld(new Vector2(1f, 1f)), transform.forward * _projectionDistance);

            if (_scatterMap == null) return;
            if (_weights == null) return;
            if (_scatterPoints == null) return;

            for (int i = 0; i < _weights.Length; i++)
            {
                float weight = _weights[i];
                if (weight > _weightCutoff)
                {
                    Vector2 normalizedCoord = LinearToNormalizedV2Coord(i, _scatterMap.width, _scatterMap.height);
                    Vector3 worldPos = NormalizedV2CoordToWorld(normalizedCoord);
                    Gizmos.color = new Color(1f, 1f, 1f, weight);
                    Gizmos.DrawSphere(worldPos, 0.05f);
                }
            }

            for (int i = 0; i < _scatterPoints.Length; i++)
            {
                float weight = _scatterPoints[i].Weight;
                Gizmos.color = new Color(1f, 0f, 0f, weight);
                Gizmos.DrawSphere(_scatterPoints[i].Position, 0.05f);
            }
        }

        private Mesh GetQuadMesh()
        {
            if(_quadMesh == null)
            {
                _quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            }
            return _quadMesh;
        }

        private Vector2 LinearToNormalizedV2Coord(int index, int width, int height)
        {
            int x = index % width;
            int y = index / width;
            return new Vector2((float)x / (width - 1), (float)y / (height - 1));
        }

        private Vector3 NormalizedV2CoordToWorld(Vector2 normalizedCoord)
        {
            Vector3 localPos = new Vector3((normalizedCoord.x - 0.5f) * _size.x, (normalizedCoord.y - 0.5f) * _size.y, 0f);
            return transform.TransformPoint(localPos);
        }
#endif
    }
}