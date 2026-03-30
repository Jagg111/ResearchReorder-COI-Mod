# COI Modding Reference

Technical deep-dive on game APIs and modding patterns for **Update 4 (v0.8.2c)**. Everything here has been verified by direct DLL inspection or in-game testing. See CLAUDE.md for project overview and scope.

> **Note on COI-Extended:** We initially used the COI-Extended mod as a reference, but it was built for an older game version. Its `IMod` constructor signature, UI base classes (`WindowView`, `BaseWindowController<T>`, `IToolbarItemInputController`), and other patterns either don't compile or don't exist in Update 4. We no longer use it as a reference — all patterns below are verified against the current game DLLs.

## DLL Inspection Tools

Game install path: `C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry`
Game DLLs: `<game>/Captain of Industry_Data/Managed/` (`Mafi.dll`, `Mafi.Core.dll`, `Mafi.Unity.dll`)

**ILSpy CLI** (`ilspycmd`) — installed globally via `dotnet tool install -g ilspycmd`. Decompiles game types to readable C# source. Essential for understanding how native UI is built.

```bash
# Decompile a specific type (fully qualified name)
ilspycmd "path/to/Mafi.Unity.dll" -t Mafi.Unity.Ui.Research.ResearchDetailUi

# Use with COI_ROOT env var
ilspycmd "$(cygpath -w "$COI_ROOT/Captain of Industry_Data/Managed/Mafi.Unity.dll")" -t Some.Type.Name
```

**PowerShell reflection** — for quick constructor/field/method checks without full decompilation:
```powershell
$asm = [System.Reflection.Assembly]::LoadFrom("path/to/Mafi.Unity.dll")
$type = $asm.GetType('Mafi.Unity.UiToolkit.Library.Panel')
$type.GetConstructors() | ForEach-Object { $_.GetParameters() }
```

## Game API — Research System

### Key Classes (`Mafi.Core.dll` / `Mafi.Core.Research` namespace)

| Class/Member | Purpose |
|---|---|
| `ResearchManager` | Manages all research; only one node researched at a time |
| `ResearchManager.CurrentResearch` | The node currently being researched |
| `ResearchManager.GetResearchNode(proto)` | Gets the runtime `ResearchNode` wrapper for a proto |
| `ResearchManager.TryStartResearch(proto, out msg)` | Starts researching a node |
| `ResearchManager.StopResearch()` | Stops current research |
| `ResearchNode` | Runtime wrapper for a research tree node |
| `ResearchNode.State` | Current state (queued, researching, done, etc.) |
| `ResearchNode.CanBeEnqueued` | Whether dependencies are valid for queuing |
| `ResearchNode.CanBeEnqueuedDirect` | Whether direct dependencies are satisfied |
| `ResearchNodeProto` | Data definition of a research tree node |
| `m_researchQueue` | **Private field** — `Queueue<ResearchNode>` (Mafi custom collection) |
| `TryEnqueueResearch` / `TryDequeueResearch` | **Internal** queue manipulation methods |
| `ResearchStartCmd` / `ResearchStopCmd` | Commands to start/stop research |

### Display Names for Research Nodes (verified via DLL inspection)

`ResearchNodeProto` inherits from `Mafi.Core.Prototypes.Proto`, which has a `Strings` property of type `Proto.Str` (a nested struct). The `Str` struct has two public fields:

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `LocStr` | Human-readable display name (e.g., "Water recovery") |
| `DescShort` | `LocStr` | Short description of the research node |

`LocStr` (`Mafi.Localization.LocStr` in `Mafi.dll`) is a struct with:
- `string Id` — localization key
- `string TranslatedString` — the translated human-readable text
- `ToString()` — returns the translated string
- Implicit conversion to `LocStrFormatted`

**To get a display name from a `ResearchNode`:**
```csharp
// Option 1: Direct field access
string name = node.Proto.Strings.Name.TranslatedString;

// Option 2: Via ToString()
string name = node.Proto.Strings.Name.ToString();

// Option 3: Implicit conversion to LocStrFormatted (for UI components)
LocStrFormatted formatted = node.Proto.Strings.Name;  // implicit LocStr -> LocStrFormatted
```

### Queue Access Strategy (verified in Phase 1)

The queue is `Queueue<ResearchNode>` (`Mafi.Collections.Queueue`), a custom Mafi collection. Accessed via reflection on private field `m_researchQueue`.

**Confirmed working:**
- `ResearchManager` obtained via `resolver.GetResolvedInstance<ResearchManager>().Value` in `Initialize()`
- `m_researchQueue` field found via `BindingFlags.NonPublic | BindingFlags.Instance`
- Queue is `IEnumerable` — can iterate items
- Each item is a `ResearchNode` (not `ResearchNodeProto`)
- `ResearchNode.ToString()` returns just the type name; need `Proto.Id` for display (but beware `AmbiguousMatchException` — use `DeclaredOnly` binding flag or cast to known type)

**Queueue<T> API for reordering (confirmed in Phase 2):**
- `PopAt(int index)` — removes and returns the item at the given index
- `EnqueueAt(T item, int index)` — inserts item at the given index (item first, index second!)
- Move pattern: `var item = queue.PopAt(fromIndex); queue.EnqueueAt(item, toIndex);`

**Queue manipulation behavior (verified in-game, Phase 2):**
- Directly mutating `m_researchQueue` via reflection works — no side effects observed
- Game UI (queue tooltip on beaker icon) updates immediately after queue mutation in `Initialize()`
- Save/reload preserves the reordered queue — the game serializes `m_researchQueue` as-is
- Currently-researching item (`CurrentResearch`) is NOT disrupted by reordering the queue behind it
- No events or notifications need to be fired after mutation — the game reads the queue state directly

## Modding API Resources

- Official repo: https://github.com/MaFi-Games/Captain-of-industry-modding
- Wiki (WIP): https://wiki.coigame.com/Modding
- Game assemblies: `Mafi.dll`, `Mafi.Core.dll`, `Mafi.Base.dll`, `Mafi.Unity.dll`
- Discord #modding-dev-general channel — community + dev support

### Mod Base Classes

| Class | When to use |
|---|---|
| `DataOnlyMod` | Simple mods that only modify data/prototypes |
| `IMod` | Full mods with UI, patches, and lifecycle hooks (**ResearchReorder uses this**) |

### `IMod` Implementation (Update 4 — verified working)

```csharp
public sealed class MyMod : IMod
{
    public string Name => "MyMod";
    public int Version => 1;
    public bool IsUiOnly => false;
    public ModManifest Manifest { get; }
    public Option<IConfig> ModConfig => Option<IConfig>.None;
    public ModJsonConfig JsonConfig { get; }

    // Constructor MUST take ModManifest as first param
    public MyMod(ModManifest manifest) {
        Manifest = manifest;
        JsonConfig = new ModJsonConfig(this);  // REQUIRED — null crashes the game
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded) { }
    public void ChangeConfigs(Lyst<IConfig> configs) { }
    public void RegisterPrototypes(ProtoRegistrator registrator) { }
    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool wasLoaded) { }
    public void EarlyInit(DependencyResolver resolver) { }
    public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues) { }
    public void Dispose() { }
}
```

**Important gotchas:**
- Constructor takes `ModManifest`, not `(CoreMod, BaseMod)` (older pattern)
- `JsonConfig` must be `new ModJsonConfig(this)`, never null
- `Manifest` property must be stored from constructor
- `Option<IConfig>` for ModConfig, not `IModConfig`

### Key Concepts
- **`RegisterPrototypes(ProtoRegistrator)`** — override to add/modify game data
- **`ModManifest`** — passed to constructor, contains mod metadata at runtime
- **`[GlobalDependency(RegistrationMode.AsEverything)]`** — attribute that auto-registers a class with the game's dependency injection system

## Reflection Patterns

Standard C# reflection for accessing internal game state. This is the primary way to interact with game internals that aren't exposed via public API.

```csharp
// Access a private instance field
FieldInfo queueField = typeof(ResearchManager).GetField(
    "m_researchQueue",
    BindingFlags.NonPublic | BindingFlags.Instance
);
object queue = queueField.GetValue(researchManagerInstance);

// Access a private property
PropertyInfo prop = typeof(SomeClass).GetProperty(
    "PropertyName",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
);
MethodInfo setter = prop.GetSetMethod(nonPublic: true);
setter.Invoke(instance, new object[] { newValue });

// Access a private static field
FieldInfo staticField = typeof(SomeClass).GetField(
    "FIELD_NAME",
    BindingFlags.Static | BindingFlags.NonPublic
);
staticField.SetValue(null, newValue);
```

## Harmony Patching

**Lib.Harmony v2.2.2** (NuGet package) can be used for runtime method patching. We haven't needed it — reflection has been sufficient for all phases including injecting UI into the research screen (Phase 4).

```csharp
// Attribute-based patching
[HarmonyPatch(typeof(TargetClass), "MethodName")]
internal static class MyPatcher
{
    public static void Prefix() { /* runs before original */ }
    public static void Postfix() { /* runs after original */ }
}

// Manual patching
var harmony = new Harmony("com.mymod.patch");
harmony.Patch(originalMethod, prefix, postfix);
```

## Research Tree UI Classes (`Mafi.Unity.Ui.Research` namespace)

Found via DLL inspection of `Mafi.Unity.dll`. These are the classes that make up the in-game research tree screen.

### ResearchWindow (`Mafi.Unity.Ui.Research.ResearchWindow`)

- **Not public** — cannot be referenced directly in mod code, cannot be loaded via `typeof()`, and cannot be resolved via `DependencyResolver.GetResolvedInstance<T>()`. Must be accessed via reflection (see "Finding ResearchWindow at Runtime" below).
- **Extends `Mafi.Unity.UiToolkit.Library.Window`** (full-screen window, not a tool window overlay)
- Contains the full research tree view (the scrollable node graph)
- **Not in DI directly** — `ResearchWindow` itself is NOT in `AllResolvedInstances`. Only its nested `Controller` type is registered. The window is lazily created — `Option<ResearchWindow>` on the controller is empty until the player first opens the research tree.
- **Discovered at runtime via:** `ResearchWindow+Controller` (in DI) → `m_window` field on base class `WindowController<ResearchWindow>` → unwrap `Option<T>` via `HasValue` + `ValueOrNull`.

**Key nested types:**
  - `ResearchWindow+Controller` — controller for the window (not public). Handles toolbar integration and the `ToggleResearchWindow` shortcut.
  - `ResearchWindow+ResearchNodeUi` — **private nested class**, extends `Column`. Represents a single node in the tree view
    - Constructor: `ctor(ResearchManager, ResearchNode, Action<ResearchNodeUi> onClick, Action<ResearchNodeUi> onRightClick)`
    - Properties: `ResearchNode Node`, `ChildClickManipulator ClickManipulator`
    - Methods: `ToggleHighlightReason(bool, HighlightReason)`, `Matches(string[])`
  - `ResearchWindow+ConnectionsRenderer` — private, extends `UiComponent`. Draws lines between nodes
  - `ResearchWindow+HighlightReason` — private enum

