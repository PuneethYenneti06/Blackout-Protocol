# ai.md — Shared AI Context for Blackout-Protocol

> **READ THIS FIRST.** Every new Claude chat that opens this folder must read this whole file before doing anything else. It is the single source of truth for what has been built, decided, and what to do next. The original "parent chat" created it; all chats keep it up to date.

---

## 0. Protocol for every Claude chat (MANDATORY)

1. **On start:** read `ai.md` fully. Do not ask the user for context that is already here.
2. **Before finishing any task:** update `ai.md` — edit the relevant sections (Status, Decisions, Architecture, Next Steps) and append an entry to the **Session Log** (section 8).
3. **Keep it accurate, not long.** Rewrite stale facts in place instead of piling up contradictions. Move finished work from "Next Steps" to "Status". Keep the Session Log to short bullets.
4. **Record decisions with reasons** (section 5) so later chats don't relitigate them.
5. **Never delete** other chats' log entries; condense old ones when the log exceeds ~40 lines.
6. **Don't edit** `Library/`, `Logs/`, `UserSettings/`, `.vs/`, `*.csproj`, `*.sln*` (Unity-generated). Don't hand-edit `.unity`/`.prefab`/`.controller` YAML unless essential — prefer scripts + the user wiring things in the Editor. If you must, say so in the log.
7. Always put a `.meta`-safe approach in mind: never create/rename/delete assets in a way that breaks GUID references; new `.cs` files are fine (Unity generates `.meta`).
8. User preference: **do not create extra files unless asked** (ai.md and requested scripts are fine).

---

## 1. Project overview

- **Name:** Blackout-Protocol
- **Genre / goal:** Single-player **extraction shooter** (raid into a hostile map, loot, fight AI, reach extraction with loot; die and lose what you carried).
- **Owner:** Puneeth (CSE student, PES University). Solo/small-team student project.
- **Engine:** Unity **6000.5.9f1** (Unity 6), **URP 17.5.0**, new **Input System 1.20.0** package installed (`InputSystem_Actions.inputactions` exists).
- **Other packages of note:** AI Navigation 2.0.14 (NavMesh — use for enemy AI), Timeline, Visual Scripting, XR/XRI folders (template leftovers), Unity AI Assistant/Inference.
- **Location:** `D:\3rd year\unity\Blackout-Protocol` (git repo, branch history: "Test Unity project version control" → "soldier asset added" → "added a Playable Character, Control and motion scripts").

## 2. Core game vision (target design — to be refined with user)

Core loop: **Hideout/menu → choose loadout → deploy to map → loot / fight AI → extract (or die) → keep/lose items → upgrade → repeat.**

Planned pillars (not yet built unless listed in Status):
- Player: movement, aim/shoot, reload, health, stamina, inventory/weight
- Weapons: hitscan first, data-driven (ScriptableObject) weapon stats
- Enemies: NavMesh AI (patrol → detect → chase → attack), loot drops
- Loot: containers, item database (ScriptableObjects), grid or list inventory
- Extraction: timed extraction zones, raid timer, win/lose result screen
- Meta: persistent stash (JSON save), simple vendor/upgrades
- Polish: HUD, audio, post-processing, blackout/lighting theme (name suggests power-outage/dark atmosphere — flashlight, light switches)

> Open design questions are in section 7. Update this section once the user decides.

## 3. Current status (what actually exists)

**Version-control state:** 3 commits. Uncommitted at time of writing: `Assets/Scenes/SampleScene.unity`, `Assets/SoldierController.controller`.

**Scene(s):** `Assets/Scenes/SampleScene.unity` (main working scene), `Assets/Scenes/Door 1.unity`. Environment is an indoor building (rooms: Classroom, Discussion room, Labs, Pantry, Small room, UPS; `Main.blend`, `MAIN-ROOF.fbx`) — looks like a college-building/lab level to be used as the raid map.

