# Changelog
All notable changes to Unity Tools. Versions are git tags: install one with `https://github.com/qhenshaw/UnityTools.git#<version>`.

## Unreleased
- **Breaking** The package now needs Unity 6.5 (6000.5) or later (`package.json` `unity` is `6000.5`), for Graph Toolkit.
- Added the Placement Graph (`UnityTools.Placement`): procedural prefab placement with a Graph Toolkit node graph (.placegraph). Nodes: Volume Sampler, Surface Sampler, Noise Density, Density Filter, Slope Filter, Transform Points and Spawn Prefabs. A Placement Graph component runs a graph in a box volume and keeps the result as its children, regenerating automatically in edit mode. Results are deterministic per seed. Weighted prefab lists are Prefab Palette assets.
- Placement Graph: new source nodes Terrain Sampler (height, normal and an optional terrain layer filter), Child Markers (the children of the component's new Markers field) and Spline Sampler (along or inside a Spline Container set in the component's new Spline field; only compiled with the Splines package).
- Placement Graph: new nodes Height Filter, Self Pruning (minimum distance, densest first), Difference (remove points near another set), Distance Density, Merge and Copy Points.
- Placement Graph: the Markers and Spline objects are kept when the graph regenerates, and moving them regenerates it with Auto Generate on.
- Placement Graph: nodes show readable titles in the graph window ("Volume Sampler" instead of the class name) and are grouped in the add-node menu under Sources, Density, Filters, Points and Spawners. Existing graphs are unaffected.

