using System;
using System.Reflection;
using UnityEngine;

namespace UnityTools.DebugTools
{
    /// <summary>Old API: marks a public, parameterless instance method as a debug menu button.</summary>
    [Obsolete("Register a section instead: DebugMenu.Register(this, \"Title\", section => section.Button(\"Label\", Method)).")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class DebugCommandAttribute : Attribute
    {
        /// <summary>The button label.</summary>
        public string Name { get; private set; }

        public DebugCommandAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Old API, kept so existing code compiles. RegisterObject turns an object's [DebugCommand] methods into a
    /// section of buttons in the new debug menu. A DebugMenuSystem component in a scene does nothing else; the new
    /// menu is created automatically.
    /// </summary>
    [Obsolete("Use DebugMenu.Register(this, \"Title\", section => ...) and DebugMenu.Unregister(this). The menu itself is created automatically.")]
    [AddComponentMenu("")]
    public class DebugMenuSystem : MonoBehaviour
    {
        private static DebugMenuSystem _instance;
        private static bool _quitting;
        private static bool _warnedPlaced;
        private static bool _creating;

        /// <summary>The obsolete system's shared instance, created hidden on first use.</summary>
        public static DebugMenuSystem Instance
        {
            get
            {
                // Keep the old instance while quitting instead of creating objects during teardown;
                // RegisterObject and DeregisterObject don't need it to be alive.
                if (ReferenceEquals(_instance, null) || (!_quitting && _instance == null))
                {
                    var go = new GameObject("Debug Menu System (obsolete)") { hideFlags = HideFlags.HideInHierarchy };
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                    _creating = true;
                    _instance = go.AddComponent<DebugMenuSystem>();
                    _creating = false;
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _quitting = false;
            _warnedPlaced = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting() => _quitting = true;

        private void Awake()
        {
            if (!_creating && !_warnedPlaced)
            {
                _warnedPlaced = true;
                Debug.LogWarning("DebugMenuSystem is obsolete and does nothing: the debug menu is now created automatically. " +
                                 "Remove this component (and the old Debug Menu prefab) from your scenes.", this);
            }
            if (_instance == null) _instance = this;
        }

        /// <summary>Adds a section titled with the object's type name, with a button per [DebugCommand] method.</summary>
        public void RegisterObject(object component)
        {
            if (component == null) return;
            Type type = component.GetType();
            DebugMenu.Register(component, type.Name, section =>
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var attribute = method.GetCustomAttribute<DebugCommandAttribute>(true);
                    if (attribute == null) continue;
                    if (method.GetParameters().Length > 0)
                    {
                        Debug.LogWarning($"Debug Menu: [DebugCommand] {type.Name}.{method.Name} takes parameters, so it was skipped. Debug commands can't take parameters.");
                        continue;
                    }
                    section.Button(attribute.Name, () => method.Invoke(component, null));
                }
            });
        }

        /// <summary>Removes the object's section.</summary>
        public void DeregisterObject(object component)
        {
            DebugMenu.Unregister(component);
        }
    }
}
