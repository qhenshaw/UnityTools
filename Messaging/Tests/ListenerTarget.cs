using UnityEngine;

namespace UnityTools.Messaging.Tests
{
    // Test-only Unity object whose methods are used as listeners, to check destroyed listeners are removed.
    public class ListenerTarget : ScriptableObject
    {
        public int Calls;

        public void OnMessage(int value) => Calls++;
        public void OnPing() => Calls++;
    }
}
