using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace ObjectPooling
{
    public class PooledObject : MonoBehaviour, IPoolable
    {
        [field: SerializeField] public int PoolSize { get; set; } = 10;
        [field: SerializeField] public bool LogCreation { get; set; } = true;
        
        public GameObject GameObject => gameObject;
        public Transform Transform => transform;
        public string Name => name;
        public LinkedPool<IPoolable> Pool { get; set; }
        public bool IsInPool { get; set; }

        public void ReturnToPool()
        {
            if (IsInPool) return;
            Pool.Release(this);
        }
    }
}