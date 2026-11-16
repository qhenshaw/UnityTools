using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityTools.Art;
using UnityTools.Art.Editor;
using UnityTools.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityTools.Toolbar;

namespace UnityTools.EditorTools.Tests
{
    public class EditorToolsTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private GameObject Cube(string name, Vector3 position, Vector3? scale = null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale ?? Vector3.one;
            _created.Add(go);
            return go;
        }

        // ---- Snap to Floor ----

        private GameObject Floor(float topY = 0f) => Cube("Floor", new Vector3(0f, topY - 0.5f, 0f), new Vector3(100f, 1f, 100f));

        [Test]
        public void Snap_DropsOntoTheFloor()
        {
            Floor();
            GameObject box = Cube("Box", new Vector3(0f, 5f, 0f));

            Assert.AreEqual(1, LDHotkeys.SnapToFloor(new[] { box }));
            Assert.AreEqual(0.5f, box.transform.position.y, 1e-3f);
        }

        [Test]
        public void Snap_SunkObject_ComesUpToTheSurface()
        {
            Floor();
            GameObject box = Cube("Box", new Vector3(0f, 0.2f, 0f)); // bottom at -0.3, inside the floor

            LDHotkeys.SnapToFloor(new[] { box });
            Assert.AreEqual(0.5f, box.transform.position.y, 1e-3f);
        }

        [Test]
        public void Snap_IgnoresTriggers()
        {
            Floor();
            GameObject trigger = Cube("Trigger", new Vector3(0f, 2f, 0f), new Vector3(10f, 1f, 10f));
            trigger.GetComponent<BoxCollider>().isTrigger = true;
            GameObject box = Cube("Box", new Vector3(0f, 6f, 0f));

            LDHotkeys.SnapToFloor(new[] { box });
            Assert.AreEqual(0.5f, box.transform.position.y, 1e-3f, "lands on the floor, not on the trigger volume");
        }

        [Test]
        public void Snap_SeveralObjects_IgnoreEachOther()
        {
            Floor();
            GameObject lower = Cube("Lower", new Vector3(0f, 3f, 0f));
            GameObject upper = Cube("Upper", new Vector3(0f, 6f, 0f)); // directly above the lower one
            GameObject aside = Cube("Aside", new Vector3(5f, 9f, 0f));

            Assert.AreEqual(3, LDHotkeys.SnapToFloor(new[] { lower, upper, aside }));
            Assert.AreEqual(0.5f, lower.transform.position.y, 1e-3f);
            Assert.AreEqual(0.5f, upper.transform.position.y, 1e-3f, "doesn't land on the other selected object");
            Assert.AreEqual(0.5f, aside.transform.position.y, 1e-3f);
        }

        [Test]
        public void Snap_LandsOnUnselectedObjects()
        {
            Floor();
            Cube("Table", new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 4f)); // top at 2
            GameObject box = Cube("Box", new Vector3(0f, 6f, 0f));

