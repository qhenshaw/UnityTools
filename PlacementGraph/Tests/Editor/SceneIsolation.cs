using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UnityTools.Placement.Tests
{
    /// <summary>
    /// Wraps every test in a fixture marked with it, outside its SetUp and TearDown, so the test leaves nothing behind in
    /// any scene, including the one open in the editor:
    /// <list type="bullet">
    /// <item>Undo operations the test records (Generate, Clear and the cell updates record Undo) are reverted while its
    /// scene is still loaded. Otherwise the test runner reverts them at the end of the run, after it has reopened the
    /// user's scene, and every object a test destroyed through Undo comes back into that scene.</item>
    /// <item>Root objects the test adds to any loaded scene and doesn't remove fail the test, and are destroyed.</item>
    /// </list>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SceneIsolationAttribute : NUnitAttribute, IOuterUnityTestAction
    {
        private int _undoGroup;
        private HashSet<GameObject> _roots;

        public IEnumerator BeforeTest(ITest test)
        {
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            _roots = new HashSet<GameObject>(Roots());
            _graphObjects = new HashSet<Object>(UnityTools.Placement.Editor.PlacementGraphCache.Loaded());
            yield break;
        }

        private HashSet<Object> _graphObjects;

        public IEnumerator AfterTest(ITest test)
        {
            // Anything the test added and its TearDown didn't remove is a leak.
            List<GameObject> leaked = Roots().Where(root => !_roots.Contains(root)).ToList();
            string names = string.Join(", ", leaked.Select(root => root.name));

            // Revert the test's Undo history now, in its own scene, so there's nothing left for the runner to replay
            // into the user's scene later. Objects that brings back are removed with the rest.
            Undo.RevertAllDownToGroup(_undoGroup);
            foreach (GameObject root in Roots())
            {
                if (!_roots.Contains(root)) Object.DestroyImmediate(root);
            }

            // Graph Toolkit keeps an in-memory copy of every graph the test loaded, even once its temporary asset is
            // deleted, and re-enables it on every domain reload (where a dangling subgraph reference throws). Free the
            // ones this test loaded whose file is gone; graphs loaded before the test are left alone.
            UnityTools.Placement.Editor.PlacementGraphCache.ReleaseDeleted(
                UnityTools.Placement.Editor.PlacementGraphCache.Loaded().Where(g => !_graphObjects.Contains(g)), _ => true);

            if (leaked.Count > 0) Assert.Fail($"The test left {leaked.Count} root object(s) in the scene: {names}");
            yield break;
        }

        private static IEnumerable<GameObject> Roots()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
            }
            return roots;
        }
    }
}
