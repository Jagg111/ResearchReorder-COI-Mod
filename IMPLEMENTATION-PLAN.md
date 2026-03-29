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
Each task is a single focused change. Do them in any order unless noted.

- [x] **5a: Match native panel styling (do first)** — Deep-dive decompiled `ResearchDetailUi` to document its full visual construction in MODDING-REFERENCE.md. Applied two key fixes:
  - **Full-height stretch:** Added `AlignSelfStretch()` — the same call the native panel uses. Panel now fills the entire right side, completely covering the diamond plate texture.
  - **Title bar styling:** Added `titleRow.Background(new ColorRgba(3700253, 83))` using the game's `IN_QUEUE_COLOR` for a colored header bar matching native style.
  - Background and bolts/frame unchanged (already correct from Phase 4d).
- [x] **5b: Empty queue state** — "Queue is empty" text centered horizontally and vertically in the panel using a `Row` wrapper with `JustifyItemsCenter().FlexGrow(1f)`, matching the title bar centering pattern.
- [ ] **5c: Currently-researching item** — Show active research at position 0, visually distinct (bold or "▶" prefix).
- [ ] **5d: Remove from queue** — Add ✕ button per item. Wire to the game's dequeue command.
- [ ] **5e: Reactive updates** — Auto-refresh when queue changes externally. Research `ResearchManager` events; fall back to polling if none exist.
- [ ] **5f: Reflection error handling** — try/catch around reflection access. Show "Queue unavailable" on failure.
- [x] **5g: Visual polish — wider panel** — Read `MIN_WIDTH` static field from `ResearchDetailUi` via reflection at runtime (fallback 468px). Added `MaxWidth(25.Percent())` cap. Panel now matches native width exactly.
- [x] **5h: Visual polish — title bar background** — *(Completed by 5a — title row now has `IN_QUEUE_COLOR` background.)*
- [ ] **5i: Visual polish — row spacing** — Increase padding/margins on queue rows for breathing room.
- [ ] **5j: Save compatibility testing** — Test on fresh and existing saves. Install, reorder, save, reload, remove mod, reload.

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

**Phases 1–4: COMPLETE.** Queue access, reorder logic, UI rendering, and research tree integration all working. Standalone F9 window removed in favor of the embedded panel.

**Phase 5a: COMPLETE** — Panel now matches native `ResearchDetailUi` styling: full-height via `AlignSelfStretch()`, title bar with `IN_QUEUE_COLOR` background. Detailed visual construction documented in MODDING-REFERENCE.md. Phase 5h (title bar background) also addressed.

**Phase 5 in progress** — Remaining: 5c–5f, 5i, 5j.

## Phase Details

### Phase 1 — Issues Encountered & Resolved

- `IMod` constructor must take `ModManifest` as first param (not `(CoreMod, BaseMod)` from older game versions)
- `JsonConfig` property must be initialized as `new ModJsonConfig(this)` — null crashes the game
- `ResearchNodeProto.Id` has multiple inherited `Id` properties causing `AmbiguousMatchException` — resolved by casting to typed `ResearchNode` and using `Proto.Id` directly

### Phase 2 — Reorder Strategy

Use `PopAt(fromIndex)` + `EnqueueAt(item, toIndex)` to move a single item. This is a one-step move operation — no need to clear and rebuild the queue. **Note:** `EnqueueAt` signature is `EnqueueAt(item, index)` — item first, index second.

**Verified (Phase 2):**
- Game UI updates immediately after queue mutation — yes
- Save/reload preserves reordered queue — yes
- Currently-researching item not disrupted by reordering behind it — confirmed

### Phase 3a — What We Tried & Learned

**First attempt (FAILED):**
- Used `Window` base class from `Mafi.Unity.UiToolkit.Library` + `ForceVisible()` in `Initialize()`
- Compiled fine but window never appeared. DLL inspection revealed `Window` is **not a public type** in Update 4.

**Old patterns that don't exist in Update 4:**
- `WindowView`, `BaseWindowController<T>`, `IToolbarItemInputController` — none of these types exist in current game DLLs

**Phase 3a working approach (superseded by Phase 4):**
The standalone `PanelWithHeader` + `IToolbarItemController` + F9 hotkey pattern was removed in Phase 4e when the queue panel was embedded directly into the research tree. Documented in MODDING-REFERENCE.md under "UI Window Patterns" for reference if a standalone tool window is ever needed again.

**Current approach (Phase 4+):**
- `[GlobalDependency]` class injects panel directly into the research tree's content Row
- No standalone window, no toolbar button, no hotkey
- Panel visibility toggled by polling `m_selectedNode` on `ResearchWindow`

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

#### Phase 4 Strategy (Reflection-Based, No Harmony) — IMPLEMENTED

All steps below are implemented and working. See `ResearchReorderWindowController.cs` for the final code.

**Step 1: Find the ResearchWindow instance** — Match `ResearchWindow+Controller` in `resolver.AllResolvedInstances` by `FullName`. Get `m_window` field from base class `WindowController<ResearchWindow>`, unwrap `Option<T>`. Window is lazily created; `ScheduleDeferredExtraction` retries on first open.

**Step 2: Find ResearchDetailUi** — NOT a field on ResearchWindow (corrected from original plan). Found via recursive `FindParentOfType` search through the component tree using `AllChildren`, matching `child.GetType().Name == "ResearchDetailUi"`. Returns the parent Row for sibling injection.

**Step 3: Inject queue panel** — `new Panel()` with `AlignSelfStretch()`, added to the same Row as `ResearchDetailUi` via `contentRow.Add()`.

**Step 4: Toggle visibility** — Poll `m_selectedNode.HasValue` via `schedule.Execute()` loop. Asymmetric toggle prevents flicker (see MODDING-REFERENCE.md for pattern details).

**Resolved risks:** ResearchWindow is NOT in `AllResolvedInstances` (only its Controller is) — resolved via Controller's `m_window` field. Parent container structure is a `Row` — confirmed. Escape behavior works correctly with no extra code.

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
