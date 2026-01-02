using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Messaging
{
    public class GlobalVariableService
    {
        public static GlobalVariableService Instance { get; private set; }

        private Dictionary<Enum, object> Values { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            GlobalVariableService service = new GlobalVariableService();
            service.Values = new Dictionary<Enum, object>();
            Instance = service;
        }

        public T Get<T>(Enum tag)
        {
            if (Values.TryGetValue(tag, out object value) && value is T)
            {
                return (T)value;
            }

            return default(T);
        }

        public void Set<T>(Enum tag, T value)
        {
            if (!Values.ContainsKey(tag))
            {
                Values.Add(tag, value);
            }

            Values[tag] = value;
        }

        public bool HasValue(Enum tag)
        {
            return Values.ContainsKey(tag);
        }
    }
}