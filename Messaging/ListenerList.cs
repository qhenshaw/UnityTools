using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace UnityTools.Messaging
{
    // A list of listeners that is safe to change while it is being called:
    // - listeners added during a call wait for the next one, listeners removed during a call are skipped for the rest of it
    // - a listener on a destroyed Unity object is skipped and removed, with one warning
    // - a listener that throws is logged and the rest still run
    internal sealed class ListenerList<TDelegate> where TDelegate : Delegate
    {
        private readonly List<TDelegate> _items = new List<TDelegate>();
        private readonly List<TDelegate> _removedDuringCall = new List<TDelegate>();
        private int _callDepth;

        public int Count => _items.Count;

        public void Add(TDelegate listener) => _items.Add(listener);

        public bool Remove(Delegate listener)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Equals(listener)) continue;
                if (_callDepth > 0) _removedDuringCall.Add(_items[i]);
                _items.RemoveAt(i);
                return true;
            }
            return false;
        }

        // Calls invoke(listener, arg) for each live listener. The callback and its argument are passed separately
        // so the caller can use a static lambda and nothing is allocated per call.
        public void Invoke<TArg>(TArg arg, Action<TDelegate, TArg> invoke, string owner)
        {
            if (_items.Count == 0) return;
            _callDepth++;
            using (ListPool<TDelegate>.Get(out List<TDelegate> snapshot))
            {
                snapshot.AddRange(_items);
                try
                {
                    foreach (TDelegate listener in snapshot)
                    {
                        if (!IsLive(listener, owner)) continue;
                        try
                        {
                            invoke(listener, arg);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception, listener.Target as UnityEngine.Object);
                        }
                    }
                }
                finally
                {
                    if (--_callDepth == 0) _removedDuringCall.Clear();
                }
            }
        }

        public void Describe(List<string> into)
        {
            foreach (TDelegate listener in _items) into.Add(Describe(listener));
        }

        internal static string Describe(Delegate listener)
        {
            object target = listener.Target;
            string owner = target != null ? target.GetType().Name : listener.Method.DeclaringType?.Name;
            if (target is UnityEngine.Object unityObject && unityObject != null) owner = $"{unityObject.name} ({owner})";
            return $"{owner}.{listener.Method.Name}";
        }

        private bool IsLive(TDelegate listener, string owner)
        {
            if (_removedDuringCall.Count > 0 && _removedDuringCall.Contains(listener)) return false;
            if (listener.Target is UnityEngine.Object unityObject && unityObject == null)
            {
                Remove(listener);
                Debug.LogWarning($"{owner}: removed listener {listener.Target.GetType().Name}.{listener.Method.Name} because its object was destroyed. Remove listeners in OnDisable or OnDestroy.");
                return false;
            }
            return true;
        }
    }
}