**Key fields (verified at runtime via reflection — all private):**

| Field | Type | Purpose |
|-------|------|---------|
| `m_inputScheduler` | `IInputScheduler` | Input scheduler |
| `m_shortcutsManager` | `ShortcutsManager` | Keyboard shortcuts |
| `m_treeView` | `PanAndZoom` | The scrollable/zoomable tree view container |
| `m_nodesContainer` | `Row` | Container holding all node UIs |
| `m_searchField` | `TextField` | Search input field |
| `m_searchResultView` | `Row` | Search results display |
| `m_staticConnectionsRenderer` | `ConnectionsRenderer` | Draws static connection lines |
| `m_highlightedConnectionsRenderer` | `ConnectionsRenderer` | Draws highlighted connection lines |
| `m_completedConnectionsRenderer` | `ConnectionsRenderer` | Draws completed research connection lines |
| `m_positionsMap` | `Dict<,>` | Maps nodes to grid positions |
| `m_childrenMap` | `Dict<,>` | Maps parent nodes to children |
| `m_nodesViewMap` | `Dict<,>` | Maps `ResearchNode` to `ResearchNodeUi` elements |
| `m_connections` | `Dict<,>` | Connection data |
| `m_completedConnections` | `Set<>` | Set of completed connection paths |
| `m_highlightedConnections` | `Set<>` | Set of highlighted connection paths |
| `m_selectedNode` | `Option<ResearchNodeUi>` | **Currently selected node** — `IsNone` when no node is clicked. Key field for Phase 4 toggle logic. Use `HasValue`/`ValueOrNull` to check. |
| `m_nodeClickTime` | `float` | Timestamp of last click (for double-click detection) |
| `m_contentSize` | `Vector2i` | Size of the tree content |
| `m_searchResults` | `Lyst<>` | Current search results |
| `m_previousSearchQuery` | `string` | Previous search query string |
| `m_searchResultIndex` | `int` | Index in search results |

**NOTE:** `ResearchDetailUi` is NOT a field — it's a **child component** in the hierarchy. See "ResearchWindow Component Tree" below.

**Key methods (found via binary string analysis — all private):**

| Method | Purpose |
|--------|---------|
| `buildTree` | Constructs the visual tree from research node data |
| `updateResearchedLines` | Updates connection line visuals for completed research |
| `updateSelectionHighlights` | Updates highlight visuals when selection changes |
| `handleNodeClicked` | Left-click handler — shows detail panel, sets `m_selectedNode` |
| `handleNodeRightClick` | Right-click handler |
| `selectAndFocusNode` | Selects a node and pans the view to center on it |
| `highlightNodeParentsRecursive` | Recursively highlights parent nodes |
| `nodeDoubleClicked` | Double-click handler (likely starts research) |
| `moveSearchResultSelection` | Navigates between search results |
| `openResearch` | Opens/starts research for a specific node |
| `getNodePos` | Gets a node's position in the tree layout |

### ResearchWindow Component Tree (verified at runtime)

```
ResearchWindow (extends Window)
└── [Frame] UiComponent
    ├── UiComponent (shadow/overlay)
    ├── Column (window chrome)
    │   ├── WindowBackground
    │   └── Column (= Body)
    │       ├── Row                          ← main content row
    │       │   ├── PanAndZoom               ← scrollable/zoomable research tree
    │       │   │   └── ScrollBoth → UiComponent (nodes container)
    │       │   └── ResearchDetailUi         ← RIGHT-HAND DETAIL PANEL (child[1] of Row)
    │       │       ├── UiComponent
    │       │       ├── Column → Column
    │       │       ├── UiComponent
    │       │       └── UiComponent
    │       └── PanelTop                     ← bottom toolbar/search bar
    │           ├── UiComponent
    │           ├── Row (buttons, search)
    │           └── UiComponent
    └── UiComponent (input blocking overlay)
```

**Key navigation path to inject a sibling panel:**
`Body` field (from `Window` base) → `AllChildren[0]` (the `Row`) → `AllChildren[1]` is `ResearchDetailUi`. To add our queue panel as a sibling, `Add()` to the same `Row` parent.

### ResearchDetailUi (`Mafi.Unity.Ui.Research.ResearchDetailUi`)

- **Public class**, extends `Panel` (which extends `PanelBase<Panel, Column>`)
- This is the **right-hand detail panel** shown when you click a research node
- **Not a field on ResearchWindow** — it's a **child component** added to `Body → Row` at index 1
- **No `[GlobalDependency]` attribute** — NOT registered in DI. Created directly by `ResearchWindow`. Cannot be resolved via `DependencyResolver.GetResolvedInstance<ResearchDetailUi>()`.
- Constructor: `ctor(UiContext, ProtoPopupProvider, NewInstanceOf<InfoPopup>, ResearchManager, OrbitManager)`
- **Key method:** `void Value(ResearchNode node)` — sets/updates the panel to show details for the given node

**Fields (verified via reflection, all private):**

| Field | Type | Purpose |
|-------|------|---------|
| `m_context` | `UiContext` | UI context reference |
| `m_popupProvider` | `ProtoPopupProvider` | Popup provider for info tooltips |
| `m_infoPopup` | `ProtoPopup` | Info popup instance |
| `<Node>k__BackingField` | `ResearchNode` | The currently displayed research node |
| `m_unlocksMainCol` | `Column` | Container for "Unlocks" section |
| `m_unlocksRow` | `Row` | Row for unlock icons |
| `m_reqsMainCol` | `Column` | Container for "Requires" section |
| `m_reqsRow` | `Row` | Row for requirement icons |
| `m_spacePointsReqTile` | `IconWithTitle` | Space points requirement display |
| `m_recipes` | `RecipesColumn` | Recipes section |
| `m_recipesCol` | `Column` | Container for recipes |
| `m_recipesCache` | `Dict<IProto, StaticRecipeUi>` | Cache of recipe UI elements |
| `MIN_WIDTH` | `Px` | Minimum panel width (static) |
| `MAX_RECIPES_HEIGHT` | `Px` | Maximum recipes section height (static) |
| `PANEL_OVERHEAD` | `Px` | Panel overhead space (static) |

**Properties:**
- `ResearchNode Node { get; }` — the currently displayed node

**Nested types (all private):**
  - `ProtoRequiredLock`, `GlobalStatsLock`, `LabTierLock`, `SpaceStationLock` — lock condition displays
  - `IconWithTitle` — base class for lock displays, extends `Column`

### ResearchThemeHelper (`Mafi.Unity.Ui.Research.ResearchThemeHelper`)

- **Public static class** — helper for node visual styling
- `static ColorRgba ResolveTitleBgColor(bool isBlocked, InfoForUi nodeInfo)`
- `static Option<T> ResolveStateIcon(bool isBlocked, InfoForUi nodeInfo, out LocStrFormatted tooltip)`

### CurrentResearchDisplayHud (`Mafi.Unity.Ui.Hud.CurrentResearchDisplayHud`)

- **Public class**, extends `Row`
- The HUD element in the top toolbar showing current research progress
- Method: `void SetAvailableSpace(float space)`

### ResearchTab (`Mafi.Unity.Ui.Hud.Notifications.ResearchTab`)

- **Public class**, extends `NotificationsTabBase` (which extends `FrostedPanelWithHeader`)
- The research notifications tab
- Nested type: `MessageUi` (private) — individual notification items

### ResearchLabInspector (`Mafi.Unity.Ui.Inspectors.ResearchLabInspector`)

- Inspector panel for research lab buildings (not the tree screen)

### Key Actions/Commands (found in binary strings)

| String | Purpose |
|--------|---------|
| `OpenResearchFor` | Opens research tree focused on a specific node |
| `OpenResearchForRecipe` | Opens research for a recipe |
| `OpenResearch_Action` | Action to open research window |
| `ToggleResearchWindow` | Toggle research window visibility |
| `StartNewResearch_Action` | Start new research |
| `StartResearch_Action` | Start research |
| `ResearchQueueDequeueCmd` | Command to enqueue/dequeue research: `new ResearchQueueDequeueCmd(proto, isEnqueue: bool)` |

### Finding ResearchWindow at Runtime (Verified, Working)

Since `ResearchWindow` is not public and not directly in DI, it must be found via its nested Controller:

**Step 1: Find ResearchWindow+Controller in DI**
```csharp
// In a [GlobalDependency] constructor that takes DependencyResolver
object rwController = null;
foreach (object obj in resolver.AllResolvedInstances) {
    if (obj.GetType().FullName == "Mafi.Unity.Ui.Research.ResearchWindow+Controller") {
        rwController = obj;
        break;
    }
}
```

**Step 2: Get the ResearchWindow from the controller's m_window field**
```csharp
// m_window is on the base class WindowController<ResearchWindow>
// It's Option<ResearchWindow> — lazily created, empty until research tree is first opened
FieldInfo windowField = rwController.GetType().BaseType?.GetField("m_window",
    BindingFlags.NonPublic | BindingFlags.Instance);
object optionValue = windowField.GetValue(rwController);

// Unwrap Option<T> — use HasValue + ValueOrNull (NOT Value, which doesn't exist)
var optType = optionValue.GetType();
bool hasValue = (bool)optType.GetProperty("HasValue").GetValue(optionValue);
if (hasValue) {
    object researchWindow = optType.GetProperty("ValueOrNull").GetValue(optionValue);
}
```

**Step 3: Navigate to ResearchDetailUi (child component, not a field)**
```csharp
// ResearchDetailUi is NOT a field — it's a child component in the visual tree.
// Use recursive search through AllChildren to find it by type name:
var rwComponent = (UiComponent)researchWindow;
var contentRow = FindParentOfType(rwComponent, "ResearchDetailUi");
// contentRow is the Row containing both PanAndZoom and ResearchDetailUi
```

**Step 4: Monitor selection state**
```csharp
// m_selectedNode is Option<ResearchNodeUi> (not a nullable reference)
FieldInfo selectedField = researchWindow.GetType().GetField(
    "m_selectedNode",
    BindingFlags.NonPublic | BindingFlags.Instance
);
object selectedOption = selectedField.GetValue(researchWindow);
// Unwrap with HasValue + ValueOrNull, same as m_window above
```

**Important notes (verified in-game):**
- `ResearchWindow` is NOT in `AllResolvedInstances` — only its `Controller` is
- The window is **lazily created** — `Option` is empty at construction, populated after first research tree open. Must retry later (e.g., in `Activate()`)
- `Option<T>` API: use `HasValue` (bool) and `ValueOrNull` (returns T or null). There is NO `Value` property.
- `m_selectedNode` is `Option<ResearchNodeUi>`, NOT a nullable reference — must unwrap with `HasValue`/`ValueOrNull`
- `ResearchDetailUi` is NOT a field on `ResearchWindow` — it's a child component at `Body → Row[0] → child[1]`
- The `ResearchWindow+Controller` has only 2 fields: `m_researchManager` (ResearchManager) and `Context` (ControllerContext)
- The controller's base type is `WindowController<ResearchWindow>` which has the `m_window` field