**Hostage models (added 2026-11-13):** `Assets/npc_casual_set_00/Prefabs/` → 4 civilian prefabs `npc_csl_00_character_01f_01`, `01m_03`, `02f_01`, `02m_01` (Chepatack npc_casual_set_00, free, Asset Store EULA). Only their dependencies were imported (not the full 274 MB pack). Rigs are **Humanoid**, no animations (use Mixamo). Prefabs use `Materials/` (contents replaced with the pack's URP Lit versions from `MaterialsUPR`, GUIDs kept). A few unused leftovers from a failed earlier import remain (`Mesh/Mesh_Parts`, `Prefabs/Prefabs_Parts`, `MaterialsUPR`, 6 unused materials).

**Mission assets (added 2026-11-13, all URP Lit):** Bomb `Assets/Bomb/TimeBomb.fbx` (decimated, ~400 KB; mat `Assets/Bomb/textures/TimeBomb.mat`, has emission map; credit "Time Bomb by barism09 (CC BY)"). Ammo `Assets/AmmoBox/AmmoBox.FBX` (+ `Materials/AmmoBox.mat`; ignore demo `AmmoBox.unity`). Medkit `Assets/GeeKay3D/First-Aid-Set/Assets/Prefabs/FirstAidKit_Red.prefab` (Red/White/Green are URP; Biohazard is not). Mixamo clips (no .meta yet = not imported when last checked): `Assets/Animations/character@Death.fbx`; `Animations/Hostage/` Hostage_Scared@Praying, Hostage@Injured Run, Terrified_running@Goofy Running; `Animations/player_guard/` player@Walk Forward, guard@Run Forward, player_idle@Reloading, player@Hit Reaction, player_diffusing_bomb@Kneeling Inspecting, player_using_medkit@Drinking. Rigs: hostages + Insurgent guards are Humanoid; player `LowPolySoldiers_demo/models/Soldier_demo.FBX` and its demo_combat clips are **Generic** (Mixamo clips won't play on it until converted). SampleScene has 2 TerroristGuard instances (design needs 10). Active Input Handling is already Both.

**Third-party assets:** `LowPolySoldiers_demo` (player/enemy soldier models), `StudioBillion`, `01_AssetStore/DoorPackFree` (doors), `TextMesh Pro`, `Whiteboard`, `SoldierController.controller` (animator with Idle / Walk / Run states).

**Scripts (`Assets/Scripts/`, ~500 lines total). Only `PlayerMovement.cs` was actually read; other purposes are inferred from filenames — verify before relying on them:**

| Script | Purpose (brief) |
|---|---|
| `PlayerMovement.cs` | CharacterController-based walk/run/jump, camera-relative movement, Animator states Idle/Walk/Run. **Uses legacy `Input.GetAxisRaw`** (see tech debt) |
| `CameraController.cs` | Camera control |
| `ThirdPersonCamera.cs` | Third-person follow/orbit camera |
| `FollowPlayer.cs` | Simple follow-the-player helper |
| `CollisionAvoider.cs` | Collision avoidance helper |
| `DoorHandle.cs`, `PushFunc.cs`, `FollowPhysics.cs` | Door / physics-interaction pieces (VR-style door-handle leftovers from tutorial work) |
| `ChangeFloor.cs`, `Capsule.cs`, `Fan.cs`, `lightonoff.cs` | Misc. prototype/interaction scripts (floor change, capsule, fan spin, light toggle) |

**Not yet built:** shooting, weapons, health/damage, enemies/AI, loot, inventory, extraction zones, UI/HUD, save system, game manager.

## 4. Architecture & conventions

- Language: C#. One class per file, filename = class name. Prefer PascalCase for new scripts (existing `lightonoff.cs` is the exception — leave unless renaming via Unity).
- Put new code in `Assets/Scripts/<Feature>/` subfolders (Player, Weapons, Enemies, Loot, UI, Core). Existing scripts are flat in `Scripts/` — move only via Unity Editor to keep GUIDs.
- Data: use **ScriptableObjects** for items/weapons/enemy stats.
- Input: migrate to the **new Input System** (`InputSystem_Actions.inputactions`) for all new code.
- Enemies: NavMeshAgent + simple state machine.
- Keep systems decoupled (events/interfaces like `IDamageable`, `IInteractable`).
- Layers/tags to define when needed: Player, Enemy, Interactable, Loot.

## 5. Decisions log

| Date | Decision | Reason |
|---|---|---|
| 2026-11-05 | Use `ai.md` in project root as shared cross-chat memory; parent chat owns the plan | User request — continuity between Claude chats |
| 2026-11-05 | Single-player extraction shooter; AI enemies only | User request |

## 6. Known issues / tech debt

- `PlayerMovement.cs` uses legacy `Input` API while the project has the new Input System package → may throw `InvalidOperationException` if Active Input Handling is set to "Input System Package (New)" only. Check **Project Settings → Player → Active Input Handling** (set to *Both* or migrate).
- Prototype scripts are unorganized/flat; some are VR/tutorial leftovers (XR, XRI, DoorHandle, PushFunc, FollowPhysics) — decide whether to keep.
- Two uncommitted modified assets (scene + animator controller).
- Template leftovers: `Readme.asset`, `TutorialInfo`, `Samples`, `XR`, `XRI` — candidates for cleanup.

## 7. Open questions for the user

1. Camera: **first-person**, **third-person** (current prototype), or over-the-shoulder?
2. Setting/theme: keep the college-building map, or a different raid environment?
3. Weapons scope: hitscan only, or projectile ballistics? Number of weapons for v1?
4. Inventory style: Tarkov-like grid, or simple list/slots?
5. Persistence: persistent stash between raids (save file) in v1?
6. Team size / deadline (is this for a course deliverable?) — determines scope.

## 8. Next steps (prioritized)

1. Get answers to section 7 and fill section 2 firmly.
2. Fix input handling; build a clean Player (move, look, sprint, crouch) on the new Input System.
3. Health/damage system (`IDamageable`) + hitscan weapon with ammo/reload.
4. Basic enemy AI (NavMesh patrol/chase/attack) and bake NavMesh on the map.
5. Loot containers + item ScriptableObjects + simple inventory.
6. Extraction zone + raid timer + win/lose flow + HUD.
7. Stash/save, menu, polish.

## 9. Session log (newest last)

- **2026-11-05 — Parent chat:** Inspected project (Unity 6000.5.9f1, URP, 12 prototype scripts, soldier + building assets, 3 git commits). Created this `ai.md` with protocol, vision, status, conventions, open questions. No game code changed.
- **2026-11-13 — design chat:** Agreed mission design (bomb/hostages/lockdown/ammo/medkits, 5-hit health) and a milestone prompt (M0–M6) for the build session; not yet applied here. Imported 4 hostage NPCs from npc_casual_set_00 by extracting only their dependencies (Unity 6.5 Import window threw NullReferenceException in PackageImportTreeView; an earlier 14:38 import also failed with 'Cannot open file … for write'). Converted their materials to URP Lit. No scripts changed.
