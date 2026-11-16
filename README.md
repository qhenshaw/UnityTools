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
Settings: Project Settings => [Custom] Art Asset Import Settings (saved in ProjectSettings/ArtAssetImportSettings.asset).
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
This adds an inspector button to create and assign a new ScriptableObject asset.  
```
Wrap any ScriptableObject in a serialized DataReference<YourType>
```
Access the SO through the Persistent or RunTime properties, RunTime automatically creates a unique instance of the SO.

### Debug Menu
This menu will automatically create debug buttons for all matching registered function/object pairs and call them simultaneously. Customize the prefab in the inspector, the default open button is tilde (~).
```
Add the sample Debug Menu prefab from the package manager  
Add the [DebugCommand] attribute to any instance methods  
Add to menu through DebugMenuSystem.Instance.RegisterObject  
Remove from menu through DebugMenuSystem.Instance.DeregisterObject  
```

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
Debugging: Window > UnityTools > Messaging Debugger lists every tag with its payload type, listeners and remembered payload, and every global variable with its value.

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

### Hide Children
A component that can hide child objects and components on scene objects or prefabs.

### Light Bake Settings
A component that can apply lighting settings to all meshes and terrains in scene under a parent.  
When nesting objects deeper component settings are used. Skinned meshes and objects without a renderer are left untouched. Apply is undoable and works on prefab instances.

### Prefab Spawner
Allows for quickly swapping in-scene prefabs with a spawner object.  
Helps prevent accidental overrides and forces better editing practices.
```
Right click prefab in scene => Replace with Prefab Spawner
```

### Service Locator
A simple service locator system.  
Register a unique service instance:
```cs
using ServiceAccess;

public class YourService : MonoBehaviour
{
    private void Start()
    {
        ServiceLocator.GlobalServices.Register(this);
    }
}
```
Find the instance:
```cs
using ServiceAccess;

public class OtherClass : MonoBehaviour
{
    private void Start()
    {
        if(ServiceLocator.GlobalServices.TryGet(out YourService yourService))
        {
            // do something with yourService
        }
    }
}
```

### Simple State Machine
A very simple C# driven state machine.  
Bind the StateMachine to a class and add transitions to your States.
Call Update() and FixedUpdate() on it for use in States.
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

        _stateMachine.NextState<IdleState>();
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
    public ChaseState(StateMachine<EnemyController> stateMachine, EnemyController binding) : base(stateMachine, binding) { }

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