            LDHotkeys.SnapToFloor(new[] { box });
            Assert.AreEqual(2.5f, box.transform.position.y, 1e-3f);
        }

        [Test]
        public void Snap_NothingBelow_DoesNothing()
        {
            GameObject box = Cube("Box", new Vector3(500f, 5f, 0f));
            Assert.AreEqual(0, LDHotkeys.SnapToFloor(new[] { box }));
            Assert.AreEqual(5f, box.transform.position.y);
        }

        [Test]
        public void Snap_IsOneUndoStep()
        {
            Floor();
            GameObject a = Cube("A", new Vector3(0f, 4f, 0f));
            GameObject b = Cube("B", new Vector3(5f, 7f, 0f));
            Undo.IncrementCurrentGroup();

            LDHotkeys.SnapToFloor(new[] { a, b });
            Undo.PerformUndo();

            Assert.AreEqual(4f, a.transform.position.y, 1e-4f);
            Assert.AreEqual(7f, b.transform.position.y, 1e-4f);
        }

        [Test]
        public void Snap_SelectedChild_MovesWithItsParent()
        {
            Floor();
            GameObject parent = Cube("Parent", new Vector3(0f, 4f, 0f));
            GameObject child = Cube("Child", new Vector3(0f, 5f, 0f));
            child.transform.SetParent(parent.transform, true);

            Assert.AreEqual(1, LDHotkeys.SnapToFloor(new[] { parent, child }));
            Assert.AreEqual(0.5f, parent.transform.position.y, 1e-3f);
            Assert.AreEqual(1.5f, child.transform.position.y, 1e-3f);
        }

        // ---- Gradient ----

        [Test]
        public void Gradient_FirstAndLastPixelsHitTheEnds()
        {
            var size = new Vector2Int(4, 2);
            Assert.AreEqual(0f, GradientEditor.SamplePosition(0, size, false, false));
            Assert.AreEqual(1f, GradientEditor.SamplePosition(3, size, false, false), "the last pixel is the end colour");
            Assert.AreEqual(1f / 3f, GradientEditor.SamplePosition(5, size, false, false), 1e-6f, "second row repeats");
        }

        [Test]
        public void Gradient_VerticalAndFlip()
        {
            var size = new Vector2Int(2, 5);
            Assert.AreEqual(0f, GradientEditor.SamplePosition(1, size, true, false));
            Assert.AreEqual(1f, GradientEditor.SamplePosition(9, size, true, false));
            Assert.AreEqual(0f, GradientEditor.SamplePosition(9, size, true, true));
        }

        [Test]
        public void Gradient_OnePixelLong_UsesStartColour()
        {
            Assert.AreEqual(0f, GradientEditor.SamplePosition(0, new Vector2Int(1, 4), false, false));
        }

        // ---- Material Creator ----

        [TestCase("Rock_BaseColor", 0)]
        [TestCase("Rock_albedo", 0)]
        [TestCase("Rock_Diffuse", 0)]
        [TestCase("Rock_Normal", 1)]
        [TestCase("Rock_NormalDX", 1)]
        [TestCase("Rock_NormalGL", 1)]
        [TestCase("Rock_MHER", 2)]
        [TestCase("Rock_Mask", 2)]
        [TestCase("Rock_ORM", 2)]
        [TestCase("RoughStone_BaseColor", 0)]
        [TestCase("Radio_Normal", 1)]
        [TestCase("Metal_Height_BaseColor", 0)]
        public void Slot_ComesFromTheSuffixOnly(string name, int expected)
        {
            Assert.AreEqual((MaterialCreator.Slot)expected, MaterialCreator.GetSlot(name));
        }

        [TestCase("Rock")]
        [TestCase("Rock_Roughness")]
        [TestCase("BaseColor_Rock")]
        public void Slot_UnknownSuffix_IsNull(string name)
        {
            Assert.IsNull(MaterialCreator.GetSlot(name));
        }

        [Test]
        public void BaseName_MatchesTheOldNaming()
        {
            Assert.AreEqual("RockMoss", MaterialCreator.BaseName("Rock_Moss_BaseColor"));
            Assert.AreEqual("Rock", MaterialCreator.BaseName("Rock_BaseColor"));
        }

        private Texture2D Tex(string name)
        {
            var texture = new Texture2D(2, 2) { name = name };
            _created.Add(texture);
            return texture;
        }

        [Test]
        public void Match_SortsTheThreeTextures()
        {
            Texture2D mask = Tex("Crate_MHER");
            Texture2D color = Tex("Crate_BaseColor");
            Texture2D normal = Tex("Crate_NormalDX");

            Assert.IsTrue(MaterialCreator.TryMatchTextures(new Texture[] { mask, color, normal }, out Texture[] sorted, out _));
            Assert.AreSame(color, sorted[0]);
            Assert.AreSame(normal, sorted[1]);
            Assert.AreSame(mask, sorted[2]);
        }

        [Test]
        public void Match_NamesTheProblem()
        {
            Assert.IsFalse(MaterialCreator.TryMatchTextures(new Texture[] { Tex("A_BaseColor"), Tex("A_Normal") }, out _, out string count));
            StringAssert.Contains("2 selected", count);

            Assert.IsFalse(MaterialCreator.TryMatchTextures(new Texture[] { Tex("B_BaseColor"), Tex("B_Normal"), Tex("B_Roughness") }, out _, out string unknown));
            StringAssert.Contains("B_Roughness", unknown);

            Assert.IsFalse(MaterialCreator.TryMatchTextures(new Texture[] { Tex("C_BaseColor"), Tex("D_BaseColor"), Tex("C_MHER") }, out _, out string doubled));
            StringAssert.Contains("both a Base Color", doubled);
        }

        [Test]
        public void Match_OpenGLNormal_Warns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("OpenGL normal map"));
            Assert.IsTrue(MaterialCreator.TryMatchTextures(new Texture[] { Tex("E_BaseColor"), Tex("E_NormalGL"), Tex("E_MHER") }, out _, out _));
        }

        private Material UberMaterial()
        {
            Shader shader = Shader.Find(MaterialCreator.UberShaderName);
            if (shader == null) Assert.Ignore("The VFS/Uber shader isn't in this project.");
            var material = new Material(shader);
            _created.Add(material);
            return material;
        }

        private static int EnabledLayers(Material material) => Mathf.RoundToInt(material.GetFloat(MaterialCreator.EnabledLayersProperty));

        [Test]
        public void AssignLayer_RaisesEnabledLayers()
        {
            Material material = UberMaterial();
            Texture[] textures = { Tex("L_BaseColor"), Tex("L_Normal"), Tex("L_MHER") };

            MaterialCreator.AssignLayer(material, 2, textures);

            Assert.AreEqual(2, EnabledLayers(material));
            Assert.IsTrue(material.IsKeywordEnabled(MaterialCreator.EnabledLayersKeywords[2]));
            Assert.IsFalse(material.IsKeywordEnabled(MaterialCreator.EnabledLayersKeywords[0]));
            Assert.AreSame(textures[0], material.GetTexture("_BlendBaseColor_1"));
        }

        [Test]
        public void AssignLayer_NeverLowersEnabledLayers()
        {
            Material material = UberMaterial();
            Texture[] textures = { Tex("L_BaseColor"), Tex("L_Normal"), Tex("L_MHER") };
            MaterialCreator.SetEnabledLayers(material, 3);

            MaterialCreator.AssignLayer(material, 1, textures);

            Assert.AreEqual(3, EnabledLayers(material));
            Assert.IsTrue(material.IsKeywordEnabled(MaterialCreator.EnabledLayersKeywords[3]));
            Assert.AreSame(textures[0], material.GetTexture("_BlendBaseColor"));
        }

        [Test]
        public void AssignBaseLayer_SetsTheMainTextures()
        {
            Material material = UberMaterial();
            Texture[] textures = { Tex("M_BaseColor"), Tex("M_Normal"), Tex("M_MHER") };

            MaterialCreator.AssignLayer(material, 0, textures);

            Assert.AreSame(textures[0], material.GetTexture("_MainTex"));
            Assert.AreSame(textures[1], material.GetTexture("_Normal"));
            Assert.AreSame(textures[2], material.GetTexture("_Mask"));
            Assert.AreEqual(0, EnabledLayers(material));
        }

        // ---- Toolbar ----

        [Test]
        public void AutoRefresh_ReadsAllThreeModes()
        {
            int before = EditorPrefs.GetInt(ToolbarCompileOptions.AutoRefreshKey, 1);
            try
            {
                foreach (int mode in new[] { 0, 1, 2 })
                {
                    EditorPrefs.SetInt(ToolbarCompileOptions.AutoRefreshKey, mode);
                    Assert.AreEqual(mode, (int)ToolbarCompileOptions.CurrentMode);
                }
                EditorPrefs.SetInt(ToolbarCompileOptions.AutoRefreshKey, 7);
                Assert.AreEqual(ToolbarCompileOptions.AutoRefreshMode.Enabled, ToolbarCompileOptions.CurrentMode, "an unknown value reads as Enabled");
            }
            finally
            {
                EditorPrefs.SetInt(ToolbarCompileOptions.AutoRefreshKey, before);
            }
        }

        [Test]
        public void TimeScale_DoesNothingInEditMode()
        {
            float before = Time.timeScale;
            Assert.IsFalse(ToolbarTimeSlider.SetTimeScale(0.25f));
            Assert.AreEqual(before, Time.timeScale, "the project's Time Scale setting is untouched");
        }

        [Test]
        public void TimeScale_IsRestoredOnExitingPlayMode()
        {
            float before = Time.timeScale;
            try
            {
                ToolbarTimeSlider.OnPlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
                Time.timeScale = 0.3f; // what the slider would do in Play mode
                ToolbarTimeSlider.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
                Assert.AreEqual(before, Time.timeScale);
            }
            finally
            {
                Time.timeScale = before;
            }
        }

        [Test]
        public void SceneList_FindsScenesThroughTheAssetDatabase()
        {
            string[] paths = ToolbarScenesList.FindScenePaths();
            Assert.That(paths, Is.All.StartsWith("Assets/").And.All.EndsWith(".unity"));
            Assert.That(paths, Is.Ordered);
            Assert.AreEqual(System.IO.Directory.GetFiles("Assets", "*.unity", System.IO.SearchOption.AllDirectories).Length, paths.Length,
                "the same scenes a folder scan finds");
        }

        [Test]
        public void ActiveScenePath_UnsavedScene_Warns()
        {
            Scene active = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path)) Assert.AreEqual(active.path, ToolbarScenesList.GetActiveScenePath());
            else
            {
                LogAssert.Expect(LogType.Warning, new Regex("untitled"));
                Assert.IsNull(ToolbarScenesList.GetActiveScenePath());
            }
        }
    }
}
