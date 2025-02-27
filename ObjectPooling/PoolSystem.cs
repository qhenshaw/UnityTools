using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ObjectPooling
{
    public class PoolSystem : MonoBehaviour
    {
        private static PoolSystem _instance;

        public static PoolSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    Debug.Log("Initializing PoolSystem...");
                    _instance = new GameObject("PoolSystem").AddComponent<PoolSystem>();
                    _instance._poolHandlers = new Dictionary<IPoolable, PoolHandler>();
                }
                return _instance;
            }
            set => _instance = value;
        }

        private Dictionary<IPoolable, PoolHandler> _poolHandlers;

        public IPoolable Get(IPoolable prefab)
        {
            return Get(prefab, prefab.Transform.position, prefab.Transform.rotation, null);
        }

        public IPoolable Get(IPoolable prefab, Vector3 position)
        {
            return Get(prefab, position, prefab.Transform.rotation, null);
        }

        public IPoolable Get(IPoolable prefab, Vector3 position, Quaternion rotation)
        {
            return Get(prefab, position, rotation, null);
        }

        public IPoolable Get(IPoolable prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
            if (!_poolHandlers.ContainsKey(prefab))
            {
                PoolHandler handler = new PoolHandler(prefab, prefab.PoolSize, transform);
                _poolHandlers.Add(prefab, handler);
            }

            IPoolable pooled = _poolHandlers[prefab].Pool.Get();
            pooled.Transform.position = position;
            pooled.Transform.rotation = rotation;
            pooled.Transform.parent = parent;
            return pooled;
        }
    }
}