### Injecting UI into ResearchWindow (Verified in Phase 4b)

**Working pattern:** Subscribe to `IUnityInputMgr.ControllerActivated`, detect research tree controller, then search the component tree and `Add()` our panel to the content Row.

```csharp
// In constructor — subscribe to event
_inputMgr.ControllerActivated += OnControllerActivated;

// Event handler — detect research tree opening
private void OnControllerActivated(IUnityInputController controller) {
    if (!ReferenceEquals(controller, _rwController)) return;
    // Extract window if not yet found, then inject panel
}

// Injection — find ResearchDetailUi's parent Row and add our panel
var rwComponent = (UiComponent)_researchWindow;
var contentRow = FindParentOfType(rwComponent, "ResearchDetailUi");
contentRow.Add(ourPanel);  // Added as sibling of ResearchDetailUi
```

**Key finding: `FindParentOfType` recursive search** — `ResearchDetailUi` is NOT a field, so we search the component tree recursively using `AllChildren` (direct children only) and match by `child.GetType().Name == "ResearchDetailUi"`. Returns the parent `Row`.

**Critical timing discovery:** On first open, `ControllerActivated` fires BEFORE the `ResearchWindow` is created — `Option<ResearchWindow>` is still empty. The window is built during/after the controller's `Activate()` method, not before. Fix: use `VisualElement.schedule.Execute()` to defer extraction by one frame (~60ms). Also subscribe to `ControllerDeactivated` as a safety net — when the window closes, it definitely exists.

```csharp
// In OnControllerActivated, if window not found:
// Use any live UiComponent's RootElement for scheduling (e.g., ToolbarHud's m_mainContainer)
_schedulerSource.RootElement.schedule.Execute(() => {
    TryExtractResearchWindow();
    if (_researchWindowFound) TryInjectPanel();
});
```

**Recurring polling with `schedule.Execute()`** — Can be used as a per-frame polling loop by having the callback schedule itself again. Useful for monitoring game state changes (e.g., watching `m_selectedNode`) without needing `InputUpdate()` (which is tied to your controller being active). Use a `bool` flag to start/stop the loop:

```csharp
private bool _pollingActive;

private void StartPolling() {
    _pollingActive = true;
    Poll();
}

private void Poll() {
    if (!_pollingActive) return;
    // ... do your per-frame check here ...
    // Schedule next check on the next frame
    someComponent.RootElement.schedule.Execute(() => Poll());
}

// To stop: set _pollingActive = false (e.g., in OnControllerDeactivated)
```

**Panel visibility coordination (asymmetric toggle)** — When swapping between two panels (e.g., a custom panel and `ResearchDetailUi`), naive `SetVisible` calls cause 1-frame flicker. The fix is to treat each direction differently:
- **Hiding game panel (deselect):** Force-hide `ResearchDetailUi` immediately, show your panel. Prevents both panels showing for 1 frame.
- **Showing game panel (select):** Do NOT force-show `ResearchDetailUi` — wait until `IsVisible()` returns true (meaning the game has updated content and made it visible), THEN hide your panel. Prevents (a) stale content flashing and (b) a 1-frame gap with no panel.

```csharp
if (nodeSelected) {
    // Wait for game to show detail panel (with updated content) before hiding ours
    if (detailPanel.IsVisible()) {
        ourPanel.SetVisible(false);
    }
} else {
    // Immediately show ours and hide detail panel — no overlap
    ourPanel.SetVisible(true);
    detailPanel.SetVisible(false);
}
```

### Input Controller System (`Mafi.Unity.InputControl`)

The game manages full-screen windows (research, map, stats, etc.) via an input controller system:

**`IUnityInputMgr`** (`Mafi.Unity` namespace, NOT `Mafi.Unity.InputControl`) — central manager for input controllers (public interface):

| Method | Purpose |
|--------|---------|
| `ActivateNewController(IUnityInputController)` | Activates a controller (shows its window) |
| `DeactivateController(IUnityInputController)` | Deactivates a controller (hides its window) |
| `ToggleController(IUnityInputController)` | Toggles controller on/off |
| `DeactivateAllControllers()` | Hides all windows |
| `IsWindowControllerOpen()` | Returns true if any window controller is active |

**Events on `IUnityInputMgr`:**
- `ControllerActivated` — fires when any controller is activated. Handler: `Action<IUnityInputController>`
- `ControllerDeactivated` — fires when any controller is deactivated. Handler: `Action<IUnityInputController>`

These events could be used to detect when the research window opens/closes without needing to poll.

**`ShortcutsManager`** — manages keyboard shortcuts (public class):
- `ToggleResearchWindow` — `KeyBindings` property for the research window toggle (default: G key)
- Other toggle properties: `ToggleMap`, `ToggleStats`, `ToggleConsole`, `ToggleRecipeBook`, etc.
- Method: `IsDown(KeyBindings)`, `IsUp(KeyBindings)`, `IsOn(KeyBindings)` — check key state

**`ShortcutsMap`** — stores the actual key bindings (get/set):
- All `Toggle*` properties have both getters and setters
- Backing fields use `<PropertyName>k__BackingField` pattern

### UiComponent Parent/Hierarchy Access

`UiComponent` (base class for all UI components) provides these hierarchy methods:

| Member | Type | Purpose |
|--------|------|---------|
| `Parent` | `Option<UiComponent>` | Parent component (None if root) |
| `HasParent` | `bool` | Whether component has a parent |
| `Root` | `Option<UiRoot>` | The root of the UI tree |
| `RootElement` | `VisualElement` | The underlying Unity VisualElement |
| `IsVisibleInHierarchy` | `bool` | Whether visible considering parent visibility |
| `RemoveFromHierarchy()` | method | Removes this component from its parent |
| `TryGetClosestParent(Func, out UiComponent)` | method | Finds ancestor matching a condition |
| `AttachToRoot(UiRoot)` | method | Attaches to a UI root |
| `GetChildrenContainer()` | method | Gets the VisualElement that holds children |

### Harmony Evaluation (Update 4)

**Status: Harmony is NOT bundled with the game.**
- No `0Harmony.dll` in the game's Managed folder
- No BepInEx or plugin loader framework installed
- The official modding repo and ExampleMod make no mention of Harmony
- If needed, Harmony would have to be added as a NuGet package and its DLL distributed alongside the mod

