using UnityEngine;

namespace UnityTools.ServiceAccess.Tests
{
    // A ScriptableObject rather than a MonoBehaviour: this test assembly is editor-only, and Unity won't add
    // components from editor assemblies. It's still a UnityEngine.Object that can be destroyed.
    public class TestAudioManager : ScriptableObject, ITestSound, ITestShared { }
}
