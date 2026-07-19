using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using InspectorAttributes;
using UnityEngine.Search;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;
#endif

namespace ScatterTool
{
    [ExecuteAlways, DisallowMultipleComponent]
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

        [System.Serializable]
        public class PositionOffset
        {
            public enum OffsetMode
            {
                None,
                Surface,
                World
            }

            public OffsetMode Mode = OffsetMode.None;
            public Vector3 FixedOffset = Vector3.zero;
            public Vector2 RandomX = new Vector2(0f, 0f);
            public Vector2 RandomY = new Vector2(0f, 0f);
            public Vector2 RandomZ = new Vector2(0f, 0f);

            public Vector3 GetOffset(Vector3 normal, Vector3 tangent)
            {
                Vector3 offset = FixedOffset;
                offset.x += Random.Range(RandomX.x, RandomX.y);
                offset.y += Random.Range(RandomY.x, RandomY.y);
                offset.z += Random.Range(RandomZ.x, RandomZ.y);
                switch (Mode)
                {
                    case OffsetMode.None:
                        return Vector3.zero;
                    case OffsetMode.Surface:
                        Quaternion rotation = Quaternion.LookRotation(tangent, normal);
                        return rotation * offset;
                    case OffsetMode.World:
                        return offset;
                    default:
                        return offset;
                }
            }
        }

