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

### Phase 3d: Polish
- [ ] Handle edge cases: empty queue, single item
- [ ] Reactive updates if queue changes externally (e.g., research completes)
- [ ] Match game UI styling
- [ ] Should currently-researching item appear in list (locked/grayed out)?
- [ ] Error handling for reflection failures (graceful degradation)
- [ ] Test on fresh saves and existing saves

### Phase 4: Integrate into research screen (stretch goal)
- [ ] Investigate injecting reorder UI directly into the existing research panel
- [ ] Likely requires Harmony patching to hook into the research screen
- [ ] Optional: upgrade to drag-and-drop
- [ ] Update manifest version for release

## Current Status

**Phase 1: COMPLETE** — Queue access works. All 6 queued research items logged with correct IDs. `Queueue<T>` API fully mapped.

**Phase 2: COMPLETE** — Reorder confirmed working. `PopAt()` + `EnqueueAt()` moves items correctly, UI updates immediately, save/reload preserves order, active research not disrupted. Reusable `MoveItem(fromIndex, toIndex)` helper ready for Phase 3 UI.

**Phase 3a: COMPLETE** — Blank "Research Queue" panel renders on screen. Uses `PanelWithHeader` + `IToolbarItemController` + `ToolbarHud`. F9 toggles visibility. Toolbar button appears in bottom bar when window is active.

**Phase 3b: COMPLETE** — Queue items display as numbered text list with human-readable names. Uses `ScrollColumn` + `Display` labels, `Proto.Strings.Name.TranslatedString` for display names. Controller injects `ResearchManager`, reads queue via reflection, refreshes on `Activate()`. Verified in-game: 4-item queue matches tooltip exactly. Note: text renders ALL CAPS due to game's default `Display` styling — cosmetic fix deferred to Phase 3d.

**Phase 3c: COMPLETE** — Each queue item is now a `Row` with a text label + ▲/▼ `ButtonText` buttons. First item hides ▲, last item hides ▼. Buttons call `MoveItem(fromIndex, toIndex)` which uses `PopAt` + `EnqueueAt` on the reflected queue, then refreshes the display. Verified in-game: buttons reorder the actual game queue correctly.

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

**Working approach (Update 4):**
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

### Phase 3 — Open Questions

- Can we inject UI into the existing research screen, or do we need a standalone window? (Deferred to Phase 4)
- Should the currently-researching item appear in the list (locked/grayed out)?

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
