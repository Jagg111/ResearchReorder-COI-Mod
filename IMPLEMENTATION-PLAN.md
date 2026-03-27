# Implementation Plan — ResearchReorder Mod

## Overall Approach

Phased, iterative development. Each phase is a small testable increment verified in-game before moving on. The player needs to load the game after each build to validate changes.

## Task Checklist

### Phase 1: Access the research queue via reflection
- [x] Upgrade from `DataOnlyMod` to `IMod`
- [x] Get `ResearchManager` via DI in `Initialize()`
- [x] Access private `m_researchQueue` via reflection
- [x] Log queue contents with research IDs
- [x] Discover `Queueue<T>` API methods
- [x] Verify in-game: queue items logged correctly (6 items confirmed)

### Phase 2: Implement queue reorder logic
- [x] Hard-code a test swap: move last queue item to position 0
- [x] Log before/after queue state
- [x] Verify in-game: queue tooltip shows new order
- [x] Verify: saving and reloading preserves new order
- [x] Verify: currently-researching item is not disrupted
- [x] Remove hard-coded test, expose a reusable MoveItem(fromIndex, toIndex) helper

### Phase 3a: Show a blank window (prove UI rendering works)
- [x] Get ANY mod-created window to appear on screen
- [x] Abandon `Window` base class (not public in Update 4) and `WindowView`/`BaseWindowController` (don't exist)
- [x] Use `PanelWithHeader` (UiToolkit) + `IToolbarItemController` + `ToolbarHud` pattern
- [x] Implement `ResearchReorderWindowController` with F9 hotkey via `ToolbarHud.AddMainMenuButton()`
- [x] Register window panel via `ToolbarHud.AddToolWindow()`
- [x] Remove `ForceVisible()` hack from Initialize()
- [x] Verify in-game: press F9 → "Research Queue" panel appears, F9 again hides it

### Phase 3b: Show research queue as text list in the window
- [x] Add queue reading via reflection (reuse existing pattern)
- [x] Render each queued item as a numbered text label
- [x] Use `Proto.Strings.Name.TranslatedString` for human-readable display names
- [x] Verify in-game: window shows queue items matching 1:1 with the native tooltip that shows the queue when hovering over the beaker & research buttons in the UI

### Phase 3c: Add up/down reorder buttons
- [x] Add move-up / move-down buttons next to each queue item
- [x] Wire buttons to `PopAt` + `EnqueueAt` via reorder helper
- [x] Rebuild list after each move
- [x] Verify in-game: buttons reorder queue, tooltip confirms new order

### Phase 4: Integrate queue panel into the research tree screen
**Goal:** Replace the standalone F9 window with a panel embedded in the research tree's right-hand side. When no node is selected, the right panel shows the research queue. When a node is selected, it shows the node details (standard game behavior). Deselecting returns to the queue view.

- [x] Research: find the research tree screen controller/view classes in game DLLs
- [x] Research: determine how the right-hand detail panel is shown/hidden (what triggers it)
- [x] Research: evaluate whether Harmony is needed, or if the game's modding framework provides sufficient hooks
- [x] **4a: Find ResearchWindow instance at runtime** — Found via `ResearchWindow+Controller` in DI → `m_window` (`Option<ResearchWindow>`) → `ValueOrNull`. Window is lazily created (empty until research tree is first opened). `ResearchDetailUi` is a child component at `Body → child[0] (Row) → child[1]`, not a field. `m_selectedNode` is `Option<ResearchNodeUi>`.
- [x] **4b: Inject queue panel as sibling** — Get `ResearchDetailUi`'s parent Row, create our `Panel` and `Add()` it as a sibling. Verify it renders next to or overlapping the detail panel area.
- [x] **4c: Wire up visibility toggle** — Poll `m_selectedNode` field via `schedule.Execute()` loop: when empty → show queue panel + force-hide `ResearchDetailUi`; when has value → wait for `ResearchDetailUi.IsVisible()` then hide queue panel. Independent of F9 window.
- [x] **4d: Port queue UI** — Embedded panel uses `Panel` (same base as `ResearchDetailUi`) with native styling: `Label` for text (not `Display`), `.FontBold().FontSize(15)` title, `ScrollColumn` body. Queue rows built via `BuildQueueRows` helper. Arrow buttons reorder the actual queue and refresh the embedded view.
- [x] **4e: Remove standalone F9 window** — Embedded panel is functional. Removed `IToolbarItemController` implementation, F9 hotkey, `ToolbarHud` registration, and `ResearchReorderWindowView` class. Moved `BuildQueueRows` into the controller. `ToolbarHud` kept as constructor dependency solely for scheduling (via `m_mainContainer` reflection).
- [x] **4f: Escape behavior** — Confirmed: first Escape deselects node and shows queue panel, second Escape closes research tree. No extra code needed.
- [x] **4g: In-game verification** — Full flow confirmed: open research tree → see queue → click node → see details → click empty space → see queue again → Escape closes tree.
- [x] Update manifest version — bumped to 0.0.2

### Phase 5: Core polish (do these before Phase 6)
Each task is a single focused change. Do them in any order.

- [ ] **5a: Empty queue state** — When queue is empty, show "No research queued" message in the panel.
- [ ] **5b: Currently-researching item** — Show active research at position 0, visually distinct (bold or "▶" prefix).
- [ ] **5c: Remove from queue** — Add ✕ button per item. Wire to the game's dequeue command.
- [ ] **5d: Reactive updates** — Auto-refresh when queue changes externally. Research `ResearchManager` events; fall back to polling if none exist.
- [ ] **5e: Reflection error handling** — try/catch around reflection access. Show "Queue unavailable" on failure.
- [ ] **5f: Visual polish — wider panel** — Increase panel width from 300px to ~450px to match native `ResearchDetailUi`.
- [ ] **5g: Visual polish — title bar background** — Add a background color to the title row to match native panel headers.
- [ ] **5h: Visual polish — row spacing** — Increase padding/margins on queue rows for breathing room.
- [ ] **5i: Save compatibility testing** — Test on fresh and existing saves. Install, reorder, save, reload, remove mod, reload.

### Phase 6: Drag-and-drop reordering
**Goal:** Replace arrow buttons with drag handles so players can drag queue items to reorder, matching the feel of the game's recipe reorder UI. Keep arrow buttons as a fallback if drag-and-drop proves infeasible.

- [ ] **6a: Research `Reorderable` manipulator** — Study `Mafi.Unity.UiToolkit.Component.Manipulators.Reorderable` in the DLL. Document: constructor params, required parent container setup, what events/callbacks it exposes for reorder completion, how the recipe list in building windows uses it.
- [ ] **6b: Prototype drag handles** — Add a drag handle element to each queue row. Attach `Reorderable` manipulator. Get basic drag working (item visually moves) without wiring to actual reorder logic yet.
- [ ] **6c: Wire drag completion to reorder** — When a drag completes, read the new visual order and call `MoveItem()` to update the actual game queue. Refresh the list.
- [ ] **6d: Drag edge cases & polish** — Test: dragging first/last item, dragging to same position (no-op), visual feedback during drag (ghost/placeholder), scroll behavior when dragging near top/bottom of a long list.
- [ ] **6e: Decide on arrow buttons** — Based on how drag-and-drop feels, decide whether to keep arrow buttons alongside drag handles, or remove them. Get user input.

### Phase 7: Nice-to-have enhancements
Independent subtasks — do any/all based on user priority. No required order.

- [ ] **7a: Click-to-navigate** — Clicking a queue item selects that node in the research tree and shows its details in the `ResearchDetailUi` panel. Needs: find the method that `ResearchWindow` calls to select a node, invoke it via reflection.
- [ ] **7b: Additional research info per item** — Show icons, research points remaining, and/or progress bars alongside queue item names. Research which fields are available on `ResearchNode` / `ResearchNodeProto`.
- [ ] **7c: Keyboard shortcuts** — Arrow keys to move selection within queue, Enter to navigate to node, Delete to remove from queue.

## Current Status

**Phase 1: COMPLETE** — Queue access works. All 6 queued research items logged with correct IDs. `Queueue<T>` API fully mapped.

**Phase 2: COMPLETE** — Reorder confirmed working. `PopAt()` + `EnqueueAt()` moves items correctly, UI updates immediately, save/reload preserves order, active research not disrupted. Reusable `MoveItem(fromIndex, toIndex)` helper ready for Phase 3 UI.

**Phase 3a: COMPLETE** — Blank "Research Queue" panel renders on screen. Uses `PanelWithHeader` + `IToolbarItemController` + `ToolbarHud`. F9 toggles visibility. Toolbar button appears in bottom bar when window is active.

**Phase 3b: COMPLETE** — Queue items display as numbered text list with human-readable names. Uses `ScrollColumn` + `Display` labels, `Proto.Strings.Name.TranslatedString` for display names. Controller injects `ResearchManager`, reads queue via reflection, refreshes on `Activate()`. Verified in-game: 4-item queue matches tooltip exactly. Note: text renders ALL CAPS due to game's default `Display` styling — cosmetic fix deferred to Phase 3d.

**Phase 3c: COMPLETE** — Each queue item is now a `Row` with a text label + ▲/▼ `ButtonText` buttons. First item hides ▲, last item hides ▼. Buttons call `MoveItem(fromIndex, toIndex)` which uses `PopAt` + `EnqueueAt` on the reflected queue, then refreshes the display. Verified in-game: buttons reorder the actual game queue correctly.

**Phase 4a: COMPLETE** — ResearchWindow instance found at runtime. Discovery path: `ResearchWindow+Controller` (in DI) → `m_window` field on base class `WindowController<ResearchWindow>` → unwrap `Option<T>` via `HasValue` + `ValueOrNull`. Window is lazily created — `Option` is empty at construction, populated after first research tree open. Retry logic via `ScheduleDeferredExtraction` handles first-open timing. Component tree mapped: `Body → Row → [PanAndZoom, ResearchDetailUi]`. `m_selectedNode` is `Option<ResearchNodeUi>` (not nullable).

**Phase 4b: COMPLETE** — Placeholder panel successfully injected into the research tree's content Row as a sibling of `ResearchDetailUi`. Approach: subscribe to `IUnityInputMgr.ControllerActivated` event (from `Mafi.Unity` namespace), detect when research tree controller activates, then recursively search the component tree for `ResearchDetailUi`'s parent Row and `Add()` our panel to it. Confirmed in-game: placeholder text renders in the correct right-hand area, both our panel and `ResearchDetailUi` show side by side when a node is selected (as expected — visibility toggle is 4c).

**4b timing fix: RESOLVED** — On first open, `ControllerActivated` fires BEFORE the `ResearchWindow` is created (the `Option<ResearchWindow>` is still empty). Fixed with `ScheduleDeferredExtraction`: uses `VisualElement.schedule.Execute()` to retry extraction one frame later. Window is found on attempt 1 (~60ms delay). Also added `ControllerDeactivated` handler as a safety net. Panel now renders on the very first research tree open.

**Phase 4c: COMPLETE** — Visibility toggle working flicker-free. Uses `schedule.Execute()` polling loop reading `m_selectedNode.HasValue` via reflection. Key insight: naive show/hide causes flicker during transitions. Final approach uses asymmetric logic — when deselecting, force-hide `ResearchDetailUi` immediately (prevents both panels showing); when selecting, wait until `ResearchDetailUi.IsVisible()` is true before hiding our panel (prevents empty gap). Polling starts on `OnControllerActivated`, stops on `OnControllerDeactivated`. `ResearchDetailUi` reference captured during `TryInjectPanel()` by iterating the content Row's children.

**Phase 4d: COMPLETE** — Queue UI ported into the embedded panel. Key discovery: decompiled `ResearchDetailUi` with ILSpy to learn the exact native styling approach. Native panel uses `Panel` default constructor (bolts ON, no `BackgroundStyle()` call), `Label` for text (not `Display` — this fixes the ALL CAPS issue), and `.FontBold().FontSize(15)` for titles. Title row uses `Row(1.pt())` with `Padding(8.px()).MarginLeftRight(-PanelBase<Panel, Column>.PADDING)` to match native header alignment. Queue rows built via shared `BuildQueueRows` static helper (used by both embedded panel and F9 window). Arrow buttons trigger `MoveItem` + `RefreshEmbeddedPanel` to update in-place.

## Phase Details

### Phase 1 — Issues Encountered & Resolved

- `IMod` constructor must take `ModManifest` as first param (not `(CoreMod, BaseMod)` from older game versions)
- `JsonConfig` property must be initialized as `new ModJsonConfig(this)` — null crashes the game
- `ResearchNodeProto.Id` has multiple inherited `Id` properties causing `AmbiguousMatchException` — resolved by casting to typed `ResearchNode` and using `Proto.Id` directly

### Phase 2 — Reorder Strategy

Use `PopAt(fromIndex)` + `EnqueueAt(item, toIndex)` to move a single item. This is a one-step move operation — no need to clear and rebuild the queue. **Note:** `EnqueueAt` signature is `EnqueueAt(item, index)` — item first, index second.

**What to watch for:**
- Does the game UI update immediately after we manipulate the queue in `Initialize()`?
- Does saving and reloading preserve the new order?
- Any side effects (e.g., does the currently-researching item get disrupted)?

### Phase 3a — What We Tried & Learned

**First attempt (FAILED):**
- Used `Window` base class from `Mafi.Unity.UiToolkit.Library` + `ForceVisible()` in `Initialize()`
- Compiled fine but window never appeared. DLL inspection revealed `Window` is **not a public type** in Update 4.

**Old patterns that don't exist in Update 4:**
- `WindowView`, `BaseWindowController<T>`, `IToolbarItemInputController` — none of these types exist in current game DLLs

**Working approach (For Update 4 of Captain of Industry):**
- `PanelWithHeader` — public UiToolkit component, used as the window body
- `IToolbarItemController` (extends `IUnityInputController`) — handles toolbar button + activation
- `ToolbarHud.AddMainMenuButton()` — registers a toolbar button with hotkey
- `ToolbarHud.AddToolWindow()` — registers a UiComponent as a tool window
- `ControllerConfig.Window` — pre-built config for window-type controllers
- `UiComponent.SetVisible(bool)` — controls visibility
- Both view and controller use `[GlobalDependency(RegistrationMode.AsEverything)]` for DI auto-registration

**Behavioral notes:**
- Toolbar button appears in bottom bar only while window is active (standard game behavior for tool windows)
- F9 hotkey works for toggle. Uses `KeyBindings.FromKey(KbCategory.Windows, ShortcutMode.Game, KeyCode.F9)`
- Empty string `""` for icon path works fine (no icon shown)
- Constructor-based registration on `ToolbarHud` works — no special lifecycle hook needed

### Phase 3 — Open Questions (Resolved)

- Should the currently-researching item appear in the list? **Yes** — show it and allow moving it (changes active research). Deferred to Phase 5b.

### Phase 4 — Research Findings

#### Research Tree Screen Architecture

The research tree screen is built from these key classes (all in `Mafi.Unity.Ui.Research` namespace in `Mafi.Unity.dll`):

- **`ResearchWindow`** — the full-screen research tree view. **Not public.** Contains the scrollable node graph, search field, connection line renderers, and the detail panel.
- **`ResearchDetailUi`** — the right-hand detail panel. **Public class**, extends `Panel`. Shows when you click a research node. Has a `Value(ResearchNode)` method. **Not registered in DI** — created directly by `ResearchWindow`.
- **`ResearchWindow+Controller`** — handles toolbar integration and the `ToggleResearchWindow` keyboard shortcut. **Not public.**

#### How Node Selection Works

When a player clicks a research node in the tree:
1. `handleNodeClicked` is called on `ResearchWindow`
2. `m_selectedNode` field is set to the clicked `ResearchNodeUi`
3. `researchDetail.Value(node)` is called to populate the right-hand panel
4. `updateSelectionHighlights` updates the visual highlighting

When the player deselects (clicks empty space or presses Escape):
1. `m_selectedNode` is set to null
2. `researchDetail` is hidden (via `SetVisible(false)` or similar)
3. Selection highlights are cleared

#### Harmony Evaluation

**Verdict: Harmony is NOT needed for Phase 4.**

- Harmony is not bundled with the game (no `0Harmony.dll`, no BepInEx)
- The official modding docs don't mention Harmony
- Our reflection-based approach should be sufficient:
  - Find `ResearchWindow` by iterating `DependencyResolver.AllResolvedInstances`
  - Read `m_selectedNode` to detect selection state
  - Inject our panel as a sibling of `researchDetail` via `Parent` container
- Only reconsider Harmony if runtime instance discovery fails

#### Phase 4 Strategy (Reflection-Based, No Harmony)

**Step 1: Find the ResearchWindow instance**
- In a `[GlobalDependency]` class constructor or `Initialize()`, iterate `resolver.AllResolvedInstances`
- Match by `obj.GetType().FullName == "Mafi.Unity.Ui.Research.ResearchWindow"`
- Fallback: listen to `IUnityInputMgr.ControllerActivated` event and capture the instance when the research window opens

**Step 2: Get the ResearchDetailUi and its parent container**
- Via reflection: `researchDetail` field on `ResearchWindow` → cast to `ResearchDetailUi` (public type)
- Call `researchDetail.Parent` to get the container that holds the detail panel

**Step 3: Create and inject our queue panel**
- Create a `Panel` (same type as `ResearchDetailUi`) with our queue UI inside
- `Add()` it to the same parent container as a sibling

**Step 4: Toggle visibility based on selection**
- Poll `m_selectedNode` field on `ResearchWindow` (via reflection)
- When null → show our queue panel, ensure `researchDetail` is hidden
- When non-null → hide our queue panel (game shows `researchDetail` naturally)
- Polling can happen in `InputUpdate()` on our controller, or via an `UpdaterBuilder` observer

**Risks & unknowns:**
- `ResearchWindow` might not be in `AllResolvedInstances` — it could be created by its Controller rather than by DI directly
- The parent container structure is unknown — we're assuming `researchDetail` has a parent we can inject into
- Escape key behavior might need special handling — if the game's deselection logic also hides the right panel area entirely, our panel would disappear too
- All of these will be tested in step 4a (runtime discovery proof-of-concept)

**Key API for Phase 6 (drag-and-drop):**
- `Mafi.Unity.UiToolkit.Component.Manipulators.Reorderable` — **public class**, constructor: `(VisualElement dragHandle, bool lockDragToAxis)`. This is the same manipulator used by the recipe list in building windows (e.g., Assembly III). Extends `UnityEngine.UIElements.Manipulator`.

## Technical Reference

**Key types:**
- `ResearchManager` — obtained via `resolver.GetResolvedInstance<ResearchManager>().Value`
- `ResearchNode` — runtime wrapper for a research item (has `Proto.Id` for display name)
- `Queueue<ResearchNode>` — the queue collection (accessed via reflection on `m_researchQueue`)
- `ResearchManager.CurrentResearch` — `Option<ResearchNode>` for the active research

**Reflection pattern for queue access:**
```csharp
FieldInfo queueField = typeof(ResearchManager).GetField(
    "m_researchQueue",
    BindingFlags.NonPublic | BindingFlags.Instance
);
var queue = (Queueue<ResearchNode>)queueField.GetValue(researchMgr);
```

**IMod boilerplate (Update 4):**
- Constructor: `MyMod(ModManifest manifest)`
- Must set: `Manifest = manifest; JsonConfig = new ModJsonConfig(this);`
- See MODDING-REFERENCE.md for full template
