# Unity Tools
[![Unity 6+](https://img.shields.io/badge/unity-6%2B-blue.svg)](https://unity3d.com/get-unity/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE.md)

This package comes with several useful tools focused on organization, level design, and lighting.

## System Requirements
Unity 6+. Will likely work on earlier versions but this is the version I tested with. 
Currently requires Odin Inspector for custom editor windows, it won't compile without Odin.

> [!Warning]
> As of 5.2.0 this package is being maintained with AI-assisted code. If that's not your thing feel free to fork a previous version.

> [!WARNING]
> GameEvents have been fully replaced by the MessagingService in 5.0.0, if you still want to use GameEvents use 4.5.0.

## Installation
Use the Package Manager and use Add package from git URL, using the following: 
```
https://github.com/qhenshaw/UnityTools.git
```
If you need the version with GameEvents use this version:
```
https://github.com/qhenshaw/UnityTools.git#4.5.0
```

## Included

### Gradient Editor
Customize and bake gradient textures directly in-engine.
```
Project view => Create => Color Gradient Editor
```

### Vertex Painter
Paint vertex colors onto any scene mesh.
```
Scene view => Select any GO with a mesh => Select VP (paint brush) icon in Unity's floating tools bar  
Select Add Component in tool overlay and begin painting
```
Painted meshes are saved as mesh assets in the project, the original model is never modified.
- **Prefab Mode:** paint is stored in the prefab and shared by all instances. Saved to `<PrefabFolder>/<PrefabName>_VertexPaint/`
- **Scene / prefab instance:** the first stroke creates a unique copy for that object. Saved to `<SceneFolder>/<SceneName>/VertexPaint/` (`Assets/VertexPaint/` for unsaved scenes)
- Duplicated objects get their own copy on their first stroke. Use **Make Unique** in the inspector if objects in different scenes share a painted mesh.
- After reimporting the source model use **Rebuild from Source** to update the painted mesh, colors are kept if the vertex count is unchanged.
- Meshes painted with older versions are stored in the scene, use **Save to Asset** in the inspector to convert them.
- Painted mesh assets are not deleted when the painter or object is removed. Use `Tools => Vertex Painter => Clean Up Unused Paint Meshes` to find and delete unreferenced meshes (deleted files go to the OS trash).

### Mesh Importer
Unreal style collision meshes are converted into colliders: `UCX_` (convex mesh), `UBX_` (box), `USP_` (sphere) and `UCP_` (capsule).  
The name must start with the prefix and an underscore (any case). The collider stays on the collision child and its renderer is removed.  
All meshes will have Generate Mesh LODs enabled on first import, can be disabled later.  
Settings: Project Settings => Unity Tools => Art Asset Import (saved in ProjectSettings/ArtAssetImportSettings.asset).
```
Automatically applied on import
```

### Material Creator
A materal creator for use with the unified Unity/Unreal pipeline and Uber shaders.This will be of no use outside of my classes and school art pipeline.
```
Select all 3 BaseColor/MHER/NormalMap
Right Click => Create => Art Pipeline => [Material]
```

### Data Reference
Wraps a ScriptableObject asset so the game can change a copy of its values without editing the asset. In the Inspector it shows the asset field with a "New" button that creates and assigns a new asset. In Play mode, a "Runtime copy" foldout shows the copy's live values.
```cs
using UnityTools.Data;

public class Enemy : MonoBehaviour
{
    [SerializeField] private DataReference<EnemyStats> _stats;

    private void Start()
    {
        if (!_stats.HasAsset) return;     // optional: skip the "no asset assigned" error
        _stats.Runtime.Health -= 10;      // changes this enemy's copy, not the asset
        Debug.Log(_stats.Persistent.Health); // the asset's value is unchanged
    }
}
```
- **Runtime** is this field's own copy, made the first time it's read in Play mode. Outside Play mode it's the asset itself.
- **Each field gets its own copy**, including each instance of a prefab. Two fields that point at the same asset don't share changes: to show the player's health on a HUD, have the HUD read the player's DataReference (or use Persistent if the data never changes at runtime).
- **Persistent** is the asset. Changes made to it in Play mode stay in the asset after you stop.
- **ResetRuntime()** throws the copy away; the next read of Runtime makes a fresh copy from the asset. All copies are destroyed when Play mode ends.
- With no asset assigned, Runtime and Persistent log one error and return null. **HasAsset** checks without the error.

### Debug Menu
An in-game debug menu where every feature adds its own section of buttons, toggles, sliders, dropdowns and live readouts. It's created automatically in the Editor and in Development builds (and in release builds with the `DEBUG_MENU` scripting define); there's nothing to place in your scenes. Open it with F5, or Select + Start on a gamepad.
```cs
using UnityTools.DebugTools;

public class Player : MonoBehaviour
{
    private void OnEnable() => DebugMenu.Register(this, "Player", section => section
        .Button("Heal", () => _health.Heal())
        .Toggle("God mode", () => _godMode, value => _godMode = value)
        .Slider("Speed", 0f, 20f, () => _speed, value => _speed = value)
        .SliderInt("Lives", 0, 9, () => _lives, value => _lives = value)
        .Dropdown("Weapon", () => _weapon, value => _weapon = value)   // any enum
        .Readout("Health", () => $"{_health.Current}/{_health.Max}"));

    private void OnDisable() => DebugMenu.Unregister(this);
}
```
- Sections with the same title are shown together. Readouts and values refresh while the menu is open
- In builds without the menu, the Register and Unregister calls (and their lambdas) are compiled out
- The game keeps running while the menu is open. The cursor is freed while it's open and restored when it closes
- A Game section adds FPS, time scale and reload scene
- Settings: Project Settings => Unity Tools => Debug Menu (toggle binding, start open, pause while open, block gameplay input, Game section, refresh rate). Saved in ProjectSettings/UnityToolsDebugMenu.asset and copied into builds that include the menu
- To restyle it, import the Debug Menu sample from the Package Manager and place its prefab in a scene; it replaces the automatic menu
- With the Pipeline package installed, every control is also a CLI command:
```
unity command debugmenu list [--section Player]
unity command debugmenu do --section Player --label "God mode" --value true
```

### Playtest Feedback
In-game feedback tool for playtests. Players press **F8** to pause the game and open a side panel, where they pick a category (**Bug**, **Feedback**, **Idea**, or your own), write their feedback, and snip part of the screen and draw on it. The report goes to a **Discord channel** through a webhook, so there's no server to host. Every report includes the game version, date, platform, hardware, FPS, scene, recent logs, plus any game state you add.

#### Setup
1. In Discord, create a **Forum channel** for feedback (each report gets its own thread; a normal text channel works too). Channel settings => Integrations => Webhooks => New Webhook => Copy Webhook URL. Optionally create forum tags such as *Bug*, *Feedback*, *Idea*, and leave "Require people to select tags when posting" off.
2. In Unity: Project Settings => Unity Tools => Playtest Feedback (or Tools => UnityTools => Playtest Feedback => Open Settings). Paste the webhook URL and click **Send Test Message**.
3. Enter Play mode and press **F8**. The tool creates itself at startup, so there's nothing to add to your scenes.

Settings are saved in `ProjectSettings/UnityToolsPlaytestFeedback.asset`, **including the webhook URL**, and copied into builds that include the tool.

#### What a report looks like in Discord
| Part | Contents |
|---|---|
| Title | `🐞 [Bug] First line of the feedback` |
| Embed | Full feedback text, player name, category colour, and fields for version, scene, playtime, platform, GPU, resolution, FPS and error counts, plus your custom game context |
| `screenshot.jpg` | The annotated snip, shown inline in the embed |
| `log.txt` | The last few hundred log lines, with stack traces for errors |
| `context.json` | Everything above as JSON, including all the game context values |

#### Controls
| Where | Input | Action |
|---|---|---|
| Game | F8 / L3 + R3 (configurable) | Open or close the panel |
| Panel | Ctrl + Enter | Send |
| Panel | Esc | Close (the draft is kept) |
| Snip | Drag | Select an area |
| Snip | Enter | Use the full screen |
| Snip / Draw | Esc / gamepad B | Cancel |
| Draw | Enter | Done |
| Draw | Ctrl + Z | Undo |

The **Hotkey** setting is a regular Input System action with the usual binding editor (Listen, several bindings, combos such as Ctrl + F8). With no bindings it uses F8 and L3 + R3.

#### Adding game state
Implement `IFeedbackContextProvider` on any component, or add single values with a lambda. Values are read once when the panel opens, before the game is paused. See the **Playtest Feedback Game Context** sample in the Package Manager.
```cs
using UnityTools.PlaytestFeedback;

public class PlayerFeedbackContext : MonoBehaviour, IFeedbackContextProvider
{
    private void OnEnable() => FeedbackManager.RegisterProvider(this);
    private void OnDisable() => FeedbackManager.UnregisterProvider(this);

    public void CollectFeedbackContext(IDictionary<string, object> context)
    {
        context["Player Position"] = transform.position;
        context["Health"] = GetComponent<Health>().Current;
    }
}

FeedbackManager.AddContext("Seed", () => worldGenerator.Seed);
```

#### Scripting API
```cs
FeedbackManager.Open();                 // e.g. from a "Send Feedback" button in your pause menu
FeedbackManager.Close();
FeedbackManager.IsOpen;
FeedbackManager.Opened    += ...;       // pause systems that ignore Time.timeScale
FeedbackManager.Closed    += ...;
FeedbackManager.Submitted += report => ...;
FeedbackSettings.WebhookUrlOverride = "..."; // use a webhook from somewhere other than the settings
FeedbackSettings.Current;               // the settings in effect
```

#### While the panel is open
Each of these can be turned off in the settings:
- `Time.timeScale` is set to 0 and audio is paused (`AudioListener.pause`). Both are restored on close.
- The cursor is unlocked and shown, then restored.
- **Every enabled Input System action is disabled**, gameplay and UI maps alike. Use *Action Maps To Keep Enabled* for exceptions such as push-to-talk.
- **The game's `EventSystem`s are disabled** (their selection is remembered). The panel runs on its own private `EventSystem` and UI actions, so menus behind it get no clicks, navigation or submit. Turn off *Isolate UI Input* to share the game's `EventSystem`.
- Everything is restored one frame after closing, so the Esc that closed the panel doesn't also open your pause menu.

Code that reads devices directly (`Keyboard.current.spaceKey.wasPressedThisFrame`) bypasses actions, so it can't be blocked. Check `FeedbackManager.IsOpen` there.

#### Reliability
If a report can't be sent (offline, Discord down, rate limited), it's saved under `Application.persistentDataPath/PlaytestFeedback/Outbox` and sent on the next launch. Tools => UnityTools => Playtest Feedback => Open Outbox Folder opens it. Reports Discord rejects outright (bad webhook, invalid tag) are renamed to `.failed` and not retried.

#### Forum tags per category
Each category has an optional **Forum Tag Id**; when it's set, the post is tagged automatically. Discord doesn't show tag IDs in the app; read them from the channel's `available_tags` with a bot token:
```bash
curl -H "Authorization: Bot <token>" https://discord.com/api/v10/channels/<forum channel id>
```
You don't need tags: the category is always in the post title (`[Bug] …`), so Discord search works without them.

#### Security and privacy
- The webhook URL is saved in `ProjectSettings/`, which is **committed with your project**: if the repository is public, anyone can find it. It's also stored in builds, where **anyone who has a build can extract it**. Use **one webhook per playtest round** and delete it in Discord when the round ends, or immediately if it leaks. To keep it out of the repository, leave it empty and set `FeedbackSettings.WebhookUrlOverride` at runtime instead.
- Player text can never ping `@everyone` or roles (`allowed_mentions` is empty).
- Screenshots may show things like usernames. The panel tells players what gets sent.
- Turn on **Development Builds Only**, or add the `PLAYTEST_FEEDBACK_DISABLED` scripting define, to keep the tool out of release builds.

#### Limits
- Discord allows about 10 MB of attachments per message. Screenshots are JPG and scaled to at most 1920 px (configurable). If the total is still too big, the log is dropped first.
- An embed holds at most 6000 characters, so long text or many context values are trimmed in the post. The full data is always in `context.json`.
- On Android and WebGL, builds can't read the settings copy from StreamingAssets, so no webhook is set: set `FeedbackSettings.WebhookUrlOverride` at startup there.

#### Troubleshooting
| Problem | Fix |
|---|---|
| Nothing happens on F8 | Check that *Enabled In Editor* (or *Auto Create*) is on in the settings. |
| Game menus react behind the panel | Make sure *Disable Game Input* and *Isolate UI Input* are on, and *Action Maps To Keep Enabled* doesn't list your UI map. |
| Panel opens but can't be clicked | If the project has a `PanelInputConfiguration` that stops UI Toolkit from using the `EventSystem`, turn off *Isolate UI Input* and add your UI map to *Action Maps To Keep Enabled*. |
| Input stops working while paused | Input System *Update Mode* set to *Process Events In Fixed Update* doesn't process input when `timeScale` is 0. Use Dynamic Update, or turn off *Pause Time*. |
| `400` error mentioning `thread_name` | The webhook isn't in a Forum channel. Untick *Post To Forum Channel*. |
| `404 Unknown Webhook` | The webhook was deleted. Create a new one and update the settings. |

### Custom Hierarchy Drawer
Redraws any items in the hierarchy that have ```= ``` at the start of their name with bold text and a darker background. Useful for highlighting empty category transforms. Unity's new hierarchy breaks this unfortunately.

### LD Hotkeys
Very simple at the moment with more planned for the future:  
```End``` Snap selected mesh to floor

### Surround with Light Probe Volume / Reflection Probe / Local Volume
Wraps the selected objects tightly with the selected volume type.
```
Right click objects(s) in scene and select Light => Surround with x
```

### APV Bake Checklist
A walkthrough for baking HDRP lighting with Adaptive Probe Volumes and no lightmaps. Checks the HDRP assets, frame settings, Lighting Settings, Static Lighting Sky, Baking Set, Probe Volumes, renderers, lights and emissive materials of every loaded scene, and offers a Fix and a Select button per row. Fix All applies every safe fix as one undo step. Includes bake buttons and post-bake checks (baked data, zero lightmaps). Realtime lights are listed, never changed. Requires HDRP.
```
Window => Rendering => APV Bake Checklist
```

### Transform Search Editor
Adds a search field to the Transform and RectTransform inspector, displays matching field results from GameObject components.  
Matches field names, or every field of a component whose type name matches. Arrays and lists match by their own name.  
Searching never changes your component foldouts. The search clears when the selection changes, and each Inspector window has its own search.  
Hidden components (like URP/HDRP Additional Light Data) and fields drawn only by custom inspectors are not searched.

### Inspect Attributes
Used internally to add buttons to my tools. For anything bigger, get Odin, Naughty Attributes, or Alchemy.
```csharp
// Full-width button, drawn in place of a placeholder field
[SerializeField, Button("Bake", true, Method = nameof(Bake))] private string _bakeButton;

// Button on the same row as a field: New Scene Name [field] [Add]
[SerializeField, InlineButton(nameof(AddNewScene), "Add")] private string _newSceneName;
```
Methods must be parameterless; private methods and methods in base classes work. Buttons run on every selected object as one undo step, after the inspector has finished drawing. A missing method shows on the button as `(!)` with the reason in its tooltip.  
`InlineButton` works alongside another drawer on the same field (e.g. a dropdown). If your own PropertyDrawer should keep the button when an inspector such as Odin picks your drawer, call `position = InlineButtonGUI.Draw(position, property, fieldInfo);` at the start of its `OnGUI`.

### Global Variables
Register global variables using any enum.  
Source:
```cs
[SerializeField] private GlobalVariable<GameObject> _player = new GlobalVariable<GameObject>(GlobalVariables.PlayerCharacter);

private void Start()
{
    _player.Value = gameObject;
}
```
Others referencing:
```cs
[SerializeField] private GlobalVariable<GameObject> _player = new GlobalVariable<GameObject>(GlobalVariables.PlayerCharacter);

private void Update()
{
    if(_player.HasValue)
    {
        Debug.Log(_player.Value.transform.position);
    }
}
```
Every `GlobalVariable` with the same tag shares the value, and `OnUpdated` fires on all of them whenever it is set:
```cs
private void OnEnable() => _player.OnUpdated += OnPlayerChanged;
private void OnDisable() => _player.OnUpdated -= OnPlayerChanged;

private void OnPlayerChanged(object sender, GameObject player)
{
    // react to the new player
}
```
A value passed to the constructor (`new GlobalVariable<int>(GlobalVariables.Score, 0)`) is a starting value: it is only used if the tag has no value yet, so spawning another object with the same field doesn't reset it.  
`HasValue` is false when the tag is unset, the value is null, or the object was destroyed.  
`GlobalVariableService.Instance.Remove(tag)` and `Clear()` remove values and notify listeners with the default value (null / 0).  
In the Inspector a `GlobalVariable` field shows its tag and value (the starting value in edit mode, the live value in Play).

### Messaging Service
Send global messages between objects and scenes using an enum as a tag.  
Each tag carries one payload type, set by its first listener. A listener or send with a different type logs an error naming the tag and both types. A listener that throws is logged and the other listeners still run.  
A listener only receives messages sent after it was added, unless the sender passes `sendToLate: true` (see below).  
Listeners on destroyed objects are removed with a warning, but remove your listeners in `OnDisable` anyway.  
Sender:
```cs
private void Start()
{
    MessagingService.Instance.Send(GlobalMessages.PlayerSpawned, gameObject);
}
```
Listener:
```cs
private void OnEnable()
{
    MessagingService.Instance.AddListener<GameObject>(GlobalMessages.PlayerSpawned, OnPlayerSpawned);
}

private void OnDisable()
{
    MessagingService.Instance.RemoveListener<GameObject>(GlobalMessages.PlayerSpawned, OnPlayerSpawned);
}

private void OnPlayerSpawned(GameObject gameObject)
{
    // cache the reference here
}
```
Late listeners: pass `sendToLate: true` and listeners added later get the last payload as soon as they are added (e.g. a HUD enabled after the player spawned). Once a tag has a remembered payload, every later send to it replaces it. `ClearSticky(tag)` forgets it.
```cs
MessagingService.Instance.Send(GlobalMessages.PlayerSpawned, gameObject, sendToLate: true);
```
No payload: listen without caring about the payload (fires on every send to the tag), or send without one (only on tags with no typed listeners).
```cs
MessagingService.Instance.AddListener(GlobalMessages.PlayerDied, PlayDeathSound);
MessagingService.Instance.Send(GlobalMessages.LevelLoaded);
```
Sending doesn't allocate, as long as the call doesn't name a single type argument: `Send(tag, payload)` is allocation-free, `Send<DamageInfo>(tag, payload)` boxes the tag (name both types, `Send<GlobalMessages, DamageInfo>`, or none).  
Debugging: Tools > UnityTools > Messaging Debugger lists every tag with its payload type, listeners and remembered payload, and every global variable with its value.

### Object Pooling
A simple object pooling solution for GameObjects.  
Your pooled prefab has a component inheriting from PooledObject. Reference the prefab by that component type and get instances from the pool instead of instantiating them:
```cs
[SerializeField] private Bullet _bulletPrefab;

Bullet bullet = PoolSystem.Instance.Get(_bulletPrefab, position, rotation);
```
Return it to the pool when finished, instead of destroying it (optionally after a delay, like `Destroy(gameObject, 2f)`):
```cs
bullet.ReturnToPool();
bullet.ReturnToPool(2f);
```
Instances are placed before they're activated, so `Awake` and `OnEnable` see the spawn position. A reused instance keeps its old state (velocity, health, trails), so reset it in the pool hooks:
```cs
public class Bullet : PooledObject
{
    protected override void OnTakenFromPool() { /* runs after it's placed and active */ }
    protected override void OnReturnedToPool() { /* runs while it's still active */ }
}
```
- **Prewarm Count**: instances created when the pool is first used, so the first shots don't instantiate mid-game
- **Max Size**: most instances kept waiting in the pool; extras are destroyed when returned (0 = unlimited)
- Pools belong to the scene they were created in and are destroyed with it. Place a PoolSystem in a scene to prewarm prefabs as soon as the scene loads; otherwise one is created on first use
- `ReturnToPool()` on an object that didn't come from a pool (placed in the scene, or whose pool was unloaded) destroys it
- Destroying a pooled object (or its parent) logs an error once per prefab, since the pool can't reuse it. If your PooledObject has its own `OnDestroy`, override it and call `base.OnDestroy()`

### Runtime Mesh Combine
A situationally useful optimization tool. Add to any GameObject root in your scene and it will automatically combine meshes on Start.  
> [!NOTE]
> With SRP Batcher and GPU Resident Drawer this likely won't result in any measurable performance increase. Always test.

### Projection Scatter Tool
Allows for non-destructive rapid prefab placement in scenes.  
Groups can be duplicated and have their position, shape, density, and prefabs customized after placement.  
```
Scene view => Right click => Projection Scatter Tool  
Size and position volume and add then customize Prefabs list
Select PS tool in Unity's floating tools bar  
Paint weights on volume and prefabs will automatically position themselves in scene
```
Placed prefabs retain prefab connection in scene and are spawned as-is, all physics/collision/components will behave as normal during play.

### Additive Scene Manager
A scene management component that will automatically load groups of scenes in editor and at runtime.  
```
Scene view => Right click => Scene Management => Additive Scene Manager
Type in a name and click Add New Scene to automatically create a scene and add it to the list
Pick a scene from the Existing Scene dropdown and click Add Existing Scene to add a scene that already exists
```
Scene List entries are dropdowns of the project's scenes; entries that point at a deleted or moved scene show as Missing.

Every scene dropdown in UnityTools also checks Build Settings. A scene that isn't in the build (or is disabled there) shows "(not in build)" with an **Add to Build** button, because loading it at runtime would fail.

### Streaming Scene Manager
Keeps one set of scenes loaded at a time, for levels split into areas. Put the manager in a scene that stays loaded, list the scenes that are always loaded under **Persistent** and the first set under **Initial**, and add **Streaming Scene Transition** triggers (GameObject > Scene Management) that load a set when the player walks in.
```cs
using SceneManagement;

StreamingSceneManager.Instance.LoadSet("Cave", "Cave Lighting");
StreamingSceneManager.Instance.SetLoadStarted += set => _loadingScreen.Show();
StreamingSceneManager.Instance.SetLoaded += set => _loadingScreen.Hide();
// IsLoading, Progress (0-1) and CurrentSet are there for loading bars.
```
- `LoadSet` loads the new set first, then unloads every other scene except the Persistent ones and the manager's own scene.
- Calling `LoadSet` while a set is loading switches to the newest set as soon as the current scene finishes loading or unloading; sets asked for in between are skipped. Asking for the set that's already loaded or loading does nothing, so a trigger entered several times loads once.
- Scenes are loaded by name and must be in Build Settings. A scene that isn't is skipped with an error.
- **Disable Instead Of Unload** hides scenes by deactivating their root objects. Showing a scene again re-activates only the objects that were active when it was hidden.
- New transitions go on the "Trigger" layer if the project has one, otherwise on Ignore Raycast.

### Hide Children
A component that can hide child objects and components on scene objects or prefabs.  
Hides inactive children too, and keeps any other hide flags they have. With **Show In Prefab Mode** on, the children are shown while the prefab is open in prefab mode, without changing the saved **Is Hidden** value.

### Light Bake Settings
A component that can apply lighting settings to all meshes and terrains in scene under a parent.  
When nesting objects deeper component settings are used. Skinned meshes and objects without a renderer are left untouched. Apply is undoable and works on prefab instances.

### Prefab Spawner
Allows for quickly swapping in-scene prefabs with a spawner object.  
Helps prevent accidental overrides and forces better editing practices.
```
Right click prefab in scene => Replace with Prefab Spawner
```
- Replace works on every selected prefab instance as one Undo step, keeping each one's position, rotation, parent and place in the hierarchy. Prefab overrides can't be kept by a spawner, so instances with overrides are listed first: replace anyway, skip them, or cancel.
- The prefab spawns at the spawner's position and rotation and keeps its own scale; the spawner's scale is ignored.
- The Scene view preview is drawn with the **Preview Material** in the Preview colour, or as tinted gizmos when no material is set.

### Service Locator
A simple service locator: finds the one instance of a class (an AudioManager, a ScoreManager) without a direct reference.  
Use it for "the one X" found by type; use Global Variables for values found by tag (e.g. the current player).  
Register in **Awake**, and unregister when destroyed:
```cs
using ServiceAccess;

public class AudioManager : MonoBehaviour
{
    private void Awake() => ServiceLocator.Register(this);
    private void OnDestroy() => ServiceLocator.Unregister(this);
}
```
Get it in **Start** or later, so every service has registered first:
```cs
using ServiceAccess;

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
- Services survive scene loads. A destroyed service counts as unregistered, so the next scene's manager can register in its place
- Registering a second live instance of the same type logs an error and keeps the first
- `Get<IDamageable>()` finds a service registered as exactly that type, otherwise the one registered service that implements it (an error if there are several). Register under an interface with `ServiceLocator.Register<IDamageable>(this)`
- Everything is cleared when Play starts. Registered services show in Tools > UnityTools > Messaging Debugger

### Simple State Machine
A very simple C# driven state machine.  
Bind the StateMachine to a class and add transitions to your States.
Call Update() and FixedUpdate() on it for use in States.
- Each state type has **one instance per machine, reused every time it's entered**. Reset any fields the state changes in `Enter()`
- When several transitions are true at once, the one **added last** wins. Transitions from any state are checked first
- `TimeInState` is the seconds since the state was entered, so you don't need your own timers
- `AddTransition` returns a handle; pass it to `RemoveTransition(handle)` to remove that transition
```cs
using System;
using SimpleStateMachine;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [field: SerializeField] public Character Target { get; private set; }
    public bool HasTarget => Target != null && Target.IsAlive;

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

    private void OnDrawGizmos()
    {
        // editor only: shows the current state above the enemy in the Scene view
        _stateMachine?.DrawDebugLabel(transform.position + Vector3.up * 2f);
    }

    private void FixedUpdate()
    {
        _stateMachine.FixedUpdate(Time.fixedDeltaTime);
    }

    private void Update()
    {
        _stateMachine.Update(Time.deltaTime);
    }
}
```
```cs
using SimpleStateMachine;

public class ChaseState : State<EnemyController>
{
    // no constructor needed: Binding and StateMachine are set before Enter

    public override void Enter()
    {
        // reset anything this state changes, since the same instance is reused
    }

    public override void Update(float deltaTime)
    {
        if (Binding.HasTarget)
        {
            // nagivate to Target
        }
        else
        {
            // stop movement
        }
    }
}
```

### Toolbar Buttons
New options have been added to Unity's toolbar (left/right of Play/Pause/Step).  
Right click on empty space on the toolbar to enable:  
```
Scene Selector  
TimeScale Options  
Compilation Options  
```