## 7.0.0 - 2026-11-15
- **Breaking** Every tool now uses a `UnityTools.*` namespace, and every assembly is named after its namespace. Scripts need their `using` lines updated, and your own assembly definitions that reference these assemblies by name need the new names (references by GUID, Unity's default, keep working). Scene, prefab and asset references are unaffected.

| Tool | Old namespace | Old assembly | New namespace and assembly |
|---|---|---|---|
| Object Pooling | `ObjectPooling` | `ObjectPooling.Runtime` | `UnityTools.Pooling` |
| Simple State Machine | `SimpleStateMachine` | `SimpleStateMachine` | `UnityTools.States` |
| Service Locator | `ServiceAccess` | `ServiceAccess` | `UnityTools.Services` |
| Messaging Service, Global Variables | `UnityTools.Messaging` | `Messaging` | `UnityTools.Messaging` |
| Debug Menu | `UnityTools.DebugTools` | `DebugMenu.Runtime` | `UnityTools.DebugTools` |
| Scenes, Prefab Spawner, Scene Dropdown | `SceneManagement` | `SceneManagement` | `UnityTools.Scenes` |
| Projection and Physics Scatter | `ScatterTool` | `ScatterTool` | `UnityTools.Scatter` |
| Art pipeline | `ArtPipeline` | `ArtPipeline` | `UnityTools.Art` |
| Lighting tools | `LightingTools` | `LightingTools` | `UnityTools.Lighting` |
| Inspector Attributes | `InspectorAttributes` | `InspectorAttributes` | `UnityTools.Attributes` |
| Inspector Attributes (editor) | `InspectorAttributes.Editors` | `InspectorAttributes.Editors` | `UnityTools.Attributes.Editor` |
| Snap to Floor, Transform Search | `EditorTools` | `EditorTools` | `UnityTools.EditorTools` |
| Toolbar Buttons | `UnityTools.ToolbarButtons` | `UnityTools.ToolbarButtons` | `UnityTools.Toolbar` |

- Editor code follows the same pattern, in `<namespace>.Editor` (e.g. `SceneManagement.Editor` is now `UnityTools.Scenes.Editor`). UnityTools.Data and UnityTools.PlaytestFeedback are unchanged.
- To update a project: replace each old `using` line with the new one, e.g. `using ObjectPooling;` becomes `using UnityTools.Pooling;`, and update any fully qualified names such as `SceneManagement.StreamingSceneManager`.

## 6.9.0 - 2026-11-15
- README rewritten: a table of contents, tools grouped by category (gameplay systems, debugging and playtesting, scenes, level design, art pipeline, lighting, editor and inspector), every section checked against the code, and code samples that compile as written (with their usings)
- README: corrected the requirements (Odin isn't needed; URP or HDRP is), menu paths, setting names and defaults throughout, and removed the Custom Hierarchy Drawer section (removed in 5.1.1)
- README: documented Scene Dropdown, Enable Objects Sequential, Assign Volume to Open Scenes, the toolbar's Play From dropdown, and which assemblies to reference from your own assembly definitions
- Doc comments added or corrected on the public API across the package
- CHANGELOG: consistent headings and typo fixes

## 6.8.0 - 2026-11-15
- **Breaking** Removed Runtime Mesh Combine and its UnityTools.Optimization assembly. A RuntimeMeshCombine component left in a scene shows as a missing script; remove it
- Toolbar: the Time Scale slider and reset button work in Play mode only, and exiting Play mode restores the time scale. Before, using them in edit mode changed the project's Time Scale setting (TimeManager.asset), which ships in builds
- Toolbar: Auto Compile is now a dropdown with all three Auto Refresh modes. The old toggle showed "Enabled Outside Playmode" as off and overwrote it when turned on
- Toolbar: the recompile button refreshes and then always recompiles; before, it did nothing when no script had changed
- Toolbar: the scene list comes from the AssetDatabase instead of a folder scan on every project change, and Find Scene / Restart Scene use the active scene's path instead of its name
- Snap to Floor: works on every selected object as one Undo step, ignores triggers and the other selected objects, and brings objects that are sunk into the floor up to its surface
- Gradient Editor: the last pixel is now the gradient's end colour (it sampled x / width, never reaching 1)
- Material Creator: textures are identified by the suffix after their last underscore (BaseColor/Albedo/Diffuse, Normal/NormalDX/NormalGL, MHER/Mask/ORM), so names like "RoughStone_BaseColor" are no longer mistaken for mask maps, and errors name the texture that didn't match
- Material Creator: Assign Layer raises the material's Enabled Layers keyword to include the layer (never lowers it), using the shader's current keyword names; before, the layer stayed hidden
- Material Creator: a missing VFS/Uber shader gives a clear error instead of throwing, import settings change only once all three textures are found (with SaveAndReimport), a new material never overwrites an existing one with the same name, and layer assignment supports Undo
- Added EditMode tests for the toolbar options, Snap to Floor, Gradient Editor and Material Creator

## 6.7.0 - 2026-11-15
- Physics Scatter: Stop now puts every Rigidbody's Is Kinematic and Use Gravity back exactly as they were and removes the Rigidbodies it added. Before, plain (non-prefab) objects stayed kinematic and kept the added Rigidbodies
- Physics Scatter: the simulated moves are one Undo step ("Physics Scatter"), and scenes are marked dirty once on Stop instead of on every Scene view event
- Physics Scatter: Stop restores the project's physics simulation mode to what it was, instead of always setting it to FixedUpdate (even when no simulation ran)
- Physics Scatter: the simulation runs in real time from the editor update loop; before, it advanced once per Scene view event, so it sped up when the mouse moved and stalled when it didn't
- Physics Scatter: toggling the tool no longer adds another copy of each overlay button handler, so Launch fires once
- Physics Scatter: entering Play mode or recompiling mid-simulation stops it cleanly first, and the overlay's Start/Stop button follows the tool instead of the physics simulation mode
- Physics Scatter: Quick Copy (C) duplicates like Edit > Duplicate, keeping scale, prefab overrides and added components
- Physics Scatter: keys and mouse buttons are read from the editor event, so it no longer throws without a keyboard or mouse device. Fixed the "Phyisics Scatter" tool name
- Projection Scatter: paint strokes, Reset Mask, Flood Mask, Clear and Bake are each one Undo step, and painted weights mark the scene dirty so they're saved
- Projection Scatter: inside a prefab instance it no longer throws trying to destroy the prefab's children; it pauses with one warning (edit it in prefab mode, or unpack the instance)
- Projection Scatter: the brush ends its stroke on mouse release anywhere, and reads Ctrl from the event, so it can't get stuck painting or erasing
- Projection Scatter: the overlay's Create button sets the scatter up like the GameObject menu (pointing down, random seed, Default layer mask), so it spawns
- Projection Scatter: its settings now exist in builds, so builds no longer log serialization layout errors for scenes with a scatter. The inspector notes that its children are generated and destroyed on regeneration
- README: documented Physics Scatter and corrected Projection Scatter's "non-destructive" description
- Added EditMode tests for Physics Scatter and Projection Scatter

## 6.6.0 - 2026-11-15
- Prefab Spawner: the preview no longer creates (and leaks) a new material on every repaint, and no longer throws every frame without a Preview Material; with none set it draws tinted gizmo meshes
- Prefab Spawner: the preview places nested and scaled child meshes where they'd actually spawn (it used the children's local scale and ignored their offsets), and skips inactive objects and disabled renderers
- Prefab Spawner: Replace with Prefab Spawner handles every selected instance in one Undo step and keeps rotation, parent, place in the hierarchy and scene (it kept only position). Instances with prefab overrides are listed first, with Replace Anyway, Skip Those or Cancel. Selecting a child of an instance replaces the whole instance
- Prefab Spawner: Spawn() with no prefab logs an error and returns null instead of throwing, and doesn't destroy the spawner outside Play mode
- Hide Children: inactive children are now hidden too, and only the hide flags it owns are added or removed, so other flags on the children are kept
- Hide Children: Show In Prefab Mode now shows the children without changing Is Hidden. Before, saving in prefab mode could store Is Hidden off, and closing the prefab forced it on. Nested Hide Children in a prefab are handled too, and the "Showing/Hiding prefab components" logs are gone
- Additive Scene Manager: Add New Scene no longer overwrites an existing scene with the same name; it offers to add the existing scene instead. Adding scenes records Undo and marks the scene dirty
- Additive Scene Manager: the scene list starts empty instead of null, so a manager added from code no longer throws
- Added EditMode tests for Prefab Spawner, the mesh preview, Hide Children and Additive Scene Manager

## 6.5.0 - 2026-11-15
- Streaming Scene Manager: no longer unloads the scene it's in when that scene isn't listed as Persistent
- Streaming Scene Manager: LoadSet calls made while a set is loading no longer race each other. The manager finishes the scene it's loading or unloading, then switches to the newest set; sets asked for in between are skipped
- Streaming Scene Manager: asking for the set that's already loaded or loading (in any order) does nothing, so a transition entered several times, e.g. by a player with several colliders, loads once
- Streaming Scene Manager: a scene that isn't in Build Settings is skipped with a clear error instead of throwing; the rest of the set still loads
- Streaming Scene Manager: Disable Instead Of Unload now re-activates only the root objects that were active when the scene was hidden, instead of every root object
- Streaming Scene Manager: added SetLoadStarted and SetLoaded events and the IsLoading, Progress, CurrentSet and IsSceneLoaded members, for loading screens. LoadSet now takes params, e.g. LoadSet("A", "B")
- Streaming Scene Manager: empty Persistent or Initial lists no longer throw, the singleton is cleared when the manager is destroyed (and on entering Play mode with domain reload off), and Unload Unused Assets runs once per set instead of after each scene
- Streaming Scene Transition: creating one from the GameObject menu no longer throws when the project has no "Trigger" layer; it uses Ignore Raycast instead
- Scene dropdowns: a scene that isn't in Build Settings (or is disabled there) shows "(not in build)" and an Add to Build button
- Added EditMode and PlayMode tests for the streaming scene manager, transitions and the Build Settings check

## 6.4.0 - 2026-11-15
- Data Reference: Runtime no longer throws when no asset is assigned. Runtime and Persistent log one error naming the data type and return null. Added HasAsset to check without the error
- Data Reference: outside Play mode, Runtime returns the asset itself instead of making a copy. The runtime copy is no longer serialized, so it can't be saved into scenes or prefabs
- Data Reference: runtime copies are destroyed when Play mode ends (also with domain reload off). Added ResetRuntime() to throw away a field's copy and start fresh from the asset, e.g. on a restart. Assigning a different asset also replaces the copy
- Data Reference: each field still gets its own runtime copy (now documented), including each instance of a prefab
- Data Reference: new inspector drawer: the asset field with a "New" button on the same line (the assignment supports Undo), and in Play mode a "Runtime copy" foldout showing the copy's live values. The old Create New button field is gone; existing assignments load unchanged
- Data Reference: Persistent can now be set from code. CreateNew() was removed (use the inspector's New button); CreateAt<U>() is unchanged
- Added EditMode and PlayMode tests for Data Reference

## 6.3.0 - 2026-11-15
- Added Playtest Feedback, moved in from the standalone com.qhenshaw.playtestfeedback package (0.1.1, commit c2342b9): players press F8 to pause the game, write feedback, pick a category, snip and draw on a screenshot, and send it with game version, system info, logs and custom game state to a Discord channel through a webhook
- Playtest Feedback: the namespace is now UnityTools.PlaytestFeedback (and the assemblies UnityTools.PlaytestFeedback and UnityTools.PlaytestFeedback.Editor)
- Playtest Feedback: settings moved from the Assets/Resources/PlaytestFeedbackSettings.asset asset to Project Settings > Unity Tools > Playtest Feedback, saved in ProjectSettings/UnityToolsPlaytestFeedback.asset (including the webhook URL, which is now committed with the project) and copied into builds that include the tool. An existing PlaytestFeedbackSettings asset is copied over once and deleted. FeedbackSettings is now a plain settings class; use FeedbackSettings.Current instead of FeedbackSettings.Load()
- Playtest Feedback: Panel Settings Override and Extra Style Sheet were removed (settings are copied into builds as JSON, which can't hold asset references); edit FeedbackPanel.uss to restyle the panel
- Playtest Feedback: menus moved to Tools > UnityTools > Playtest Feedback (Open Settings, Open Outbox Folder)
- Playtest Feedback: fixed a custom hotkey reverting to F8 after its settings were loaded, because the default bindings were added before the saved ones were read. The hotkey now starts with no bindings, which means F8 / L3 + R3
- To switch a project from the standalone package: remove com.qhenshaw.playtestfeedback, update UnityTools, open the project once so the settings are migrated, and replace `using PlaytestFeedback;` with `using UnityTools.PlaytestFeedback;`
- UnityTools now depends on the UI Toolkit, UnityWebRequest, Image Conversion, Screen Capture and Audio modules, and on Input System 1.8.2 or newer
- Added EditMode and PlayMode tests for Playtest Feedback

## 6.2.0 - 2026-11-15
- **Breaking** Debug Menu: rebuilt around sections. Features call DebugMenu.Register(this, "Title", section => section.Button(...).Toggle(...).Slider(...).SliderInt(...).Dropdown(...).Readout(...)) in OnEnable and DebugMenu.Unregister(this) in OnDisable. The menu is drawn with UI Toolkit, one foldout per section
- **Breaking** Debug Menu: the namespace is now UnityTools.DebugTools. To update a project: replace `using DebugMenu;` with `using UnityTools.DebugTools;`. [DebugCommand] and DebugMenuSystem.Instance.RegisterObject/DeregisterObject still work but are obsolete; each registered object becomes a section of buttons named after its type. Commands with parameters are skipped with a warning
- **Breaking** Debug Menu: the old uGUI "Demo Debug Menu" sample is removed. A DebugMenuSystem component left in a scene logs a warning once and does nothing; remove it
- Debug Menu: the menu is now created automatically in the Editor and Development builds, and in release builds with the DEBUG_MENU scripting define. Before, it needed the undocumented DEBUG_MENU define and a placed prefab, and otherwise silently never opened
- Debug Menu: added Project Settings > Unity Tools > Debug Menu: toggle binding (default F5, or Select + Start on gamepad), Start Open, Pause While Open, Block Gameplay Input, Show Game Section and Refresh Interval. Builds that include the menu get a copy in StreamingAssets
- Debug Menu: opening the menu no longer forces every PlayerInput to the "UI" map and back to "Player", and no longer resets the time scale to 1 on close. The cursor is restored to its previous state. Pause While Open restores the previous time scale, and Block Gameplay Input turns back on only the PlayerInputs it turned off
- Debug Menu: no longer throws every frame when there's no keyboard (e.g. a Steam Deck using only its gamepad)
- Debug Menu: sections whose owner was destroyed without unregistering are dropped instead of throwing when used
- Debug Menu: added a built-in Game section with FPS, time scale, reset time scale and reload scene
- Debug Menu: added a Debug Menu sample (prefab, UXML, USS and Panel Settings) to restyle the menu. Placed in a scene, it replaces the automatic menu
- Debug Menu: with the Pipeline package installed, `unity command debugmenu list` and `unity command debugmenu do --section S --label L --value V` read and drive every control. DebugMenuJson.List/Do do the same from code and tests
- Art Asset Import Settings moved from Project Settings > [Custom] Art Asset Import Settings to Project Settings > Unity Tools > Art Asset Import. The settings file is unchanged
- Added EditMode and PlayMode tests for Debug Menu

## 6.1.0 - 2026-11-15
- **Breaking** Service Locator: ServiceLocator is now a static class with Register, Unregister, Get and TryGet, and the ServiceLocator component (with its Register On Awake list and LocalServices) is removed. To update a project: delete ServiceLocator components from scenes and prefabs (they show as missing scripts), register those services in their own Awake, and replace `ServiceLocator.GlobalServices.X` with `ServiceLocator.X`. GlobalServices and ServiceRegistry still compile but are obsolete and forward to the static registry
- Service Locator: global services now survive scene loads. Before, they lived on a GameObject that was destroyed by the next LoadScene, so every lookup failed afterwards
- Service Locator: destroyed services count as unregistered, and a new instance can register in a destroyed one's place without an error
- Service Locator: Unregister(service) only removes that exact instance, so an old object's OnDestroy no longer removes its replacement
- Service Locator: Get<T>() finds a service registered as exactly T, otherwise the one registered service that is a T (base class or interface). Several matches log an error naming them
- Service Locator: Get<T>(context) points its "not registered" error at the calling object, and the message explains to register in Awake and get in Start or later. TryGet never logs
- Service Locator: registering null, a destroyed object, or an object under a type it isn't now logs an error
- Service Locator: registering and unregistering no longer log by default; set ServiceLocator.LogChanges to log them
- Service Locator: registered services are listed in the Messaging Debugger window (Tools > UnityTools > Messaging Debugger)
- The Messaging Debugger window moved from Window > UnityTools to Tools > UnityTools
- Added EditMode tests for Service Locator

## 6.0.0 - 2026-11-15
- **Breaking** Simple State Machine: each state type now has one instance per machine, reused every time the state is entered (before, a new instance was created on every transition). To update a project: reset any fields a state changes (timers, "done" flags) in its Enter()
- **Breaking** Simple State Machine: states use a parameterless constructor; StateMachine and Binding are set before the first Enter. To update a project: delete each state's `(StateMachine<T>, T) : base(...)` constructor
- Simple State Machine: added AddTransitionFromAny<V>(condition), checked from every state before normal transitions and never while already in V
- Simple State Machine: added AddTransition<U, V>(U state => bool) overloads that pass the current state typed, replacing `CurrentState is U u && u.Flag`
- Simple State Machine: added State.TimeInState (seconds since Enter, counted from the deltaTime passed to Update)
- Simple State Machine: added PreviousState and DrawDebugLabel(position), an editor-only Scene view label showing the current state, its time and the previous state
- Simple State Machine: AddTransition returns a Transition handle. RemoveTransition(handle) and RemoveTransitions<U, V>() remove transitions. RemoveTransition<U, V>(condition) is obsolete: a lambda never matched, so it silently removed nothing; it now warns when nothing matched
- Simple State Machine: Update and FixedUpdate before the first state no longer throw
- Simple State Machine: NextState called from inside Enter, Exit or OnStateChanged now runs after the current switch finishes, so Exit/Enter calls and OnStateChanged stay in order
- Simple State Machine: states are created without reflection, and a state without a parameterless constructor is a compile error instead of a runtime exception
- Simple State Machine: documented that the transition added last wins when several are true
- Added EditMode tests for Simple State Machine

## 5.9.0 - 2026-11-15
- Object Pooling: added PoolSystem.Instance.Get(prefab, ...) overloads that return your own component type (Bullet bullet = PoolSystem.Instance.Get(_bulletPrefab, position, rotation)). The old Get(IPoolable) overloads still work but are obsolete
- Object Pooling: instances are now placed (parent, position, rotation) before they're activated, so Awake and OnEnable see the spawn position. Before, they ran at the y = -1000 hide position, so trails streaked up from under the map
- Object Pooling: new instances are created inactive, so Awake doesn't run until an instance is first used
- Object Pooling: added OnTakenFromPool() and OnReturnedToPool() hooks to PooledObject (and IPoolable, with empty defaults) for resetting reused objects
- Object Pooling: PoolSize is split into Prewarm Count (instances created when the pool is first used) and Max Size (0 = unlimited). Existing PoolSize values become Max Size, and PoolSize is obsolete
- Object Pooling: added ReturnToPool(delay). Calling it again restarts the timer, and any other return cancels it
- Object Pooling: ReturnToPool() on an object that didn't come from a pool, or whose pool was unloaded with its scene, destroys it instead of throwing
- Object Pooling: destroying a pooled object (directly or with its parent) logs an error once per prefab, and the pool skips destroyed objects instead of handing them out
- Object Pooling: a PoolSystem placed in a scene no longer throws. It becomes PoolSystem.Instance and prewarms a list of prefabs on Awake. A second PoolSystem passes its list to the first and removes itself
- Object Pooling: added PoolSystem.Prewarm(prefab), Prewarm(prefab, count), CountInactive(prefab) and PoolSystem.HasInstance
- Object Pooling: Log Creation now defaults to off
- Added PlayMode tests for Object Pooling

## 5.8.0 - 2026-11-15
- Messaging Service: a listener with the wrong payload type no longer stops the rest of a message, and sending a null payload no longer throws
- Messaging Service: each tag carries one payload type, set by its first listener. A listener or send with a different type logs a clear error naming the tag and both types (before, a second listener type threw an unexplained ArgumentException)
- Messaging Service: a listener that throws is logged and the other listeners still run
- Messaging Service: listeners can send messages while a message is being sent
- Messaging Service: Send(tag, payload, sendToLate: true) remembers the payload and gives it to listeners added later. Once remembered, every later send to the tag replaces it, and ClearSticky(tag) forgets it
- Messaging Service: no-payload listeners, AddListener(tag, Action), fire on every send to a tag whatever the payload, and Send(tag) sends without a payload
- Messaging Service: a listener removed while a message is being sent is skipped for the rest of that send
- Messaging Service: listeners on destroyed objects are removed with a warning instead of throwing on every send
- Messaging Service and Global Variables: sending no longer allocates. Enum tags are no longer boxed, tags held in an Enum variable are read without allocating, and sticky struct payloads are stored without boxing. Calls that name a single type argument (Send<int>(tag, 1)) still box the tag at the call site, so leave the type out or name both (Send<MyTag, int>)
- Global Variables: OnUpdated now fires on every GlobalVariable with the tag, whichever one (or GlobalVariableService.Set) changed the value. Before, only the instance that set it was notified
- Global Variables: added GlobalVariableService.ValueChanged (tag, value)
- Global Variables: a value passed to the constructor is now a starting value, only used if the tag has no value yet. Before, every new instance overwrote the global, and the constructor threw in edit mode
- Global Variables: HasValue no longer throws when the value is null, and is false for destroyed objects
- Global Variables: reading a value as the wrong type logs an error instead of silently returning the default
- Global Variables: added GlobalVariableService.Remove(tag) and Clear(), which notify listeners with the default value
- Global Variables: GlobalVariable fields show their tag and value in the Inspector (starting value in edit mode, live value in Play) instead of editable fields that were ignored
- MessagingService.Instance and GlobalVariableService.Instance now exist on first use (including edit mode) and reset when Play starts, also with domain reload disabled
- Added the Messaging Debugger window (Window > UnityTools > Messaging Debugger)
- Added EditMode tests for the Messaging Service and Global Variables, including allocation checks and checks that GlobalVariable tags survive Unity serialization (add "testables": ["com.qhenshaw.unitytools"] to a project's manifest to run them)

## 5.7.0 - 2026-11-15
- Transform Search: searching no longer changes your component foldouts (before, typing expanded every component and clearing the search collapsed every component, including Transform)
- Transform Search: each Inspector window has its own search, and a locked Inspector keeps its search until its object changes
- Transform Search: the search clears when the selection changes
- Transform Search: hidden components (e.g. URP/HDRP Additional Light Data) are no longer listed
- Transform Search: much faster with large components, matches are only rebuilt when the query, components or undo state change instead of every redraw
- Transform Search: arrays and lists match by their own name and are shown as one expandable field instead of every element being searched
- Transform Search: added RectTransform support (UI objects), with the anchor and pivot handles still working
- Transform Search: fields that share a parent show its path once instead of above every field

## 5.6.1 - 2026-11-15
- Vertex Painter: fixed the painted area not matching the brush circle on scaled meshes (the brush radius is now always in world units, including non-uniform and mirrored scale)
- Vertex Painter: preview dots now face the right way on rotated and non-uniformly scaled meshes
- Vertex Painter: much faster on large meshes. Mesh data, world positions and a spatial grid are cached per selection, raycasts use a hidden MeshCollider instead of testing every triangle, and the brush only updates when the mouse, camera, radius or mesh changes
- Vertex Painter: preview dots are drawn in one batch and thinned out above 4000 vertices

## 5.6.0 - 2026-11-15
- Mesh Importer: added Unreal UBX_ (box), USP_ (sphere) and UCP_ (capsule) collision prefixes, fitted to each collision mesh's bounds on its own transform
- Mesh Importer: UCX_ colliders are now convex, matching Unreal (warns when a mesh is over Unity's 255 triangle convex limit)
- Mesh Importer: collision meshes must now start with the prefix and an underscore (e.g. UCX_), names that only contain "ucx" no longer match
- Art Asset Import Settings now work: Generate Unreal Colliders (was Generate UCX Colliders), Generate Mesh LODs, Import Cameras and Import Lights were being ignored
- Art Asset Import Settings moved to ProjectSettings/ArtAssetImportSettings.asset, values from the old Assets/Settings asset are copied over and the old asset is deleted

## 5.5.0 - 2026-11-14
- Added InlineButton attribute: draws a button on the same row as a field, and works alongside another drawer on the field (e.g. a dropdown)
- Button attribute: added Method = nameof(...) so buttons no longer depend on the field's saved string (which broke on method renames and for new array elements); the old style still works
- Buttons now run on every selected object as one undo step, after the inspector finishes drawing, find private methods in base classes, log exceptions instead of breaking the inspector, and show "(!)" with the reason when the method is missing
- Added SceneDropdown attribute: a searchable, folder-grouped scene picker for string fields and lists (stores the asset path, or the scene name with storeName: true)
- Additive Scene Manager: scenes are picked from the searchable dropdown, with inline Add buttons for New Scene Name and Existing Scene
- Streaming Scene Manager and Streaming Scene Transition: scene fields use the searchable scene dropdown (still stored as scene names)
- Fixed Add New Scene saving scene paths with a doubled slash

## 5.4.0 - 2026-11-14
- Added APV Bake Checklist window (Window > Rendering > APV Bake Checklist): checks an HDRP Adaptive Probe Volume bake with no lightmaps across the project, loaded scenes and objects, with per-row Fix/Select buttons, Fix All, bake controls and post-bake verification
- APV Bake Checklist: the Baking Set fix now works when the Lighting window is in Single Scene mode (its temporary set used to block the active scene) and switches the Lighting window to the new set
- Fixed Assign Volume to Open Scenes using the wrong Static Lighting Sky and creating hidden duplicates when a scene had several root objects
- Fixed Assign Volume to Open Scenes failing on unloaded scenes or with nothing selected; it now logs what it skipped instead of failing silently
- Assign Volume to Open Scenes uses the profile's sky when the profile has no Visual Environment, and warns instead of silently picking HDRI Sky
- Lighting tools now detect HDRP automatically, so the HDRP scripting define is no longer needed (menu items were missing on platforms without it)
- Surround with Probe Volume / Reflection Probe / Local Volume now warn when nothing usable is selected, skip terrains without data, and create one object for a multi-selection instead of one per selected object
- Light Bake Settings: Apply is now undoable and records prefab overrides (changes to prefab instances could be lost before)
- Light Bake Settings: Apply now supports terrains and leaves objects without a renderer untouched instead of clearing their static flags
- Light Bake Settings: custom static flags no longer reset to the preset after a domain reload
- Light Bake Settings: fixed a serialization layout mismatch between the Editor and player builds

## 5.3.0 - 2026-10-27
- Vertex Painter now saves painted meshes as mesh assets in the project instead of embedding them in scenes
- Painting in Prefab Mode stores paint in the prefab, painting a prefab instance creates a unique copy for that instance
- Added Vertex Painter inspector with Save to Asset, Rebuild from Source, Make Unique and Remove options
- Added undo support for vertex painting
- Added Vertex Paint cleanup window to find and delete unused painted mesh assets

## 5.2.0 - 2026-10-16
- Updated gradient editor with EXR support and import options

## 5.1.5 - 2026-10-06
- Added HDRP check for lighting helper menu options

## 5.1.4 - 2026-10-02
- PrefabSpawner now uses transform rotation for spawned object

## 5.1.3 - 2026-08-12
- Fixed issues with LightingSettings and environment volumes not saving correctly

## 5.1.2 - 2026-08-10
- Reorganized lighting tools into new assembly
- Added quick assign option for volumes and multi-scene lighting setups

## 5.1.1 - 2026-08-09
- Updated to support Unity 6.5 EntityID changes
- Removed CustomInspector until new hierarchy view stops changing things

## 5.1.0 - 2026-08-08
- Fixed error caused by chaining messages
- Added new right click options for assigning 3 texture MHER setups directly to material layers
- Added physics scatter tool that can simulate physics during edit mode for rapid accurate prop placement

## 5.0.0 - 2026-07-22
- Removed Odin Inspector dependencies for package
- Added new HideChildren component that can hide scene/prefab child GOs and even components
- Added new Projection Scatter tool for non-destructive rapid set dressing
- Reduced GC alloc of MessagingService by 90%
- Removed the following deprecated features:
  - GameEvents, replaced by MessagingService
  - Prefab Placer tool, replaced by Projection Scatter tool
  - Export Project to Zip menu item

## 4.5.0 - 2026-06-26
- Added new custom Toolbar buttons to give easier access to:
  - Time Scale
  - Auto and manual compile/refresh
  - Scene switching

## 4.4.3 - 2026-06-23
- Fixed error from PoolSystem when stopping play
- Changed default PoolSystem behaviour to keep PooledObjects parented

## 4.4.2 - 2026-05-05
- Temporary bypass of art import settings load that's throwing errors

## 4.4.1 - 2026-04-24
- Replaced deprecated instance ID calls in custom hierarchy drawer
- Added first import check for model importer settings

## 4.4.0 - 2026-04-03
- Added Art Asset Import Settings to project settings menu to configure default model import settings
- Moved art pipeline related scripts to ArtPipeline namespace

## 4.3.4 - 2026-01-23
- Fixed enumeration error when removing listener during message send

## 4.3.3 - 2026-01-12
- Fixed HasValue check in GlobalVariable
- Fixed EnableLogging not working in StateMachine
- Renamed SCT to SST

## 4.3.2 - 2026-01-05
- Updated DebugMenu to work with new Input System

## 4.3.1 - 2026-01-05
- Fixed bad parameter named in MessagingService add listener method
- Switched MessagingService to RuntimeInitializeOnLoad setup

## 4.3.0 - 2026-01-03
- Added transition add/remove for state machine

## 4.2.0 - 2026-01-02
- Added GlobalVariable and DataReference systems

## 4.1.0 - 2025-12-16
- Improved async performance of StreamingSceneManager
- EnableObjectsSequential now gathers only root of child objects
- Added option to disable scenes instead of unloading them when streaming additive scenes

## 4.0.3 - 2025-12-01
- Added fix for null ref from pooled object when editor stops

## 4.0.2 - 2025-11-28
- Added vertex painting support for ProBuilder meshes
- Fixed painting radius being larger than visualization

## 4.0.1 - 2025-11-27
- Added "MHER" as option for PBR material creation from texture names

## 4.0.0 - 2025-11-23
- Added new Streaming Scene Manager system, removed old scene loading components
- Migrated Vertex Painter tool to Unity's Tool/Overlay API

## 3.7.0 - 2025-11-06
- Added a code-oriented global messaging service

## 3.6.0 - 2025-10-04
- Added vertex painting component

## 3.5.3 - 2025-10-02
- Added HDR to gradient editor

## 3.5.2 - 2025-09-29
- Fixed mesh combine tool destroying GOs with colliders remaining

## 3.5.1 - 2025-09-12
- Added property to expose GameEventAsset reference in Caller/Listener

## 3.5.0 - 2025-09-11
- Added gradient texture baker

## 3.4.1 - 2025-09-04
- Added ProBuilder support for runtime mesh combine

## 3.4.0 - 2025-08-10
- Added runtime mesh combine tool for optimization

## 3.3.0 - 2025-07-03
- Added functional debug menu sample

## 3.2.1 - 2025-06-26
- Added auto layer change to TriggerVolume

## 3.2.0 - 2025-05-03
- Added simple state machine implementation

## 3.1.5 - 2025-03-26
- Streamlined adding new scenes to AdditiveSceneManager

## 3.1.4
- Removed error from Service Locator TryGet function

## 3.1.3 - 2025-03-21
- 3.1.2 has no separate notes
- Fixed error stopping builds
- Added new Scene button to AdditiveSceneManager

## 3.1.1 - 2025-03-07
- Fixed HideChildren not working at runtime
- Fixed mesh preview parented mesh offsets incorrect
- Added parenting options to PrefabSpawner

## 3.1.0 - 2025-02-28
- Added PrefabSpawner component

## 3.0.0 - 2025-02-27
- Reorganized entire package
- Added GameEvents, DebugMenu, ObjectPooling, and ServiceLocator

## 2.9.0 - 2025-02-27
- Removed redundant prefab swapper
- Added Service Locator component

## 2.8.1 - 2025-01-28
- Added 'VFS' to uber material menu shortcut

## 2.8.1 - 2024-12-18
- Added missing support for Terrains and SkinnedMeshRenderers

## 2.8.0 - 2024-11-02
- Updated find object calls to Unity 6 standards
- Added helper menu items for surrounding meshes with APV and 

## 2.7.0 - 2024-10-21
- Added Lightbake Settings component for configuring scene lighting settings more easily

## 2.6.0 - 2024-08-22
- Added AutoAssign attribute for getting components automatically

## 2.5.0 - 2024-08-21
- Added SceneLoadTrigger component that allows for runtime async load/unload of scenes

## 2.4.0 - 2024-08-17
- Updated material creator for new Uber shader/channel setup

## 2.3.0 - 2024-06-22
- Added HideChildren component

## 2.2.0 - 2024-04-28
- Added modified project zip export menu option from Jonathan Tremblay: https://github.com/JonathanTremblay/UnityExportToZip

## 2.1.0 - 2024-03-09
- Added helper method for wrapping geo with Reflection Probes

## 2.0.0 - 2024-02-27
- Removed light probe volume in favor of new APV lighting method
- Added Additive Scene Manager for handling multi-scene setups

## 1.5.1 - 2023-10-25
- Fixed material creator not assigning base color
- Fixed snap to floor not sorting by nearest collision

## 1.5.0 - 2023-08-03
- Added editor-mode physics sim option

## 1.4.2 - 2023-08-03
- Fixed offset position not working

## 1.4.1 - 2023-08-03
- Removed debug draw lines
- Hid physics sim button until it's working

## 1.4.0 - 2023-08-01
- Added individual raycasts for spawn positions within spread distance for better placement

## 1.3.0 - 2023-07-12
- Added uniform random scale option
- Fixed issue with random scale Z not working correctly
- Fixed random rotation sliders not showing

## 1.2.0 - 2023-07-04
- Added surface normal rotation mode to Prefab Placer

## 1.1.0 - 2023-03-03
- Added Snap to Floor hotkey, Tools => LD Shortcuts => Snap to Floor

## 1.0.0 - 2022-11-23
- This is the first release of Editor Tools
