using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Messaging
{
    public class MessagingService
    {
        private static MessagingService _instance;
        public static MessagingService Instance
        {
            get
            {
                if (_instance == null) CreateInstance();
                return _instance;
            }
        }

        private Dictionary<Enum, IList> Channels { get; set; }

        private static MessagingService CreateInstance()
        {
            MessagingService service = new MessagingService();
            service.Channels = new Dictionary<Enum, IList>();
            _instance = service;
            return service;
        }

        public void AddListener<T>(Enum tag, Action<T> onPlayerDamage)
        {
            if (!Channels.ContainsKey(tag))
            {
                Type listType = typeof(List<>).MakeGenericType(typeof(Action<T>));
                IList list = (IList)Activator.CreateInstance(listType);
                Channels.Add(tag, list);
            }

            Channels[tag].Add(onPlayerDamage);
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