using System.Collections;
using System.Collections.Generic;
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
            if(BoundsUtils.TryGetSelectionBounds(out Bounds bounds, 4f))
            {
                GameObject previousSelected = Selection.activeGameObject;
                Selection.objects = null;
                GameObject gameObject = new GameObject("Reflection Probe");
                if (previousSelected != null) SceneManager.MoveGameObjectToScene(gameObject, previousSelected.scene);
                Undo.RegisterCreatedObjectUndo(gameObject, "Create" + gameObject.name);
                ReflectionProbe volume = Undo.AddComponent(gameObject, typeof(ReflectionProbe)) as ReflectionProbe;
                volume.transform.position = bounds.center;
                volume.size = bounds.size;
                Selection.activeGameObject = gameObject;

                Debug.Log("Reflection Probe created.");
            }
        }

        [MenuItem("GameObject/Light/Surround with Probe Volume", false, priority = -99)]
        static void SurroundWithProbeVolume(MenuCommand menuCommand)
        {
            if (BoundsUtils.TryGetSelectionBounds(out Bounds bounds, 4f))
            {
                GameObject previousSelected = Selection.activeGameObject;
                Selection.objects = null;
                GameObject gameObject = new GameObject("Probe Volume");
                if (previousSelected != null) SceneManager.MoveGameObjectToScene(gameObject, previousSelected.scene);
                Undo.RegisterCreatedObjectUndo(gameObject, "Create" + gameObject.name);
                ProbeVolume volume = Undo.AddComponent(gameObject, typeof(ProbeVolume)) as ProbeVolume;
                volume.transform.position = bounds.center;
                volume.size = bounds.size;
                Selection.activeGameObject = gameObject;

                Debug.Log("Probe Volume created.");
            }
        }

        [MenuItem("GameObject/Light/Surround with Local Volume", false, priority = -98)]
        static void SurroundWithLocalVolume(MenuCommand menuCommand)
        {
            if (BoundsUtils.TryGetSelectionBounds(out Bounds bounds, 1f))
            {
                GameObject previousSelected = Selection.activeGameObject;
                Selection.objects = null;
                GameObject gameObject = new GameObject("Local Volume");
                if (previousSelected != null) SceneManager.MoveGameObjectToScene(gameObject, previousSelected.scene);
                Undo.RegisterCreatedObjectUndo(gameObject, "Create" + gameObject.name);
                BoxCollider box = Undo.AddComponent(gameObject, typeof(BoxCollider)) as BoxCollider;
                Volume volume = Undo.AddComponent(gameObject, typeof(Volume)) as Volume;
                box.isTrigger = true;
                volume.isGlobal = false;
                volume.blendDistance = 1f;
                box.transform.position = bounds.center;
                box.size = bounds.size;
                Selection.activeGameObject = gameObject;

                Debug.Log("Local Post-Processing Volume created.");
            }
        }
    }
}