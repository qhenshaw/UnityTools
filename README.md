# Unity Tools
[![Unity 6](https://img.shields.io/badge/unity-6-blue.svg)](https://unity.com/releases/editor/archive)
[![License: MIT](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE.md)

A collection of gameplay systems, debugging tools and editor tools for Unity, built for teaching game development: ready-made plumbing (pooling, state machines, messaging, a debug menu, playtest feedback) plus level design, art pipeline and lighting helpers.

> [!WARNING]
> As of 5.2.0 this package is maintained with AI-assisted code. If that's not your thing, feel free to fork an earlier version.

## Contents
- [Requirements](#requirements)
- [Installation](#installation)
- **Gameplay systems**
  - [Object Pooling](#object-pooling)
  - [Simple State Machine](#simple-state-machine)
  - [Service Locator](#service-locator)
  - [Messaging Service](#messaging-service)
  - [Global Variables](#global-variables)
  - [Data Reference](#data-reference)
- **Debugging and playtesting**
  - [Debug Menu](#debug-menu)
  - [Playtest Feedback](#playtest-feedback)
- **Scenes**
  - [Streaming Scene Manager](#streaming-scene-manager)
  - [Additive Scene Manager](#additive-scene-manager)
  - [Prefab Spawner](#prefab-spawner)
  - [Hide Children](#hide-children)
  - [Enable Objects Sequential](#enable-objects-sequential)
- **Level design**
  - [Projection Scatter](#projection-scatter)
  - [Physics Scatter](#physics-scatter)
  - [Placement Graph](#placement-graph)
  - [Snap to Floor](#snap-to-floor)
- **Art pipeline**
  - [Mesh Importer](#mesh-importer)
  - [Material Creator](#material-creator)
  - [Vertex Painter](#vertex-painter)
  - [Gradient Editor](#gradient-editor)
- **Lighting**
  - [Surround with Volume](#surround-with-volume)
  - [Assign Volume to Open Scenes](#assign-volume-to-open-scenes)
  - [Lightbake Settings](#lightbake-settings)
  - [APV Bake Checklist](#apv-bake-checklist)
- **Editor and inspector**
  - [Toolbar Buttons](#toolbar-buttons)
  - [Transform Search](#transform-search)
  - [Inspector Attributes](#inspector-attributes)
  - [Scene Dropdown](#scene-dropdown)
- [Assemblies and namespaces](#assemblies-and-namespaces)

## Requirements
- **Unity 6.5 (6000.5) or later.** The Placement Graph is built on Graph Toolkit, which is part of the editor from 6000.5.
- **URP or HDRP.** The lighting tools reference the Scriptable Render Pipeline core package, so the Built-in Render Pipeline isn't supported.
- Installed automatically: Input System, Cinemachine, TextMeshPro, uGUI, 2D Sprite and the UI Toolkit, UnityWebRequest, Image Conversion, Screen Capture and Audio modules.
- Optional: **HDRP** enables the APV Bake Checklist and Assign Volume to Open Scenes. The **Pipeline** package (`com.unity.pipeline`) adds CLI commands for the Debug Menu.
- No Odin Inspector needed (since 5.0.0).

## Installation
In the Package Manager, choose **Add package from git URL** and enter:
```
https://github.com/qhenshaw/UnityTools.git
```
To lock a project to one version, add the version tag (see the [changelog](CHANGELOG.md) for versions):
```
https://github.com/qhenshaw/UnityTools.git#7.0.0
```
GameEvents were replaced by the Messaging Service in 5.0.0. For GameEvents, use `#4.5.0`.

---

# Gameplay systems

## Object Pooling
Reuses GameObjects instead of instantiating and destroying them, for things like bullets and hit effects.

Give the prefab a component that inherits from `PooledObject`, reference the prefab by that type, and get instances from the pool:
```cs
using UnityTools.Pooling;
using UnityEngine;

public class Gun : MonoBehaviour
{
    [SerializeField] private Bullet _bulletPrefab;

    public void Fire()
    {
        Bullet bullet = PoolSystem.Instance.Get(_bulletPrefab, transform.position, transform.rotation);
    }
}
```
Return it to the pool instead of destroying it, optionally after a delay (like `Destroy(gameObject, 2f)`):
```cs
bullet.ReturnToPool();
bullet.ReturnToPool(2f);
```
A reused instance keeps its old state (velocity, health, trails), so reset it in the pool hooks:
```cs
using UnityTools.Pooling;

public class Bullet : PooledObject
{
    protected override void OnTakenFromPool() { /* runs after it's placed and active, after OnEnable */ }
    protected override void OnReturnedToPool() { /* runs while it's still active */ }
}
```
- Instances are placed (parent, position, rotation) before they're activated, so `OnEnable` sees the spawn position. `Awake` runs once per instance; `OnEnable` and `OnTakenFromPool` run on every `Get`.
- `Get(prefab)` uses the prefab's own position and rotation, `Get(prefab, position)` the prefab's rotation, and `Get(prefab, position, rotation, parent)` takes a world-space position.
- **Prewarm Count** (on the PooledObject): instances created when the pool is first used, so the first shots don't instantiate mid-game. **Max Size**: most instances kept waiting in the pool; extras are destroyed when returned (0 = unlimited). Both are read when the pool is created.
- The pool is created on first use, in the active scene, and is destroyed with that scene. To prewarm as soon as a scene loads, place a **PoolSystem** in it and add prefabs to its **Prewarm** list. A second PoolSystem passes its list to the first and removes itself.
- `PoolSystem.Instance.Prewarm(prefab)` / `Prewarm(prefab, count)` and `CountInactive(prefab)` are there for loading screens and debugging.
- `ReturnToPool(delay)` uses scaled time. Calling it again restarts the timer, and returning another way cancels it.
- `ReturnToPool()` on an object that didn't come from a pool (placed in the scene, or whose pool was unloaded) destroys it.
- Destroying a pooled object (or its parent) logs an error once per prefab, since the pool can't reuse it. If your PooledObject has its own `OnDestroy`, override it and call `base.OnDestroy()`.

## Simple State Machine
A small C# state machine: each state is a class, and transitions are conditions between state types.
```cs
using UnityTools.States;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private float _health = 100f;

    public bool HasTarget => _target != null;
    public bool IsAlive => _health > 0f;

    private StateMachine<EnemyController> _stateMachine;

    private void Start()
    {
        _stateMachine = new StateMachine<EnemyController>(this);
        _stateMachine.AddTransition<IdleState, ChaseState>(() => HasTarget);
        _stateMachine.AddTransition<ChaseState, IdleState>(() => !HasTarget);
        // typed condition: gets the current state as a ChaseState
        _stateMachine.AddTransition<ChaseState, IdleState>(chase => chase.TimeInState > 10f);
        // checked from every state (except DeathState itself), before the others
        _stateMachine.AddTransitionFromAny<DeathState>(() => !IsAlive);

        _stateMachine.NextState<IdleState>();
    }

    private void Update() => _stateMachine.Update(Time.deltaTime);
    private void FixedUpdate() => _stateMachine.FixedUpdate(Time.fixedDeltaTime);

    // editor only: shows the current state above the enemy in the Scene view
    private void OnDrawGizmos() => _stateMachine?.DrawDebugLabel(transform.position + Vector3.up * 2f);
}
```
```cs
using UnityTools.States;

public class ChaseState : State<EnemyController>
{
    // no constructor needed: Binding and StateMachine are set before Enter

    public override void Enter()
    {
        // reset anything this state changes, since the same instance is reused
    }

    public override void Update(float deltaTime)
    {
        // Binding is the EnemyController: navigate towards its target here
    }

    public override void Exit() { }
}

public class IdleState : State<EnemyController> { }
public class DeathState : State<EnemyController> { }
```
- Call the machine's `Update` every frame: **transitions are checked and `TimeInState` advances only in `Update`** (`FixedUpdate` only runs the state's `FixedUpdate`). Set `FixedUpdateConditionsCheck = true` to also check in FixedUpdate.
- Each state type has **one instance per machine, reused every time it's entered**. Reset any fields the state changes in `Enter()`.
- When several transitions are true at once, the one **added last** wins, and only one transition fires per check. Transitions from any state are checked first.
- `TimeInState` is the seconds since the state was entered, so you don't need your own timers.
- A state can switch itself with `NextState<OtherState>()`. Entering the current state again re-enters it (Exit, then Enter). Calls made from inside Enter or Exit are queued.
- `AddTransition` returns a handle; pass it to `RemoveTransition(handle)` to remove that transition. `RemoveTransitions<From, To>()` removes all of them between two states.
- `CurrentState`, `PreviousState` and the `OnStateChanged` event (raised after Enter) are on the machine. `new StateMachine<T>(this, true)` logs every change.

## Service Locator
Finds "the one" instance of a class (an AudioManager, a ScoreManager) without a direct reference. Use it for the one X found by type; use [Global Variables](#global-variables) for values found by tag (e.g. the current player).

Register in **Awake**, and unregister when destroyed:
```cs
using UnityTools.Services;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    private void Awake() => ServiceLocator.Register(this);
    private void OnDestroy() => ServiceLocator.Unregister(this);
}
```
Get it in **Start** or later, so every service has registered first:
```cs
using UnityTools.Services;
using UnityEngine;

public class Door : MonoBehaviour
{
    private AudioManager _audio;

    private void Start()
    {
        _audio = ServiceLocator.Get<AudioManager>(this);   // logs an error pointing at this Door if there's none
        if (ServiceLocator.TryGet(out ScoreManager score)) { /* optional service, no error */ }
    }
}
```
- Registrations survive scene loads, but the objects don't (unless they use `DontDestroyOnLoad`). A destroyed service counts as unregistered, so the next scene's manager can register in its place.
- Registering a second live instance of the same type logs an error and keeps the first. `Unregister` is safe to call in `OnDestroy` even if another instance has taken the slot.
- `Register(this)` registers under the type the call is compiled as: called from a base class's `Awake`, it registers the base class. `Get<IDamageable>()` finds a service registered as exactly that type, otherwise the one registered service that implements it (an error if there are several). Register under an interface with `ServiceLocator.Register<IDamageable>(this)`.
- Everything is cleared when Play starts. Registered services show in Tools > UnityTools > Messaging Debugger. `ServiceLocator.LogChanges = true` logs every register and unregister.

## Messaging Service
Sends global messages between objects and scenes, using an enum value as the tag. Define your own enum of messages:
```cs
public enum GlobalMessages { PlayerSpawned, PlayerDied, LevelLoaded }
```
Sender:
```cs
using UnityEngine;
using UnityTools.Messaging;

public class Player : MonoBehaviour
{
    private void Start() => MessagingService.Instance.Send(GlobalMessages.PlayerSpawned, gameObject);
}
```
Listener:
```cs
using UnityEngine;
using UnityTools.Messaging;

public class Hud : MonoBehaviour
{
    private void OnEnable() => MessagingService.Instance.AddListener<GlobalMessages, GameObject>(GlobalMessages.PlayerSpawned, OnPlayerSpawned);
    private void OnDisable() => MessagingService.Instance.RemoveListener<GlobalMessages, GameObject>(GlobalMessages.PlayerSpawned, OnPlayerSpawned);

    private void OnPlayerSpawned(GameObject player)
    {
        // cache the reference here
    }
}
```
- **One payload type per tag**, set by its first typed listener. A listener or send with a different type logs an error naming the tag and both types. A payload of a derived type reaches base-type listeners, and a null payload is fine for reference types.
- A listener that throws is logged, and the other listeners still run. Listeners added during a send wait for the next one; listeners removed during a send are skipped.
- A listener only receives messages sent after it was added, unless the sender passes `sendToLate: true`: listeners added later then get the last payload as soon as they're added (e.g. a HUD enabled after the player spawned). Once a tag has a remembered payload, every later send replaces it. `ClearSticky(tag)` forgets it.
  ```cs
  MessagingService.Instance.Send(GlobalMessages.PlayerSpawned, gameObject, sendToLate: true);
  ```
- No payload: listen without caring about the payload (fires on every send to the tag), or send without one (only for tags with no typed listeners). Remove it with `RemoveListener(tag, method)`.
  ```cs
  MessagingService.Instance.AddListener(GlobalMessages.PlayerDied, PlayDeathSound);
  MessagingService.Instance.Send(GlobalMessages.LevelLoaded);
  ```
- Listeners on destroyed objects are removed with a warning, but remove your listeners in `OnDisable` anyway. Everything is cleared when Play starts.
- Sending doesn't allocate, as long as the call doesn't name a single type argument: `Send(tag, payload)` is allocation-free, `Send<DamageInfo>(tag, payload)` boxes the tag. Name both types (`Send<GlobalMessages, DamageInfo>`) or none.
- **Messaging Debugger** (Tools > UnityTools > Messaging Debugger) lists every tag with its payload type, listeners and remembered payload, every global variable and every registered service, with a search filter. It updates live in Play mode.

## Global Variables
Shared values found by an enum tag, such as the current player or the score. Define your own enum of variables:
```cs
public enum GlobalVariables { PlayerCharacter, Score }
```
Set the value in one place:
```cs
using UnityEngine;
using UnityTools.Messaging;

public class Player : MonoBehaviour
{
    private GlobalVariable<GameObject> _player = new GlobalVariable<GameObject>(GlobalVariables.PlayerCharacter);

    private void Start() => _player.Value = gameObject;
}
```
Read it anywhere else:
```cs
using UnityEngine;
using UnityTools.Messaging;

public class EnemyAim : MonoBehaviour
{
    [SerializeField] private GlobalVariable<GameObject> _player = new GlobalVariable<GameObject>(GlobalVariables.PlayerCharacter);

    private void Update()
    {
        if (_player.HasValue) transform.LookAt(_player.Value.transform);
    }

    private void OnEnable() => _player.OnUpdated += OnPlayerChanged;
    private void OnDisable() => _player.OnUpdated -= OnPlayerChanged;

    private void OnPlayerChanged(object sender, GameObject player)
    {
        // react to the new player
    }
}
```
- Every `GlobalVariable` with the same tag shares the value, and `OnUpdated` fires on all of them whenever it's set.
- A value passed to the constructor (`new GlobalVariable<int>(GlobalVariables.Score, 0)`) is a starting value: it's only used if the tag has no value yet, so spawning another object with the same field doesn't reset it.
- `HasValue` is false when the tag is unset, the value is null, or the object was destroyed. Reading a value as the wrong type logs an error and returns the default.
- `GlobalVariableService.Instance` has `Get<T>(tag)`, `Set(tag, value)`, `HasValue(tag)`, `Remove(tag)`, `Clear()` and a `ValueChanged` event. Remove and Clear notify listeners with the default value (null / 0). Everything resets when Play starts.
- In the Inspector a `GlobalVariable` field shows its tag and value, read-only (the starting value in edit mode, the live value in Play).

## Data Reference
Wraps a ScriptableObject asset so the game can change a copy of its values without editing the asset.
```cs
using UnityEngine;

[CreateAssetMenu(menuName = "Enemy Stats")]
public class EnemyStats : ScriptableObject
{
    public int Health = 100;
}
```
```cs
using UnityEngine;
using UnityTools.Data;

public class Enemy : MonoBehaviour
{
    [SerializeField] private DataReference<EnemyStats> _stats;

    private void Start()
    {
        if (!_stats.HasAsset) return;        // optional: skip the "no asset assigned" error
        _stats.Runtime.Health -= 10;         // changes this enemy's copy, not the asset
        Debug.Log(_stats.Persistent.Health); // the asset's value is unchanged
    }
}
```
- **Runtime** is this field's own copy, made the first time it's read in Play mode. Outside Play mode it's the asset itself. If the assigned asset changes in Play, the next read makes a fresh copy.
- **Each field gets its own copy**, including each instance of a prefab. Two fields that point at the same asset don't share changes: to show the player's health on a HUD, have the HUD read the player's DataReference (or use Persistent if the data never changes at runtime).
- **Persistent** is the asset. Changes made to it in Play mode stay in the asset after you stop.
- **ResetRuntime()** throws the copy away; the next read of Runtime makes a fresh copy from the asset. All copies are destroyed when Play mode ends.
- With no asset assigned, Runtime and Persistent log one error and return null. **HasAsset** checks without the error.
- In the Inspector the field has a **New** button that creates and assigns a new asset. In Play mode a **Runtime copy** foldout shows the copy's live values, and you can edit them. Inspectors drawn with IMGUI (custom editors, Odin) show only the field and the New button.

---

# Debugging and playtesting

## Debug Menu
An in-game debug menu where every feature adds its own section of buttons, toggles, sliders, dropdowns and live readouts. It's created automatically in the Editor and in Development builds (and in release builds with the `DEBUG_MENU` scripting define), so there's nothing to place in your scenes. Open it with **F5**, or **Select + Start** on a gamepad.
```cs
using UnityEngine;
using UnityTools.DebugTools;

public class Player : MonoBehaviour
{
    public enum Weapon { Pistol, Shotgun }

    private bool _godMode;
    private float _speed = 5f;
    private int _lives = 3;
    private Weapon _weapon;
    private int _health = 100;

    private void OnEnable() => DebugMenu.Register(this, "Player", section => section
        .Button("Heal", () => _health = 100)
        .Toggle("God mode", () => _godMode, value => _godMode = value)
        .Slider("Speed", 0f, 20f, () => _speed, value => _speed = value)
        .SliderInt("Lives", 0, 9, () => _lives, value => _lives = value)
        .Dropdown("Weapon", () => _weapon, value => _weapon = value)   // any enum
        .Readout("Health", () => $"{_health}/100"));

    private void OnDisable() => DebugMenu.Unregister(this);
}
```
- The section's build callback runs once, when you register: labels and dropdown options are fixed, and only the values are re-read while the menu is open. To change the controls, Unregister and Register again. Sections with the same title are shown together.
- In builds without the menu, the Register and Unregister calls (and their lambdas) are compiled out.
- The game keeps running while the menu is open, unless **Pause While Open** is on. The cursor is freed while it's open and restored when it closes.
- A **Game** section adds FPS, time scale, reset time scale and reload scene (scenes in the build only).
- **Settings:** Project Settings > Unity Tools > Debug Menu: **Toggle Action** (default F5 / Select + Start), **Start Open**, **Pause While Open**, **Block Gameplay Input**, **Show Game Section**, **Refresh Interval** (seconds). Saved in `ProjectSettings/UnityToolsDebugMenu.asset` and copied into builds that include the menu. Changes apply from the next Play. Android and WebGL builds can't read the copy and use the defaults.
- **Block Gameplay Input** turns off `PlayerInput` components only. Input actions you use directly (an InputActionAsset or a generated C# class) aren't blocked.
- **Restyling:** import the **Debug Menu** sample from the Package Manager and place its prefab in a scene; it replaces the automatic menu in that scene. A custom UXML needs a `debug-panel` element and a `debug-sections` ScrollView.
- **From tests and the command line:** `DebugMenuJson.List(section)` and `DebugMenuJson.Do(section, label, value)` read and drive every control. With the Pipeline package installed, the same works from the CLI in Play mode:
  ```
  unity command debugmenu list [--section Player]
  unity command debugmenu do --section Player --label "God mode" --value true
  ```
  Toggles take true/false/1/0/on/off, dropdowns take the option text or its index, buttons need no value, and readouts can't be set.
- The old `[DebugCommand]` attribute and `DebugMenuSystem.Instance.RegisterObject` still work but are obsolete: each registered object becomes a section of buttons.

## Playtest Feedback
An in-game feedback tool for playtests. Players press **F8** to open a side panel (the game pauses by default), pick a category (**Bug**, **Feedback**, **Idea**, or your own), write their feedback, and snip part of the screen and draw on it. The report goes to a **Discord channel** through a webhook, so there's no server to host. Every report includes the game version, date, platform, OS, hardware, FPS, playtime, scene, error counts, recent logs, and any game state you add.

### Setup
1. In Discord, create a **Forum channel** for feedback (each report gets its own thread). Channel settings > Integrations > Webhooks > New Webhook > Copy Webhook URL. Optionally create forum tags such as *Bug*, *Feedback* and *Idea*, and leave "Require people to select tags when posting" off. A normal text channel also works if you untick **Post To Forum Channel** in the settings.
2. In Unity: Project Settings > Unity Tools > Playtest Feedback (or Tools > UnityTools > Playtest Feedback > Open Settings). Paste the webhook URL and click **Send Test Message**.
3. Enter Play mode and press **F8**. The tool creates itself at startup, so there's nothing to add to your scenes.

Settings are saved in `ProjectSettings/UnityToolsPlaytestFeedback.asset`, **including the webhook URL**, and copied into builds that include the tool. An old `Assets/Resources/PlaytestFeedbackSettings.asset` from the standalone package is copied over once and deleted.

### What a report looks like in Discord
| Part | Contents |
|---|---|
| Thread title (forum channels) | `🐞 [Bug] First line of the feedback` |
| Embed | Full feedback text, player name, category colour, and fields for version, scene, playtime, platform, OS, GPU, resolution, FPS and error counts, plus your game context (when **Show Game Context In Embed** is on) |
| `screenshot.jpg` | The annotated snip, shown inline in the embed |
| `log.txt` | The last log lines (300 by default, **Log Lines**), with stack traces for errors. Only with **Include Logs** on |
| `context.json` | Everything above as JSON, including all the game context values |

### Controls
| Where | Input | Action |
|---|---|---|
| Game | F8 / L3 + R3 (configurable) | Open or close the panel |
| Panel | Ctrl (or Cmd) + Enter | Send |
| Panel | Esc | Close (the draft is kept) |
| Snip | Drag | Select an area |
| Snip | Enter | Use the full screen |
| Snip / Draw | Esc / gamepad B | Cancel |
| Draw | Enter | Done |
| Draw | Ctrl + Z | Undo |

The hotkey is the **Toggle Action** setting, a regular Input System action with the usual binding editor (Listen, several bindings, combos such as Ctrl + F8). With no bindings it uses F8 and L3 + R3.

### Adding game state
Implement `IFeedbackContextProvider` on any component, or add single values with a lambda. Values are read once when the panel opens, before the game is paused. See the **Playtest Feedback Game Context** sample in the Package Manager.
```cs
using System.Collections.Generic;
using UnityEngine;
using UnityTools.PlaytestFeedback;

public class PlayerFeedbackContext : MonoBehaviour, IFeedbackContextProvider
{
    [SerializeField] private int _seed;

    private void OnEnable()
    {
        FeedbackManager.RegisterProvider(this);
        FeedbackManager.AddContext("Seed", () => _seed);
    }

    private void OnDisable()
    {
        FeedbackManager.UnregisterProvider(this);
        FeedbackManager.RemoveContext("Seed");
    }

    public void CollectFeedbackContext(IDictionary<string, object> context)
    {
        context["Player Position"] = transform.position;
    }
}
```
- `AddContext` with an existing key replaces it. Providers run after `AddContext` values, so a provider can overwrite a key. A getter that throws is recorded as `<error: …>`.
- Values can be strings, numbers, bools, enums, DateTime, vectors, quaternions, colours, lists and dictionaries.

### Scripting API
```cs
using UnityTools.PlaytestFeedback;

FeedbackManager.Open();                 // e.g. from a "Send Feedback" button in your pause menu
FeedbackManager.Close();
FeedbackManager.IsOpen;
FeedbackManager.Opened    += ...;       // pause systems that ignore Time.timeScale
FeedbackManager.Closed    += ...;
FeedbackManager.Submitted += report => ...;
FeedbackSettings.WebhookUrlOverride = "..."; // use a webhook from somewhere other than the settings
FeedbackSettings.Current;               // the settings in effect
```
- `Open()` does nothing if the tool isn't running, and the panel opens at the end of the frame, so `IsOpen` isn't true straight away.
- `Submitted` also fires when a report was saved to the outbox instead of sent.
- To place the tool yourself, add **UnityTools > Playtest Feedback Manager** to a scene and turn off **Auto Create**. A placed manager runs regardless of Enabled In Editor, Development Builds Only and `PLAYTEST_FEEDBACK_DISABLED`.

### While the panel is open
- `Time.timeScale` is set to 0 (**Pause Time**) and audio is paused (**Pause Audio**). Both are restored as soon as the panel closes.
- The cursor is unlocked and shown, then restored.
- **Every enabled Input System action is disabled** (**Disable Game Input**), gameplay and UI maps alike. List exceptions such as push-to-talk in **Action Maps To Keep Enabled**.
- **The game's EventSystems are disabled** (their selection is remembered). The panel runs on its own private EventSystem and UI actions, so menus behind it get no clicks, navigation or submit. Turn off **Isolate UI Input** to share the game's EventSystem.
- Input and EventSystems come back one frame after closing, so the Esc that closed the panel doesn't also open your pause menu.

Code that reads devices directly (`Keyboard.current.spaceKey.wasPressedThisFrame`) bypasses actions, so it can't be blocked. Check `FeedbackManager.IsOpen` there.

### Reliability
- If a report can't be sent (offline, Discord down, rate limited), it's saved under `Application.persistentDataPath/PlaytestFeedback/Outbox` (**Queue Failed Submissions**) and sent a few seconds after the next launch. Tools > UnityTools > Playtest Feedback > Open Outbox Folder opens it.
- With no valid webhook set, reports are saved to the outbox and the player is told; they're sent once a webhook is set.
- A report Discord rejects outright (bad webhook, invalid tag) shows an error and keeps the draft. A queued report Discord rejects is renamed to `.failed` and not retried.

### Forum tags per category
Each category has an optional **Forum Tag Id**; when it's set, forum posts are tagged automatically. Discord doesn't show tag IDs in the app; read them from the channel's `available_tags` with a bot token:
```bash
curl -H "Authorization: Bot <token>" https://discord.com/api/v10/channels/<forum channel id>
```
You don't need tags: the category is always in the post title (`[Bug] …`), so Discord search works without them.

### Security and privacy
- The webhook URL is saved in `ProjectSettings/`, which is **committed with your project**: if the repository is public, anyone can find it. It's also stored in builds, where **anyone who has a build can extract it**. Use **one webhook per playtest round** and delete it in Discord when the round ends, or immediately if it leaks. To keep it out of the repository, leave it empty and set `FeedbackSettings.WebhookUrlOverride` at runtime instead.
- Player text can never ping `@everyone` or roles (`allowed_mentions` is empty).
- Screenshots may show things like usernames. The panel tells players what gets sent.
- **Development Builds Only** keeps the tool and its webhook out of release builds. The `PLAYTEST_FEEDBACK_DISABLED` scripting define turns the tool off wherever it's set, the Editor included if it's in Player Settings.

### Limits
- Attachments are capped at 9 MB (Discord's limit is about 10 MB). Screenshots are JPG (**JPG Quality**, default 85) scaled to at most 1920 px (**Max Image Size**). If the total is still too big, the log is dropped.
- An embed holds at most 6000 characters, so long text or many context values are trimmed in the post. The full data is always in `context.json`. **Max Text Length** limits what players can type (default 3000).
- On Android and WebGL, builds can't read the settings copy from StreamingAssets, so no webhook is set: set `FeedbackSettings.WebhookUrlOverride` at startup there.

### Troubleshooting
| Problem | Fix |
|---|---|
| Nothing happens on F8 | In the Editor, **Auto Create** and **Enabled In Editor** must both be on. In a build, it also needs a Development build (or Development Builds Only off) and no `PLAYTEST_FEEDBACK_DISABLED`. A custom Toggle Action replaces F8. |
| Game menus react behind the panel | Make sure **Disable Game Input** and **Isolate UI Input** are on, and **Action Maps To Keep Enabled** doesn't list your UI map. |
| Panel opens but can't be clicked | If the project has a `PanelInputConfiguration` that stops UI Toolkit from using the EventSystem, turn off **Isolate UI Input** and add your UI map to **Action Maps To Keep Enabled**. |
| Input stops working while paused | Input System *Update Mode* set to *Process Events In Fixed Update* doesn't process input when `timeScale` is 0. Use Dynamic Update, or turn off **Pause Time**. |
| `400` error mentioning `thread_name` | The webhook isn't in a Forum channel. Untick **Post To Forum Channel**. |
| `404 Unknown Webhook` | The webhook was deleted. Create a new one and update the settings. |

---

# Scenes

## Streaming Scene Manager
Keeps one set of scenes loaded at a time, for levels split into areas. Create one with GameObject > Scene Management > Streaming Scene Manager in a scene that stays loaded, list the scenes that are always loaded under **Persistent** and the first set under **Initial**, and add **Streaming Scene Transition** triggers (GameObject > Scene Management > Streaming Scene Transition) that load a set when the player walks in.
```cs
using UnityTools.Scenes;
using UnityEngine;

public class LoadingScreen : MonoBehaviour
{
    private void Start()
    {
        StreamingSceneManager.Instance.SetLoadStarted += set => gameObject.SetActive(true);
        StreamingSceneManager.Instance.SetLoaded += set => gameObject.SetActive(false);
        // IsLoading, Progress (0-1) and CurrentSet are there for loading bars.
    }
}

// elsewhere: StreamingSceneManager.Instance.LoadSet("Cave", "Cave Lighting");
```
- `LoadSet` loads the new set first, then unloads every other scene except the Persistent ones and the manager's own scene. **Unload Unused Assets** (on by default) frees memory afterwards.
- Calling `LoadSet` while a set is loading switches to the newest set as soon as the current scene finishes loading or unloading; sets asked for in between are skipped. Asking for the set that's already loaded or loading does nothing, so a trigger entered several times loads once.
- Scenes are loaded by name and must be in Build Settings. A scene that isn't is skipped with an error.
- **Disable Instead Of Unload** hides scenes by deactivating their root objects. Showing a scene again re-activates only the objects that were active when it was hidden.
- A transition loads its **Override On Enter** set when an object tagged **Tag Filter** (default "Player") enters its trigger. Unity only sends trigger events when the player has a Rigidbody or CharacterController. New transitions go on the "Trigger" layer if the project has one, otherwise on Ignore Raycast.
- A second Streaming Scene Manager destroys itself.

## Additive Scene Manager
Loads a list of scenes together, in the editor and at runtime. Create one with GameObject > Scene Management > Additive Scene Manager.
- Type a name in **New Scene Name** and press **Add** to create a scene next to the active one (named `<ActiveScene>-<Name>.unity`) and add it to the list. If that file already exists, it offers to add the existing scene instead.
- Pick a scene in **Existing Scene** and press **Add** to add a scene that already exists.
- In the editor, the list opens automatically whenever a scene containing the manager is opened. **Load Scene List** opens it by hand.
- In Play mode the list loads on Awake, by scene name, so the scenes must be in Build Settings (the list's dropdowns warn when one isn't).
- GameObject > Scene Management > Add Empty Scene adds a new empty scene alongside the open ones.

## Prefab Spawner
Stands in for a prefab in a scene and spawns it at runtime, which keeps prefab instances from collecting accidental overrides. Create one with GameObject > Prefab Spawner, or turn placed prefab instances into spawners with **GameObject > Replace with Prefab Spawner** (also in the Hierarchy right-click menu).
- **Spawn On Start** (on), **Parent Mode** (None, Same Scene (default), Self, Same Parent), **Destroy Spawner** (on; Play mode only, never with Parent Mode Self) and **Auto Name** (renames the spawner to "<Prefab> Spawner").
- Replace works on every selected prefab instance as one Undo step, keeping each one's position, rotation, parent and place in the hierarchy. Prefab overrides can't be kept by a spawner, so instances with overrides are listed first: replace anyway, skip them, or cancel.
- The prefab spawns at the spawner's position and rotation and keeps its own scale; the spawner's scale is ignored.
- The Scene view preview is drawn with the **Preview Material** tinted by **Color**, or as tinted gizmos when no material is set. The default preview material uses an HDRP Shader Graph: in URP, clear it to use the gizmo preview.
- From code: `Spawn()`, `Spawn(prefab)` and `SetPrefab(prefab)`.

## Hide Children
A component that hides an object's children (active and inactive) from the Hierarchy and Inspector, and optionally its own components, on scene objects or prefabs. Add it with Add Component.
- **Is Hidden** (on), **Hide Components** (off; hides the object's own components except Transform and Hide Children) and **Show In Prefab Mode** (on).
- It only adds or removes its own hide flags, so other flags on the children are kept. Removing the component in edit mode unhides everything.
- With Show In Prefab Mode on, the children are shown while the prefab is open in prefab mode, without changing the saved Is Hidden value.
- From code, use `SetHidden(bool)`: setting the `IsHidden` property alone doesn't update the children.

## Enable Objects Sequential
Spreads out the cost of enabling many objects. Add it with Add Component: on Awake it disables its objects (its direct children if the list is empty), then re-enables **Enabled Per Step** (5) of them every **Step Time** seconds (0 = every frame).

---

# Level design

## Projection Scatter
Scatters prefabs onto the surfaces below a box, driven by a mask you paint. Groups can be duplicated and have their position, shape, density and prefabs changed after placement.
1. GameObject > Projection Scatter Tool (or the Hierarchy right-click menu) creates a scatter pointing down. The overlay's **Create Projection Scatter** button does the same when nothing is selected.
2. Size and position the box with its handles, and fill the **Prefabs** list.
3. Pick the **Projection Scatter** tool in the Scene view's tools overlay. Left click paints the mask, Ctrl + left click erases. The **Projection Scatter Overlay** has Reset Mask, Flood Mask, Radius, Strength, Falloff and dot display settings.

- **Its children are generated.** With **Auto Bake** on (default), every change (moving it, painting, editing settings) regenerates them, and **anything you place under it is destroyed**. With Auto Bake off, press **Bake**. **Clear** and **Bake** always replace all children. Keep hand-placed objects elsewhere.
- Paint strokes, Reset/Flood Mask, Clear and Bake are each one Undo step.
- Settings: Seed, Resolution (mask grid, 4-32), Density, Size, Projection Mask (Default layer), Jitter, Weight Cutoff and Scale Multiplier. Each Prefabs entry has its own Weight, Normal Filter, Position Offset, Rotation Range and Scale Range. Position Offset's mode defaults to **None**, which ignores the offset values.
- If any Prefabs entry has an empty Prefab slot, nothing is scattered.
- Placed prefabs keep their prefab connection and behave normally in Play mode. The scatter itself only works in edit mode. Inside a prefab instance it doesn't regenerate (the children belong to the prefab); edit it in prefab mode or unpack the instance.
- From code: `ProjectionScatter.Create()`, `Bake()`, `Clear()` and `Flood(value)`.

## Physics Scatter
Drops, throws and settles the selected objects with physics in edit mode, so props land naturally on the ground and on each other. Select objects, then pick **Physics Scatter** in the Scene view's tools overlay.

| Key | Action |
|---|---|
| Space | Start / stop the simulation |
| G | Toggle gravity (starts off) |
| M | Move the selection to the mouse (over scene geometry) |
| C | Copy the selection to the mouse (over scene geometry) |
| W / E / R | Move, rotate or scale the selection with the tool's own handles |

- With gravity off, the selection is pulled towards the move handle (Move Force and drag in the overlay's Physics Settings). **Launch** starts the simulation, turns gravity on and throws the selection (Launch Mode: Random, Sphere or Custom). **Randomize in Bounds** scatters it inside the box handle. **Remain Upright** keeps objects standing.
- While simulating, every other Rigidbody in the open scenes is frozen and the selected objects get a free Rigidbody (added if they don't have one). The objects need colliders. Objects that fall below **Reset Height** are put back above it.
- Stop puts every Rigidbody back exactly as it was, removes the Rigidbodies it added, restores the project's physics simulation mode, and records where the objects ended up as one Undo step.
- The simulation runs in real time; Time Step and Step Count set its accuracy. Entering Play mode, recompiling or switching tools stops it first.
- Copy (C) duplicates like Edit > Duplicate, keeping scale, prefab overrides and components.
- The overlay settings reset when scripts recompile.

## Placement Graph
Places prefabs procedurally with a node graph, in the spirit of Unreal's PCG: nodes sample points in a volume, weight and filter them, adjust them, then spawn prefabs on what's left. Graphs are edited in Unity's Graph Toolkit window and run by a **Placement Graph** component in the scene.
1. Create a graph with **Assets > Create > UnityTools > Placement Graph** and double-click it to edit. Create a **Prefab Palette** (Assets > Create > UnityTools > Prefab Palette) and fill it with weighted prefabs.
2. Add nodes and wire them, for example Surface Sampler > Noise Density > Density Filter > Transform Points > Spawn Prefabs, and set the Spawn Prefabs node's Palette. Save the graph.
3. Create a **GameObject > Placement Graph**, assign the graph, and size the box with its handles. With **Auto Generate** on (default), it regenerates whenever the graph is saved, a palette changes, or it moves, resizes or has its settings changed. **Generate** and **Clear** are each one Undo step.

- **Its children are generated.** Generate, Clear and every automatic run replace all of them, so **anything you place under it is destroyed**. Keep hand-placed objects elsewhere. The two exceptions are the objects set as **Markers** and **Spline**, which are kept when they are direct children.
- Results are deterministic: the same graph, **Seed** and volume always give the same placement, and each node draws its own random numbers, so changing one node doesn't reshuffle the others.
- Placed prefabs keep their prefab connection. Inside a prefab instance it doesn't regenerate (the children belong to the prefab); edit it in prefab mode or unpack the instance.
- The inspector lists every node with the number of points it produced on the last run. **Show** draws that node's points in the Scene view (red to green by density, with their normals).
- Nodes, grouped in the graph window's add-node menu under Sources, Density, Filters, Points and Spawners:

| Node | What it does |
|---|---|
| Volume Sampler | A jittered grid of points (Spacing, Jitter) on the top face of the volume. |
| Surface Sampler | Drops points straight down through the volume onto colliders (Layer Mask) and keeps the hits with their surface normals. Makes its own grid (Spacing, Jitter), or projects the points wired into it. |
| Noise Density | Multiplies density by world-space Perlin noise (Scale in metres, Contrast, Invert). |
| Density Filter | Keeps points by density: at or above a Threshold, or at random with density as the chance. Rejected points come out of the Rejected port. |
| Slope Filter | Keeps points whose surface is between Min Angle and Max Angle from flat. Rejected points come out of the Rejected port. |
| Transform Points | Random offset, rotation (Euler ranges) and uniform scale per point, optionally aligned to the surface normal. |
| Spawn Prefabs | Spawns a prefab from its Palette at every point, picked by weight, and passes the points on. |
| Terrain Sampler | A jittered grid (Spacing, Jitter) dropped onto the active terrains, with their height and normal. Set Layer Index (-1 is off) and Min Weight to keep only points where that terrain layer is painted. |
| Child Markers | One point per active child of the component's **Markers** Transform, at its position, rotation and scale. Use it to place things by hand-placed markers. |
| Spline Sampler | Needs the Splines package. Reads the component's **Spline** (a Spline Container). Along: a point every Spacing metres along each spline, facing along it. Inside: a grid (Spacing, Jitter) over the volume, kept inside each closed spline seen from above. |
| Height Filter | Keeps points whose world height is between Min and Max. Rejected points come out of the Rejected port. |
| Self Pruning | Keeps points at least Radius apart, keeping the densest first. Rejected points come out of the Rejected port. |
| Difference | Drops the In points within Radius of any Exclude point, for example to keep grass away from trees. Rejected points come out of the Rejected port. |
| Distance Density | Multiplies density by the distance to the nearest Target point: 1 within Inner Radius, falling to 0 at Outer Radius (Invert flips it). |
| Merge | Combines A and B into one set, A first. |
| Copy Points | Places a copy of the Pattern points at every Source point, moved, rotated and scaled by it (pattern positions are relative to the pattern's centre). Densities multiply. |

- **Settings are ports.** Every node setting (numbers, toggles, vectors, modes, layer masks and palettes) is an input port with an inline field, so you can type a value or wire something into it.
- **Parameters.** To expose a setting per component, add a variable in the graph's Blackboard (+), drag it onto the graph and wire it into the setting. Every Blackboard variable of type float, int, bool, Vector3, Prefab Palette or LayerMask is one of the graph's parameters, with the variable's value as the default; one variable can drive several settings. New variables are made **Input**, which also makes them ports when the graph is used as a subgraph; **Local** variables are parameters too but stay inside a subgraph. To change a variable's kind, right-click it in the Blackboard and use **Set Variable Scope To**.
- The Placement Graph component lists the graph's parameters under **Parameters**: tick one to override it for this instance, untick it to go back to the graph's default. From code: `SetParameter(name, value)`, `ClearParameter(name)` and `ParameterOverrides`; `SyncParameters()` matches the list to the graph after it changes (the inspector does this for you).
- **Subgraphs.** Reuse part of a graph by making it a subgraph: drag another .placegraph into the graph, or create a local subgraph from the graph window. In the subgraph, Blackboard variables of kind **Input** become the subgraph node's input ports and variables of kind **Output** its output ports. Use a Point Data variable for points (wire an Input one out to the first node, and the last node into an Output one) and value variables for settings. On import, subgraphs are inlined: every use gets its own copy of the nodes and its own random numbers. A subgraph's Input variables aren't parameters of the graph that uses it, but you can wire that graph's own parameters into the subgraph node's ports. Editing a subgraph asset reimports the graphs that use it. A subgraph can't contain itself.
- Several wires into one input are merged. Samplers stop at 250,000 points and warn, so a tiny Spacing can't freeze the editor.
- From code: `Generate()`, `Clear()`, `Evaluate()` (runs the graph without touching the scene) and the `Graph`, `Seed`, `Size`, `Markers` and `Spline` properties (`Spline` only with the Splines package). A compiled graph (`PlacementGraphAsset`) has no editor dependency.

### Authoring graphs from code
Editor scripts and AI agents can build and edit graphs with `UnityTools.Placement.Editor.PlacementGraphAuthoring`. The result is an ordinary .placegraph asset that opens in the graph window; it uses Graph Toolkit's own editing API. Node names are the titles in the graph window, and port and setting names are as shown on the nodes (matching ignores case and spaces).

```csharp
using UnityTools.Placement;
using UnityTools.Placement.Editor;

PlacementGraphHandle graph = PlacementGraphAuthoring.Create("Assets/Forest.placegraph");
PlacementNodeHandle ground = graph.AddNode("Surface Sampler").Set("Spacing", 1.5f);
PlacementNodeHandle noise = graph.AddNode("Noise Density").Set("Scale", 12f);
PlacementNodeHandle filter = graph.AddNode<DensityFilterNode>().Set("Mode", "Random");
PlacementNodeHandle spawn = graph.AddNode("Spawn Prefabs").Set("Palette", "Assets/Trees.asset");
graph.Connect(ground, "Out", noise, "In");
graph.Connect(noise, "Out", filter, "In");
graph.Connect(filter, "Out", spawn, "In");
graph.AddParameter("Spacing", 1.5f);            // an Input variable: a per-component parameter
graph.BindParameter("Spacing", ground, "Spacing");
PlacementGraphAsset compiled = graph.Save();      // writes the file and reimports it
```

- `PlacementGraphAuthoring.ListNodeTypes()` returns every node type with its category, point ports and settings (name, type, default); `DescribeNodeTypes()` gives the same as text, which is handy for an agent.
- `Open(path)` edits an existing graph: `Nodes`, `FindNode(title)`, `Get(setting)`, `Disconnect`, `RemoveNode`. `Validate()` lists what the graph window would warn about, and `Compile()` compiles in memory without saving.
- Subgraphs: in the subgraph, `AddPointInput(name)` and `AddPointOutput(name)` add the point ports and `AddParameter` the value ports; in the graph using it, `AddSubgraph(path)` adds the node, whose ports are named after those variables.
- Values convert where it's clear: numbers between numeric types, enum names (`"Threshold"`), an int for a LayerMask, three numbers for a Vector3 and an asset path for a Prefab Palette. Unknown nodes, ports or settings and mismatched types throw an exception that lists what's available.

## Snap to Floor
Press **End** (Tools > LD Shortcuts > Snap to Floor) to drop every selected object onto the first solid surface below it, as one Undo step.
- Triggers and the other selected objects are ignored, and objects sunk into the floor come up to its surface. A selected child of another selected object moves with its parent.
- The floor needs a collider on a layer raycasts can hit (not Ignore Raycast). Objects with nothing below them stay where they are.
- The key can be changed in Edit > Shortcuts (Main Menu > Tools > LD Shortcuts).

---

# Art pipeline

## Mesh Importer
Applied automatically when models are imported.
- Unreal-style collision meshes become colliders: `UCX_` (convex mesh), `UBX_` (box), `USP_` (sphere) and `UCP_` (capsule). The name must start with the prefix and an underscore (any case). The collider stays on the collision child, and its renderer and MeshFilter are removed. Convex meshes over Unity's 255-triangle limit log a warning. Colliders are rebuilt on every reimport.
- Settings: Project Settings > Unity Tools > Art Asset Import (saved in `ProjectSettings/ArtAssetImportSettings.asset`): **Generate Unreal Colliders** (on), **Generate Mesh LODs** (on), **Import Cameras** (off) and **Import Lights** (off). The last three are applied on a model's first import only, so you can change them per model afterwards.

## Material Creator
Creates materials for the school's unified Unity/Unreal art pipeline. It needs the **VFS/Uber** shader, which isn't included in this package, so it's of no use outside that pipeline.
```
Select the 3 textures (BaseColor, Normal, MHER)
Right click > Create > VFS Uber > Create Material from Textures
```
- Textures are identified by the part of their name after the last underscore: `_BaseColor` / `_Albedo` / `_Diffuse`, `_Normal` / `_NormalDX` / `_NormalGL` (GL logs a reminder to flip green) and `_MHER` / `_Mask` / `_ORM`.
- It fixes the textures' import settings (base colour sRGB, normal map type, mask linear), and saves the material next to the base colour, named from it without underscores (`Rock_Moss_BaseColor` makes `RockMoss.mat`). An existing material is never overwritten.
- **Assign Layer (Base / R-L / G-E / B-G / A-C)**, in the same menu: select VFS/Uber materials together with 3 textures. It sets that blend layer's textures and raises the material's Enabled Layers to include it.

## Vertex Painter
Paints vertex colours onto scene meshes (MeshFilter + MeshRenderer; skinned meshes aren't supported). Select the object, pick **Vertex Painter** (the brush icon) in the Scene view's tools overlay, and press **Add Vertex Painter Component** in its overlay.
- The overlay has the colour (with Base, R, G, B and A presets; Base is black with alpha 0), Radius (world units), Strength, Falloff, Reset Colors and the dot display size. Each stroke is one Undo step.
- Painted meshes are saved as mesh assets in the project; the original model is never modified.
  - **Prefab Mode:** paint is stored in the prefab and shared by all instances. Saved to `<PrefabFolder>/<PrefabName>_VertexPaint/`.
  - **Scene / prefab instance:** the first stroke creates a unique copy for that object. Saved to `<SceneFolder>/<SceneName>/VertexPaint/` (`Assets/VertexPaint/` for unsaved scenes).
- Duplicated objects get their own copy on their first stroke. Use **Make Unique** in the inspector if objects in different scenes share a painted mesh.
- After reimporting the source model, use **Rebuild from Source** to update the painted mesh; colours are kept if the vertex count is unchanged.
- Meshes painted with older versions are stored in the scene: use **Save to Asset** in the inspector to convert them.
- **Remove Painter & Restore Source Mesh** puts the original mesh back (the painted asset is kept). Painted mesh assets are never deleted automatically: Tools > Vertex Painter > Clean Up Unused Paint Meshes (or **Find Unused Paint Meshes...** in the inspector) finds unreferenced ones and moves them to the OS trash.

## Gradient Editor
Designs gradients and bakes them to textures in the editor. Create one with Create > Color Gradient Editor in the Project window, then press **Bake** in its inspector.
- Settings: Gradient (HDR), Size (default 128 x 4), Flip, Vertical, File Type (PNG, TGA or EXR), sRGB (ignored for EXR), Wrap Mode and Generate Mipmaps.
- The texture is written next to the asset with the asset's name, replacing any earlier bake, with those import settings applied. The first and last pixels are the gradient's end colours.

---

# Lighting

## Surround with Volume
Wraps the selected objects with a lighting volume sized to their bounds (mesh renderers, skinned meshes and terrains, including children). Select objects, then GameObject > Light (also in the Hierarchy right-click menu):
- **Surround with Reflection Probe** and **Surround with Probe Volume** (an Adaptive Probe Volume in Local mode), 2 units larger on every side.
- **Surround with Local Volume**: a local post-processing Volume with a trigger box, 0.5 units larger on every side and with no profile assigned.

A multi-selection makes one volume, created at the root of the selected objects' scene.

## Assign Volume to Open Scenes
HDRP only. Select a Volume with a profile, then GameObject > Light > Assign Volume to Open Scenes: its profile becomes the Static Lighting Sky of every loaded scene, and the Lighting Settings asset of the Volume's scene is copied to all of them, as one Undo step.

## Lightbake Settings
A component that applies lighting settings (static flags, Receive GI, shadow casting) to every mesh renderer and terrain under it. Add **Lightbake Settings** to a parent, pick a **Preset** (Full Static, Static No Shadows, Full Dynamic, Dynamic No Shadows, Dynamic Bake Reflections) or set the fields, and press **Apply**.
- Nothing changes until you press Apply. Apply is undoable and works on prefab instances.
- When Lightbake Settings are nested, the nearest one above an object wins, and Apply on a parent also applies the nested ones.
- Terrains get only the static flags and shadow casting mode. Skinned meshes and objects without a renderer are left untouched.

## APV Bake Checklist
A walkthrough for baking HDRP lighting with Adaptive Probe Volumes and no lightmaps. Open it with Window > Rendering > APV Bake Checklist.
- It checks the HDRP assets, frame settings, Lighting Settings, Static Lighting Sky, Baking Set, Probe Volumes, renderers, Lightbake Settings components, lights and emissive materials of every loaded scene, with **Fix** and **Select** buttons per row. **Fix All** applies every safe fix as one Undo step.
- Toolbar buttons: Refresh, Bake (probes + reflections), Bake probes only, Clear baked data, Add Adjustment Volume, and Cancel while baking. Post-bake checks confirm the baked data and zero lightmaps.
- Realtime lights are listed, never changed. Manual check ticks are remembered per scene on this computer.
- Without HDRP the window only shows a message.

---

# Editor and inspector

## Toolbar Buttons
Extra controls for Unity's main toolbar. Right-click empty space on the toolbar to turn them on:
- **Scene Selector:** a dropdown of every scene in the project (with a Build Settings submenu) to switch scenes; in Play mode it loads scenes by name, so they must be in the build. Right-click it for **Find Scene** and **Restart Scene**, which use the active scene's path. A second **Play From** dropdown picks the scene Play mode starts from (Current, or any enabled Build Settings scene).
- **Timescale Options:** a Time Scale slider (0-4) and a reset-to-1 button. They work in Play mode only, and exiting Play mode puts the time scale back, so the project's Time Scale setting never changes.
- **Compilation Options:** an **Auto Compile** dropdown for Unity's Auto Refresh mode (Disabled, Enabled, Enabled Outside Playmode) and a button that refreshes and always recompiles.

## Transform Search
Adds a search field to the Transform and RectTransform inspectors that shows matching fields from the object's other components.
- Matches field names and display names, or every field of a component whose type name matches. Arrays and lists match by their own name.
- Searching never changes your component foldouts. The search clears when the selection changes (a locked Inspector keeps it), and each Inspector window has its own search.
- Hidden components (like URP/HDRP Additional Light Data) and fields drawn only by custom inspectors aren't searched.

## Inspector Attributes
Small attributes for adding buttons to inspectors, used by these tools. For anything bigger, use Odin, Naughty Attributes or Alchemy.
```cs
using UnityTools.Attributes;
using UnityEngine;

public class Baker : MonoBehaviour
{
    // Full-width button, drawn in place of a placeholder field. true = also works outside Play mode
    [SerializeField, Button("Bake", true, Method = nameof(Bake))] private string _bakeButton;

    // Button on the same row as a field: New Scene Name [field] [Add]
    [SerializeField, InlineButton(nameof(AddNewScene), "Add")] private string _newSceneName;

    private void Bake() { }
    private void AddNewScene() { }
}
```
- `[Button]` buttons are greyed out outside Play mode unless the second argument (`allowEditMode`) is `true`. `[InlineButton]` works in edit mode by default.
- Methods must be parameterless instance methods; private methods and methods in base classes work. Buttons run on every selected object as one Undo step, after the inspector has finished drawing. A missing method shows on the button as `(!)` with the reason in its tooltip.
- `InlineButton` works alongside another drawer on the same field (e.g. a dropdown). If your own PropertyDrawer should keep the button when an inspector such as Odin picks your drawer, call `position = InlineButtonGUI.Draw(position, property, fieldInfo);` (namespace `UnityTools.Attributes.Editor`) at the start of its `OnGUI`.
- `Button` can clash with `UnityEngine.UI.Button` or `UnityEngine.UIElements.Button` in files that use those namespaces: write `UnityTools.Attributes.Button` there.

## Scene Dropdown
`[SceneDropdown]` draws a string field (or every element of a string list or array) as a searchable scene picker, grouped by folder.
```cs
using UnityTools.Scenes;
using UnityEngine;

public class LevelSelect : MonoBehaviour
{
    [SerializeField, SceneDropdown] private string _scenePath;                    // "Assets/Scenes/Level 1.unity"
    [SerializeField, SceneDropdown(storeName: true)] private string[] _levels;   // "Level 1"
}
```
- It stores the scene's asset path, or just its name with `storeName: true` (for code that loads scenes by name).
- A scene that was deleted or moved shows as **Missing**, and a name shared by several scenes is highlighted.
- A scene that isn't in Build Settings (or is disabled there) shows "(not in build)" with an **Add to Build** button, because loading it at runtime would fail.

---

## Assemblies and namespaces
Every tool's namespace and assembly start with `UnityTools.`. The runtime assemblies are auto-referenced, so scripts in the default Assembly-CSharp can use them directly. If your scripts are in their own assembly definition, add the assembly to its references.

| Tool | Namespace and assembly |
|---|---|
| Object Pooling | `UnityTools.Pooling` |
| Simple State Machine | `UnityTools.States` |
| Service Locator | `UnityTools.Services` |
| Messaging Service, Global Variables | `UnityTools.Messaging` |
| Data Reference | `UnityTools.Data` |
| Debug Menu | `UnityTools.DebugTools` |
| Playtest Feedback | `UnityTools.PlaytestFeedback` |
| Scenes, Prefab Spawner, Scene Dropdown | `UnityTools.Scenes` |
| Projection Scatter, Physics Scatter | `UnityTools.Scatter` |
| Placement Graph | `UnityTools.Placement` |
| Mesh Importer, Material Creator, Vertex Painter, Gradient Editor | `UnityTools.Art` |
| Lighting tools | `UnityTools.Lighting` |
| Inspector Attributes | `UnityTools.Attributes` |
| Snap to Floor, Transform Search | `UnityTools.EditorTools` |
| Toolbar Buttons | `UnityTools.Toolbar` |

Editor code is in `<namespace>.Editor` (e.g. `UnityTools.Scenes.Editor`). Upgrading from 6.x? See the [7.0.0 changelog](CHANGELOG.md) for the old names.
