using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LightingTools.Editor
{
    public class ReflectionProbeHelper
    {
        [MenuItem("GameObject/Light/Surround with Reflection Probe", false, priority = -100)]
        static void SurroundWithReflectionProbe(MenuCommand menuCommand)
        {
            if (!TryGetBoundsOnce(menuCommand, 4f, out Bounds bounds)) return;

            GameObject gameObject = CreateAtSelection("Reflection Probe", bounds.center);
            // HDRP adds HDAdditionalReflectionData afterwards and initialises its influence box from this size.
            ReflectionProbe probe = Undo.AddComponent<ReflectionProbe>(gameObject);
            probe.size = bounds.size;
            Selection.activeGameObject = gameObject;

            Debug.Log("Reflection Probe created.", gameObject);
        }

        [MenuItem("GameObject/Light/Surround with Probe Volume", false, priority = -99)]
        static void SurroundWithProbeVolume(MenuCommand menuCommand)
        {
            if (!TryGetBoundsOnce(menuCommand, 4f, out Bounds bounds)) return;

            GameObject gameObject = CreateAtSelection("Probe Volume", bounds.center);
            ProbeVolume volume = Undo.AddComponent<ProbeVolume>(gameObject);
            volume.mode = ProbeVolume.Mode.Local;
            volume.size = bounds.size;
            Selection.activeGameObject = gameObject;

            Debug.Log("Probe Volume created.", gameObject);
        }

        [MenuItem("GameObject/Light/Surround with Local Volume", false, priority = -98)]
        static void SurroundWithLocalVolume(MenuCommand menuCommand)
        {
            if (!TryGetBoundsOnce(menuCommand, 1f, out Bounds bounds)) return;

            GameObject gameObject = CreateAtSelection("Local Volume", bounds.center);
            BoxCollider box = Undo.AddComponent<BoxCollider>(gameObject);
            Volume volume = Undo.AddComponent<Volume>(gameObject);
            box.isTrigger = true;
            box.size = bounds.size;
            volume.isGlobal = false;
            volume.blendDistance = 1f;
            Selection.activeGameObject = gameObject;

            Debug.Log("Local Post-Processing Volume created.", gameObject);
        }

        /// <summary>
        /// Hierarchy context menus run once per selected object; only the first call does the work so a
        /// multi-selection makes one surrounding object instead of one per selected object.
        /// </summary>
        private static bool TryGetBoundsOnce(MenuCommand menuCommand, float padding, out Bounds bounds)
        {
            bounds = default;
            if (menuCommand.context != null && Selection.objects.Length > 1 && menuCommand.context != Selection.objects[0]) return false;
            return BoundsUtils.TryGetSelectionBounds(out bounds, padding);
        }

        /// <summary>Creates an undoable GameObject in the selected object's scene (or the active scene).</summary>
        internal static GameObject CreateAtSelection(string name, Vector3 position)
        {
            GameObject previousSelected = Selection.activeGameObject;
            GameObject gameObject = new GameObject(name);
            if (previousSelected != null && previousSelected.scene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(gameObject, previousSelected.scene);
            }
            gameObject.transform.position = position;
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + name);
            return gameObject;
        }
    }
}
