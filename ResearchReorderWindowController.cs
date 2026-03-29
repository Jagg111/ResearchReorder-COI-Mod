using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Research;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace ResearchReorder;

/// <summary>
/// Discovers the game's ResearchWindow and injects a queue panel into it.
/// When no research node is selected, the panel shows the research queue with
/// arrow buttons for reordering.
/// </summary>
[GlobalDependency(RegistrationMode.AsEverything)]
public class ResearchReorderWindowController {

	private readonly ResearchManager _researchMgr;
	private readonly FieldInfo _queueField;
	private UiComponent _schedulerSource; // For deferred frame scheduling before panel exists

	// Phase 4: ResearchWindow discovery
	private object _rwController;     // The game's ResearchWindow+Controller instance
	private FieldInfo _windowField;   // m_window field on WindowController<ResearchWindow>
	private object _researchWindow;   // The ResearchWindow instance once found
	private bool _researchWindowFound;

	// Phase 4b-d: Embedded queue panel in the research tree
	private readonly IUnityInputMgr _inputMgr;
	private bool _panelInjected;
	private Panel _injectedPanel;             // Our Panel injected into the research tree
	private ScrollColumn _embeddedScroll;     // Scrollable area for queue rows inside the panel
	private readonly List<UiComponent> _embeddedRows = new List<UiComponent>();
	private UiComponent _researchDetailUi;    // The game's detail panel (toggled opposite to ours)
	private FieldInfo _selectedNodeField;      // m_selectedNode field on ResearchWindow
	private PropertyInfo _hasValueProp;        // HasValue property on Option<ResearchNodeUi>
	private bool _pollingActive;

	public ResearchReorderWindowController(
		ToolbarHud toolbar,
		ResearchManager researchManager,
		DependencyResolver resolver,
		IUnityInputMgr inputMgr) {
		_researchMgr = researchManager;
		_inputMgr = inputMgr;

		// Get a UiComponent from ToolbarHud's internal container for frame scheduling.
		// Needed by ScheduleDeferredExtraction before our injected panel exists.
		try {
			var containerField = typeof(ToolbarHud).GetField("m_mainContainer",
				BindingFlags.NonPublic | BindingFlags.Instance);
			_schedulerSource = containerField?.GetValue(toolbar) as UiComponent;
		} catch (Exception ex) {
			Log.Warning($"ResearchReorder: Could not get scheduler source: {ex.Message}");
		}

		// Cache the reflection field for queue access
		_queueField = typeof(ResearchManager).GetField(
			"m_researchQueue",
			BindingFlags.NonPublic | BindingFlags.Instance
		);

		if (_queueField == null) {
			Log.Error("ResearchReorder: Could not find m_researchQueue field!");
		}

		// Find ResearchWindow via the game's ResearchWindow+Controller.
		// The controller extends WindowController<ResearchWindow> and stores the
		// ResearchWindow in an inherited field: m_window (Option<ResearchWindow>).
		// The window is lazily created — Option is empty until research tree is first opened.
		foreach (object obj in resolver.AllResolvedInstances) {
			if (obj.GetType().FullName == "Mafi.Unity.Ui.Research.ResearchWindow+Controller") {
				_rwController = obj;
				break;
			}
		}

		if (_rwController != null) {
			_windowField = _rwController.GetType().BaseType?.GetField("m_window",
				BindingFlags.NonPublic | BindingFlags.Instance);

			if (_windowField != null) {
				TryExtractResearchWindow();
				if (!_researchWindowFound) {
					Log.Info("ResearchReorder: ResearchWindow not yet created — will retry on open");
				}
			} else {
				Log.Warning("ResearchReorder: m_window field NOT found on WindowController base type");
			}
		} else {
			Log.Warning("ResearchReorder: ResearchWindow+Controller NOT found in DI");
		}

		if (_researchWindowFound) {
			TryInjectPanel();
		}

		_inputMgr.ControllerActivated += OnControllerActivated;
		_inputMgr.ControllerDeactivated += OnControllerDeactivated;

		Log.Info("ResearchReorder: Controller constructed");
	}

	/// <summary>
	/// Tries to extract the ResearchWindow from the controller's m_window Option field.
	/// </summary>
	private void TryExtractResearchWindow() {
		if (_researchWindowFound || _windowField == null || _rwController == null) return;

		object optionValue = _windowField.GetValue(_rwController);
		if (optionValue == null) return;

		var optType = optionValue.GetType();
		var hasValueProp = optType.GetProperty("HasValue");
		if (hasValueProp == null || !(bool)hasValueProp.GetValue(optionValue)) return;

		var valueProp = optType.GetProperty("ValueOrNull");
		if (valueProp == null) return;

		_researchWindow = valueProp.GetValue(optionValue);
		if (_researchWindow != null) {
			_researchWindowFound = true;
			Log.Info("ResearchReorder: ResearchWindow found!");
		}
	}

