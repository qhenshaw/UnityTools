using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace ObjectPooling
{
    public class PoolHandler
    {
        public LinkedPool<IPoolable> Pool { get; private set; }

        private Transform _parent;
        private Vector3 _hidePosition = new Vector3(0f, -1000f, 0f);
        private IPoolable _prefab;

        public PoolHandler(IPoolable prefab, int poolSize, Transform root)
        {
            _prefab = prefab;
            Pool = new LinkedPool<IPoolable>(OnCreateItem, OnTakeItem, OnReturnItem, OnDestroyItem, true, poolSize);
            _parent = new GameObject($"{_prefab.Name} Pool").transform;
            _parent.SetParent(root);
            if(prefab.LogCreation) Debug.Log($"{_prefab.Name} pool created, size: {poolSize}");
        }

        // called when pool is empty and we need a new object
        private IPoolable OnCreateItem()
        {
            GameObject instantiated = GameObject.Instantiate(_prefab.GameObject, _hidePosition, Quaternion.identity, _parent);
            if(instantiated.TryGetComponent(out IPoolable poolable))
            poolable.Pool = Pool;
            return poolable;
        }

        // called when object is removed from pool for use
        private void OnTakeItem(IPoolable obj)
        {
            obj.GameObject.SetActive(true);
            obj.IsInPool = false;
        }

        // called when object is returned to wait for next use
        private void OnReturnItem(IPoolable obj)
        {
            obj.IsInPool = true;
            obj.GameObject.SetActive(false);
            obj.Transform.position = _hidePosition;
            obj.Transform.SetParent(_parent);
        }

        // called when object is destroyed (permanently removed from pool)
        // usually happens when pool overflows
        private void OnDestroyItem(IPoolable obj)
        {
            GameObject.Destroy(obj.GameObject);
        }
    }
}