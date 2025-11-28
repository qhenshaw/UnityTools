# Changelog

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
