# Changelog

[5.9.0] - 2026-11-15
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

[5.8.0] - 2026-11-15
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

[5.7.0] - 2026-11-15
- Transform Search: searching no longer changes your component foldouts (before, typing expanded every component and clearing the search collapsed every component, including Transform)
- Transform Search: each Inspector window has its own search, and a locked Inspector keeps its search until its object changes
- Transform Search: the search clears when the selection changes
- Transform Search: hidden components (e.g. URP/HDRP Additional Light Data) are no longer listed
- Transform Search: much faster with large components, matches are only rebuilt when the query, components or undo state change instead of every redraw
- Transform Search: arrays and lists match by their own name and are shown as one expandable field instead of every element being searched
- Transform Search: added RectTransform support (UI objects), with the anchor and pivot handles still working
- Transform Search: fields that share a parent show its path once instead of above every field

[5.6.1] - 2026-11-15
- Vertex Painter: fixed the painted area not matching the brush circle on scaled meshes (the brush radius is now always in world units, including non-uniform and mirrored scale)
- Vertex Painter: preview dots now face the right way on rotated and non-uniformly scaled meshes
- Vertex Painter: much faster on large meshes. Mesh data, world positions and a spatial grid are cached per selection, raycasts use a hidden MeshCollider instead of testing every triangle, and the brush only updates when the mouse, camera, radius or mesh changes
- Vertex Painter: preview dots are drawn in one batch and thinned out above 4000 vertices

