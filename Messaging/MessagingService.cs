using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Messaging
{
    public class MessagingService
    {
        public static MessagingService Instance { get; private set; }

        private Dictionary<Enum, IList> Channels { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            MessagingService service = new MessagingService();
            service.Channels = new Dictionary<Enum, IList>();
            Instance = service;
        }

        public void AddListener<T>(Enum tag, Action<T> listenerMethod)
        {
            if (!Channels.ContainsKey(tag))
            {
                Type listType = typeof(List<>).MakeGenericType(typeof(Action<T>));
                IList list = (IList)Activator.CreateInstance(listType);
                Channels.Add(tag, list);
            }

            Channels[tag].Add(listenerMethod);
        }

        public void Send<T>(Enum tag, T payload)
        {
            if (!Channels.ContainsKey(tag)) return;

            IList list = Channels[tag];
            foreach (var item in list)
            {
                if(item is Action<T> action)
                {
                    action.Invoke(payload);
                }
                else
                {
                    Debug.LogError($"Wrong type sent, Tag: {tag}, Payload: {payload.GetType()}, Expected type: {item.GetType()}");
                    return;
                }
            }
        }

        public void RemoveListener<T>(Enum tag, Action<T> toRemove)
        {
            if (!Channels.ContainsKey(tag)) return;

            IList list = Channels[tag];
            foreach (Action<T> action in list)
            {
                if (action.Equals(toRemove))
                {
                    list.Remove(action);
                    return;
                }
            }
        }
    }
}