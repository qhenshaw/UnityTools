using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ServiceAccess
{
    /// <summary>
    /// Finds the one instance of a class, e.g. the AudioManager, without a direct reference.
    /// Register in Awake (<c>ServiceLocator.Register(this)</c>) and get in Start or later
    /// (<c>ServiceLocator.Get&lt;AudioManager&gt;()</c>). Services survive scene loads; destroyed ones count as
    /// unregistered. Everything is cleared when Play starts.
    /// </summary>
    public static class ServiceLocator
    {
        /// <summary>One registered service, for debugging tools.</summary>
        public readonly struct ServiceInfo
        {
            public readonly Type RegisteredAs;
            public readonly object Service;
            public bool IsDestroyed => IsMissing(Service);

            public ServiceInfo(Type registeredAs, object service)
            {
                RegisteredAs = registeredAs;
                Service = service;
            }
        }

        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private static readonly List<Type> _destroyedKeys = new List<Type>();

        /// <summary>Log every register and unregister.</summary>
        public static bool LogChanges { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForTests()
        {
            _services.Clear();
        }

        /// <summary>Registers <paramref name="service"/> as a <typeparamref name="T"/>, e.g. <c>ServiceLocator.Register(this)</c> or <c>Register&lt;IDamageable&gt;(this)</c>.</summary>
        public static void Register<T>(T service) where T : class
        {
            Register(typeof(T), service);
        }

        /// <summary>Registers <paramref name="service"/> under <paramref name="type"/>, for a type chosen at runtime.</summary>
        public static void Register(Type type, object service)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (IsMissing(service))
            {
                Debug.LogError($"ServiceLocator: tried to register a null {type.Name}.");
                return;
            }
            if (!type.IsInstanceOfType(service))
            {
                Debug.LogError($"ServiceLocator: {Describe(service)} can't be registered as {type.Name} because it isn't one.", service as Object);
                return;
            }

            if (_services.TryGetValue(type, out object existing) && !IsMissing(existing))
            {
                if (ReferenceEquals(existing, service)) return;
                Debug.LogError($"ServiceLocator: a {type.Name} is already registered ({Describe(existing)}), so {Describe(service)} was not registered. " +
                               "Is there more than one in the scene?", service as Object);
                return;
            }

            _services[type] = service;
            if (LogChanges) Debug.Log($"ServiceLocator: registered {Describe(service)} as {type.Name}", service as Object);
        }

        /// <summary>
        /// Removes <paramref name="service"/> wherever it's registered. Does nothing if another instance has taken its
        /// place, so it's safe to call from OnDestroy. Returns true if anything was removed.
        /// </summary>
        public static bool Unregister(object service)
        {
            if (service == null) return false;
            bool removed = false;
            foreach (KeyValuePair<Type, object> entry in _services)
            {
                if (ReferenceEquals(entry.Value, service)) _destroyedKeys.Add(entry.Key);
            }
            foreach (Type key in _destroyedKeys)
            {
                _services.Remove(key);
                removed = true;
                if (LogChanges) Debug.Log($"ServiceLocator: unregistered {Describe(service)} as {key.Name}");
            }
            _destroyedKeys.Clear();
            return removed;
        }

        /// <summary>Removes <paramref name="service"/> from <paramref name="type"/>, only if it's the one registered there.</summary>
        public static bool Unregister(Type type, object service)
        {
            if (type == null || !_services.TryGetValue(type, out object existing) || !ReferenceEquals(existing, service)) return false;
            _services.Remove(type);
            if (LogChanges) Debug.Log($"ServiceLocator: unregistered {Describe(service)} as {type.Name}");
            return true;
        }

        /// <summary>
        /// Gets the registered <typeparamref name="T"/>: the service registered as exactly that type, otherwise the one
        /// registered service that is a <typeparamref name="T"/>. Logs an error and returns null if there's none
        /// (or more than one). Pass <c>this</c> as <paramref name="context"/> so the error points at the caller.
        /// </summary>
        public static T Get<T>(Object context = null) where T : class
        {
            T service = Find<T>(out string ambiguous);
            if (service != null) return service;

            if (ambiguous != null)
            {
                Debug.LogError($"ServiceLocator: more than one registered service is a {typeof(T).Name} ({ambiguous}). " +
                               $"Register the one you want as {typeof(T).Name} (Register<{typeof(T).Name}>(service)), or get it by its own type.", context);
            }
            else
            {
                Debug.LogError($"ServiceLocator: no {typeof(T).Name} is registered. Check that one is in the scene, that it registers in Awake, " +
                               "and that you get it in Start or later.", context);
            }
            return null;
        }

        /// <summary>Like <see cref="Get{T}"/>, but returns false instead of logging when there's no single <typeparamref name="T"/>.</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            service = Find<T>(out _);
            return service != null;
        }

        /// <summary>Adds every registered service to <paramref name="results"/>, for debugging tools.</summary>
        public static void CollectDebugInfo(List<ServiceInfo> results)
        {
            foreach (KeyValuePair<Type, object> entry in _services) results.Add(new ServiceInfo(entry.Key, entry.Value));
        }

        private static T Find<T>(out string ambiguous) where T : class
        {
            ambiguous = null;
            Type type = typeof(T);
            if (_services.TryGetValue(type, out object exact))
            {
                if (!IsMissing(exact)) return (T)exact;
                _services.Remove(type);
            }

            object found = null;
            Type foundAs = null;
            foreach (KeyValuePair<Type, object> entry in _services)
            {
                if (IsMissing(entry.Value))
                {
                    _destroyedKeys.Add(entry.Key);
                    continue;
                }
                if (!(entry.Value is T)) continue;
                if (found == null)
                {
                    found = entry.Value;
                    foundAs = entry.Key;
                }
                else if (!ReferenceEquals(found, entry.Value) && ambiguous == null)
                {
                    ambiguous = $"{Describe(found)} as {foundAs.Name}, {Describe(entry.Value)} as {entry.Key.Name}";
                }
            }

            foreach (Type key in _destroyedKeys) _services.Remove(key);
            _destroyedKeys.Clear();
            return ambiguous == null ? (T)found : null;
        }

        private static bool IsMissing(object service) => service == null || (service is Object obj && obj == null);

        private static string Describe(object service) => service is Object obj ? $"'{obj.name}' ({service.GetType().Name})" : service.GetType().Name;

#pragma warning disable CS0618
        private static ServiceRegistry _legacyRegistry;

        [Obsolete("Use the static ServiceLocator methods instead: ServiceLocator.Register(this), ServiceLocator.Get<T>(), ServiceLocator.TryGet(out T service).")]
        public static ServiceRegistry GlobalServices => _legacyRegistry ??= new ServiceRegistry(false, true, null);
#pragma warning restore CS0618
    }
}
