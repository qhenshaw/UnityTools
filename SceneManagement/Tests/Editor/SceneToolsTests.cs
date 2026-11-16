using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace UnityTools.Scenes.Tests
{
    /// <summary>EditMode tests for PrefabSpawner, MeshPreview, HideChildren and AdditiveSceneManager.</summary>
    public class SceneToolsTests
    {
        private const string Folder = "Assets/UnityToolsSceneToolsTests";

        private readonly List<Object> _created = new();
        private Scene _scene;
        private Scene _previousActive;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UnityToolsSceneToolsTests");
            // The test runner works in an untitled scene of its own. Save it into the test folder: Unity can't
            // create scenes additively while an untitled scene is open.
            _previousActive = SceneManager.GetActiveScene();
            _scene = _previousActive;
            if (string.IsNullOrEmpty(_scene.path)) EditorSceneManager.SaveScene(_scene, $"{Folder}/Test Host.unity");
        }

        [TearDown]
        public void TearDown()
        {
            PrefabSpawner.AskAboutOverrides = message => EditorUtility.DisplayDialogComplex(
                "Replace with Prefab Spawner", message, "Replace Anyway", "Cancel", "Skip Those");
            AdditiveSceneManager.AskAddExisting = path => EditorUtility.DisplayDialog(
                "Scene already exists", path, "Add Existing", "Cancel");

            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();

            if (_previousActive.IsValid() && _previousActive.isLoaded) SceneManager.SetActiveScene(_previousActive);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != _scene && scene.path.StartsWith(Folder + "/")) EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.DeleteAsset(Folder);
        }

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else _created.Add(go);
            return go;
        }

        private static Mesh CubeMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        private static void AddMesh(GameObject go, Mesh mesh)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
        }

        private GameObject SavePrefab(GameObject source, string name)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, $"{Folder}/{name}.prefab");
            Object.DestroyImmediate(source);
            _created.Remove(source);
            return prefab;
        }

        // ---- MeshPreview ----

        [Test]
        public void Preview_PlacesNestedAndScaledChildrenLikeASpawnedInstance()
        {
            Mesh mesh = CubeMesh();
            GameObject root = Make("Root");
            root.transform.SetPositionAndRotation(new Vector3(50f, 0f, 0f), Quaternion.Euler(0f, 30f, 0f));
            root.transform.localScale = Vector3.one * 2f;
            AddMesh(root, mesh);
            GameObject middle = Make("Middle", root.transform);
            middle.transform.localPosition = new Vector3(1f, 0f, 0f);
            middle.transform.localScale = Vector3.one * 3f;
            GameObject child = Make("Child", middle.transform);
            child.transform.localPosition = new Vector3(0f, 1f, 0f);
            child.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            AddMesh(child, mesh);

            var position = new Vector3(0f, 5f, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 90f, 0f);
            List<(Mesh mesh, Matrix4x4 matrix)> meshes = MeshPreview.GetMeshes(root, position, rotation).ToList();

            GameObject spawned = Object.Instantiate(root, position, rotation);
            _created.Add(spawned);
            Transform spawnedChild = spawned.transform.Find("Middle/Child");

            Assert.AreEqual(2, meshes.Count);
            AssertMatrix(spawned.transform.localToWorldMatrix, meshes[0].matrix);
            AssertMatrix(spawnedChild.localToWorldMatrix, meshes[1].matrix);
        }

        [Test]
        public void Preview_SkipsInactiveObjectsAndDisabledRenderers()
        {
            Mesh mesh = CubeMesh();
            GameObject root = Make("Root");
            GameObject inactive = Make("Inactive", root.transform);
            AddMesh(inactive, mesh);
            inactive.SetActive(false);
            GameObject underInactive = Make("Under Inactive", inactive.transform);
            AddMesh(underInactive, mesh);
            GameObject disabled = Make("Disabled", root.transform);
            AddMesh(disabled, mesh);
            disabled.GetComponent<MeshRenderer>().enabled = false;
            GameObject visible = Make("Visible", root.transform);
            AddMesh(visible, mesh);

            Assert.AreEqual(1, MeshPreview.GetMeshes(root, Vector3.zero, Quaternion.identity).Count);
        }

        [Test]
        public void Preview_NullPrefab_DrawsNothing()
        {
            Assert.AreEqual(0, MeshPreview.GetMeshes(null, Vector3.zero, Quaternion.identity).Count);
        }

        private static void AssertMatrix(Matrix4x4 expected, Matrix4x4 actual)
        {
            for (int i = 0; i < 16; i++) Assert.AreEqual(expected[i], actual[i], 1e-4f, $"matrix element {i}");
        }

        // ---- PrefabSpawner ----

        [Test]
        public void PreviewMaterial_IsCachedAndTinted()
        {
            PrefabSpawner spawner = Make("Spawner").AddComponent<PrefabSpawner>();
            var source = new Material(Shader.Find("Hidden/Internal-Colored"));
            _created.Add(source);
            spawner.SetPreview(source, Color.red);

            Material first = spawner.GetPreviewMaterial();
            Material second = spawner.GetPreviewMaterial();
            Assert.AreSame(first, second, "one copy, not a new material per repaint");
            Assert.AreNotSame(source, first);
            Assert.AreEqual(Color.red, first.color);

            var other = new Material(source);
            _created.Add(other);
            spawner.SetPreview(other, Color.green);
            Material third = spawner.GetPreviewMaterial();
            Assert.AreNotSame(first, third);
            Assert.IsTrue(first == null, "the old copy is destroyed");
            Assert.AreEqual(Color.green, third.color);
        }

        [Test]
        public void PreviewMaterial_None_ReturnsNull()
        {
            PrefabSpawner spawner = Make("Spawner").AddComponent<PrefabSpawner>();
            spawner.SetPreview(null, Color.red);
            Assert.IsNull(spawner.GetPreviewMaterial());
        }

        [Test]
        public void Spawn_NoPrefab_LogsAndReturnsNull()
        {
            PrefabSpawner spawner = Make("Spawner").AddComponent<PrefabSpawner>();
            LogAssert.Expect(LogType.Error, new Regex("has no prefab to spawn"));
            Assert.IsNull(spawner.Spawn());
        }

        [Test]
        public void Spawn_UsesSpawnerPositionAndRotation_KeepsPrefabScale()
        {
            GameObject prefab = Make("Prefab");
            prefab.transform.localScale = Vector3.one * 2f;
            GameObject spawnerGo = Make("Spawner");
            spawnerGo.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 45f, 0f));
            spawnerGo.transform.localScale = Vector3.one * 7f;
            PrefabSpawner spawner = spawnerGo.AddComponent<PrefabSpawner>();
            spawner.Configure(prefab, PrefabSpawner.ParentMode.SameScene, destroySpawner: true);

            GameObject spawned = spawner.Spawn();
            _created.Add(spawned);

            Assert.AreEqual(new Vector3(3f, 4f, 5f), spawned.transform.position);
            Assert.AreEqual(Quaternion.Euler(0f, 45f, 0f).eulerAngles, spawned.transform.rotation.eulerAngles);
            Assert.AreEqual(Vector3.one * 2f, spawned.transform.localScale);
            Assert.AreEqual(spawnerGo.scene, spawned.scene);
            Assert.IsTrue(spawner != null, "outside Play mode the spawner isn't destroyed");
        }

        [Test]
        public void Spawn_ParentModes()
        {
            GameObject prefab = Make("Prefab");
            GameObject parent = Make("Parent");
            GameObject spawnerGo = Make("Spawner", parent.transform);
            spawnerGo.transform.localPosition = Vector3.right;
            PrefabSpawner spawner = spawnerGo.AddComponent<PrefabSpawner>();

            spawner.Configure(prefab, PrefabSpawner.ParentMode.Self, false);
            GameObject self = spawner.Spawn();
            Assert.AreSame(spawnerGo.transform, self.transform.parent);
            Assert.AreEqual(spawnerGo.transform.position, self.transform.position);

            spawner.Configure(prefab, PrefabSpawner.ParentMode.SameParent, false);
            GameObject sibling = spawner.Spawn();
            Assert.AreSame(parent.transform, sibling.transform.parent);
            Assert.AreEqual(spawnerGo.transform.position, sibling.transform.position);

            spawner.Configure(prefab, PrefabSpawner.ParentMode.None, false);
            GameObject none = spawner.Spawn();
            _created.Add(none);
            Assert.IsNull(none.transform.parent);
        }

        [Test]
        public void Replace_KeepsPlacementParentAndSiblingIndex()
        {
            GameObject source = Make("Crate");
            source.AddComponent<BoxCollider>();
            GameObject prefab = SavePrefab(source, "Crate");

            GameObject parent = Make("Props");
            Make("First", parent.transform);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            Make("Last", parent.transform);
            instance.transform.SetLocalPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
            Assert.AreEqual(1, instance.transform.GetSiblingIndex());

            List<GameObject> spawners = PrefabSpawner.ReplaceWithSpawners(new[] { instance });

            Assert.AreEqual(1, spawners.Count);
            Transform spawner = spawners[0].transform;
            Assert.IsTrue(instance == null, "the instance is removed");
            Assert.AreSame(parent.transform, spawner.parent);
            Assert.AreEqual(1, spawner.GetSiblingIndex());
            Assert.AreEqual(new Vector3(1f, 2f, 3f), spawner.localPosition);
            Assert.AreEqual(90f, spawner.localEulerAngles.y, 1e-3f);
            Assert.AreSame(prefab, spawner.GetComponent<PrefabSpawner>().Prefab);
            Assert.AreEqual(_scene, spawner.gameObject.scene);
        }

        [Test]
        public void Replace_HandlesSeveralAndChildrenOfInstances()
        {
            GameObject source = Make("Crate");
            Make("Lid", source.transform);
            GameObject prefab = SavePrefab(source, "Crate");
            var a = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject plain = Make("Plain");

            LogAssert.Expect(LogType.Warning, new Regex("Plain isn't a prefab instance"));
            List<GameObject> spawners = PrefabSpawner.ReplaceWithSpawners(new[] { a.transform.GetChild(0).gameObject, a, b, plain });

            Assert.AreEqual(2, spawners.Count, "a child of an instance replaces its instance, once");
            Assert.IsTrue(a == null && b == null);
            Assert.IsTrue(plain != null);
            _created.AddRange(spawners);
        }

        [Test]
        public void Replace_WithOverrides_AsksAndCanSkipOrCancel()
        {
            GameObject source = Make("Crate");
            GameObject prefab = SavePrefab(source, "Crate");
            var plain = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var changed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            changed.AddComponent<BoxCollider>();

            string asked = null;
            PrefabSpawner.AskAboutOverrides = message => { asked = message; return 1; };
            Assert.AreEqual(0, PrefabSpawner.ReplaceWithSpawners(new[] { plain, changed }).Count);
            StringAssert.Contains("Crate", asked);
            Assert.IsTrue(plain != null && changed != null, "cancel replaces nothing");

            PrefabSpawner.AskAboutOverrides = _ => 2;
            List<GameObject> skipped = PrefabSpawner.ReplaceWithSpawners(new[] { plain, changed });
            Assert.AreEqual(1, skipped.Count);
            Assert.IsTrue(plain == null);
            Assert.IsTrue(changed != null, "the instance with overrides is skipped");

            PrefabSpawner.AskAboutOverrides = _ => 0;
            Assert.AreEqual(1, PrefabSpawner.ReplaceWithSpawners(new[] { changed }).Count);
            Assert.IsTrue(changed == null);
        }

        [Test]
        public void Replace_WithoutOverrides_DoesNotAsk()
        {
            GameObject prefab = SavePrefab(Make("Crate"), "Crate");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(4f, 0f, 0f); // root placement isn't an override

            bool asked = false;
            PrefabSpawner.AskAboutOverrides = _ => { asked = true; return 1; };
            Assert.AreEqual(1, PrefabSpawner.ReplaceWithSpawners(new[] { instance }).Count);
            Assert.IsFalse(asked);
        }

        [Test]
        public void Replace_IsOneUndoStep()
        {
            GameObject prefab = SavePrefab(Make("Crate"), "Crate");
            var a = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            a.name = "A";
            b.name = "B";
            Undo.IncrementCurrentGroup();

            PrefabSpawner.ReplaceWithSpawners(new[] { a, b });
            Undo.PerformUndo();

            GameObject[] roots = _scene.GetRootGameObjects();
            Assert.IsTrue(roots.Any(r => r.name == "A") && roots.Any(r => r.name == "B"), "one undo brings both back");
            Assert.IsFalse(roots.Any(r => r.GetComponent<PrefabSpawner>() != null));
        }

        // ---- HideChildren ----

        [Test]
        public void HideChildren_HidesInactiveChildrenToo()
        {
            GameObject root = Make("Root");
            GameObject active = Make("Active", root.transform);
            GameObject inactive = Make("Inactive", root.transform);
            inactive.SetActive(false);
            GameObject nested = Make("Nested", inactive.transform);

            root.AddComponent<HideChildren>().SetHidden(true);

            Assert.IsTrue((active.hideFlags & HideFlags.HideInHierarchy) != 0);
            Assert.IsTrue((inactive.hideFlags & HideFlags.HideInHierarchy) != 0);
            Assert.IsTrue((nested.hideFlags & HideFlags.HideInHierarchy) != 0);
        }

        [Test]
        public void HideChildren_KeepsOtherFlags()
        {
            GameObject root = Make("Root");
            GameObject child = Make("Child", root.transform);
            child.hideFlags = HideFlags.NotEditable;
            HideChildren hide = root.AddComponent<HideChildren>();

            hide.SetHidden(true);
            Assert.AreEqual(HideFlags.NotEditable | HideChildren.HiddenFlags, child.hideFlags);

            hide.SetHidden(false);
            Assert.AreEqual(HideFlags.NotEditable, child.hideFlags);
        }

        [Test]
        public void HideChildren_HideComponents()
        {
            GameObject root = Make("Root");
            BoxCollider box = root.AddComponent<BoxCollider>();
            HideChildren hide = root.AddComponent<HideChildren>();
            hide.HideComponents = true;

            hide.SetHidden(true);
            Assert.IsTrue((box.hideFlags & HideFlags.HideInInspector) != 0);
            Assert.AreEqual(HideFlags.None, hide.hideFlags & HideChildren.HiddenFlags, "never hides itself");

            hide.SetHidden(false);
            Assert.AreEqual(HideFlags.None, box.hideFlags);
        }

        [Test]
        public void ShowTemporarily_DoesNotChangeIsHidden()
        {
            GameObject root = Make("Root");
            GameObject child = Make("Child", root.transform);
            HideChildren hide = root.AddComponent<HideChildren>();
            hide.SetHidden(true);

            hide.ShowTemporarily(true);
            Assert.IsTrue(hide.IsHidden, "the saved value is unchanged");
            Assert.IsFalse(hide.IsEffectivelyHidden);
            Assert.AreEqual(HideFlags.None, child.hideFlags);

            hide.ShowTemporarily(false);
            Assert.IsTrue((child.hideFlags & HideFlags.HideInHierarchy) != 0);
        }

        [Test]
        public void ShowTemporarily_LeavesAVisibleSetupVisible()
        {
            GameObject root = Make("Root");
            GameObject child = Make("Child", root.transform);
            HideChildren hide = root.AddComponent<HideChildren>();
            hide.SetHidden(false);

            hide.ShowTemporarily(true);
            hide.ShowTemporarily(false);
            Assert.IsFalse(hide.IsHidden, "closing prefab mode no longer forces Is Hidden on");
            Assert.AreEqual(HideFlags.None, child.hideFlags);
        }

        [Test]
        public void HideChildren_InSavedPrefab_KeepsIsHidden()
        {
            GameObject source = Make("Hidden Thing");
            Make("Child", source.transform);
            source.AddComponent<HideChildren>().SetHidden(true);
            GameObject prefab = SavePrefab(source, "Hidden Thing");

            // Edit the prefab the way prefab mode does, show its children temporarily, and save.
            GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            contents.GetComponent<HideChildren>().ShowTemporarily(true);
            PrefabUtility.SaveAsPrefabAsset(contents, AssetDatabase.GetAssetPath(prefab));
            PrefabUtility.UnloadPrefabContents(contents);

            Assert.IsTrue(prefab.GetComponent<HideChildren>().IsHidden);
        }

        // ---- AdditiveSceneManager ----

        private AdditiveSceneManager MakeManager()
        {
            AdditiveSceneManager manager = Make("Additive Scene Manager").AddComponent<AdditiveSceneManager>();
            Assert.IsNotNull(manager.SceneList);
            return manager;
        }

        private static string SaveScene(string name)
        {
            string path = $"{Folder}/{name}.unity";
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var marker = new GameObject("Original Content");
            SceneManager.MoveGameObjectToScene(marker, scene);
            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            return path;
        }

        [Test]
        public void AddNewScene_CreatesAndAddsIt()
        {
            AdditiveSceneManager manager = MakeManager();
            string path = $"{Folder}/Fresh.unity";

            manager.AddNewSceneAt(path);

            Assert.IsTrue(File.Exists(path));
            CollectionAssert.Contains(manager.SceneList, path);
            Assert.IsTrue(SceneManager.GetSceneByPath(path).isLoaded);
            Assert.AreEqual(_scene, SceneManager.GetActiveScene(), "the active scene is restored");
        }

        [Test]
        public void AddNewScene_ExistingFile_CancelLeavesItAlone()
        {
            AdditiveSceneManager manager = MakeManager();
            string path = SaveScene("Existing");
            string before = File.ReadAllText(path);

            string asked = null;
            AdditiveSceneManager.AskAddExisting = p => { asked = p; return false; };
            manager.AddNewSceneAt(path);

            Assert.AreEqual(path, asked);
            Assert.AreEqual(before, File.ReadAllText(path), "the existing scene isn't overwritten");
            CollectionAssert.DoesNotContain(manager.SceneList, path);
        }

        [Test]
        public void AddNewScene_ExistingFile_AddExistingKeepsItsContent()
        {
            AdditiveSceneManager manager = MakeManager();
            string path = SaveScene("Existing");
            string before = File.ReadAllText(path);

            AdditiveSceneManager.AskAddExisting = _ => true;
            LogAssert.Expect(LogType.Log, new Regex("Existing scene added"));
            manager.AddNewSceneAt(path);

            Assert.AreEqual(before, File.ReadAllText(path));
            CollectionAssert.Contains(manager.SceneList, path);
            Scene opened = SceneManager.GetSceneByPath(path);
            Assert.IsTrue(opened.isLoaded);
            Assert.IsTrue(opened.GetRootGameObjects().Any(r => r.name == "Original Content"));
        }

        [Test]
        public void AddScene_RecordsUndo()
        {
            AdditiveSceneManager manager = MakeManager();
            string path = SaveScene("Undoable");
            Undo.IncrementCurrentGroup();

            AdditiveSceneManager.AskAddExisting = _ => true;
            LogAssert.Expect(LogType.Log, new Regex("Existing scene added"));
            manager.AddNewSceneAt(path);
            CollectionAssert.Contains(manager.SceneList, path);

            Undo.PerformUndo();
            CollectionAssert.DoesNotContain(manager.SceneList, path);
        }
    }
}
