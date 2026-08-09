using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityTools.Messaging
{
    public class MessagingService
    {
        private class ChannelCollection
        {
            public IList Actions { get; set; }
            public List<object> Copies { get; set; }

            public ChannelCollection(IList actions, List<object> copies)
            {
                Actions = actions;
                Copies = copies;
            }
        }

        public static MessagingService Instance { get; private set; }

        private Dictionary<Enum, ChannelCollection> Channels { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            MessagingService service = new MessagingService();
            service.Channels = new Dictionary<Enum, ChannelCollection>();
            Instance = service;
        }

        public void AddListener<T>(Enum tag, Action<T> listenerMethod)
        {
            if (!Channels.ContainsKey(tag))
            {
                Type listType = typeof(List<>).MakeGenericType(typeof(Action<T>));
                IList list = (IList)Activator.CreateInstance(listType);
                Channels.Add(tag, new ChannelCollection(list, new List<object>()));
            }

            Channels[tag].Actions.Add(listenerMethod);
        }

        public void Send<T>(Enum tag, T payload)
        {
            if (!Channels.ContainsKey(tag)) return;

            IList list = Channels[tag].Actions;
            Channels[tag].Copies.Clear();
            foreach (var item in list) Channels[tag].Copies.Add(item);
            foreach (var item in Channels[tag].Copies)
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

            IList list = Channels[tag].Actions;
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