using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Research;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Component.Manipulators;
using Mafi.Unity.UiToolkit.Library;

namespace ResearchReorder;

/// <summary>
/// Discovers the game's ResearchWindow and injects a queue panel into it.
/// When no research node is selected, the panel shows the current research
/// with a live progress bar, plus the research queue with reorder buttons.
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

	// Phase 5c: Current research section
	private ProgressBarPercentInline _progressBar;
	private Label _currentResearchNameLabel;
	private Column _currentResearchContent;   // Visible when research is active
	private Label _noResearchLabel;           // Visible when no research
	private PropertyInfo _currentResearchProp;   // Cached: CurrentResearch property on ResearchManager
	private MethodInfo _refreshQueueMethod;      // Cached: refreshQueueValues() on ResearchManager

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

		// Cache reflection for current research manipulation
		_currentResearchProp = typeof(ResearchManager).GetProperty("CurrentResearch");
		_refreshQueueMethod = typeof(ResearchManager).GetMethod("refreshQueueValues",
			BindingFlags.NonPublic | BindingFlags.Instance);

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
	/// of ResearchDetailUi. The panel has two sections:
	/// 1. CURRENT RESEARCH — shows active research with progress bar and controls
	/// 2. RESEARCH QUEUE — shows queued items with reorder buttons
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

			// Build the queue panel matching native ResearchDetailUi styling
			_injectedPanel = new Panel();
			Px panelWidth = new Px(468); // fallback
			if (_researchDetailUi != null) {
				var minWidthField = _researchDetailUi.GetType().GetField("MIN_WIDTH",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				if (minWidthField != null) {
					panelWidth = (Px)minWidthField.GetValue(null);
					Log.Info($"ResearchReorder: Using native MIN_WIDTH = {panelWidth}");
				}
			}
			_injectedPanel.Width(panelWidth);
			_injectedPanel.MaxWidth(25.Percent());
			_injectedPanel.AlignSelfStretch();
			_injectedPanel.Body.JustifyItemsCenter();

			var contentCol = new Column(2.pt());
			contentCol.AlignItemsStretch();

			// ── Main title row (colored background) ──
			var titleRow = new Row(1.pt());
			titleRow.Padding(8.px()).MarginLeftRight(-PanelBase<Panel, Column>.PADDING).JustifyItemsCenter();
			titleRow.Background(new ColorRgba(3700253, 83));
			var title = new Label(new LocStrFormatted("Research Queue"));
			title.TextCenterMiddle().FontBold().FontSize(15);
			titleRow.Add(title);

			// ── CURRENT RESEARCH section ──
			var currentResearchHeader = new Label(new LocStrFormatted("Current Research"));
			currentResearchHeader.FontBold().UpperCase();

			// Content shown when research is active (name + progress bar + buttons)
			_currentResearchContent = new Column(1.pt());
			_currentResearchContent.AlignItemsStretch();

			_currentResearchNameLabel = new Label(new LocStrFormatted(""));
			_currentResearchNameLabel.FontSize(15);

			_progressBar = new ProgressBarPercentInline();

			var cancelBtn = new ButtonIcon(Button.Danger,
				"Assets/Unity/UserInterface/General/Cancel.svg",
				() => CancelCurrentResearch());
			cancelBtn.AlignSelfCenter();

			_currentResearchContent.Add(_currentResearchNameLabel, _progressBar, cancelBtn);

			// Label shown when no research is active
			_noResearchLabel = new Label(new LocStrFormatted("No research"));
			_noResearchLabel.FontSize(14).TextCenterMiddle();

			// ── RESEARCH QUEUE section ──
			var queueHeader = new Label(new LocStrFormatted("Research Queue"));
			queueHeader.FontBold().UpperCase();

			_embeddedScroll = new ScrollColumn();
			_embeddedScroll.FlexGrow(1f);

			// Assemble everything
			contentCol.Add(titleRow);
			contentCol.Add(currentResearchHeader);
			contentCol.Add(_currentResearchContent);
			contentCol.Add(_noResearchLabel);
			contentCol.Add(queueHeader);
			contentCol.Add(_embeddedScroll);
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
	/// Refreshes both the current research section and the queue list.
	/// </summary>
	private void RefreshEmbeddedPanel() {
		if (_embeddedScroll == null) return;

		// Update current research display
		UpdateCurrentResearchSection();

		// Rebuild queue rows
		foreach (var row in _embeddedRows) {
			row.RemoveFromHierarchy();
		}
		_embeddedRows.Clear();

		var items = ReadQueueItems();
		BuildQueueRows(_embeddedScroll, _embeddedRows, items);
	}

	/// <summary>
	/// Updates the current research section: name, progress bar, button visibility.
	/// Called on refresh and can be called independently for progress updates.
	/// </summary>
	private void UpdateCurrentResearchSection() {
		if (_currentResearchContent == null) return;

		var currentOpt = _researchMgr.CurrentResearch;

		if (currentOpt.HasValue) {
			var node = currentOpt.ValueOrNull;
			if (node == null) {
				_currentResearchContent.SetVisible(false);
				_noResearchLabel.SetVisible(true);
				return;
			}

			_currentResearchContent.SetVisible(true);
			_noResearchLabel.SetVisible(false);

			_currentResearchNameLabel.Value(
				new LocStrFormatted(node.Proto.Strings.Name.TranslatedString));

			// Update progress bar
			var progress = node.ProgressInPerc;
			_progressBar.Value(progress);
			bool hasLab = _researchMgr.HasActiveLab;
			_progressBar.SetState(hasLab ? DisplayState.Positive : DisplayState.Warning);
		} else {
			_currentResearchContent.SetVisible(false);
			_noResearchLabel.SetVisible(true);
		}
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
			UpdateProgressBar();
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
	/// Updates the progress bar every frame for real-time feedback.
	/// Only runs when our panel is visible.
	/// </summary>
	private void UpdateProgressBar() {
		if (_progressBar == null || !_injectedPanel.IsVisible()) return;

		var currentOpt = _researchMgr.CurrentResearch;
		if (!currentOpt.HasValue) return;

		var node = currentOpt.ValueOrNull;
		if (node == null) return;

		_progressBar.Value(node.ProgressInPerc);
		bool hasLab = _researchMgr.HasActiveLab;
		_progressBar.SetState(hasLab ? DisplayState.Positive : DisplayState.Warning);
	}

	/// <summary>
	/// Cancels the current research and auto-starts the next queued item (if any).
	/// Unlike the native StopResearch() which clears the entire queue, this
	/// preserves the queue — only the current item is removed.
	/// </summary>
	private void CancelCurrentResearch() {
		var currentOpt = _researchMgr.CurrentResearch;
		if (!currentOpt.HasValue) return;

		var current = currentOpt.ValueOrNull;
		if (current == null) return;

		// Cancel research on the node (resets state, preserves progress points)
		((IResearchNodeFriend)current).CancelResearch();

		// Clear CurrentResearch via reflection (private setter)
		var setter = _currentResearchProp?.GetSetMethod(true);
		if (setter != null) {
			setter.Invoke(_researchMgr, new object[] { Option<ResearchNode>.None });
		}

		// Auto-start next queued item if available
		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
		if (queue.Count > 0) {
			var next = queue.Dequeue();
			_researchMgr.TryStartResearch(next.Proto, out _);
		}

		_refreshQueueMethod?.Invoke(_researchMgr, null);
		RefreshEmbeddedPanel();
		Log.Info($"ResearchReorder: Cancelled '{current.Proto.Strings.Name.TranslatedString}'");
	}

	/// <summary>
	/// Promotes a queue item to active research. The old active research (if any)
	/// is placed at the front of the queue so it's next in line.
	/// </summary>
	private void PromoteToActive(int queueIndex) {
		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
		if (queueIndex < 0 || queueIndex >= queue.Count) return;

		var promoted = queue.PopAt(queueIndex);

		// If something is currently being researched, cancel it and put it at queue front
		var currentOpt = _researchMgr.CurrentResearch;
		if (currentOpt.HasValue) {
			var oldCurrent = currentOpt.ValueOrNull;
			if (oldCurrent != null) {
				((IResearchNodeFriend)oldCurrent).CancelResearch();
				var setter = _currentResearchProp?.GetSetMethod(true);
				if (setter != null) {
					setter.Invoke(_researchMgr, new object[] { Option<ResearchNode>.None });
				}
				queue.EnqueueAt(oldCurrent, 0);
			}
		}

		_researchMgr.TryStartResearch(promoted.Proto, out _);

		_refreshQueueMethod?.Invoke(_researchMgr, null);
		RefreshEmbeddedPanel();
		Log.Info($"ResearchReorder: Promoted '{promoted.Proto.Strings.Name.TranslatedString}' to active research");
	}

	/// <summary>
	/// Removes an item from the research queue entirely.
	/// </summary>
	private void RemoveFromQueue(int queueIndex) {
		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
		if (queueIndex < 0 || queueIndex >= queue.Count) return;

		var removed = queue.PopAt(queueIndex);
		_refreshQueueMethod?.Invoke(_researchMgr, null);
		RefreshEmbeddedPanel();
		Log.Info($"ResearchReorder: Removed '{removed.Proto.Strings.Name.TranslatedString}' from queue");
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
	/// Builds queue rows with drag handles for reordering, a promote button (▶)
	/// to start researching that item, and a remove button (✕) to dequeue.
	/// </summary>
	private void BuildQueueRows(
		UiComponent container,
		List<UiComponent> trackingList,
		List<string> queueItems) {

		if (queueItems.Count == 0) {
			var emptyLabel = new Label(new LocStrFormatted("Empty"));
			emptyLabel.FontSize(14).TextCenterMiddle();
			container.Add(emptyLabel);
			trackingList.Add(emptyLabel);
			return;
		}

		for (int i = 0; i < queueItems.Count; i++) {
			int index = i; // capture for closure

			var row = new Row(1.pt());
			row.Margin(1.pt());
			row.JustifyItemsCenter();

			// Drag handle — styled like the game's LeftDragHandle but inline (not absolute-positioned)
			var dragCol = new Column();
			dragCol.Width(24.px()).AlignSelfStretch().JustifyItemsCenter()
				.Background(3224115)
				.BorderRight(1.px(), 2763306)
				.BorderRadiusLeft(4)
				.Padding(1.pt());
			var gripLabel = new Label(new LocStrFormatted("\u2630")); // ☰ trigram icon
			gripLabel.TextCenterMiddle().FontSize(10).Opacity(0.6f);
			dragCol.Add(gripLabel);
			row.Add(dragCol);

			// Research name label
			var label = new Label(new LocStrFormatted(queueItems[i]));
			label.FontSize(15).FlexGrow(1f).Margin(2.px());
			row.Add(label);

			// Promote button — start researching this item now
			var promoteBtn = new ButtonText(Button.Primary, new LocStrFormatted("\u25b6"));
			promoteBtn.OnClick((Action)(() => PromoteToActive(index)), allowKeyPresses: false);
			row.Add(promoteBtn);

			// Remove button — gray text button matching native ResearchDetailUi "Remove from queue"
			var removeBtn = new ButtonText(Tr.ResearchQueue__Remove);
			removeBtn.OnClick((Action)(() => RemoveFromQueue(index)), allowKeyPresses: false);
			row.Add(removeBtn);

			// Wire up drag-and-drop reordering via the game's Reorderable manipulator
			var reorderable = new Reorderable(dragCol.RootElement);
			reorderable.OnOrderChanged += (oldIdx, newIdx) => MoveItem(oldIdx, newIdx);
			row.AddManipulator(reorderable);

			container.Add(row);
			trackingList.Add(row);
		}
	}
}
