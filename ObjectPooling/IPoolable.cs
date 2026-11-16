using System;
using UnityEngine;
using UnityEngine.Pool;

namespace ObjectPooling
{
    /// <summary>
    /// A component that <see cref="PoolSystem"/> can pool. Most pooled prefabs should inherit
    /// <see cref="PooledObject"/> instead of implementing this directly.
    /// </summary>
    public interface IPoolable
    {
        /// <summary>Instances created when the pool is first used, so the first Get doesn't have to instantiate.</summary>
        public int PrewarmCount => 0;

        /// <summary>Most instances kept waiting in the pool. Extras returned beyond this are destroyed. 0 = unlimited.</summary>
#pragma warning disable CS0618
        public int MaxSize => PoolSize;
#pragma warning restore CS0618

        [Obsolete("Use MaxSize (and PrewarmCount) instead.")]
        public int PoolSize => 0;

        public GameObject GameObject { get; }
        public Transform Transform { get; }
        public string Name { get; }
        /// <summary>Log a message when this prefab's pool is created.</summary>
        public bool LogCreation { get; }
        /// <summary>The pool this instance belongs to. Set by the pool when the instance is created.</summary>
        public LinkedPool<IPoolable> Pool { get; set; }
        /// <summary>True while the instance is waiting in its pool. Set by the pool.</summary>
        public bool IsInPool { get; set; }

        /// <summary>Gives this instance back to its pool instead of destroying it.</summary>
        void ReturnToPool();

        /// <summary>Called after the object is taken from the pool: it's already positioned and active.</summary>
        void OnTakenFromPool() { }

        /// <summary>Called when the object goes back to the pool, while it's still active.</summary>
        void OnReturnedToPool() { }
    }
}
