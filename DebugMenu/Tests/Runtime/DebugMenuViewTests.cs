using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace UnityTools.DebugTools.PlayModeTests
{
    public class DebugMenuViewTests
    {
        private class Owner { }

        private readonly List<Object> _created = new List<Object>();
        private readonly Owner _owner = new Owner();
        private DebugMenuView _view;

        private VisualElement Root => _view.GetComponent<UIDocument>().rootVisualElement;
        private VisualElement Panel => Root.Q<VisualElement>("debug-panel");
        private List<Foldout> Foldouts => Root.Q<ScrollView>("debug-sections").Query<Foldout>().ToList();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DebugMenu.Unregister(_owner);
            if (_view != null) _view.SetOpen(false);
            foreach (Object obj in _created)
            {
                if (obj != null) Object.Destroy(obj);
            }
            _created.Clear();
            DebugMenuSettings.OverrideForTests(null);
            Time.timeScale = 1f;

            // Leave a normal automatic menu behind, like at the start of Play.
            if (DebugMenuView.Active != null && !DebugMenuView.Active.IsAutoSpawned) Object.Destroy(DebugMenuView.Active.gameObject);
            yield return null;
            if (DebugMenuView.Active == null) DebugMenuView.CreateAutoSpawned();
            yield return null;
        }

        // Replaces the current menu with a fresh automatic one using these settings.
        private IEnumerator CreateMenu(DebugMenuSettings settings = null)
        {
            DebugMenuSettings.OverrideForTests(settings ?? new DebugMenuSettings { ShowGameSection = false });
            if (DebugMenuView.Active != null) Object.Destroy(DebugMenuView.Active.gameObject);
            yield return null;
            _view = DebugMenuView.CreateAutoSpawned();
            yield return null;
        }

        private T Find<T>(string label) where T : VisualElement
        {
            return Root.Query<T>().ToList().FirstOrDefault(e => e is BaseField<bool> t ? t.label == label
                : e is BaseField<float> f ? f.label == label
                : e is BaseField<int> i ? i.label == label
                : e is BaseField<string> s ? s.label == label
                : e is Button b && b.text == label);
        }

        // ---------- Creation ----------

        [Test]
        public void Menu_IsCreatedAutomatically_AndSurvivesSceneLoads()
        {
            Assert.IsNotNull(DebugMenuView.Active, "no debug menu was created when Play started");
            Assert.IsTrue(DebugMenuView.Active.IsAutoSpawned);
            Assert.AreEqual("DontDestroyOnLoad", DebugMenuView.Active.gameObject.scene.name);
        }

        [UnityTest]
        public IEnumerator PlacedMenu_ReplacesTheAutomaticOne()
        {
            yield return CreateMenu();
            DebugMenuView automatic = _view;

            DebugMenuView placed = CreatePlacedMenu("Placed Menu");
            yield return null;

            Assert.IsTrue(automatic == null, "the automatic menu should be destroyed");
            Assert.AreSame(placed, DebugMenuView.Active);
            _view = placed;
        }

        [UnityTest]
        public IEnumerator SecondPlacedMenu_IsRemovedWithWarning()
        {
            yield return CreateMenu();
            DebugMenuView first = CreatePlacedMenu("First");
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("already the debug menu"));
            DebugMenuView second = CreatePlacedMenu("Second");
            yield return null;

            Assert.IsTrue(second == null);
            Assert.AreSame(first, DebugMenuView.Active);
            _view = first;
        }

        private DebugMenuView CreatePlacedMenu(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.SetActive(false);
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UnityToolsDebugMenu/DebugTheme");
            _created.Add(panelSettings);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = Resources.Load<VisualTreeAsset>("UnityToolsDebugMenu/DebugMenu");
            var view = go.AddComponent<DebugMenuView>();
            go.SetActive(true);
            return view;
        }

        // ---------- Open and close ----------

        [UnityTest]
        public IEnumerator StartsClosed_AndOpensAndCloses()
        {
            yield return CreateMenu();

            Assert.IsFalse(_view.IsOpen);
            Assert.AreEqual(DisplayStyle.None, Panel.style.display.value);

            _view.SetOpen(true);
            Assert.AreEqual(DisplayStyle.Flex, Panel.style.display.value);

            _view.SetOpen(false);
            Assert.AreEqual(DisplayStyle.None, Panel.style.display.value);
        }

        [UnityTest]
        public IEnumerator StartOpen_OpensWhenCreated()
        {
            yield return CreateMenu(new DebugMenuSettings { StartOpen = true, ShowGameSection = false });

            Assert.IsTrue(_view.IsOpen);
        }

        [UnityTest]
        public IEnumerator Open_ShowsOneFoldoutPerSectionTitle()
        {
            yield return CreateMenu();
            DebugMenu.Register(_owner, "Enemies", s => s.Button("Spawn", () => { }));
            DebugMenu.Register(_owner, "Enemies", s => s.Button("Kill all", () => { }));
            DebugMenu.Register(_owner, "Player", s => s.Button("Heal", () => { }));

            _view.SetOpen(true);

            CollectionAssert.AreEqual(new[] { "Enemies", "Player" }, Foldouts.Select(f => f.text));
            Assert.AreEqual(2, Foldouts[0].Query<Button>().ToList().Count);
        }

        [UnityTest]
        public IEnumerator SectionsAddedWhileOpen_ShowUpNextFrame()
        {
            yield return CreateMenu();
            _view.SetOpen(true);

            DebugMenu.Register(_owner, "Late", s => s.Button("X", () => { }));
            yield return null;

            Assert.IsTrue(Foldouts.Any(f => f.text == "Late"));
        }

        [UnityTest]
        public IEnumerator SectionsOfDestroyedOwners_AreDropped()
        {
            yield return CreateMenu();
            var unityOwner = new GameObject("Owner");
            DebugMenu.Register(unityOwner, "Gone", s => s.Button("X", () => { }));
            Object.Destroy(unityOwner);
            yield return null;

            _view.SetOpen(true);

            Assert.IsFalse(Foldouts.Any(f => f.text == "Gone"));
        }

        [UnityTest]
        public IEnumerator GameSection_IsShownWhenEnabled()
        {
            yield return CreateMenu(new DebugMenuSettings { ShowGameSection = true });
            _view.SetOpen(true);

            Assert.IsTrue(Foldouts.Any(f => f.text == "Game"));
            StringAssert.Contains("\"label\":\"Time scale\"", DebugMenuJson.List("Game"));
        }

        [UnityTest]
        public IEnumerator GameSection_IsHiddenWhenDisabled()
        {
            yield return CreateMenu(new DebugMenuSettings { ShowGameSection = false });
            _view.SetOpen(true);

            Assert.IsFalse(Foldouts.Any(f => f.text == "Game"));
        }

        // ---------- Controls ----------

        [UnityTest]
        public IEnumerator Button_RunsItsAction()
        {
            yield return CreateMenu();
            int pressed = 0;
            DebugMenu.Register(_owner, "S", s => s.Button("Press", () => pressed++));
            _view.SetOpen(true);

            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                Button button = Find<Button>("Press");
                submit.target = button;
                button.SendEvent(submit);
            }
            yield return null;

            Assert.AreEqual(1, pressed);
        }

        [UnityTest]
        public IEnumerator Fields_SetTheirValues()
        {
            yield return CreateMenu();
            bool flag = false;
            float speed = 0f;
            int count = 0;
            int choice = 0;
            DebugMenu.Register(_owner, "S", s => s
                .Toggle("Flag", () => flag, v => flag = v)
                .Slider("Speed", 0f, 10f, () => speed, v => speed = v)
                .SliderInt("Count", 0, 10, () => count, v => count = v)
                .Dropdown("Choice", new[] { "Red", "Green" }, () => choice, v => choice = v));
            _view.SetOpen(true);

            Find<Toggle>("Flag").value = true;
            Find<Slider>("Speed").value = 4f;
            Find<SliderInt>("Count").value = 6;
            Find<DropdownField>("Choice").value = "Green";
            yield return null;

            Assert.IsTrue(flag);
            Assert.AreEqual(4f, speed);
            Assert.AreEqual(6, count);
            Assert.AreEqual(1, choice);
        }

        [UnityTest]
        public IEnumerator Refresh_UpdatesReadoutsAndBoundValues()
        {
            yield return CreateMenu();
            bool flag = false;
            int hp = 10;
            DebugMenu.Register(_owner, "S", s => s
                .Toggle("Flag", () => flag, v => flag = v)
                .Readout("HP", () => hp.ToString()));
            _view.SetOpen(true);

            flag = true;
            hp = 3;
            _view.RefreshValues();

            Assert.IsTrue(Find<Toggle>("Flag").value);
            Assert.IsTrue(Root.Query<Label>().ToList().Any(l => l.text == "HP: 3"));
        }

        [UnityTest]
        public IEnumerator ReadoutThatThrows_IsLogged_AndOthersStillRefresh()
        {
            yield return CreateMenu();
            int hp = 10;
            bool broken = false;
            DebugMenu.Register(_owner, "S", s => s
                .Readout("Broken", () => broken ? throw new System.InvalidOperationException("boom") : "ok")
                .Readout("HP", () => hp.ToString()));
            _view.SetOpen(true);

            broken = true;
            hp = 1;
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            _view.RefreshValues();

            Assert.IsTrue(Root.Query<Label>().ToList().Any(l => l.text == "HP: 1"));
        }

        [UnityTest]
        public IEnumerator FoldoutState_IsKeptAcrossRebuilds()
        {
            yield return CreateMenu();
            DebugMenu.Register(_owner, "S", s => s.Button("X", () => { }));
            _view.SetOpen(true);
            Foldouts.First(f => f.text == "S").value = true;
            yield return null;

            DebugMenu.Register(_owner, "Other", s => { });
            yield return null;

            Assert.IsTrue(Foldouts.First(f => f.text == "S").value);
        }

        // ---------- State while open ----------

        [UnityTest]
        public IEnumerator Cursor_IsFreedWhileOpen_AndRestoredOnClose()
        {
            yield return CreateMenu();
            UnityEngine.Cursor.lockState = CursorLockMode.Confined;
            UnityEngine.Cursor.visible = false;

            _view.SetOpen(true);
            Assert.AreEqual(CursorLockMode.None, UnityEngine.Cursor.lockState);
            Assert.IsTrue(UnityEngine.Cursor.visible);

            _view.SetOpen(false);
            Assert.AreEqual(CursorLockMode.Confined, UnityEngine.Cursor.lockState);
            Assert.IsFalse(UnityEngine.Cursor.visible);

            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        [UnityTest]
        public IEnumerator TimeScale_IsUntouched_ByDefault()
        {
            yield return CreateMenu();
            Time.timeScale = 0.5f;

            _view.SetOpen(true);
            Assert.AreEqual(0.5f, Time.timeScale);
            _view.SetOpen(false);
            Assert.AreEqual(0.5f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator PauseWhileOpen_RestoresThePreviousTimeScale()
        {
            yield return CreateMenu(new DebugMenuSettings { PauseWhileOpen = true, ShowGameSection = false });
            Time.timeScale = 0.5f;

            _view.SetOpen(true);
            Assert.AreEqual(0f, Time.timeScale);
            _view.SetOpen(false);
            Assert.AreEqual(0.5f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator BlockGameplayInput_TurnsOffOnlyActivePlayerInputs()
        {
            yield return CreateMenu(new DebugMenuSettings { BlockGameplayInput = true, ShowGameSection = false });
            PlayerInput active = CreatePlayerInput("Active");
            PlayerInput alreadyOff = CreatePlayerInput("Already off");
            yield return null;
            alreadyOff.DeactivateInput();
            Assert.IsTrue(active.inputIsActive);

            _view.SetOpen(true);
            Assert.IsFalse(active.inputIsActive);

            _view.SetOpen(false);
            Assert.IsTrue(active.inputIsActive);
            Assert.IsFalse(alreadyOff.inputIsActive, "a PlayerInput that was off before opening was turned on");
        }

        [UnityTest]
        public IEnumerator GameplayInput_IsNotBlocked_ByDefault()
        {
            yield return CreateMenu();
            PlayerInput playerInput = CreatePlayerInput("Player");
            yield return null;

            _view.SetOpen(true);

            Assert.IsTrue(playerInput.inputIsActive);
        }

        private PlayerInput CreatePlayerInput(string name)
        {
            var actions = ScriptableObject.CreateInstance<InputActionAsset>();
            _created.Add(actions);
            InputActionMap map = actions.AddActionMap("Player");
            map.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");

            var go = new GameObject(name);
            _created.Add(go);
            go.SetActive(false);
            var playerInput = go.AddComponent<PlayerInput>();
            playerInput.actions = actions;
            playerInput.defaultActionMap = "Player";
            go.SetActive(true);
            return playerInput;
        }

        // ---------- Old API ----------

#pragma warning disable CS0618
        [UnityTest]
        public IEnumerator PlacedLegacyComponent_WarnsOnce()
        {
            LogAssert.Expect(LogType.Warning, new Regex("DebugMenuSystem is obsolete"));
            var first = new GameObject("Old menu");
            _created.Add(first);
            first.AddComponent<DebugMenuSystem>();
            var second = new GameObject("Old menu 2");
            _created.Add(second);
            second.AddComponent<DebugMenuSystem>();
            yield return null;

            LogAssert.NoUnexpectedReceived();
        }
#pragma warning restore CS0618
    }
}
