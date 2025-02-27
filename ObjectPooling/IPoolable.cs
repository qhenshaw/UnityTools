using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace ObjectPooling
{
    public interface IPoolable
    {
        public int PoolSize { get; }
        public GameObject GameObject { get; }
        public Transform Transform { get; }
        public string Name { get; }
        public bool LogCreation { get; }
        public LinkedPool<IPoolable> Pool { get; set; }
        public bool IsInPool { get; set; }

        void ReturnToPool();
    }
}