[5.6.0] - 2026-11-15
- Mesh Importer: added Unreal UBX_ (box), USP_ (sphere) and UCP_ (capsule) collision prefixes, fitted to each collision mesh's bounds on its own transform
- Mesh Importer: UCX_ colliders are now convex, matching Unreal (warns when a mesh is over Unity's 255 triangle convex limit)
- Mesh Importer: collision meshes must now start with the prefix and an underscore (e.g. UCX_), names that only contain "ucx" no longer match
- Art Asset Import Settings now work: Generate Unreal Colliders (was Generate UCX Colliders), Generate Mesh LODs, Import Cameras and Import Lights were being ignored
- Art Asset Import Settings moved to ProjectSettings/ArtAssetImportSettings.asset, values from the old Assets/Settings asset are copied over and the old asset is deleted

[5.5.0] - 2026-11-14
- Added InlineButton attribute: draws a button on the same row as a field, and works alongside another drawer on the field (e.g. a dropdown)
- Button attribute: added Method = nameof(...) so buttons no longer depend on the field's saved string (which broke on method renames and for new array elements); the old style still works
- Buttons now run on every selected object as one undo step, after the inspector finishes drawing, find private methods in base classes, log exceptions instead of breaking the inspector, and show "(!)" with the reason when the method is missing
- Added SceneDropdown attribute: a searchable, folder-grouped scene picker for string fields and lists (stores the asset path, or the scene name with storeName: true)
- Additive Scene Manager: scenes are picked from the searchable dropdown, with inline Add buttons for New Scene Name and Existing Scene
- Streaming Scene Manager and Streaming Scene Transition: scene fields use the searchable scene dropdown (still stored as scene names)
- Fixed Add New Scene saving scene paths with a doubled slash

[5.4.0] - 2026-11-14
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

[5.3.0] - 2026-10-27
- Vertex Painter now saves painted meshes as mesh assets in the project instead of embedding them in scenes
- Painting in Prefab Mode stores paint in the prefab, painting a prefab instance creates a unique copy for that instance
- Added Vertex Painter inspector with Save to Asset, Rebuild from Source, Make Unique and Remove options
- Added undo support for vertex painting
- Added Vertex Paint cleanup window to find and delete unused painted mesh assets

[5.2.0] - 2026-10-16
- Updated gradient editor with EXR support and import options

[5.1.5] - 2026-10-06
- Added HDRP check for lighting helper menu options

[5.1.4] - 2026-10-02
- PrefabSpawner now uses transform rotation for spawned object

[5.1.3] - 2026-08-12
- Fixed issues with LightingSettings and environment volumes not saving correctly

[5.1.2] - 2026-08-10
- Reorganized lighting tools into new assembly
- Added quick assign option for volumes and multi-scene lighting setups

[5.1.1] - 2026-08-09
- Updated to support Unity 6.5 EntityID changes
- Removed CustomInspector until new hierarchy view stops changing things

[5.1.0] - 2026-08-08
- Fixed error caused by chaining messages
- Added new right click options for assigning 3 texture MHER setups directly to material layers
- Added physics scatter tool that can simulate physics during edit mode for rapid accurate prop placement

[5.0.0] - 2026-07-22
- Removed Odin Inspector dependencies for package
- Added new HideChildren component that can hide scene/prefab child GOs and event components
- Added mew Projection Scatter tool for non-destructive rapid set dressing
- Reduced GC alloc of MessagingService by 90%
- Removed the following deprecated features:
  - GameEvents, replaced my MessagingService
  - Prefab Placer tool, replaced by Projection Scatter tool
  - Export Project to Zip menu item

[4.5.0] - 2026-06-26
- Added new custom Toolbar buttons to give easier access to:
  - Time Scale
  - Auto and manual compile/refresh
  - Scene switching

[4.4.3] - 2026-06-23
- Fixed error from PoolSystem when stopping play
- Changed default PoolSystem behaviour to keep PooledObjects parented

[4.4.2] - 2026-05-05
- Temporary bypass of art import settings load that's throwing errors

[4.4.1] - 2026-04-24
- Replaced deprected instance ID calls in cutom hierarchy drawer
- Added first import check for model importer settings

[4.4.0] - 2026-04-03
- Added Art Asset Import Settings to project settings menu to configure default model import settings
- Moved art pipeline related scripts to ArtPipeline namespace

[4.3.4] - 2026-01-23
- Fixed enumeration error when removing listener during message send

[4.3.3] - 2026-01-12
- Fixed HasValue check in GlobalVariable
- Fixed EnableLogging not working in StateMachine
- Renamed SCT to SST

[4.3.2] - 2026-01-05
- Updated DebugMenu to work with new Input System

[4.3.1] - 2026-01-05
- Fixed bad parameter named in MessagingService add listener method
- Switched MessagingService to RuntimeInitializeOnLoad setup

[4.3.0] - 2026-01-03
- Added transition add/remove for state machine

[4.2.0] - 2026-01-02
- Added GlobalVariable and DataReference systems

[4.1.0] - 2025-12-16
- Improved async performance of StreamingSceneManager
- EnableObjectsSequential now gathers only root of child objects
- Added option to disable scenes instead of unloading them when streaming additive scenes

[4.0.3] - 2025-12-01
- Added fix for null ref from pooled object when editor stops

[4.0.2] - 2025-11-28
- Added vertex painting support for probduiler meshes
- Fixed painting radius being larger than visualization

[4.0.1] - 2025-11-27
- Added "MHER" as option for PBR material creation from textue names

[4.0.0] - 2025-11-23
- Added new Streaming Scene Manager system, removed old scene loading components
- Migrated Vertex Painter tool to Unity's Tool/Overlay API

[3.7.0] - 2025-11-06
- Added a code-oriented global messaging service

[3.6.0] - 2025-10-04
- Added vertex painting component

[3.5.3] - 2025-10-02
- Added HDR to gradient editor

[3.5.2] - 2025-09-29
- Fixed mesh combine tool destroying GOs with colliders remaining

[3.5.1] - 2025-09-12
- Added property to expose GameEventAsset reference in Caller/Listener

[3.5.0] - 2025-09-11
- Added gradient texture baker

[3.4.1] - 2025-09-04
- Added ProBuilder support for runtime mesh combine

[3.4.0] - 2025-08-10
- Added runtime mesh combine tool for optimization

[3.3.0] - 2025-07-03
- Added functional debug menu sample

[3.2.1] - 2025-06-26
- Added auto layer change to TriggerVolume

[3.2.0] - 2025-05-03
- Added simple state machine implementation

[3.1.5] - 2025-03-26
- Streamlined adding new scenes to AdditiveSceneManager

[3.1.4]
- Removed error from Service Locator TryGet function

[3.1.2]
[3.1.3] - 2025-03-21
- Fixed error stopping builds
- Added new Scene button to AdditiveSceneManager

[3.1.1] - 2025-03-07
- Fixed HideChildren not working at runtime
- Fixed mesh preview parented mesh offsets incorrect
- Added parenting options to PrefabSpawner

[3.1.0] - 2025-02-28
- Added PrefabSpawner component

[3.0.0] - 2025-02-27
- Reorganized entire package
- Added GameEvents, DebugMenu, ObjectPooling, and ServiceLocator

[2.9.0] - 2025-02-27
- Removed redundant prefab swapper
- Added Service Locator component

[2.8.1] - 2025-01-28
- Added 'VFS' to uber material menu shortcut

[2.8.1] - 2024-12-18
- Added missing support for Terrains and SkinnedMeshRenderers

[2.8.0] - 2024-11-02
- Updated find object calls to Unity 6 standards
- Added helper menu items for surrounding meshes with APV and 

[2.7.0] - 2024-10-21
- Added Lightbake Settings component for configuring scene lighting settings more easily

[2.6.0] - 2024-08-22
- Added AutoAssign attribute for getting components automatically

[2.5.0] - 2024-08-21
- Added SceneLoadTrigger component that allows for runtime async load/unload of scenes

[2.4.0] - 2024-08-17
- Updated material creator for new Uber shader/channel setup

[2.3.0] - 2024-06-22
- Added HideChildren component

[2.2.0] - 2024-04-28
- Added modified project zip export menu option from Jonathan Tremblay: https://github.com/JonathanTremblay/UnityExportToZip

[2.1.0] - 2024-03-09
- Added helper method for wrapping geo with Reflection Probes

[2.0.0] - 2024-02-27
- Removed light probe volume in favor of new APV lighting method
- Added Additive Scene Manager for handling multi-scene setups

[1.5.1] - 2023-10-25
- Fixed material creator not assigning base color
- Fixed snap to floor not sorting by nearest collision

[1.5.0] - 2023-08-03
- Added editor-mode physics sim option

[1.4.2] - 2023-08-03
- Fixed offset position not working

[1.4.1] - 2023-08-03
- Removed debug draw lines
- Hid physics sim button until it's working

[1.4.0] - 2023-08-01
- Added individual raycasts for spawn positions within spread distance for better placement

[1.3.0] - 2023-07-12
- Added uniform random scale option
- Fixed issue with random scale Z not working correctly
- Fixed random rotation sliders not showing

[1.2.0] - 2023-07-04
- Added surface normal rotation mode to Prefab Placer

[1.1.0] - 2023-03-03
- Added Snap to Floor hotkey, Tools => LD Shortcuts => Snap to Floor

[1.0.0] - 2022-11-23
- This is the first release of Editor Tools
