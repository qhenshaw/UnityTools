using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace UnityTools.Services
{
    /// <summary>Old API, kept so existing code compiles. Every instance forwards to the one static <see cref="ServiceLocator"/>.</summary>
    [Obsolete("Use the static ServiceLocator methods instead: ServiceLocator.Register(this), ServiceLocator.Get<T>(), ServiceLocator.TryGet(out T service).")]
    public class ServiceRegistry
    {
        public List<string> ServiceNames
        {
            get
            {
                var infos = new List<ServiceLocator.ServiceInfo>();
                ServiceLocator.CollectDebugInfo(infos);
                return infos.ConvertAll(info => info.RegisteredAs.Name);
            }
        }

        public IEnumerable<object> ServicesList
        {
            get
            {
                var infos = new List<ServiceLocator.ServiceInfo>();
                ServiceLocator.CollectDebugInfo(infos);
                return infos.ConvertAll(info => info.Service);
            }
        }

        public ServiceRegistry(bool logChanges, bool isGlobal, Object owner) { }

        public ServiceRegistry Register<T>(T service)
        {
            ServiceLocator.Register(typeof(T), service);
            return this;
        }

        public ServiceRegistry Register(Type type, object service)
        {
            ServiceLocator.Register(type, service);
            return this;
        }

        public ServiceRegistry UnRegister<T>(T service)
        {
            ServiceLocator.Unregister(typeof(T), service);
            return this;
        }

        public T Get<T>() where T : class => ServiceLocator.Get<T>();

        public bool TryGet<T>(out T service) where T : class => ServiceLocator.TryGet(out service);
    }
}
