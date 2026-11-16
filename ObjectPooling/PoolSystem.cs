using System;
using System.Collections.Generic;
using UnityEngine;

namespace ObjectPooling
{
    /// <summary>
    /// Keeps one pool per prefab. <see cref="Instance"/> is created on first use in the active scene, and its
    /// pools are destroyed with that scene. Place one in a scene yourself to prewarm prefabs before gameplay starts.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public class PoolSystem : MonoBehaviour
    {
        [SerializeField, Tooltip("Pooled prefabs prewarmed (using each prefab's Prewarm Count) when this PoolSystem wakes up")]
        private List<PooledObject> _prewarm = new List<PooledObject>();

        private static PoolSystem _instance;

        /// <summary>The PoolSystem in use. One is created in the active scene if none exists yet.</summary>
        public static PoolSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    PoolSystem created = new GameObject("PoolSystem").AddComponent<PoolSystem>();
                    // Awake already ran inside AddComponent in Play mode; in edit mode it doesn't.
                    if (_instance == null) _instance = created;
                    created.Initialize();
                }
                return _instance;
            }
            set => _instance = value;
        }

        /// <summary>True if a PoolSystem exists, without creating one.</summary>
        public static bool HasInstance => _instance != null;

        internal static bool IsQuitting { get; private set; }

        internal Transform Staging { get; private set; }

        private Dictionary<IPoolable, PoolHandler> _handlers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            IsQuitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => IsQuitting = true;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"A PoolSystem already exists ({_instance.name}). This one's prewarm list is added to it and this one is removed.", this);
                foreach (PooledObject prefab in _prewarm)
                {
                    if (prefab != null) _instance.Prewarm(prefab);
                }
                Destroy(this);
                return;
            }

            _instance = this;
            Initialize();
            foreach (PooledObject prefab in _prewarm)
            {
                if (prefab != null) Prewarm(prefab);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Initialize()
        {
            if (_handlers != null) return;
            _handlers = new Dictionary<IPoolable, PoolHandler>();

            // New instances are created under this inactive object, so they don't wake up until they're placed.
            Staging = new GameObject("Staging").transform;
            Staging.SetParent(transform, false);
            Staging.gameObject.SetActive(false);
        }

        /// <summary>Gets an instance of <paramref name="prefab"/> at the prefab's own position and rotation.</summary>
        public T Get<T>(T prefab) where T : Component, IPoolable
        {
            CheckPrefab(prefab);
            return Get(prefab, prefab.transform.position, prefab.transform.rotation, null);
        }

        /// <summary>Gets an instance of <paramref name="prefab"/> at <paramref name="position"/>, with the prefab's rotation.</summary>
        public T Get<T>(T prefab, Vector3 position) where T : Component, IPoolable
        {
            CheckPrefab(prefab);
            return Get(prefab, position, prefab.transform.rotation, null);
        }

        /// <summary>Gets an instance of <paramref name="prefab"/> at <paramref name="position"/> and <paramref name="rotation"/>.</summary>
        public T Get<T>(T prefab, Vector3 position, Quaternion rotation) where T : Component, IPoolable
        {
            return Get(prefab, position, rotation, null);
        }

        /// <summary>
        /// Gets an instance of <paramref name="prefab"/>, parented to <paramref name="parent"/> (if given) and placed at
        /// <paramref name="position"/> and <paramref name="rotation"/> in world space before it's activated.
        /// </summary>
        public T Get<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent) where T : Component, IPoolable
        {
            CheckPrefab(prefab);
            return (T)GetHandler(prefab).Take(position, rotation, parent);
        }

        /// <summary>Fills <paramref name="prefab"/>'s pool up to its Prewarm Count now, instead of on first use.</summary>
        public void Prewarm<T>(T prefab) where T : Component, IPoolable
        {
            CheckPrefab(prefab);
            GetHandler(prefab);
        }

        /// <summary>Fills <paramref name="prefab"/>'s pool until <paramref name="count"/> instances are waiting.</summary>
        public void Prewarm<T>(T prefab, int count) where T : Component, IPoolable
        {
            CheckPrefab(prefab);
            GetHandler(prefab).Prewarm(count);
        }

        /// <summary>How many instances of <paramref name="prefab"/> are waiting in its pool.</summary>
        public int CountInactive(IPoolable prefab)
        {
            return _handlers != null && prefab != null && _handlers.TryGetValue(prefab, out PoolHandler handler) ? handler.Pool.CountInactive : 0;
        }

        [Obsolete("Use Get(prefab), which returns your own component type: Bullet bullet = PoolSystem.Instance.Get(_bulletPrefab);")]
        public IPoolable Get(IPoolable prefab)
        {
            return Get(prefab, prefab.Transform.position, prefab.Transform.rotation, null);
        }

        [Obsolete("Use Get(prefab, position), which returns your own component type: Bullet bullet = PoolSystem.Instance.Get(_bulletPrefab, position);")]
        public IPoolable Get(IPoolable prefab, Vector3 position)
        {
            return Get(prefab, position, prefab.Transform.rotation, null);
        }

        [Obsolete("Use Get(prefab, position, rotation), which returns your own component type: Bullet bullet = PoolSystem.Instance.Get(_bulletPrefab, position, rotation);")]
        public IPoolable Get(IPoolable prefab, Vector3 position, Quaternion rotation)
        {
            return Get(prefab, position, rotation, null);
        }

        [Obsolete("Use Get(prefab, position, rotation, parent), which returns your own component type: Bullet bullet = PoolSystem.Instance.Get(_bulletPrefab, position, rotation, parent);")]
        public IPoolable Get(IPoolable prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
            CheckPrefab(prefab);
            return GetHandler(prefab).Take(position, rotation, parent);
        }

        private PoolHandler GetHandler(IPoolable prefab)
        {
            Initialize();
            if (!_handlers.TryGetValue(prefab, out PoolHandler handler))
            {
                handler = new PoolHandler(prefab, this);
                _handlers.Add(prefab, handler);
                handler.Prewarm(prefab.PrewarmCount);
            }
            return handler;
        }

        private static void CheckPrefab(IPoolable prefab)
        {
            if (prefab == null || (prefab is UnityEngine.Object obj && obj == null))
            {
                throw new ArgumentNullException(nameof(prefab), "PoolSystem was given no prefab. Check that the prefab field is assigned in the Inspector.");
            }
        }
    }
}
