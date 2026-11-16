using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Serialization;

namespace ObjectPooling
{
    /// <summary>
    /// Base class for pooled prefabs. Get instances with <c>PoolSystem.Instance.Get(prefab, position, rotation)</c>
    /// and give them back with <see cref="ReturnToPool()"/> instead of destroying them.
    /// Override <see cref="OnTakenFromPool"/> and <see cref="OnReturnedToPool"/> to reset state
    /// (velocity, health, trails) when an instance is reused.
    /// </summary>
    public class PooledObject : MonoBehaviour, IPoolable
    {
        [SerializeField, Min(0), Tooltip("Instances created when the pool is first used, so the first Get doesn't have to instantiate")]
        private int _prewarmCount = 0;

        [SerializeField, Min(0), FormerlySerializedAs("<PoolSize>k__BackingField"),
         Tooltip("Most instances kept waiting in the pool. Extras returned beyond this are destroyed. 0 = unlimited")]
        private int _maxSize = 0;

        [field: SerializeField, Tooltip("Log a message when this prefab's pool is created")]
        public bool LogCreation { get; set; } = false;

        /// <summary>Instances created when the pool is first used. Read from the prefab when its pool is created.</summary>
        public int PrewarmCount { get => _prewarmCount; set => _prewarmCount = Mathf.Max(0, value); }
        /// <summary>Most instances kept waiting in the pool; extras returned beyond this are destroyed. 0 = unlimited. Read from the prefab when its pool is created.</summary>
        public int MaxSize { get => _maxSize; set => _maxSize = Mathf.Max(0, value); }

        [Obsolete("Use MaxSize (and PrewarmCount) instead.")]
        public int PoolSize { get => MaxSize; set => MaxSize = value; }

        public GameObject GameObject => gameObject;
        public Transform Transform => transform;
        public string Name => name;
        public LinkedPool<IPoolable> Pool { get; set; }
        /// <summary>True while this instance is waiting in its pool (inactive).</summary>
        public bool IsInPool { get; set; }

        /// <summary>The pool this instance belongs to, or null for objects that didn't come from a pool.</summary>
        internal PoolHandler Handler { get; set; }

        private Coroutine _delayedReturn;

        /// <summary>
        /// Gives this object back to its pool. Objects that didn't come from a pool (placed in the scene, or
        /// whose pool was unloaded with its scene) are destroyed instead.
        /// </summary>
        public void ReturnToPool()
        {
            if (IsInPool) return;
            if (Handler == null || !Handler.IsAlive)
            {
                Destroy(gameObject);
                return;
            }
            Handler.Release(this);
        }

        /// <summary>
        /// Gives this object back to its pool after <paramref name="delay"/> seconds (scaled time), like
        /// <c>Destroy(gameObject, delay)</c>. Calling it again restarts the timer, and the timer is cancelled
        /// if the object goes back to the pool some other way first.
        /// </summary>
        public void ReturnToPool(float delay)
        {
            if (IsInPool) return;
            if (delay <= 0f || !gameObject.activeInHierarchy)
            {
                ReturnToPool();
                return;
            }
            CancelDelayedReturn();
            _delayedReturn = StartCoroutine(ReturnAfter(delay));
        }

        private IEnumerator ReturnAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            _delayedReturn = null;
            ReturnToPool();
        }

        private void CancelDelayedReturn()
        {
            if (_delayedReturn == null) return;
            StopCoroutine(_delayedReturn);
            _delayedReturn = null;
        }

        /// <summary>Called after this object is taken from the pool: it's already positioned and active.</summary>
        protected virtual void OnTakenFromPool() { }

        /// <summary>Called when this object goes back to the pool, while it's still active.</summary>
        protected virtual void OnReturnedToPool() { }

        void IPoolable.OnTakenFromPool() => OnTakenFromPool();

        void IPoolable.OnReturnedToPool()
        {
            CancelDelayedReturn();
            OnReturnedToPool();
        }

        /// <summary>If you add your own OnDestroy, override this one and call base.OnDestroy().</summary>
        protected virtual void OnDestroy()
        {
            Handler?.NotifyDestroyed(this);
        }
    }
}
