using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityTools.DebugTools.Editor;

namespace UnityTools.DebugTools.Tests
{
    public class DebugMenuTests
    {
        private enum Difficulty { Easy, Normal, Hard }

        private class Owner { }

#pragma warning disable CS0618
        private class LegacyCommands
        {
            public int Healed;
            public int Spawned;

            [DebugCommand("Heal")]
            public void Heal() => Healed++;

            [DebugCommand("Spawn")]
            public void Spawn() => Spawned++;

            [DebugCommand("Teleport")]
            public void Teleport(Vector3 position) { }

            public void NotACommand() { }
        }
#pragma warning restore CS0618

        private readonly Owner _owner = new Owner();

        [SetUp]
        public void SetUp() => DebugMenu.ResetStatics();

        [TearDown]
        public void TearDown() => DebugMenu.ResetStatics();

        // ---------- Registry ----------

        [Test]
        public void Register_AddsSectionWithControls()
        {
            DebugMenu.Register(_owner, "Player", s => s.Button("Heal", () => { }).Readout("HP", () => "10"));

            Assert.AreEqual(1, DebugMenu.Sections.Count);
            Assert.AreEqual("Player", DebugMenu.Sections[0].Title);
            Assert.AreSame(_owner, DebugMenu.Sections[0].Owner);
            Assert.AreEqual(2, DebugMenu.Sections[0].Controls.Count);
        }

        [Test]
        public void Register_WithoutTitle_UsesOwnerTypeName()
        {
            DebugMenu.Register(_owner, "", s => { });

            Assert.AreEqual(nameof(Owner), DebugMenu.Sections[0].Title);
        }

        [Test]
        public void Register_NullOwnerOrBuilder_IsIgnored()
        {
            DebugMenu.Register(null, "A", s => { });
            DebugMenu.Register(_owner, "B", null);

            Assert.AreEqual(0, DebugMenu.Sections.Count);
        }

        [Test]
        public void Unregister_RemovesEveryOwnSection_Only()
        {
            var other = new Owner();
            DebugMenu.Register(_owner, "A", s => { });
            DebugMenu.Register(_owner, "B", s => { });
            DebugMenu.Register(other, "A", s => { });

            DebugMenu.Unregister(_owner);

            Assert.AreEqual(1, DebugMenu.Sections.Count);
            Assert.AreSame(other, DebugMenu.Sections[0].Owner);
        }

        [Test]
        public void SectionsChanged_FiresOnRegisterAndUnregister()
        {
            int changes = 0;
            DebugMenu.SectionsChanged += () => changes++;

            DebugMenu.Register(_owner, "A", s => { });
            DebugMenu.Unregister(_owner);
            DebugMenu.Unregister(_owner);

            Assert.AreEqual(2, changes);
        }

        [Test]
        public void RemoveDestroyedOwners_DropsSectionsOfDestroyedObjects()
        {
            var unityOwner = ScriptableObject.CreateInstance<ScriptableObject>();
            DebugMenu.Register(unityOwner, "Gone", s => { });
            DebugMenu.Register(_owner, "Kept", s => { });
            Object.DestroyImmediate(unityOwner);

            DebugMenu.RemoveDestroyedOwners();

            Assert.AreEqual(1, DebugMenu.Sections.Count);
            Assert.AreEqual("Kept", DebugMenu.Sections[0].Title);
        }

        // ---------- Section builder ----------

        [Test]
        public void Builder_CreatesEveryControlKind()
        {
            DebugMenu.Register(_owner, "All", s => s
                .Button("B", () => { })
                .Toggle("T", () => true, v => { })
                .Slider("S", 0f, 1f, () => 0.5f, v => { })
                .SliderInt("I", 0, 10, () => 5, v => { })
                .Dropdown("D", new[] { "a", "b" }, () => 1, v => { })
                .Readout("R", () => "x"));

            var kinds = DebugMenu.Sections[0].Controls.ConvertAll(c => c.Kind);
            CollectionAssert.AreEqual(new[]
            {
                DebugControlKind.Button, DebugControlKind.Toggle, DebugControlKind.Slider,
                DebugControlKind.IntSlider, DebugControlKind.Dropdown, DebugControlKind.Readout
            }, kinds);
        }

        [Test]
        public void EnumDropdown_MapsIndicesToValues()
        {
            Difficulty difficulty = Difficulty.Normal;
            DebugMenu.Register(_owner, "Game", s => s.Dropdown("Difficulty", () => difficulty, v => difficulty = v));
            DebugControl control = DebugMenu.Sections[0].Controls[0];

            CollectionAssert.AreEqual(new[] { "Easy", "Normal", "Hard" }, control.Options);
            Assert.AreEqual(1, control.GetInt());
            control.SetInt(2);
            Assert.AreEqual(Difficulty.Hard, difficulty);
        }