        private struct ScatterPoint
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector3 Tangent;
            public Quaternion Rotation;
            public float Scale;
            public float Weight;
            public int PrefabIndex;
        }

        [System.Serializable]
        public class ValueRange
        {
            public float Min;
            public float Max;
            public bool BasedOnWeight;
            public AnimationCurve WeightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            [Button("Reset Curve", true, 90), SerializeField]
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

        [System.Serializable]
        public class PrefabItem
        {
            [SearchContext("p: t:Prefab")] public GameObject Prefab;
            [Range(0f, 10f)] public float Weight = 1f;
            public ValueRange ScaleRange = new ValueRange(1f, 1f, true);
            [Range(0f, 10f)] public float ScaleMultiplier = 1f;

            public float GetScale(float weight)
            {
                return ScaleRange.GetValue(weight) * ScaleMultiplier;
            }
        }

        [SerializeField] private int _seed = 0;
        [SerializeField] private bool _autoBake = true;
        [SerializeField, Range(4, 32)] private int _resolution = 16;
        [SerializeField, Range(0.1f, 8f)] private float _density = 1f;  
        [SerializeField] private Vector3 _size = new Vector3(5f, 5f, 5f);
        [SerializeField, HideInInspector] private Bounds _bounds = new Bounds(new Vector3(0f, 0f, 2.5f), new Vector3(5f, 5f, 5f));
        [SerializeField] private LayerMask _projectionMask;
        private bool _ignoreSpawned = true;
        [SerializeField, Range(0f, 1f)] private float _jitter = 1f;
        private float _updateRate = 5f;
        [SerializeField, Range(0.01f, 1f)] private float _weightCutoff = 0.01f;
        [SerializeField, Range(0.1f, 10f)] private float _scaleMultiplier = 1f;
        [SerializeField] private PrefabItem[] _prefabs;
        [SerializeField] private NormalFilter _normalFilter;
        [SerializeField] private PositionOffset _positionOffset;
        [SerializeField] private RotationRange _rotationRange;
        [field: SerializeField] public int InstanceCount { get; private set; } = 0;

#pragma warning disable 0414
        [Button("Clear", allowEditMode: true, 60), SerializeField]
        private string _clear = nameof(Clear);

        [Button("Bake", allowEditMode: true, 60), SerializeField]
        private string _bake = nameof(Bake);
#pragma warning restore 0414

        public int TotalWeights => _resolution * _resolution;

        public float[] Weights
        {
            get
            {
                if (_weights == null || _weights.Length != TotalWeights)
                {
                    _weights = new float[TotalWeights];
                    IsDirty = true;
                }
                return _weights;
            }
        }

        public Bounds LocalBounds
        {
            get => _bounds;
            set => _bounds = value;
        }

        public Vector3 Size
        {
            get => _size;
            set
            {
                _size = value;
                _bounds.size = new Vector3(Mathf.Max(0.1f, _size.x), Mathf.Max(0.1f, _size.y), Mathf.Max(0.1f, _size.z));
                _bounds.center = new Vector3(0f, 0f, _bounds.size.z / 2f);
                IsDirty = true;
            }
        }

        public float ProjectionDistance => _bounds.size.z;

        public bool IsDirty { get => _isDirty; set => _isDirty = value; }

        [HideInInspector, SerializeField] private float[] _weights;
        private ScatterPoint[] _scatterPoints;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _previousScale;
        private bool _isDirty;
        private float _lastUpdateTime;
        private RaycastHit[] _raycastHits = new RaycastHit[32];

        private void Reset()
        {
            _projectionMask = LayerMask.GetMask("Default");
        }

        private void OnValidate()
        {
            _isDirty = true;
            transform.localScale = Vector3.one;
            _size = new Vector3(Mathf.Max(0.1f, _size.x), Mathf.Max(0.1f, _size.y), Mathf.Max(0.1f, _size.z));
            _bounds.center = new Vector3(0f, 0f, Size.z / 2f);
            _bounds.size = Size;
        }

        private void Update()
        {
            if (!Application.isEditor || Application.isPlaying) return;

            transform.localScale = Vector3.one;

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
            if (_prefabs == null || _prefabs.Length == 0) return;

            for (int i = 0; i < _prefabs.Length; i++)
            {
                if (_prefabs[i].Prefab == null) return;
            }

            if(_density <= 0f) _density = 0.1f;
            int safeDensity = Mathf.Max(1, Mathf.RoundToInt(_density));
            int densityTotal = Mathf.RoundToInt(TotalWeights * safeDensity);
            if (_weights == null || _weights.Length != TotalWeights) _weights = new float[TotalWeights];
            if (_scatterPoints == null || _scatterPoints.Length != densityTotal) _scatterPoints = new ScatterPoint[densityTotal];

            for (int i = 0; i < _scatterPoints.Length; i++)
            {
                _scatterPoints[i].Weight = 0f;
            }

            Clear();

            InstanceCount = 0;
            Vector2 spreadMax = new Vector2(1f / _resolution, 1f / _resolution);
            for (int i = 0; i < _weights.Length; i++)
            {
                float weight = _weights[i];
                if (weight > _weightCutoff)
                {
                    Vector2 normalizedCoord = LinearToNormalizedV2Coord(i, _resolution, _resolution);
                    for (int j = 0; j < safeDensity; j++)
                    {
                        Random.InitState(GetNormalizedCoordSeed(normalizedCoord) + j * 10000);
                        if(_density < 1f && Random.value > _density) continue;
                        Vector2 jitterAmount = Random.insideUnitCircle * _jitter * spreadMax;
                        Vector2 jitterCoord = normalizedCoord + jitterAmount;
                        Vector3 worldPos = NormalizedV2CoordToWorld(jitterCoord);
                        Vector3 direction = transform.forward;
                        Vector3 hitLocation = worldPos;

                        int hitCount = Physics.RaycastNonAlloc(worldPos, direction, _raycastHits, _bounds.size.z, _projectionMask);
                        if (hitCount >= 1)
                        {
                            RaycastHit hit;
                            if (!_ignoreSpawned)
                            {
                                Array.Sort(_raycastHits, 0, hitCount, DistanceComparer.Instance);
                                hit = _raycastHits[0];
                            }
                            else if (!GetClosestNonChildHit(_raycastHits, hitCount, transform, out hit))
                            {
                                continue;
                            }

                            if (_normalFilter.Filter(hit.normal, transform.forward))
                            {
                                Vector3 tangent = Vector3.Cross(hit.normal, direction);
                                if(tangent.magnitude < 0.1f)
                                {
                                    tangent = (tangent + new Vector3(0f, 0f, 0.01f)).normalized;
                                }
                                int prefabIndex = GetRandomWeightedPrefabIndex();
                                ScatterPoint scatterPoint = new ScatterPoint
                                {
                                    Position = hit.point + _positionOffset.GetOffset(hit.normal, tangent),
                                    Normal = hit.normal,
                                    Tangent = tangent,
                                    Rotation = _rotationRange.GetRotation(hit.normal, tangent),
                                    Scale = _prefabs[prefabIndex].GetScale(weight) * _scaleMultiplier,
                                    Weight = weight,
                                    PrefabIndex = prefabIndex,
                                };
                                _scatterPoints[InstanceCount++] = scatterPoint;
                            }
                        }
                    }
                }
            }

            if(_autoBake) Bake();
        }

        private int GetRandomWeightedPrefabIndex()
        {
            float totalWeight = 0f;
            for (int i = 0; i < _prefabs.Length; i++)
            {
                totalWeight += _prefabs[i].Weight;
            }
            float randomValue = Random.Range(0f, totalWeight);
            float cumulativeWeight = 0f;
            for (int i = 0; i < _prefabs.Length; i++)
            {
                cumulativeWeight += _prefabs[i].Weight;
                if (randomValue <= cumulativeWeight)
                {
                    return i;
                }
            }

            return 0;
        }

        private int GetNormalizedCoordSeed(Vector2 normalizedCoord)
        {
            int x = Mathf.FloorToInt(normalizedCoord.x * 5646575);
            int y = Mathf.FloorToInt(normalizedCoord.y * 8686435);
            return x + y + _seed;
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
            Random.InitState(_seed);
            for (int i = 0; i < _scatterPoints.Length; i++)
            {
                ScatterPoint point = _scatterPoints[i];
                if (point.Weight > _weightCutoff && _prefabs.Length > 0)
                {
                    PrefabItem prefabItem = _prefabs[point.PrefabIndex];
                    GameObject prefab = prefabItem.Prefab;
                    if (prefab != null)
                    {
                        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, transform) as GameObject;
                        instance.transform.position = point.Position;
                        instance.transform.rotation = point.Rotation;
                        instance.transform.localScale = new Vector3(1f / transform.localScale.x, 1f / transform.localScale.y, 1f / transform.localScale.z);
                        instance.transform.localScale *= point.Scale;
                    }
                }
            }
        }

        private Vector2 LinearToNormalizedV2Coord(int index, int width, int height)
        {
            int x = index % width;
            int y = index / width;
            return new Vector2((float)x / (width - 1), (float)y / (height - 1));
        }

        private Vector3 NormalizedV2CoordToWorld(Vector2 normalizedCoord)
        {
            Vector3 localPos = new Vector3((normalizedCoord.x - 0.5f) * _bounds.size.x, (normalizedCoord.y - 0.5f) * _bounds.size.y, 0f);
            return transform.TransformPoint(localPos);
        }

        public bool GetMouseHit(Ray mouseRay, out RaycastHit hit)
        {
            Vector3 planeNormal = -transform.forward;
            Plane projectionPlane = new Plane(planeNormal, transform.position);
            if (projectionPlane.Raycast(mouseRay, out float enter))
            {
                Vector3 hitPoint = mouseRay.GetPoint(enter);
                Vector3 localHitPoint = transform.InverseTransformPoint(hitPoint);
                Vector2 normalizedCoord = new Vector2((localHitPoint.x / _bounds.size.x) + 0.5f, (localHitPoint.y / _bounds.size.y) + 0.5f);
                if (normalizedCoord.x >= 0f && normalizedCoord.x <= 1f && normalizedCoord.y >= 0f && normalizedCoord.y <= 1f)
                {
                    hit = new RaycastHit();
                    hit.point = hitPoint;
                    hit.normal = planeNormal;
                    return true;
                }
            }
            hit = new RaycastHit();
            return false;
        }

        public Vector3 GetWorldPositionFromWeightIndex(int weightIndex)
        {
            Vector2 normalizedCoord = LinearToNormalizedV2Coord(weightIndex, _resolution, _resolution);
            return NormalizedV2CoordToWorld(normalizedCoord);
        }

        public void SetWeight(int weightIndex, float weight)
        {
            Weights[weightIndex] = Mathf.Clamp01(weight);
            _isDirty = true;
        }

        public void SetWeights(float[] newWeights)
        {
            if (newWeights.Length != Weights.Length)
            {
                Debug.LogError("New weights array length does not match the existing weights array length.");
                return;
            }
            for (int i = 0; i < Weights.Length; i++)
            {
                Weights[i] = Mathf.Clamp01(newWeights[i]);
            }

            IsDirty = true;
        }

        public void Flood(float value)
        {
            for (int i = 0; i < Weights.Length; i++)
            {
                Weights[i] = Mathf.Clamp01(value);
            }
            IsDirty = true;
        }

        [MenuItem("GameObject/Projection Scatter Tool", false, 0)]
        static void CreateCustomGameObject(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Projection Scatter");
            ProjectionScatter ps = go.AddComponent<ProjectionScatter>();
            ps._seed = Random.Range(0, int.MaxValue);
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            go.transform.forward = Vector3.down;
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
#endif
    }
}