	private void OnControllerActivated(IUnityInputController controller) {
		if (!ReferenceEquals(controller, _rwController)) return;

		if (!_researchWindowFound) {
			TryExtractResearchWindow();
		}
		if (_researchWindowFound && !_panelInjected) {
			TryInjectPanel();
		} else if (_researchWindowFound && _panelInjected) {
			RefreshEmbeddedPanel();
			StartVisibilityPolling();
		} else if (!_researchWindowFound) {
			ScheduleDeferredExtraction(1);
		}
	}

	private void OnControllerDeactivated(IUnityInputController controller) {
		if (!ReferenceEquals(controller, _rwController)) return;

		_pollingActive = false;

		if (!_researchWindowFound) {
			TryExtractResearchWindow();
		}
		if (_researchWindowFound && !_panelInjected) {
			TryInjectPanel();
		}
	}

	private void ScheduleDeferredExtraction(int attempt) {
		if (_panelInjected || attempt > 10 || _schedulerSource == null) return;

		try {
			_schedulerSource.RootElement.schedule.Execute(() => {
				if (_panelInjected) return;
				if (!_researchWindowFound) TryExtractResearchWindow();
				if (_researchWindowFound) {
					TryInjectPanel();
					Log.Info($"ResearchReorder: Deferred extraction succeeded on attempt {attempt}!");
				} else {
					ScheduleDeferredExtraction(attempt + 1);
				}
			});
		}
		catch (Exception ex) {
			Log.Warning($"ResearchReorder: ScheduleDeferredExtraction failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Injects our queue panel into the research tree's content Row as a sibling
	/// of ResearchDetailUi. Uses Panel (same base class as ResearchDetailUi) with
	/// default styling to match the native look.
	/// </summary>
	private void TryInjectPanel() {
		if (_panelInjected || _researchWindow == null) return;

		try {
			var rwComponent = _researchWindow as UiComponent;
			if (rwComponent == null) {
				Log.Warning("ResearchReorder: ResearchWindow is not a UiComponent");
				return;
			}

			var contentRow = FindParentOfType(rwComponent, "ResearchDetailUi");
			if (contentRow == null) {
				Log.Warning("ResearchReorder: Could not find parent Row of ResearchDetailUi");
				return;
			}

			// Grab ResearchDetailUi reference for visibility toggling
			foreach (var child in contentRow.AllChildren) {
				if (child.GetType().Name == "ResearchDetailUi") {
					_researchDetailUi = child;
				}
			}

			// Cache m_selectedNode field for visibility polling
			_selectedNodeField = _researchWindow.GetType().GetField(
				"m_selectedNode",
				BindingFlags.NonPublic | BindingFlags.Instance
			);
			if (_selectedNodeField != null) {
				object optionVal = _selectedNodeField.GetValue(_researchWindow);
				if (optionVal != null) {
					_hasValueProp = optionVal.GetType().GetProperty("HasValue");
				}
			}

			// Build the queue panel matching native ResearchDetailUi exactly:
			// Panel() with default bolts, AlignSelfStretch() for full height,
			// Body.JustifyItemsCenter(), single Column for content.
			_injectedPanel = new Panel();
			_injectedPanel.Width(new Px(300));
			_injectedPanel.AlignSelfStretch();  // Fill full height of parent Row (covers diamond plate)
			_injectedPanel.Body.JustifyItemsCenter();

			// Single content column matching native pattern (Column with 2pt gap)
			var contentCol = new Column(2.pt());
			contentCol.AlignItemsStretch();

			// Title row — matches native: Padding(8), MarginLeftRight(-PADDING), centered,
			// with a colored background like ResearchDetailUi's title bar.
			// Uses IN_QUEUE_COLOR from ResearchThemeHelper: ColorRgba(3700253, 83)
			var titleRow = new Row(1.pt());
			titleRow.Padding(8.px()).MarginLeftRight(-PanelBase<Panel, Column>.PADDING).JustifyItemsCenter();
			titleRow.Background(new ColorRgba(3700253, 83));
			var title = new Label(new LocStrFormatted("Research Queue"));
			title.TextCenterMiddle().FontBold().FontSize(15);
			titleRow.Add(title);

			// Scrollable list for queue items
			_embeddedScroll = new ScrollColumn();
			_embeddedScroll.FlexGrow(1f);

			contentCol.Add(titleRow, _embeddedScroll);
			_injectedPanel.Body.Add(contentCol);

			// Defer adding to next frame so layout picks it up
			_panelInjected = true;
			contentRow.RootElement.schedule.Execute(() => {
				contentRow.Add(_injectedPanel);
				_injectedPanel.SetVisible(true);
				RefreshEmbeddedPanel();
				StartVisibilityPolling();
				Log.Info("ResearchReorder: Queue panel injected into research tree!");
			});
		}
		catch (Exception ex) {
			Log.Warning($"ResearchReorder: Failed to inject panel: {ex.Message}");
		}
	}

	/// <summary>
	/// Refreshes the embedded queue panel with current queue data.
	/// </summary>
	private void RefreshEmbeddedPanel() {
		if (_embeddedScroll == null) return;

		foreach (var row in _embeddedRows) {
			row.RemoveFromHierarchy();
		}
		_embeddedRows.Clear();

		var items = ReadQueueItems();
		BuildQueueRows(_embeddedScroll, _embeddedRows, items, MoveItem);
	}

	private static UiComponent FindParentOfType(UiComponent parent, string childTypeName) {
		foreach (var child in parent.AllChildren) {
			if (child.GetType().Name == childTypeName) {
				return parent;
			}
			var result = FindParentOfType(child, childTypeName);
			if (result != null) return result;
		}
		return null;
	}

	private void StartVisibilityPolling() {
		if (_injectedPanel == null || _selectedNodeField == null || _hasValueProp == null) {
			Log.Warning("ResearchReorder: Cannot start visibility polling — missing references");
			return;
		}

		_pollingActive = true;
		PollVisibility();
	}

	private void PollVisibility() {
		if (!_pollingActive || _injectedPanel == null) return;

		try {
			UpdatePanelVisibility();
		} catch (Exception ex) {
			Log.Warning($"ResearchReorder: Visibility poll error: {ex.Message}");
			_pollingActive = false;
			return;
		}

		_injectedPanel.RootElement.schedule.Execute(() => PollVisibility());
	}

	private void UpdatePanelVisibility() {
		object optionVal = _selectedNodeField.GetValue(_researchWindow);
		if (optionVal == null) return;

		bool nodeSelected = (bool)_hasValueProp.GetValue(optionVal);

		if (nodeSelected) {
			if (_researchDetailUi != null && _researchDetailUi.IsVisible()) {
				_injectedPanel.SetVisible(false);
			}
		} else {
			_injectedPanel.SetVisible(true);
			if (_researchDetailUi != null) {
				_researchDetailUi.SetVisible(false);
			}
		}
	}

	/// <summary>
	/// Moves a queue item from one position to another using PopAt + EnqueueAt,
	/// then refreshes the embedded panel display.
	/// </summary>
	private void MoveItem(int fromIndex, int toIndex) {
		if (_queueField == null) return;

		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);

		if (fromIndex < 0 || fromIndex >= queue.Count || toIndex < 0 || toIndex >= queue.Count) {
			Log.Warning($"ResearchReorder: Invalid move {fromIndex} -> {toIndex} (queue size {queue.Count})");
			return;
		}

		var item = queue.PopAt(fromIndex);
		queue.EnqueueAt(item, toIndex);
		Log.Info($"ResearchReorder: Moved '{item.Proto.Strings.Name.TranslatedString}' from {fromIndex} to {toIndex}");

		RefreshEmbeddedPanel();
	}

	/// <summary>
	/// Reads queue item names into a list.
	/// </summary>
	private List<string> ReadQueueItems() {
		var items = new List<string>();
		if (_queueField == null) {
			items.Add("[Error: queue field not found]");
			return items;
		}

		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
		foreach (ResearchNode node in queue) {
			items.Add(node.Proto.Strings.Name.TranslatedString);
		}
		return items;
	}

	/// <summary>
	/// Builds numbered queue rows with arrow buttons into a target container.
	/// Uses Label (not Display) and native game styling patterns to match
	/// the look of ResearchDetailUi.
	/// </summary>
	private static void BuildQueueRows(
		UiComponent container,
		List<UiComponent> trackingList,
		List<string> queueItems,
		Action<int, int> onMoveRequested) {

		if (queueItems.Count == 0) {
			var emptyLabel = new Label(new LocStrFormatted("Queue is empty"));
			emptyLabel.FontSize(14);
			container.Add(emptyLabel);
			trackingList.Add(emptyLabel);
			return;
		}

		for (int i = 0; i < queueItems.Count; i++) {
			int index = i; // capture for closure

			var row = new Row(1.pt());
			row.Margin(1.pt());
			row.JustifyItemsCenter();

			// Numbered label
			string text = $"{i + 1}. {queueItems[i]}";
			var label = new Label(new LocStrFormatted(text));
			label.FontSize(15);
			label.FlexGrow(1f);
			row.Add(label);

			// Move Up button (hidden for first item)
			var upBtn = new ButtonText(new LocStrFormatted("\u25b2"), () => {
				onMoveRequested?.Invoke(index, index - 1);
			});
			upBtn.Size(new Px(30), new Px(24));
			if (i == 0) upBtn.SetVisible(false);
			row.Add(upBtn);

			// Move Down button (hidden for last item)
			var downBtn = new ButtonText(new LocStrFormatted("\u25bc"), () => {
				onMoveRequested?.Invoke(index, index + 1);
			});
			downBtn.Size(new Px(30), new Px(24));
			if (i == queueItems.Count - 1) downBtn.SetVisible(false);
			row.Add(downBtn);

			container.Add(row);
			trackingList.Add(row);
		}
	}
}