**Current assessment: Harmony is NOT needed for Phase 4.** The reflection-based approach (finding `ResearchWindow` via its Controller's `m_window` field, navigating the component tree to find `ResearchDetailUi`, polling `m_selectedNode`) is working without patching any game methods. Only reconsider Harmony if:
1. We need to intercept `handleNodeClicked` to detect selection changes (polling `m_selectedNode` should work instead)
2. We need to modify the Escape key behavior

### Built-in Reorder Support (Drag-and-Drop)

The game has a built-in `Reorderable` manipulator that provides drag-and-drop reordering for any list of UI elements. This is the same system used by building recipe lists (e.g., Assembly III), train schedule items, launch pad cargo buffers, and train car designers.

#### `Reorderable` Class — Full API

**Location:** `Mafi.Unity.UiToolkit.Component.Manipulators.Reorderable`
**Visibility:** **Public class** — safe to use from mods.
**Base class:** `UnityEngine.UIElements.Manipulator`

**Constructor:**
```csharp
public Reorderable(VisualElement dragHandle = null, bool lockDragToAxis = true)
```
- `dragHandle` — the element the user grabs to start dragging. If `null`, the entire `target` element is the drag handle. When provided, drag start threshold is 1px (very responsive); when null, threshold is 5px (prevents accidental drags from clicks).
- `lockDragToAxis` — if `true` (default), constrains dragging to the parent container's flex direction (vertical for Column, horizontal for Row). Almost always want `true`.

**Events:**
```csharp
event Action<int, int> OnOrderChanged;  // (oldIndex, newIndex) — fired when drag completes at a new position
event Action OnBeginDrag;               // fired when drag starts (after threshold met)
event Action OnEndDrag;                 // fired when drag ends (whether position changed or not)
```

**How it works internally:**
1. User presses on the `dragHandle` element → pointer captured
2. User moves pointer past threshold → `OnDragStart()` fires:
   - Records the starting index of `target` within its parent container
   - Removes `target` from the container and places it in an absolute-positioned `m_dragTargetContainer`
   - Inserts a `m_shadowSpace` placeholder (same size) at the original position
   - Starts a `schedule.Execute()` loop for `dragUpdateLoop`
3. During drag: the target container follows the cursor, and the shadow space swaps positions with sibling elements as the dragged item passes their center point (with hysteresis to prevent flickering)
4. Auto-scrolling: if the parent is inside a `ScrollView`, automatically scrolls when dragging near the edges (proportional speed, 60–600px/s)
5. On pointer up → `OnDragEnd()`: inserts `target` back at the shadow space's current position, fires `OnOrderChanged(oldIndex, newIndex)` if position changed

**Key constraint:** The `Reorderable` manipulator operates on the **visual** children of `target.parent.contentContainer`. It reorders visual elements only. You must handle the data model update yourself in the `OnOrderChanged` callback.

#### Usage Pattern — How the Game Uses It

All game consumers follow the same pattern:

```csharp
// 1. Create a drag handle element (Column with drag icon, or any element)
Column dragHandle = new Column();
dragHandle.Class(Cls.reorderHandle, Cls.reorderHandleAlphaHover)
    .Background(3224115)       // dark gray background
    .BorderRight(1.px(), 2763306)
    .BorderRadiusLeft(4)
    .JustifyItemsCenter()
    .Padding(1.pt());
dragHandle.Add(new Icon("Assets/Unity/UserInterface/General/Drag.svg")
    .Opacity(0.6f).Size(10.px()).AlignSelfCenter());

// 2. Create the Reorderable manipulator with the drag handle's RootElement
Reorderable reorderable = new Reorderable(dragHandle.RootElement);

// 3. Subscribe to OnOrderChanged to update your data model
reorderable.OnOrderChanged += (int oldIndex, int newIndex) => {
    // Move item in your data structure from oldIndex to newIndex
};

// 4. Add the manipulator to the ROW element (the item being reordered)
myRowElement.AddManipulator(reorderable);
```

**Important:** The manipulator is added to the **item** element (the row that gets dragged), but the **drag handle** passed to the constructor is the grab area. The item must be a direct child of the container whose children get reordered.

#### Concrete Examples from the Game

**1. Machine Recipe List (Assembly III, etc.) — `MachineRecipeUi`**
```csharp
// Drag handle is a Column with drag icon, positioned absolutely on the left
Column column = new Column();
column.Class(Cls.reorderHandle, Cls.reorderHandleAlphaHover)
    .Background(3224115)
    .BorderRight(HANDLE_COLUMN_BORDER, 2763306)
    .BorderRadiusLeft(4)
    .AbsolutePosition(top: 0.px(), bottom: 0.px(), left: 0.px())
    .JustifyItemsCenter().Padding(HANDLE_PADDING);
column.Add(new Icon("Assets/Unity/UserInterface/General/Drag.svg")
    .Opacity(0.6f).Size(HANDLE_SIZE).AlignSelfCenter());
MainRow.MarginLeft(HANDLE_COLUMN_TOTAL_WIDTH);  // offset content for the handle

Reorderable reorderable = new Reorderable(column.RootElement);
reorderable.OnOrderChanged += onReorder;  // Action<int, int>
AddManipulator(reorderable);  // added to the MachineRecipeUi itself

// The onReorder callback sends a command:
onReorder = (oldIdx, newIdx) => {
    ScheduleCommand(new ReorderRecipeCmd(entity.Id, oldIdx, newIdx));
};
```

**2. Launch Pad Cargo Buffers — `BufferUi`**
```csharp
// Uses the pre-built LeftDragHandle component
LeftDragHandle leftDragHandle = AddAndReturn(new LeftDragHandle());
Reorderable reorderable = new Reorderable(leftDragHandle.RootElement);
reorderable.OnOrderChanged += onReorder;
AddManipulator(reorderable);
```

**3. Train Schedule Items — `ScheduleItemUi`**
```csharp
// Drag handle is a Column on the right side with CSS class
Column column = new Column();
column.Class(Cls.dragHandle).AlignSelfStretch();
// Wrapped in a row with border and dark background

Reorderable reorderable = new Reorderable(column.RootElement);
reorderable.OnOrderChanged += (_, newIndex) => {
    inputScheduler.ScheduleInputCmd(
        new ReorderTrainLineScheduleItemCmd(scheduleItemId, newIndex));
};
AddManipulator(reorderable);
```

**4. Train Car Designer — `TrainPreviewCar`**
```csharp
// The icon itself is the drag handle (no separate handle element)
m_icon = new Icon().Size(Px.Auto, HEIGHT).Class(Cls.reorderHandle);
m_reorderable = new Reorderable(m_icon.RootElement);
m_reorderable.OnOrderChanged += onOrderChanged;
// Hides floater tooltip during drag:
m_reorderable.OnBeginDrag += () => { optionalFloater = Option<UiComponent>.None; };
m_reorderable.OnEndDrag += () => { optionalFloater = floater; };
AddManipulator(m_reorderable);
```

#### Pre-Built Drag Handle: `LeftDragHandle`

**Location:** `Mafi.Unity.Ui.Library.LeftDragHandle`
**Base class:** `Column`

A ready-made drag handle widget. Styled with dark background, right border, drag icon, absolute-positioned on the left side. Use this for the simplest integration:

```csharp
public class LeftDragHandle : Column
{
    public LeftDragHandle()
    {
        this.Class(Cls.reorderHandle, Cls.reorderHandleAlphaHover)
            .Background(3224115)
            .BorderRight(1.px(), 2763306)
            .BorderRadiusLeft(4)
            .AbsolutePosition(top: 0.px(), bottom: 0.px(), left: 0.px())
            .JustifyItemsCenter().Padding(1.pt());
        Add(new Icon("Assets/Unity/UserInterface/General/Drag.svg")
            .Opacity(0.6f).Size(10.px()).AlignSelfCenter());
    }
}
```

#### CSS Classes for Drag Handles

- `Cls.dragHandle` — used by train schedule items (simple drag area, no icon)
- `Cls.reorderHandle` — used by recipe lists, launch pads, train cars (styled drag area)
- `Cls.reorderHandleAlphaHover` — hover effect (increased opacity on hover)

#### `ReorderableMultiColumns` (Private, Not Usable by Mods)

`PinnedProductsHud.ReorderableMultiColumns` is a **private nested class** used for multi-column reordering in the pinned products sidebar. It implements its own drag logic (not using `Reorderable`) and redistributes items across columns. **Not accessible from mods** — included here for reference only.

#### Key Design Notes for Our Research Queue

1. **Container requirement:** All draggable items must be direct children of the same parent container. The `Reorderable` manipulator reads `target.parent.contentContainer.Children()` to find siblings.
2. **Visual-only reorder:** `Reorderable` moves visual elements. Our `OnOrderChanged` callback must call our existing `MoveItem(oldIndex, newIndex)` helper (which uses `PopAt` + `EnqueueAt`).
3. **ScrollView support:** Built-in auto-scroll when the list is inside a `ScrollView` and user drags near edges. Our `ScrollColumn` body should work automatically.
4. **No data binding:** The manipulator doesn't know about our data. After the data model is updated, we should rebuild the visual list to ensure consistency (or trust the visual reorder if it matches the data operation).
5. **`LeftDragHandle` is the easiest path:** Add a `LeftDragHandle` to each queue row, pass its `RootElement` to `new Reorderable(...)`, wire `OnOrderChanged` to our queue mutation logic.
6. **`AddManipulator` is on `UiComponent`:** Call `row.AddManipulator(reorderable)` directly on the Mafi `UiComponent`, NOT on `row.RootElement`. Importing `UnityEngine.UIElements` causes namespace collisions with Mafi types (Label, Column, etc.). The `UiComponent.AddManipulator()` method delegates to the underlying `VisualElement` internally.
7. **`LeftDragHandle` is absolute-positioned:** The `Cls.reorderHandle` CSS class positions the handle absolutely. For inline flex-child drag handles, create a custom `Column` with the same visual styling (background `3224115`, border-right `1px 2763306`, border-radius-left 4, padding 1pt) but without `Cls.reorderHandle`.

## UI Window Patterns (Update 4)

### Working Pattern: PanelWithHeader + IToolbarItemController + ToolbarHud

**Verified working in Update 4 (v0.8.2c).** This is the correct way to create mod windows.

The game uses **UiToolkit components** directly — there is no special "Window" base class for mods. Game UI like `ResearchDetailUi` extends `Panel`, `CurrentResearchDisplayHud` extends `Row`.

**Key types:**
- `PanelWithHeader` (`Mafi.Unity.UiToolkit.Library`) — panel with collapsible title bar, good for windows
- `IToolbarItemController` (`Mafi.Unity.UiStatic.Toolbar`) — extends `IUnityInputController`, interface for toolbar integration
- `ToolbarHud` (`Mafi.Unity.Ui.Hud`) — the game's toolbar manager
- `ControllerConfig` (`Mafi.Unity.InputControl`) — pre-built configs like `ControllerConfig.Window`
- `KeyBindings` (`Mafi.Unity.InputControl`) — hotkey definitions

**Window view (the panel):**
```csharp
[GlobalDependency(RegistrationMode.AsEverything)]
public class MyWindowView : PanelWithHeader {
    public MyWindowView()
        : base(new LocStrFormatted("Window Title")) {
        this.Size(new Px(340), new Px(400));  // extension method
        this.SetVisible(false);               // hidden by default
    }
}
```

**Window controller (toolbar button + hotkey + show/hide):**
```csharp
[GlobalDependency(RegistrationMode.AsEverything)]
public class MyWindowController : IToolbarItemController {
    private readonly MyWindowView _view;
    private bool _isVisible;

    public ControllerConfig Config => ControllerConfig.Window;
    public bool IsVisible => _isVisible;
    public bool DeactivateShortcutsIfNotVisible => false;
    public event Action<IToolbarItemController> VisibilityChanged;

    public MyWindowController(MyWindowView view, ToolbarHud toolbar) {
        _view = view;

        // Register toolbar button with F9 hotkey
        toolbar.AddMainMenuButton(
            new LocStrFormatted("Button Text"),
            this,
            "",       // icon asset path (empty = no icon)
            1500f,    // sort order
            sm => KeyBindings.FromKey(KbCategory.Windows, ShortcutMode.Game, KeyCode.F9)
        );

        // Register the panel as a tool window
        toolbar.AddToolWindow(_view);
    }

    public void Activate() {
        _isVisible = true;
        _view.SetVisible(true);
        VisibilityChanged?.Invoke(this);
    }

    public void Deactivate() {
        _isVisible = false;
        _view.SetVisible(false);
        VisibilityChanged?.Invoke(this);
    }

    public bool InputUpdate() => false;
}
```

**Behavioral notes:**
- Toolbar button appears in bottom toolbar when window is active (standard game behavior)
- Constructor-based registration on `ToolbarHud` works — no special lifecycle hook needed
- Both classes need `[GlobalDependency(RegistrationMode.AsEverything)]` for DI auto-registration
- `UiComponent.SetVisible(bool)` controls visibility; also available as extension: `.Show()`, `.Hide()`, `.Visible(bool)`

### ToolbarHud Internals (for advanced modding)

`ToolbarHud` has these internal fields (discovered via reflection, useful for understanding layout):

| Field | Type | Purpose |
|-------|------|---------|
| `m_mainContainer` | `Column` | Main toolbar container |
| `m_buttonsPanel` | `Row` | Panel holding toolbar buttons |
| `m_menu` | `ToolbarMenu` | The expandable menu |
| `m_toolboxContainer` | `Row` | Container for toolboxes |
| `m_toolWindowContainer` | `Row` | **Container where `AddToolWindow()` places windows** |
| `m_primaryToolButtonsRow` | `Row` | Primary tool buttons |
| `m_toolButtonsGrid` | `Grid` | Grid of tool buttons |
| `m_inputMgr` | `IUnityInputMgr` | Reference to the input manager |
| `m_shortcutsManager` | `ShortcutsManager` | Reference to shortcuts manager |
| `m_resolver` | `DependencyResolver` | Reference to the DI resolver |
| `m_currentController` | `Option<IToolbarItemController>` | Currently active controller |
| `PopupProvider` | `ProtoPopupProvider` | **Public field** — popup provider (useful for creating tooltips) |
| `ProtoInfoPopup` | `ProtoPopup` | **Public field** — info popup instance |

### API Signatures (verified via DLL inspection)

| Method | Signature |
|--------|-----------|
| `ToolbarHud.AddMainMenuButton` | `Button AddMainMenuButton(LocStrFormatted name, IToolbarItemController controller, String iconAssetPath, Single order, Func<ShortcutsManager, KeyBindings> shortcut)` |
| `ToolbarHud.AddToolWindow` | `void AddToolWindow(UiComponent window)` |
| `ToolbarHud.AddToolButton` | `Button AddToolButton(LocStrFormatted name, IToolbarItemController controller, String iconAssetPath, Single order, Func<ShortcutsManager, KeyBindings> shortcut, Nullable<TutorialId> tutorialId)` |
| `KeyBindings.FromKey` | `static KeyBindings FromKey(KbCategory category, ShortcutMode mode, KeyCode code)` |
| `Panel` ctor | `Panel(bool noBolts = false)` — default includes bolts (same as `ResearchDetailUi`) |
| `PanelWithHeader` ctor | `PanelWithHeader(Nullable<LocStrFormatted> title)` |
| `Column` ctor | `Column(Px gap)` or `Column(Outer outer, Inner inner, Nullable<Px> gap)` |
| `Row` ctor | `Row(Px gap)` or `Row(Outer outer, Inner inner, Nullable<Px> gap)` |
| `ScrollColumn` ctor | `ScrollColumn()` (parameterless) |
| `Label` ctor | `Label(LocStrFormatted text = default)` — normal-case text, preferred for panels |
| `Display` ctor | `Display()` or `Display(LocStrFormatted text)` — ALL CAPS, for HUD elements |

### UiToolkit Component Hierarchy (key types)

| Component | Base | Namespace | Purpose |
|-----------|------|-----------|---------|
| `UiComponent` | — | `Mafi.Unity.UiToolkit.Component` | Base class for all UI components |
| `Column` | `UiComponentDecorated<VisualElement>` | `Mafi.Unity.UiToolkit.Library` | Vertical stack |
| `Row` | `UiComponentDecorated<VisualElement>` | `Mafi.Unity.UiToolkit.Library` | Horizontal stack |
| `ScrollColumn` | `ScrollBase` | `Mafi.Unity.UiToolkit.Library` | Scrollable vertical list |
| `ScrollRow` | `ScrollBase` | `Mafi.Unity.UiToolkit.Library` | Scrollable horizontal list |
| `ScrollBoth` | `ScrollBase` | `Mafi.Unity.UiToolkit.Library` | Scrollable in both directions |
| `PanelWithHeader` | `Column` | `Mafi.Unity.UiToolkit.Library` | Panel with collapsible title bar |
| `Panel` | `PanelBase<Panel, Column>` | `Mafi.Unity.UiToolkit.Library` | Panel without header |
| `ButtonText` | `Button` | `Mafi.Unity.UiToolkit.Library` | Text button |
| `ButtonIcon` | `Button` | `Mafi.Unity.UiToolkit.Library` | Icon button |
| `ButtonIconText` | `ButtonRow` | `Mafi.Unity.UiToolkit.Library` | Button with icon and text |
| `Display` | `UiComponent` | `Mafi.Unity.Ui.Library` | **Text label** — the primary text display component |
| `DisplayWithIcon` | `Row` | `Mafi.Unity.Ui.Library` | Text label with icon |
| `Icon` | `UiComponent<VisualElement>` | `Mafi.Unity.UiToolkit.Library` | Icon image |
| `Img` | `UiComponentDecorated<VisualElement>` | `Mafi.Unity.UiToolkit.Library` | Image component |

### Adding Children to Containers (verified via DLL inspection)

**`UiComponent` base class provides these methods for all components:**
```csharp
void Add(UiComponent child)                              // Add single child
T AddAndReturn<T>(T child)                               // Add and return (for chaining)
void Add(IEnumerable<UiComponent> children)              // Add multiple children
void Add(params UiComponent[] children)                  // Add multiple (params)
void InsertAt(int index, UiComponent child, bool alreadyInHierarchy)  // Insert at position
void RemoveFromHierarchy()                               // Remove self from parent
int ChildrenCount { get; }                               // Number of children
IEnumerable<UiComponent> AllChildren { get; }            // Iterate children
```

**Extension methods (from `UiComponentExtensions`):**
```csharp
UiComponentExtensions.SetChildren(component, children)   // Replace all children
UiComponentExtensions.ReverseChildren(component)          // Reverse child order
UiComponentExtensions.ChildAtOrNone(component, index)     // Get child by index (Option<T>)
UiComponentExtensions.ChildAtOrDefault(component, index)  // Get child by index (null if missing)
```

**`Panel` / `PanelWithHeader` methods for adding to the body (from `PanelBase`):**
```csharp
Panel BodyAdd(params UiComponent[] children)                    // Add children to body
Panel BodyAdd(Action<UiComponentExtensions> applyStyles, params UiComponent[] children)  // Add with styles
Panel BodyGap(Px gap)                                            // Set gap between body items
Panel ReducedPadding()                                           // Less internal padding
Panel BrightText()                                               // Lighter text color
Panel StyleFloater()                                             // Floating panel style
Panel BackgroundStyle(DisplayState state)                        // Background tint (Neutral/Inactive/Important/Positive/Warning/Danger)
Column Body { get; }                                             // Direct access to body Column
static Px PADDING                                                // Panel's internal padding value (use for margin offsets)
```

**Usage patterns:**
```csharp
// Adding to a Column/Row directly
var column = new Column(new Px(4));  // 4px gap
column.Add(child1);
column.Add(child2, child3);

// Adding to PanelWithHeader body
panel.BodyAdd(label1, label2);       // Add to body section
panel.Body.Add(someComponent);       // Or access body directly

// ScrollColumn — just add children normally (inherits from ScrollBase > UiComponent)
var scroll = new ScrollColumn();
scroll.Add(child1);
```

### Native Panel Styling Pattern (verified via ILSpy decompilation of `ResearchDetailUi`)

The game's `ResearchDetailUi` extends `Panel` and uses these patterns. Follow this for native-looking custom panels:

```csharp
// Panel setup — default constructor, bolts ON, no BackgroundStyle() call
var panel = new Panel();              // noBolts defaults to false
// Read MIN_WIDTH from ResearchDetailUi via reflection for exact match:
var minWidthField = researchDetailUi.GetType().GetField("MIN_WIDTH",
    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
Px panelWidth = minWidthField != null ? (Px)minWidthField.GetValue(null) : new Px(468);
panel.Width(panelWidth);              // Match native panel width exactly
panel.MaxWidth(25.Percent());         // Same cap as ResearchDetailUi
panel.Body.JustifyItemsCenter();      // Center body contents

// Title row — full-width header with padding that cancels out Panel's PADDING
var titleRow = new Row(1.pt());
titleRow.Padding(8.px())
    .MarginLeftRight(-PanelBase<Panel, Column>.PADDING)  // Extend to panel edges
    .JustifyItemsCenter();
var title = new Label(text).TextCenterMiddle().FontBold().FontSize(15);
titleRow.Add(title);

// Section headers — bold, uppercase
new Label(Tr.Requires).FontBold().UpperCase()

// Normal text — Label, not Display
new Label(description).TextLeftTop().FontSize(15).Padding(1.pt())

// Grouped section
new Column(1.pt()) { c => c.StyleGroup().Padding(2.pt()), /* children */ }
```

**Key rules:**
- Use `Label` (not `Display`) for all text — avoids ALL CAPS
- Use `.pt()` extension for point-based gaps (e.g., `1.pt()`, `2.pt()`)
- Use `.px()` extension for pixel values (e.g., `8.px()`)
- Use `PanelBase<Panel, Column>.PADDING` to reference the standard panel padding
- Do NOT call `BackgroundStyle()` — the default Panel background matches the game
- Title row uses negative `MarginLeftRight` to extend edge-to-edge within the panel

### ResearchDetailUi — Full Visual Construction (deep-dive decompilation)

Detailed breakdown of how `ResearchDetailUi` is built, for replicating its look in custom panels.

#### Class hierarchy

```
ResearchDetailUi : Panel : PanelBase<Panel, Column> : Column : UiComponent
```

#### How it's added in ResearchWindow

```csharp
// ResearchWindow constructor creates a Row for the main content area:
new Row {
    c => c.FlexGrow(1f).AlignSelfStretch(),   // Row fills parent height & width
    m_treeView = new PanAndZoom(...)
        .FlexGrow(1f)
        .AlignSelfStretch(),                   // Pan-zoom fills remaining width, full height
    researchDetail.Instance
        .AlignSelfStretch()                    // CRITICAL: stretches panel to full Row height
}
```

**Key discovery:** `.AlignSelfStretch()` is what makes the detail panel fill the entire right side, covering the diamond plate background completely. Without it, a Panel only sizes to its content.

#### AlignSelfStretch extension method

- **Class:** `UiComponentLayoutExtensions` (static extension methods)
- **Namespace:** `Mafi.Unity.UiToolkit.Component`
- **Signature:** `public static T AlignSelfStretch<T>(this T component) where T : IComponentWithLayout`
- **Effect:** Sets `align-self: stretch` in Unity UIToolkit flex layout — the element fills the cross-axis of its parent container
- **Using:** Already available via `using Mafi.Unity.UiToolkit.Component;`
- **When to use:** Any time a panel is placed inside a `Row` and needs to fill the full height

#### PanelBase internal structure

```csharp
// PanelBase<TPanel, TContainer> constructor builds:
Add(
    m_background (UiComponent, class: Cls.panelBg, FixRepeatedBgForPanel()),
    Body (TContainer, class: Cls.panelBody, name: "Body"),
    Border (UiComponent, class: Cls.panelBorder, IgnoreInputPicking()),
    bolts (UiComponent, class: Cls.bolts4, IgnoreInputPicking())  // unless noBolts=true
);

// Base class is Column with shadow: base(Outer.PanelShadow)
// CSS classes applied: Cls.panel on self
// PADDING constant: 12.px() (PanelBase<Panel, Column>.PADDING)
```

The background, border, and bolts are absolute-positioned layers — when the panel stretches to full height, they all stretch with it automatically.

#### Width calculation

```csharp
// Static fields on ResearchDetailUi:
MIN_WIDTH = 4 * (IconWithTitle.WIDTH + IconWithTitle.MARGIN) + 2 * PanelBase<Panel, Column>.PADDING
          = 4 * (110px + 1pt) + 2 * 12px ≈ 468px
MAX_RECIPES_HEIGHT = 320px
PANEL_OVERHEAD = 2 * PanelBase<Panel, Column>.PADDING + 20px = 44px

// In Value() method, width can grow based on recipe layout:
this.Width((recipeWidth + PANEL_OVERHEAD).Max(MIN_WIDTH));
this.MaxWidth(25.Percent());  // Never more than 25% of parent
```

#### Title row and dynamic background color

```csharp
// Title row setup:
titleRow = new Row(1.pt()) {
    c => c.Padding(8).MarginLeftRight(-PanelBase<Panel, Column>.PADDING).JustifyItemsCenter(),
    canRepeatIcon,
    title = new Label().TextCenterMiddle().FontBold().FontSize(15),
    researchCostRow = new Row(1.pt()) { ... }  // absolute-positioned left
}

// Title bar color is set dynamically via observer:
titleRow.Background(ResearchThemeHelper.ResolveTitleBgColor(isBlocked, info));
```

The title row's background extends edge-to-edge via `MarginLeftRight(-PADDING)` which cancels out the body's 12px padding.

#### ResearchThemeHelper — title bar colors

`ResearchThemeHelper` (public static class in `Mafi.Unity.Ui.Research`) provides state-based colors:

```csharp
// All colors are: new ColorRgba(rgbInt, alpha)
BLOCKED_COLOR       = new ColorRgba(15892536, 110)  // reddish, high alpha
RESEARCHED_COLOR    = new ColorRgba(1842204, 126)    // dark gray, high alpha
IN_PROGRESS_COLOR   = new ColorRgba(3522855, 87)     // green
IN_QUEUE_COLOR      = new ColorRgba(3700253, 83)     // dark green
UNLOCKED_COLOR      = new ColorRgba(2009087, 89)     // blue (RGB ~30, 167, 255)
LOCKED_COLOR        = new ColorRgba(15166315, 40)     // reddish, low alpha
CAN_BE_ENQUEUED_COLOR = new ColorRgba(3957655, 90)   // blue-gray (RGB ~60, 99, 151)

// ResolveTitleBgColor priority:
// 1. Blocked → BLOCKED_COLOR
// 2. Researched → RESEARCHED_COLOR
// 3. In progress → IN_PROGRESS_COLOR
// 4. In queue → IN_QUEUE_COLOR
// 5. Unlocked & not locked by parents → UNLOCKED_COLOR
// 6. Locked → LOCKED_COLOR
// 7. Fallback → CAN_BE_ENQUEUED_COLOR
```

These `ColorRgba` fields are **private static readonly** — cannot be accessed directly from mod code. To use the same colors, construct `new ColorRgba(rgbInt, alpha)` with the values above.

#### Body structure (content column)

```csharp
// Single Column(2.pt()) added to Body, with AlignItemsStretch():
Body.Add(new Column(2.pt()) {
    c => c.AlignItemsStretch(),
    titleRow,           // Row with title, cost, repeat icon
    desc,               // Label, FontSize(15), Padding(1.pt())
    accBonusCol,        // Column(1.pt()), StyleGroup(), Padding(2.pt())
    reqsMainCol,        // Column(1.pt()) with "REQUIRES" header + Row of icons
    unlocksMainCol,     // Column(1.pt()) with "UNLOCKS" header + Row of icons
    recipesCol,         // Column(1.pt()) with "NEW RECIPES" header + ScrollBoth
    researchProgressCol,// Column(1.pt()) with progress bar
    noLabAvailableStatus,// Warning row
    queueStatus,        // Label, centered
    buttonsRow,         // Row with Start/Cancel/Enqueue/Dequeue buttons
    finishedInfo,       // Row with tick icon + "Finished" label
    lockedInfo          // Row with lock icon + locked reason
});
```

#### Summary — what to replicate for a native-looking side panel

1. **`AlignSelfStretch()`** on the panel when adding to a parent Row — fills full height
2. **`new Panel()`** with default constructor (bolts ON)
3. **Title row:** `new Row(1.pt()).Padding(8.px()).MarginLeftRight(-PanelBase<Panel, Column>.PADDING).JustifyItemsCenter()` with `.Background(colorRgba)` for the colored header
4. **Body:** `Body.JustifyItemsCenter()`, content in `Column(2.pt()).AlignItemsStretch()`
5. **Width:** Set via `panel.Width(px)` and optionally `panel.MaxWidth(percent)`
6. **No BackgroundStyle() call** — default panel background matches game chrome

### Text Components

#### `Label` (`Mafi.Unity.UiToolkit.Library`) — **Preferred for normal text**

`Label` is the text component used by the game's native UI (e.g., `ResearchDetailUi`). It renders text in **normal case** by default.

```csharp
// Constructor
new Label()                               // Empty label
new Label(LocStrFormatted text)           // Label with initial text

// Key methods (declared on Label)
Label UpperCase(bool upperCase = true)     // Opt-in ALL CAPS (for section headers)
Label IncFontSize()                        // Increment font size
Label TinyFontSize()                       // Small font variant
Label Selectable(bool selectable)          // Make text selectable
Label InfoIconPosition(InfoIconPos pos)    // Info icon placement (Right, Left, None)
```

**Use `Label` for:**
- Normal text (renders in proper case)
- Titles: `new Label(text).FontBold().FontSize(15)`
- Section headers: `new Label(text).FontBold().UpperCase()`

#### `Display` (`Mafi.Unity.Ui.Library`) — HUD/status text

`Display` implements `IComponentWithText` but **renders ALL CAPS by default** (game styling for HUD elements). Use `Label` instead for normal panel text.

```csharp
// Constructors
new Display()                             // Empty display
new Display(LocStrFormatted text)         // Display with initial text

// Key methods (declared on Display)
Display Large()                            // Large text variant
Display MinDigits(int minDigits)           // Minimum digit display
void SetValue(LocStrFormatted text)        // Update text content
void TextColor(ColorRgba? color)           // Set text color
Display AlignTextLeft()                    // Left-align text
Display LargeFont()                        // Larger font
Display IncFontSize()                      // Increment font size
```

**Known quirk:** `Display` renders text in ALL CAPS by default (game styling). This is cosmetic — the underlying string is normal case. For normal-case text in panels, use `Label` instead.

**Setting text via extension methods (from `UiComponentWithTextExtensions`):**
```csharp
display.Value(new LocStrFormatted("Hello"))       // Set text
display.Value(42)                                  // Set integer
display.Value(somePercent)                         // Set Percent
display.TextOverflow(TextOverflow.Ellipsis)        // Overflow behavior
display.Label(new LocStrFormatted("Label:"))       // Set label prefix
display.LabelWidth(new Px(100))                    // Label width
```

**Font/text styling (from `UiComponentFontExtensions` — works on any UiComponent):**
```csharp
component.FontBold()                    // Bold text
component.FontItalic()                  // Italic text
component.FontSize(14)                  // Set font size
component.NoTextWrap()                  // Disable text wrapping
component.TextCenterMiddle()            // Center text
component.TextLeftMiddle()              // Left-align, vertically centered
component.TextRightMiddle()             // Right-align
```

**Gotcha — centering a label in a column/scroll container:**
`TextCenterMiddle()` alone on a `Label` inside a `ScrollColumn` or `Column` won't visually center it — the label element only takes up as much width/height as its text. To center both horizontally and vertically, wrap the label in a `Row` with `JustifyItemsCenter()` and `FlexGrow(1f)`:
```csharp
var row = new Row();
row.JustifyItemsCenter().FlexGrow(1f);
var label = new Label(new LocStrFormatted("Centered text"));
label.TextCenterMiddle();
row.Add(label);
container.Add(row);
```

### Creating Text Strings: `LocStrFormatted` (`Mafi.Localization` in `Mafi.dll`)

`LocStrFormatted` is a **struct** (value type) used for all UI text. Located in `Mafi.dll`, not `Mafi.Core.dll`.

```csharp
// Constructor — from plain string
new LocStrFormatted("Hello World")

// Fields
string Value                              // The actual string value
static LocStrFormatted Empty              // Empty string constant

// Properties
bool IsEmptyOrNull { get; }
bool IsNotEmpty { get; }

// Operators
LocStrFormatted + LocStrFormatted         // Concatenation
LocStr -> LocStrFormatted                 // Implicit conversion from LocStr
```

### Pixel Unit: `Px` (`Mafi` namespace in `Mafi.dll`)

```csharp
new Px(10f)         // Explicit constructor (takes float)
Px px = 10;         // Implicit conversion from int
float f = somePx;   // Implicit conversion to float

// Extension methods on int (used by native game code)
1.pt()              // Convert int to Px (point-based, used for gaps/padding)
8.px()              // Convert int to Px (pixel-based)
25.Percent()        // Convert int to percentage-based Px
```

### ScrollColumn / ScrollBase

```csharp
// ScrollColumn constructor
new ScrollColumn()                         // No parameters needed

// ScrollBase methods (available on ScrollColumn, ScrollRow, ScrollBoth)
scroll.ScrollTo(childComponent)            // Scroll to make child visible
scroll.ScrollerAuto()                      // Auto-show scrollbar
scroll.ScrollerAlwaysVisible()             // Always show scrollbar
scroll.ScrollerHidden()                    // Hide scrollbar
scroll.AlignContentContainerStart()        // Align content to start
scroll.DisableWheelScrolling()             // Disable mouse wheel scroll
scroll.Clear()                             // Remove all children
```

### Visibility / Display Extensions

| Method | Source | Usage |
|--------|--------|-------|
| `SetVisible(bool)` | `UiComponent` instance method | `component.SetVisible(true)` |
| `Show(T)` | `UiComponentExtensions` | `component.Show()` |
| `Hide(T)` | `UiComponentExtensions` | `component.Hide()` |
| `Visible(T, bool)` | `UiComponentExtensions` | `component.Visible(true)` |
| `IsVisible()` | `UiComponent` instance method | `if (component.IsVisible())` |

### Layout Extensions

| Method | Usage |
|--------|-------|
| `Size(Px?, Px?)` | `component.Size(new Px(340), new Px(400))` |
| `Width(Px)` / `Height(Px)` | Set one dimension |
| `MaxWidth(Px)` / `MaxHeight(Px)` | Set max dimension |
| `FlexGrow(float)` | Flex layout grow factor |
| `NoShrink()` | Prevent flex shrinking |
| `Margin(Px)` | Set margins |
| `MarginLeftRight(Px)` | Horizontal margins (useful with `-PanelBase.PADDING` to extend edge-to-edge) |
| `Padding(Px)` / `PaddingLeftRight(Px)` / `PaddingTopBottom(Px)` | Set padding |
| `Fill()` | Fill parent container |
| `AlignSelfCenter()` | Center self in parent |
| `AlignItemsStretch()` | Stretch children to fill cross-axis |
| `JustifyItemsCenter()` | Center children along main axis |
| `Wrap()` | Enable flex wrap |
| `StyleGroup()` | Apply grouped visual style (subtle background) |
| `Border(int, ColorRgba, int)` | Border width, color, radius |
| `Background(ColorRgba)` | Set background color |

### Localization Strings (`Mafi.Core.Tr`)

The game's built-in translated strings live in the `Tr` static class in the `Mafi.Core` namespace. Fields are `LocStr` type (from `Mafi.Localization`). Using these ensures button text and labels match the native UI and auto-translate for non-English players.

```csharp
using Mafi.Core;           // Required for Tr access
using Mafi.Localization;   // Required for LocStr type

// Example fields used in ResearchDetailUi:
Tr.StartResearch_Action    // "Start research"
Tr.ResearchQueue__Add      // "Add to queue"
Tr.ResearchQueue__Remove   // "Remove from queue"
Tr.ResearchQueue__Status   // Queue position format string (LocStr1)
Tr.ResearchProgress        // "Research progress"
Tr.Requires                // "Requires"
Tr.Unlocks                 // "Unlocks"
Tr.Recipes__New            // "New recipes"
Tr.NoLabAvailable          // "No lab available"
Tr.ResearchFinished        // "Research finished"
Tr.Locked                  // "Locked"
Tr.Research_AccBonus       // Acceleration bonus label
```

### Button Styles (from decompiled `ResearchDetailUi`)

```csharp
new ButtonText(Button.Primary, Tr.StartResearch_Action)    // Yellow/primary styled button (renders yellow in-game, not green)
new ButtonIcon(Button.Danger, "path/to/icon.svg")          // Red/danger styled button
new ButtonText(Tr.ResearchQueue__Remove)                   // Default (unstyled/gray) button
button.OnClick((Action)delegate { /* handler */ }, allowKeyPresses: false)
button.Enabled(bool)                                       // Enable/disable
```

### Theme Colors (`Mafi.Unity.UiToolkit.Themes.Theme`)

```csharp
Theme.DefaultColor       // Standard text/icon color
Theme.PositiveColor      // Green — success, unlocked, active
Theme.WarningColor       // Yellow/orange — warnings, paused
Theme.DangerColor        // Red — errors, locked
Theme.InactiveColor      // Gray — finished, disabled
```

### DisplayState Enum (`Mafi.Unity.UiToolkit.Library.DisplayState`)

Used by `Panel.BackgroundStyle()` and `ProgressBarPercentInline.State()`:

| Value | Int | Typical use |
|-------|-----|-------------|
| `Neutral` | 0 | Default |
| `Inactive` | 1 | Grayed out |
| `Important` | 2 | Highlighted |
| `Positive` | 3 | Success/active (green) |
| `Warning` | 4 | Caution (yellow) |
| `Danger` | 5 | Error/blocked (red) |

### ProgressBarPercentInline (`Mafi.Unity.Ui.Library.ProgressBarPercentInline`)

**Decompiled from Mafi.Unity.dll** — inline progress bar with percentage display. Extends `Row`, implements `IComponentWithStateColor`.

```csharp
// Namespace: Mafi.Unity.Ui.Library (NOT Mafi.Unity.UiToolkit.Library)
public class ProgressBarPercentInline : Row, IComponentWithStateColor
{
    public ProgressBarPercentInline()           // No-arg constructor
    public ProgressBarPercentInline Value(Percent value)       // Sets both bar and % display
    public ProgressBarPercentInline DisplayValue(Percent value) // Sets only % display
    public void SetState(DisplayState state)   // Sets color on both bar and % display
}
```

**Internal structure:** `Row` with reversed direction (percentage text LEFT, bar RIGHT). Contains a `ProgressBar` (`.Fill().TranslateX(-2.px())`) and a `Display` (`.MinDigits(4)`).

**Usage pattern (from native `ResearchDetailUi`):**
```csharp
var progressCol = new Column(1.pt()) {
    c => c.AlignItemsStretch(),
    new Label(Tr.ResearchProgress).FontBold().UpperCase(),
    progressBar = new ProgressBarPercentInline()
};

// Update (called reactively or via polling):
progressBar.Value(node.ProgressInPerc);          // Percent type from ResearchNode
progressBar.SetState(isActive ? DisplayState.Positive : DisplayState.Warning);
```

### IResearchNodeFriend (`Mafi.Core.Research.IResearchNodeFriend`)

**Public interface** implemented by `ResearchNode`. Provides methods to manipulate research state directly:

```csharp
public interface IResearchNodeFriend
{
    void CancelResearch();   // Resets node from InProgress, preserves StepsDone
    void StartResearch();    // Sets node to InProgress
    void IncStepsDone(long steps);
    void ForceStepsToDone();
    // ... other members for graph building and lab requirements
}

// Usage: cast ResearchNode to the interface
((IResearchNodeFriend)researchNode).CancelResearch();
```

**Important:** `ResearchManager.StopResearch()` calls `CancelResearch()` AND `m_researchQueue.Clear()` — it clears the entire queue. To cancel just the current research without losing the queue, call `CancelResearch()` directly via the interface, then clear `CurrentResearch` via reflection.

### ResearchNode Progress Properties

```csharp
ResearchNode node = researchManager.CurrentResearch.ValueOrNull;
node.ProgressInPerc        // Percent — progress done on research
node.StepsDone             // long — completed science points
node.RemainingSteps        // long — remaining science points
node.ScienceCost           // long — total cost (first un-researched level if multi-level)
node.State                 // ResearchNodeState — InProgress, Researched, NotResearched, etc.
```

### ResearchManager Key Properties

```csharp
researchManager.CurrentResearch   // Option<ResearchNode> — currently researching node
researchManager.HasActiveLab      // bool — true if any research lab is enabled and working
researchManager.TryStartResearch(proto, out errorMsg)  // public method to start research
```

### Button Variants (`Mafi.Unity.UiToolkit.Library.Button`)

Static `ButtonVariant` fields on `Button` class:

| Field | Use |
|-------|-----|
| `Button.Primary` | Main action (styled, yellow in-game) |
| `Button.General` | Standard button |
| `Button.Danger` | Destructive action (red) |
| `Button.Warning` | Caution action (yellow) |
| `Button.IconOnly` | Icon-only, no text |
| `Button.IconOnlyDanger` | Icon-only, danger style |

```csharp
// Icon button with danger styling (red square X — used in native ResearchDetailUi)
var cancelBtn = new ButtonIcon(Button.Danger,
    "Assets/Unity/UserInterface/General/Cancel.svg",
    () => { /* onClick */ });

// Styled text button with variant + LocStr (accepts LocStr directly from Tr)
var startBtn = new ButtonText(Button.Primary, Tr.StartResearch_Action);
startBtn.OnClick((Action)(() => { /* handler */ }), allowKeyPresses: false);

// Default/gray text button (no variant = unstyled)
var removeBtn = new ButtonText(Tr.ResearchQueue__Remove);
removeBtn.OnClick((Action)(() => { /* handler */ }), allowKeyPresses: false);

// Styled text button with variant + LocStrFormatted (for custom/dynamic text)
var customBtn = new ButtonText(Button.Primary, new LocStrFormatted("custom text"));
customBtn.OnClick((Action)(() => { /* handler */ }), allowKeyPresses: false);
```

**Layout helpers verified in-game:**
- `.AlignSelfCenter()` — centers a button within a `Column` (prevents stretch-to-fill)

### Approaches That DO NOT Work in Update 4

- **`Window` base class** — exists in `Mafi.Unity.UiToolkit.Library` but is NOT public. Compiles (accessible to mods) but never renders.
- **`ForceVisible()`** — documented as "extremely non-standard", does nothing useful for mod windows.
- **`WindowView` / `BaseWindowController<T>` / `IToolbarItemInputController`** — these types from older game versions DO NOT EXIST in Update 4 DLLs.

### Other UI Patterns (not yet tested)

**Entity inspector (for building/entity-specific panels):**
```csharp
[GlobalDependency(RegistrationMode.AsEverything)]
public class MyInspector : IEntityInspector<MyEntity>
{
    public IEntityInspector Create(MyEntity entity) { ... }
}
```

**Reactive UI updates (verified via `ResearchDetailUi` decompilation):**

The game uses `this.Observe().Do()` chains on `UiComponent` for reactive data binding. These are extension methods that watch a lambda for value changes and call the handler when it changes:

```csharp
// Single observer — watch one value
this.Observe(() => Node.State)
    .Do(state => { /* update UI based on state */ });

// Multi-observer — watch multiple values, handler receives all
this.Observe(() => Node.Proto)
    .Observe(() => Node.TimesResearched)
    .Observe(() => Node.ScienceCost)
    .Do((proto, timesResearched, cost) => { /* update UI */ });

// Conditional visibility via ObserveVisible
component.ObserveVisible(parentComponent, () => someCondition);
```

These are auto-evaluated by the game's syncer/updater system — no manual polling or `schedule.Execute()` needed. Used extensively in `ResearchDetailUi` for title, progress bar, button visibility, and lock state.

## Input Manager & Controller Interfaces (`Mafi.Unity.dll`)

### `IUnityInputMgr` (`Mafi.Unity` namespace)

The central input manager interface. Manages controller activation/deactivation, global shortcuts, and escape handling. Extends `IRootEscapeManager`. Injected via DI as `IUnityInputMgr`.

**Properties:**

| Property | Type | Purpose |
|----------|------|---------|
| `ActiveControllers` | `IIndexable<IUnityInputController>` | Currently active input controllers |
| `InspectorManager` | `Option<IInspectorsManager>` | Inspector manager reference |

**Events:**

| Event | Type | Purpose |
|-------|------|---------|
| `ControllerActivated` | `Action<IUnityInputController>` | Fired when any controller is activated |
| `ControllerDeactivated` | `Action<IUnityInputController>` | Fired when any controller is deactivated |

**Methods:**

| Method | Signature | Purpose |
|--------|-----------|---------|
| `ActivateNewController` | `void ActivateNewController(IUnityInputController)` | Activates a controller |
| `DeactivateController` | `void DeactivateController(IUnityInputController)` | Deactivates a controller |
| `ToggleController` | `void ToggleController(IUnityInputController)` | Toggle activation state |
| `DeactivateAllControllers` | `void DeactivateAllControllers()` | Deactivate everything |
| `DeactivateAllUnpinned` | `void DeactivateAllUnpinned(ControllerGroup)` | Deactivate non-pinned controllers in group |
| `RegisterGlobalShortcut` | `void RegisterGlobalShortcut(Func<ShortcutsManager, KeyBindings>, IUnityInputController)` | Register a hotkey to toggle a controller |
| `RegisterGlobalShortcut` | `void RegisterGlobalShortcut(Func<ShortcutsManager, KeyBindings>, Func<bool>)` | Register a hotkey with a callback |
| `RemoveGlobalShortcut` | Two overloads (controller or callback) | Remove a registered shortcut |
| `RegisterGameMenuController` | `void RegisterGameMenuController(IGameMenuController)` | Register the game menu |
| `RegisterGameSpeedController` | `void RegisterGameSpeedController(GameSpeedController)` | Register the speed controller |
| `OnBuildModeActivated` | `void OnBuildModeActivated(IUnityInputController)` | Signal build mode started |
| `IsWindowControllerOpen` | `bool IsWindowControllerOpen()` | Check if any window controller is open |
| `SetInspectorManager` | `void SetInspectorManager(IInspectorsManager)` | Set the inspector manager |
| `OpenGameMenu` | `void OpenGameMenu()` | Open the game menu |

**Inherited from `IRootEscapeManager` (`Mafi.Unity`):**

| Method | Purpose |
|--------|---------|
| `AddRootEscapeHandler(IRootEscapeHandler)` | Register a popup-level escape handler |
| `ClearRootEscapeHandler(IRootEscapeHandler)` | Remove an escape handler |

### `IUnityInputController` (`Mafi.Unity.InputControl` namespace)

Interface for all input controllers (windows, tools, inspectors). Implemented by controllers that can be activated/deactivated.

| Member | Type/Signature | Purpose |
|--------|---------------|---------|
| `Config` | `ControllerConfig` (property) | Configuration flags for this controller |
| `Activate()` | `void` | Called when controller is activated |
| `Deactivate()` | `void` | Called when controller is deactivated |
| `InputUpdate()` | `bool` | Called every frame when active; return true if input was consumed |

### `ControllerConfig` (`Mafi.Unity.InputControl` namespace)

A struct with boolean flags and pre-built static instances. Used to configure controller behavior.

**Fields (all `bool` unless noted):**

| Field | Purpose |
|-------|---------|
| `IgnoreEscapeKey` | If true, ESC won't close this controller |
| `DeactivateOnOtherControllerActive` | If true, deactivate when another controller activates |
| `DeactivateOnNonUiClick` | If true, deactivate on click outside UI |
| `AllowInspectorCursor` | Allow inspector cursor when active |
| `BlockShortcuts` | Block global shortcuts while active |
| `DisableCameraControl` | Disable camera while active |
| `BlockCameraControlIfInputWasProcessed` | Block camera only if InputUpdate returns true |
| `PreventSpeedControl` | Prevent speed changes while active |
| `Group` | `ControllerGroup` — which group this controller belongs to |
| `GroupToCloseOnActivation` | `Option<Lyst<ControllerGroup>>` — groups to close when activating |

**Pre-built static configs:**

| Config | Typical use |
|--------|------------|
| `ControllerConfig.Window` | Standard window (most common for mods) |
| `ControllerConfig.WindowWithKeyNav` | Window with keyboard navigation |
| `ControllerConfig.ImmersiveFullscreen` | Fullscreen UI |
| `ControllerConfig.InspectorWindow` | Inspector panel |
| `ControllerConfig.Menu` | Menu controller |
| `ControllerConfig.MenuActive` | Active menu |
| `ControllerConfig.GameMenu` | Game menu (ESC menu) |
| `ControllerConfig.MessageCenter` | Message center |
| `ControllerConfig.Tool` | Tool (building, designating) |
| `ControllerConfig.ToolBlockingCamera` | Tool that blocks camera |
| `ControllerConfig.Mode` | Mode controller |
| `ControllerConfig.LayersPanel` | Layers panel |
| `ControllerConfig.PhotoMode` | Photo mode |
| `ControllerConfig.EnforcedOverlay` | Enforced overlay |

### `ControllerGroup` enum (`Mafi.Unity.InputControl` namespace)

| Value | Purpose |
|-------|---------|
| `None` | No group |
| `BottomMenu` | Bottom toolbar menu |
| `Tool` | Tool group (building, etc.) |
| `AlwaysActive` | Never auto-deactivated |
| `Window` | Window group |
| `Inspector` | Inspector group |
| `WindowFullscreen` | Fullscreen window group |

## DependencyResolver API (`Mafi.DependencyResolver` in `Mafi.dll`)

The DI container used throughout the game. Available in `IMod.Initialize()`, `IMod.EarlyInit()`, and injected into `[GlobalDependency]` constructors.

### Key Methods

| Method | Purpose |
|--------|---------|
| `GetResolvedInstance<T>()` | Returns `Option<T>` — the resolved instance of a registered type |
| `TryGetResolvedDependency<T>(out T)` | Returns bool, sets `out` param if found |
| `Resolve<T>()` | Returns `T` directly (throws if not found) |
| `TryResolve<T>()` | Returns `Option<T>` |
| `AllResolvedInstances` | `IEnumerable<object>` — **all** resolved instances. Useful for finding non-public types by type name string matching. |
| `GetResolvedInstance(Type type)` | Non-generic overload — returns `Option<object>` |
| `Instantiate<T>()` | Creates a new instance with DI constructor injection |
| `Instantiate<T>(params object[] args)` | Creates with explicit constructor args + DI |
| `ResolveAll<T>()` | Returns all implementations of an interface |

### Finding Non-Public Types at Runtime

When a type is not public (like `ResearchWindow`), you can't use `GetResolvedInstance<T>()` because you can't write `typeof(ResearchWindow)`. Instead, iterate all instances:

```csharp
object targetInstance = null;
foreach (object obj in resolver.AllResolvedInstances) {
    if (obj.GetType().FullName == "Mafi.Unity.Ui.Research.ResearchWindow") {
        targetInstance = obj;
        break;
    }
}
// Then use reflection on targetInstance.GetType() to access fields/methods
```

**Note:** This pattern is untested for `ResearchWindow` specifically. If `ResearchWindow` is not in `AllResolvedInstances`, an alternative approach is to find it via the `ControllerActivated` event on `IUnityInputMgr` — listen for controllers being activated and check their type name.

## `IMod` Lifecycle (Official Order)

1. **Constructor** — mod loaded
2. **`RegisterPrototypes()`** — register all game content (machines, recipes, research, etc.)
3. **`RegisterDependencies()`** — register custom services with DI container
4. **`EarlyInit()`** — early initialization before map generation
5. **`Initialize()`** — final initialization before game starts

For mods that only add content (no custom services), use `DataOnlyMod` base class.
For mods that need DI registration or initialization, implement `IMod` directly.

## Build Configuration

Our setup is minimal and correct for Update 4:
- **.NET Framework:** net48
- **Unity modules:** CoreModule, UIElementsModule
- **Deployment:** Post-build copy to `%APPDATA%/Captain of Industry/Mods/ResearchReorder/`

Only add Harmony and extra Unity references when actually needed.

## manifest.json Fields (Official, from MaFi repo)

### Required Fields

| Field | Type | Purpose |
|---|---|---|
| `id` | string | Unique mod ID. Must match `[a-zA-Z0-9][a-zA-Z0-9_-]*`. Must NOT start with `COI-` (reserved). |
| `version` | string | Version string: `major.minor[.patch[letter]]` (e.g. `0.0.1`, `1.2.3a`) |
| `primary_dlls` | string[] | DLL filenames to load, in dependency order |

### Optional Fields

| Field | Type | Purpose |
|---|---|---|
| `display_name` | string | Human-readable name shown in UI (max 50 chars) |
| `description_short` | string | Short description (max 180 chars) |
| `description_long` | string | Detailed description in mod details panel |
| `authors` | string or string[] | Author name(s) |
| `min_game_version` | string | Minimum compatible game version |
| `max_verified_game_version` | string | Highest tested game version |
| `links` | string[] | Web URLs (GitHub, etc.) |
| `mod_dependencies` | string[] | Required mod IDs. Supports version constraints: `"OtherMod >= 1.0.0"` |
| `optional_mod_dependencies` | string[] | Optional mod IDs (same version constraint syntax) |
| `incompatible_mods` | string[] | Mod IDs that conflict with this mod |
| `non_locking_dll_load` | bool | If true, DLLs loaded into memory (allows updating without closing game) |
| `can_add_to_saved_game` | bool | If true, mod can be added to an existing save |
| `can_remove_from_saved_game` | bool | If true, mod can be removed from an existing save |
| `primary_mod_class_name` | string | Class name when multiple `IMod` implementations exist |

### Version Auto-Generation (from .csproj)

The assembly version is auto-generated from manifest.json `version` field. Letter suffixes map to numeric revision: `1.2.3a` → assembly version `1.2.3.1` (a=1, b=2, etc.).

## Mod Configuration System (config.json)

Mods can expose player-configurable options via a `config.json` file. The game renders these in its settings UI automatically.

### Supported Types

| Type | `default` value | Extra fields |
|---|---|---|
| Boolean | `true`/`false` | — |
| String | `"text"` | `max_length`, `regex` |
| Integer | `42` | `min`, `max`, `is_integer` (must be `true`) |
| Float | `5.3` | `min`, `max` |

Parameter names must be `snake_case`.

### Accessing Config in Code

```csharp
int multiplier = JsonConfig.GetInt("production_multiplier");
bool enabled = JsonConfig.GetBool("enable_feature");

// React to player changes in settings UI
JsonConfig.OnValueChanged += paramName => { /* handle change */ };
```

Config values are persisted in save files. Use `MigrateJsonConfig()` for schema changes between versions.

## Official Build/Deploy System

### .csproj Auto-Deploy (from official ExampleMod)

The official .csproj includes MSBuild targets that on every build:
- Copy `manifest.json`, `config.json`, `readme.txt`, DLL, and asset bundles to `%APPDATA%/Captain of Industry/Mods/<ModName>/`
- Create a distributable zip file in the mods folder
- Debug builds also copy PDB files
- Assembly version auto-generated from manifest.json

Our .csproj is based on this same pattern.

## Research Node Registration Pattern (Official Example)

```csharp
ResearchNodeProto nodeProto = registrator.ResearchNodeProtoBuilder
    .Start("Unlock MyMod stuff!", ExampleModIds.Research.UnlockExampleModStuff, costMonths: 6)
    .Description("This unlocks all the awesome stuff in MyMod!")
    .AddProductToUnlock(ExampleModIds.Products.SomeProduct)
    .AddRecipeToUnlock(ExampleModIds.Recipes.SomeRecipe)
    .BuildAndAdd();

nodeProto.GridPosition = new Vector2i(4, 31);
nodeProto.AddParent(registrator.PrototypesDb.GetOrThrow<ResearchNodeProto>(Ids.Research.BasicFarming));
```

Note: We don't add research nodes, but this shows key patterns:
- `Ids.Research.CreateId("name")` — creates a typed research ID
- `registrator.PrototypesDb.GetOrThrow<T>(id)` — type-safe prototype lookup
- `costMonths:` parameter on the builder — sets research duration
- Grid position and parent set after `BuildAndAdd()`

## ID Registration Pattern (Official)

IDs are static readonly fields in partial classes, using typed ID wrappers:

```csharp
using ResNodeID = Mafi.Core.Research.ResearchNodeProto.ID;
public static readonly ResNodeID MyId = Ids.Research.CreateId("MyId");
```

Same pattern for `ProductProto.ID`, `RecipeProto.ID`, `MachineProto.ID`.

Products can use attribute-based auto-registration:
- `[CountableProduct]`, `[FluidProduct]`, `[LooseProduct]`, `[MoltenProduct]`, `[VirtualProduct]`
- Then call `registrator.RegisterAllProducts()` to register them all

## Update 4 Mod System Notes

Update 4 introduced a new mod selection UI with:
- Green checkmark = enabled, Red X = invalid/error, Unchecked = disabled
- Dependency validation (missing dependencies shown as "Missing" badge)
- Mod detail panel showing all manifest fields
- Invalid mod counter at bottom of mod list

## Useful Notes

- Logs are at `%APPDATA%/Captain of Industry/Logs` — check these for mod errors
- Use `Log.Info()` / `Log.Warning()` / `Log.Error()` for logging
- In-game console command `also_log_to_console` displays log output in the game console
- Discord #modding-dev-general channel is the best place for community + dev support
- `manifest.RootDirectoryPath` — available in mod constructor for accessing mod files at runtime

## DLL Inspection Technique

Use the reusable `inspect_dll.ps1` script in the project root to inspect any game type:

```powershell
# Inspect a specific type in a specific DLL
powershell -ExecutionPolicy Bypass -File inspect_dll.ps1 ResearchManager Mafi.Core.dll

# Inspect a type across all game DLLs (searches Mafi.dll, Mafi.Core.dll, Mafi.Base.dll, Mafi.Unity.dll)
powershell -ExecutionPolicy Bypass -File inspect_dll.ps1 PanelWithHeader

# If no exact match, prints partial matches to help find the right type name
powershell -ExecutionPolicy Bypass -File inspect_dll.ps1 Toolbar
```

Output includes: inheritance chain, interfaces, constructors, public properties, fields, and methods (declared only).

**Note:** Always use `-ExecutionPolicy Bypass` flag — the system execution policy blocks unsigned scripts.

This is more reliable than referencing other mods, which may target outdated game versions.
