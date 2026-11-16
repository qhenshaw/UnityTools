using System;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace ObjectPooling
{
    /// <summary>The pool for one prefab. Created by <see cref="PoolSystem"/>; you don't need to use it directly.</summary>
    public class PoolHandler
    {
        /// <summary>The Unity LinkedPool holding this prefab's waiting instances.</summary>
        public LinkedPool<IPoolable> Pool { get; private set; }

        /// <summary>False once the pool's GameObject is gone (destroyed, or unloaded with its scene).</summary>
        public bool IsAlive => _group != null;

        private readonly IPoolable _prefab;
        private readonly PoolSystem _owner;
        private readonly Transform _group;
        private readonly Transform _staging;
        private readonly int _maxSize;
        private bool _prewarming;
        private bool _reportedDestroyed;

        internal PoolHandler(IPoolable prefab, PoolSystem owner)
        {
            _prefab = prefab;
            _owner = owner;
            _staging = owner.Staging;
            _maxSize = prefab.MaxSize > 0 ? prefab.MaxSize : int.MaxValue;
            Pool = new LinkedPool<IPoolable>(CreateItem, OnTakeItem, OnReturnItem, DestroyItem, true, _maxSize);

            _group = new GameObject($"{prefab.Name} Pool").transform;
            _group.SetParent(owner.transform, false);
            if (prefab.LogCreation) Debug.Log($"{prefab.Name} pool created, prewarm: {prefab.PrewarmCount}, max: {(prefab.MaxSize > 0 ? prefab.MaxSize.ToString() : "unlimited")}", prefab.GameObject);
        }

        [Obsolete("Pools are created by PoolSystem.Get; use that instead.")]
        public PoolHandler(IPoolable prefab, int poolSize, Transform root)
            : this(prefab, root != null && root.TryGetComponent(out PoolSystem system) ? system : PoolSystem.Instance) { }

        /// <summary>Takes an instance, places it, then activates it, so Awake and OnEnable see the spawn position.</summary>
        internal IPoolable Take(Vector3 position, Quaternion rotation, Transform parent)
        {
            IPoolable item = Pool.Get();
            while (IsDestroyed(item))
            {
                ReportDestroyed();
                item = Pool.Get();
            }

            Transform t = item.Transform;
            t.SetParent(parent != null ? parent : _group, false);
            t.SetPositionAndRotation(position, rotation);
            item.GameObject.SetActive(true);
            item.OnTakenFromPool();
            return item;
        }

        internal void Release(IPoolable item) => Pool.Release(item);

        /// <summary>Fills the pool until <paramref name="count"/> instances are waiting (capped at the max size).</summary>
        internal void Prewarm(int count)
        {
            int target = Mathf.Min(count, _maxSize);
            _prewarming = true;
            try
            {
                while (Pool.CountInactive < target) Pool.Release(CreateItem());
            }
            finally
            {
                _prewarming = false;
            }
        }

        /// <summary>Called by a <see cref="PooledObject"/> from OnDestroy, to catch Destroy() being used instead of ReturnToPool().</summary>
        internal void NotifyDestroyed(PooledObject obj)
        {
            if (!IsAlive || PoolSystem.IsQuitting) return;
            // The pool itself is being destroyed or unloaded, taking its objects with it.
            if (_owner == null || !_owner.isActiveAndEnabled || !_owner.gameObject.scene.isLoaded) return;
            // The object's own scene is being unloaded.
            if (!obj.gameObject.scene.isLoaded) return;
            ReportDestroyed();
        }

        private void ReportDestroyed()
        {
            if (_reportedDestroyed) return;
            _reportedDestroyed = true;
            Debug.LogError($"A pooled {_prefab.Name} was destroyed while pooled (Destroy() was called on it or on its parent). " +
                           "Call ReturnToPool() instead so the pool can reuse it. This is only reported once per prefab.", _prefab.GameObject);
        }

        // Instantiates under an inactive parent so the new object's Awake and OnEnable wait until it's taken and placed.
        private IPoolable CreateItem()
        {
            GameObject instance = Object.Instantiate(_prefab.GameObject, _staging, false);
            instance.SetActive(false);
            instance.transform.SetParent(_group, false);

            IPoolable poolable = _prefab is Component component
                ? (IPoolable)instance.GetComponent(component.GetType())
                : instance.GetComponent<IPoolable>();
            poolable.Pool = Pool;
            if (poolable is PooledObject pooledObject) pooledObject.Handler = this;
            return poolable;
        }

        private void OnTakeItem(IPoolable item)
        {
            if (IsDestroyed(item)) return;
            item.IsInPool = false;
        }

        private void OnReturnItem(IPoolable item)
        {
            if (IsDestroyed(item)) return;
            item.IsInPool = true;
            if (!_prewarming) item.OnReturnedToPool();

            // Pool.Release called directly after the pool's scene was unloaded: nothing left to keep it in.
            if (!IsAlive)
            {
                Object.Destroy(item.GameObject);
                return;
            }

            item.GameObject.SetActive(false);
            item.Transform.SetParent(_group, false);
        }

        // Called when the pool is full and an extra instance comes back.
        private void DestroyItem(IPoolable item)
        {
            if (IsDestroyed(item)) return;
            if (item is PooledObject pooledObject) pooledObject.Handler = null;
            Object.Destroy(item.GameObject);
        }

        private static bool IsDestroyed(IPoolable item) => item == null || (item is Object obj && obj == null);
    }
}
