using UnityEngine;

namespace UnityTools.Scenes
{
    /// <summary>
    /// Draws a string field (or every element of a string list/array) as a searchable dropdown of the scenes in the
    /// project's Assets folder. By default the stored value is the scene's asset path, e.g. "Assets/Scenes/Level 1.unity".
    /// With <c>storeName: true</c> it stores just the scene name ("Level 1"), for code that loads scenes by name.
    /// </summary>
    public class SceneDropdownAttribute : PropertyAttribute
    {
        /// <summary>Store the scene name instead of its asset path.</summary>
        public bool StoreName { get; }

        public SceneDropdownAttribute(bool storeName = false)
        {
            StoreName = storeName;
        }
    }
}