        // ---------- JSON / CLI ----------

        [Test]
        public void JsonList_DescribesControlsWithValues()
        {
            DebugMenu.Register(_owner, "Player", s => s
                .Toggle("God mode", () => true, v => { })
                .Slider("Speed", 0f, 10f, () => 2.5f, v => { })
                .Readout("HP", () => "7/10"));

            string json = DebugMenuJson.List();

            StringAssert.Contains("\"section\":\"Player\"", json);
            StringAssert.Contains("{\"label\":\"God mode\",\"kind\":\"Toggle\",\"value\":true}", json);
            StringAssert.Contains("\"label\":\"Speed\",\"kind\":\"Slider\",\"value\":2.5", json);
            StringAssert.Contains("{\"label\":\"HP\",\"kind\":\"Readout\",\"value\":\"7/10\"}", json);
        }

        [Test]
        public void JsonList_MergesSectionsWithTheSameTitle_AndFilters()
        {
            DebugMenu.Register(_owner, "Enemies", s => s.Button("Kill all", () => { }));
            DebugMenu.Register(new Owner(), "Enemies", s => s.Button("Spawn", () => { }));
            DebugMenu.Register(new Owner(), "Player", s => s.Button("Heal", () => { }));

            string json = DebugMenuJson.List("Enemies");

            StringAssert.Contains("Kill all", json);
            StringAssert.Contains("Spawn", json);
            StringAssert.DoesNotContain("Heal", json);
            Assert.AreEqual(1, Regex.Matches(json, "\"section\"").Count);
        }

        [Test]
        public void JsonDo_DrivesEveryKind()
        {
            int pressed = 0;
            bool flag = false;
            float speed = 0f;
            int count = 0;
            int choice = 0;
            DebugMenu.Register(_owner, "S", s => s
                .Button("Press", () => pressed++)
                .Toggle("Flag", () => flag, v => flag = v)
                .Slider("Speed", 0f, 10f, () => speed, v => speed = v)
                .SliderInt("Count", 0, 10, () => count, v => count = v)
                .Dropdown("Choice", new[] { "Red", "Green", "Blue" }, () => choice, v => choice = v));

            DebugMenuJson.Do("S", "Press");
            DebugMenuJson.Do("S", "Flag", "true");
            DebugMenuJson.Do("S", "Speed", "3.5");
            DebugMenuJson.Do("S", "Count", "7");
            DebugMenuJson.Do("S", "Choice", "Blue");

            Assert.AreEqual(1, pressed);
            Assert.IsTrue(flag);
            Assert.AreEqual(3.5f, speed);
            Assert.AreEqual(7, count);
            Assert.AreEqual(2, choice);

            DebugMenuJson.Do("S", "Choice", "1");
            Assert.AreEqual(1, choice);
        }

        [Test]
        public void JsonDo_ReturnsSectionValuesAfterwards()
        {
            bool flag = false;
            DebugMenu.Register(_owner, "S", s => s.Toggle("Flag", () => flag, v => flag = v));

            string json = DebugMenuJson.Do("S", "Flag", "on");

            StringAssert.Contains("\"value\":true", json);
        }

        [Test]
        public void JsonDo_ReportsErrors()
        {
            DebugMenu.Register(_owner, "S", s => s
                .Toggle("Flag", () => false, v => { })
                .Slider("Speed", 0f, 1f, () => 0f, v => { })
                .SliderInt("Count", 0, 1, () => 0, v => { })
                .Dropdown("Choice", new[] { "A" }, () => 0, v => { })
                .Readout("R", () => ""));

            StringAssert.Contains("no control 'Nope'", DebugMenuJson.Do("S", "Nope"));
            StringAssert.Contains("is a toggle", DebugMenuJson.Do("S", "Flag", "maybe"));
            StringAssert.Contains("is a slider", DebugMenuJson.Do("S", "Speed", "fast"));
            StringAssert.Contains("whole-number", DebugMenuJson.Do("S", "Count", "1.5"));
            StringAssert.Contains("one of 'A'", DebugMenuJson.Do("S", "Choice", "B"));
            StringAssert.Contains("readout", DebugMenuJson.Do("S", "R", "x"));
        }

        [Test]
        public void Json_EscapesQuotesAndControlCharacters()
        {
            DebugMenu.Register(_owner, "S", s => s.Readout("Say \"hi\"", () => "a\\b\nc"));

            string json = DebugMenuJson.List();

            StringAssert.Contains("Say \\\"hi\\\"", json);
            StringAssert.Contains("a\\\\b\\u000ac", json);
        }

