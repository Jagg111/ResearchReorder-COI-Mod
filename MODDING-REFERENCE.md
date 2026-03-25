# COI Modding Reference

Technical deep-dive on game APIs and modding patterns for **Update 4 (v0.8.2c)**. Everything here has been verified by direct DLL inspection or in-game testing. See CLAUDE.md for project overview and scope.

> **Note on COI-Extended:** We initially used the COI-Extended mod as a reference, but it was built for an older game version. Its `IMod` constructor signature, UI base classes (`WindowView`, `BaseWindowController<T>`, `IToolbarItemInputController`), and other patterns either don't compile or don't exist in Update 4. We no longer use it as a reference — all patterns below are verified against the current game DLLs.

## Game API — Research System

Game install path: `C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry`

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

**Lib.Harmony v2.2.2** (NuGet package) can be used for runtime method patching. We haven't needed it yet — reflection has been sufficient. If we need to inject UI into the existing research screen (Phase 4), Harmony will likely be required.

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

### API Signatures (verified via DLL inspection)

| Method | Signature |
|--------|-----------|
| `ToolbarHud.AddMainMenuButton` | `Button AddMainMenuButton(LocStrFormatted name, IToolbarItemController controller, String iconAssetPath, Single order, Func<ShortcutsManager, KeyBindings> shortcut)` |
| `ToolbarHud.AddToolWindow` | `void AddToolWindow(UiComponent window)` |
| `ToolbarHud.AddToolButton` | `Button AddToolButton(LocStrFormatted name, IToolbarItemController controller, String iconAssetPath, Single order, Func<ShortcutsManager, KeyBindings> shortcut, Nullable<TutorialId> tutorialId)` |
| `KeyBindings.FromKey` | `static KeyBindings FromKey(KbCategory category, ShortcutMode mode, KeyCode code)` |
| `PanelWithHeader` ctor | `PanelWithHeader(Nullable<LocStrFormatted> title)` |
| `Column` ctor | `Column(Px gap)` or `Column(Outer outer, Inner inner, Nullable<Px> gap)` |
| `Row` ctor | `Row(Px gap)` or `Row(Outer outer, Inner inner, Nullable<Px> gap)` |
| `ScrollColumn` ctor | `ScrollColumn()` (parameterless) |
| `Display` ctor | `Display()` or `Display(LocStrFormatted text)` |

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

**`PanelWithHeader` specific methods for adding to the body:**
```csharp
PanelWithHeader BodyAdd(params UiComponent[] children)                    // Add children to body
PanelWithHeader BodyAdd(Action<UiComponentExtensions> applyStyles, params UiComponent[] children)  // Add with styles
PanelWithHeader BodyGap(Px gap)                                            // Set gap between body items
Column Body { get; }                                                       // Direct access to body Column
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

### Text Display Component: `Display` (`Mafi.Unity.Ui.Library`)

**There is no `Txt` or `Label` class.** The primary text component is `Display`, which implements `IComponentWithText`.

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

**Known quirk:** `Display` renders text in ALL CAPS by default (game styling). This is cosmetic — the underlying string is normal case. Needs investigation to override (may require USS class or font style override).

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
| `FlexGrow(float)` | Flex layout grow factor |
| `Margin(Px)` | Set margins |
| `Fill()` | Fill parent container |

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

**Reactive UI updates:**
```csharp
UpdaterBuilder uBuilder = UpdaterBuilder.Start();
uBuilder.Observe(() => someProperty)
    .Do(value => { /* update UI */ });
AddUpdater(uBuilder.Build());
```

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
