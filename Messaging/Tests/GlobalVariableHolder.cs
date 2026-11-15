using UnityEngine;

namespace UnityTools.Messaging.Tests
{
    public enum HolderTag { Score, Lives }

    // Test-only asset used to check how GlobalVariable fields survive Unity serialization.
    // A ScriptableObject goes through the same serializer as MonoBehaviour fields, and can live in an Editor-only test assembly.
    public class GlobalVariableHolder : ScriptableObject
    {
        public GlobalVariable<int> Score = new GlobalVariable<int>(HolderTag.Score);
    }
}
