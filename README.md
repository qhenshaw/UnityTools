# Unity Tools
[![Unity 6+](https://img.shields.io/badge/unity-6%2B-blue.svg)](https://unity3d.com/get-unity/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE.md)

This package comes with several useful tools focused on organization, level design, and lighting.

## System Requirements
Unity 6+. Will likely work on earlier versions but this is the version I tested with. 
Currently requires Odin Inspector for custom editor windows, it won't compile without Odin.

> [!TIP]
> As of 5.0.0 this package has no 3rd party dependencies. Odin Inspector is no longer required.

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
Mesh is overridden in scene with a painted copy, because of Unity's serialization the mesh can't be saved in a prefab. Recommend using Unity's FBX exporter if you need a baked copy of the mesh. 

### Mesh Importer
Unreal style UCX meshes will be converted into mesh colliders.  
All meshes will have Generate Mesh LODs enabled on first import, can be disabled later.
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

### Transform Search Editor
Adds a search field to the Transform inspector, displays matching field results from GameObject components.
> [!CAUTION]
> This search function is experimental and currently returns intentionally hidden Unity component fields like everything under the URP/HDRP Additional Light Data.  
>Use with discretion.

### Inspect Attributes
Used internally to add buttons to my tools, it's not good. Seriously.  
Get Odin, Naughty Attributes, or Alchemy.

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

### Messaging Service
Send global messages between objects and scenes using an enum as a tag. 
Watch out for initialization order and race conditions! 
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
    MessagingService.Instance.AddListener<Character>(GlobalMessages.PlayerSpawned, OnPlayerSpawned);
}

private void OnDisable()
{
    MessagingService.Instance.RemoveListener<Character>(GlobalMessages.PlayerSpawned, OnPlayerSpawned);
}

private void OnPlayerSpawned(GameObject gameObject)
{
    // cache the reference here
}
```

### Object Pooling
A simple object pooling solution for GameObjects.  
Your pooled prefab has a component inheriting from PooledObject.  
Get from pool:
```cs
YourType pooledObject = PoolSystem.Instance.Get(PrefabReference) as YourType;
```
Return to the pool when finished:
```cs
pooledObject.ReturnToPool();
```

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
Type in additional scenes and click Add New Scene to automatically create and add to the list
```

### Hide Children
A component that can hide child objects and components on scene objects or prefabs.

### Light Bake Settings
A component that can apply lighting settings to all meshes in scene under a parent.  
When nesting objects deeper component settings are used.

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