        [Test]
        public void Json_SkipsDestroyedOwners()
        {
            var unityOwner = ScriptableObject.CreateInstance<ScriptableObject>();
            DebugMenu.Register(unityOwner, "Gone", s => s.Button("X", () => { }));
            Object.DestroyImmediate(unityOwner);

            Assert.AreEqual("[]", DebugMenuJson.List());
        }

        // ---------- Old API ----------

#pragma warning disable CS0618
        [Test]
        public void LegacyRegisterObject_MakesButtonsForCommands_AndSkipsMethodsWithParameters()
        {
            var commands = new LegacyCommands();

            LogAssert.Expect(LogType.Warning, new Regex("Teleport takes parameters"));
            DebugMenuSystem system = DebugMenuSystem.Instance;
            try
            {
                system.RegisterObject(commands);

                DebugSection section = DebugMenu.Sections[0];
                Assert.AreEqual(nameof(LegacyCommands), section.Title);
                CollectionAssert.AreEquivalent(new[] { "Heal", "Spawn" }, section.Controls.ConvertAll(c => c.Label));

                DebugMenuJson.Do(nameof(LegacyCommands), "Heal");
                Assert.AreEqual(1, commands.Healed);

                system.DeregisterObject(commands);
                Assert.AreEqual(0, DebugMenu.Sections.Count);
            }
            finally
            {
                Object.DestroyImmediate(system.gameObject);
            }
        }
#pragma warning restore CS0618

        // ---------- Settings ----------

        [Test]
        public void SettingsJson_RoundTrips()
        {
            var settings = new DebugMenuSettings
            {
                StartOpen = true,
                PauseWhileOpen = true,
                BlockGameplayInput = true,
                ShowGameSection = false,
                RefreshInterval = 1.5f
            };
            settings.ToggleAction.AddBinding("<Keyboard>/f1");

            DebugMenuSettings copy = DebugMenuSettings.FromJson(settings.ToJson());

            Assert.IsTrue(copy.StartOpen);
            Assert.IsTrue(copy.PauseWhileOpen);
            Assert.IsTrue(copy.BlockGameplayInput);
            Assert.IsFalse(copy.ShowGameSection);
            Assert.AreEqual(1.5f, copy.RefreshInterval);
            Assert.AreEqual(1, copy.ToggleAction.bindings.Count);
            Assert.AreEqual("<Keyboard>/f1", copy.ToggleAction.bindings[0].path);
        }

        [Test]
        public void SettingsFromJson_EmptyOrMissingFields_GiveDefaults()
        {
            DebugMenuSettings fromEmpty = DebugMenuSettings.FromJson("");
            DebugMenuSettings partial = DebugMenuSettings.FromJson("{\"StartOpen\":true}");

            Assert.IsTrue(fromEmpty.ShowGameSection);
            Assert.AreEqual(0.25f, fromEmpty.RefreshInterval);
            Assert.IsTrue(partial.StartOpen);
            Assert.IsTrue(partial.ShowGameSection);
        }

        [Test]
        public void CreateToggleAction_AddsDefaults_WithoutChangingSettings()
        {
            var settings = new DebugMenuSettings();

            var action = settings.CreateToggleAction();

            Assert.AreEqual(0, settings.ToggleAction.bindings.Count);
            Assert.IsTrue(action.bindings.Count > 0);
            Assert.AreEqual("<Keyboard>/f5", action.bindings[0].path);
            action.Dispose();
        }

        [Test]
        public void CreateToggleAction_UsesCustomBindings()
        {
            var settings = new DebugMenuSettings();
            settings.ToggleAction.AddBinding("<Keyboard>/f1");

            var action = settings.CreateToggleAction();

            Assert.AreEqual(1, action.bindings.Count);
            Assert.AreEqual("<Keyboard>/f1", action.bindings[0].path);
            action.Dispose();
        }

        [Test]
        public void BuildStep_IncludesDevelopmentBuilds()
        {
            var development = new BuildPlayerOptions { options = BuildOptions.Development, targetGroup = BuildTargetGroup.Standalone };
            var withDefine = new BuildPlayerOptions { extraScriptingDefines = new[] { "DEBUG_MENU" }, targetGroup = BuildTargetGroup.Standalone };

            Assert.IsTrue(DebugMenuBuildStep.IncludesDebugMenu(development));
            Assert.IsTrue(DebugMenuBuildStep.IncludesDebugMenu(withDefine));
        }
    }
}
