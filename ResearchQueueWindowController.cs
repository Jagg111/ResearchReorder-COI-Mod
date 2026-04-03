using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Research;
using Mafi.Localization;
using Mafi.Unity;
using Mafi.Unity.Audio;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Component.Manipulators;
using Mafi.Unity.UiToolkit.Library;

namespace ResearchQueue;

/// <summary>
/// Discovers the game's ResearchWindow and injects a queue panel into it.
/// When no research node is selected, the panel shows the current research
/// with a live progress bar, plus the research queue with reorder buttons.
/// </summary>
[GlobalDependency(RegistrationMode.AsEverything)]
public class ResearchQueueWindowController {

	private readonly ResearchManager _researchMgr;
	private readonly FieldInfo _queueField;
	private readonly UnityEngine.AudioSource _invalidOpSound;
	private UiComponent _schedulerSource; // For deferred frame scheduling before panel exists

	// ResearchWindow discovery
	private object _rwController;     // The game's ResearchWindow+Controller instance
	private FieldInfo _windowField;   // m_window field on WindowController<ResearchWindow>
	private object _researchWindow;   // The ResearchWindow instance once found
	private bool _researchWindowFound;

	// Queue panel UI state
	private readonly IUnityInputMgr _inputMgr;
	private bool _panelInjected;
	private Panel _injectedPanel;             // Our Panel injected into the research tree
	private ScrollColumn _embeddedScroll;     // Scrollable area for queue rows inside the panel
	private readonly List<UiComponent> _embeddedRows = new List<UiComponent>();
	private UiComponent _researchDetailUi;    // The game's detail panel (toggled opposite to ours)
	private FieldInfo _selectedNodeField;      // m_selectedNode field on ResearchWindow
	private PropertyInfo _hasValueProp;        // HasValue property on Option<ResearchNodeUi>
	private bool _pollingActive;
	private int _lastKnownQueueCount = -1;
	private Option<ResearchNode> _lastKnownCurrentResearch;

	// Current research section
	private ProgressBarPercentInline _progressBar;
	private Label _currentResearchNameLabel;
	private Column _currentResearchContent;   // Visible when research is active
	private Label _noResearchLabel;           // Visible when no research
	private PropertyInfo _currentResearchProp;   // Cached: CurrentResearch property on ResearchManager
	private MethodInfo _refreshQueueMethod;      // Cached: refreshQueueValues() on ResearchManager

	public ResearchQueueWindowController(
		ToolbarHud toolbar,
		ResearchManager researchManager,
		DependencyResolver resolver,
		IUnityInputMgr inputMgr,
		AudioDb audioDb) {
		_researchMgr = researchManager;
		_inputMgr = inputMgr;
		_invalidOpSound = audioDb.GetSharedAudioUi("Assets/Unity/UserInterface/Audio/InvalidOp.prefab");

		// Get a UiComponent from ToolbarHud's internal container for frame scheduling.
		// Needed by ScheduleDeferredExtraction before our injected panel exists.
		try {
			var containerField = typeof(ToolbarHud).GetField("m_mainContainer",
				BindingFlags.NonPublic | BindingFlags.Instance);
			_schedulerSource = containerField?.GetValue(toolbar) as UiComponent;
		} catch (Exception ex) {
			Log.Warning($"ResearchQueue: Could not get scheduler source: {ex.Message}");
		}

		// Cache the reflection field for queue access
		_queueField = typeof(ResearchManager).GetField(
			"m_researchQueue",
			BindingFlags.NonPublic | BindingFlags.Instance
		);

		if (_queueField == null) {
			Log.Error("ResearchQueue: Could not find m_researchQueue field!");
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
					Log.Info("ResearchQueue: ResearchWindow not yet created — will retry on open");
				}
			} else {
				Log.Warning("ResearchQueue: m_window field NOT found on WindowController base type");
			}
		} else {
			Log.Warning("ResearchQueue: ResearchWindow+Controller NOT found in DI");
		}

		if (_researchWindowFound) {
			TryInjectPanel();
		}

		_inputMgr.ControllerActivated += OnControllerActivated;
		_inputMgr.ControllerDeactivated += OnControllerDeactivated;

		Log.Info("ResearchQueue: Controller constructed");
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
			Log.Info("ResearchQueue: ResearchWindow found!");
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
					Log.Info($"ResearchQueue: Deferred extraction succeeded on attempt {attempt}!");
				} else {
					ScheduleDeferredExtraction(attempt + 1);
				}
			});
		}
		catch (Exception ex) {
			Log.Warning($"ResearchQueue: ScheduleDeferredExtraction failed: {ex.Message}");
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
				Log.Warning("ResearchQueue: ResearchWindow is not a UiComponent");
				return;
			}

			var contentRow = FindParentOfType(rwComponent, "ResearchDetailUi");
			if (contentRow == null) {
				Log.Warning("ResearchQueue: Could not find parent Row of ResearchDetailUi");
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
					Log.Info($"ResearchQueue: Using native MIN_WIDTH = {panelWidth}");
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
			_embeddedScroll.ScrollerHidden(); // Hidden by default; UpdateScrollbarVisibility shows it when content overflows
			_embeddedScroll.MaxHeight(320.px()); // Match native ResearchDetailUi MAX_RECIPES_HEIGHT

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
				Log.Info("ResearchQueue: Queue panel injected into research tree!");
			});
		}
		catch (Exception ex) {
			Log.Warning($"ResearchQueue: Failed to inject panel: {ex.Message}");
		}
	}

	/// <summary>
	/// Refreshes both the current research section and the queue list.
	/// </summary>
	private void RefreshEmbeddedPanel() {
		if (_embeddedScroll == null) return;

		// Sync cached state so CheckForQueueChanges doesn't re-trigger
		if (_queueField != null) {
			var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
			_lastKnownQueueCount = queue.Count;
		}
		_lastKnownCurrentResearch = _researchMgr.CurrentResearch;

		// Update current research display
		UpdateCurrentResearchSection();

		// Rebuild queue rows
		foreach (var row in _embeddedRows) {
			row.RemoveFromHierarchy();
		}
		_embeddedRows.Clear();

		var nodes = ReadQueueNodes();
		BuildQueueRows(_embeddedScroll, _embeddedRows, nodes);

		// Defer scrollbar visibility check by one frame so layout has been computed.
		// A second nested check runs the frame after to catch gutter reflow when hiding.
		_embeddedScroll.RootElement.schedule.Execute(() => {
			UpdateScrollbarVisibility();
			_embeddedScroll.RootElement.schedule.Execute(UpdateScrollbarVisibility);
		});
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

	/// <summary>
	/// Called one frame after each panel rebuild to check whether the scroll content
	/// actually overflows the viewport. Shows the scrollbar only when needed.
	/// </summary>
	private void UpdateScrollbarVisibility() {
		if (_embeddedScroll == null) return;
		var scrollView = _embeddedScroll.RootElement as UnityEngine.UIElements.ScrollView;
		if (scrollView == null) {
			Log.Warning("ResearchQueue: Could not get ScrollView for scrollbar visibility check");
			return;
		}

		float contentHeight = scrollView.contentContainer.resolvedStyle.height;
		float viewportHeight = scrollView.contentViewport.resolvedStyle.height;

		if (float.IsNaN(contentHeight) || float.IsNaN(viewportHeight)) return;

		if (contentHeight > viewportHeight)
			_embeddedScroll.ScrollerAlwaysVisible();
		else
			_embeddedScroll.ScrollerHidden();
	}

	private void StartVisibilityPolling() {
		if (_injectedPanel == null || _selectedNodeField == null || _hasValueProp == null) {
			Log.Warning("ResearchQueue: Cannot start visibility polling — missing references");
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
			CheckForQueueChanges();
		} catch (Exception ex) {
			Log.Warning($"ResearchQueue: Visibility poll error: {ex.Message}");
			_pollingActive = false;
			return;
		}

		_injectedPanel.RootElement.schedule.Execute(() => PollVisibility());
	}

	private void UpdatePanelVisibility() {
		object optionVal = _selectedNodeField.GetValue(_researchWindow);
		if (optionVal == null) return;

		bool nodeSelected = (bool)_hasValueProp.GetValue(optionVal);
		bool wasVisible = _injectedPanel.IsVisible();
		if (nodeSelected) {
			if (_researchDetailUi != null && _researchDetailUi.IsVisible()) {
				_injectedPanel.SetVisible(false);
			}
		} else {
			_injectedPanel.SetVisible(true);
			if (!wasVisible) {
				RefreshEmbeddedPanel();
			}
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
	/// Detects external queue changes (e.g. player adding/removing research via the native
	/// ResearchDetailUi) by comparing queue count and active research against cached values.
	/// Triggers a full panel refresh when a change is detected. If the detail view was open
	/// when the change happened, also clears m_selectedNode so the tree deselects and the
	/// queue panel auto-shows — avoiding the need to manually press Escape first.
	/// </summary>
	private void CheckForQueueChanges() {
		if (_queueField == null) return;
		// Run whenever either our panel OR the detail view is visible — we need to detect
		// queue changes that happen while the detail view is open (our panel is hidden).
		bool detailViewOpen = _researchDetailUi != null && _researchDetailUi.IsVisible();
		if (!_injectedPanel.IsVisible() && !detailViewOpen) return;

		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
		var currentResearch = _researchMgr.CurrentResearch;

		bool changed = queue.Count != _lastKnownQueueCount
			|| currentResearch.HasValue != _lastKnownCurrentResearch.HasValue
			|| (currentResearch.HasValue && _lastKnownCurrentResearch.HasValue
				&& currentResearch.ValueOrNull != _lastKnownCurrentResearch.ValueOrNull);

		if (changed) {
			_lastKnownQueueCount = queue.Count;
			_lastKnownCurrentResearch = currentResearch;
			RefreshEmbeddedPanel();

			if (detailViewOpen && _selectedNodeField != null) {
				// Writing Option<T>.None to m_selectedNode deselects the node in the tree,
				// which causes UpdatePanelVisibility() to show our queue panel on the next frame.
				// NOTE: Option<T>.None is a static FIELD, not a property — GetField not GetProperty.
				var noneVal = _selectedNodeField.FieldType.GetField("None",
					BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
				if (noneVal != null) {
					_selectedNodeField.SetValue(_researchWindow, noneVal);
				}
			}
		}
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
		Log.Info($"ResearchQueue: Cancelled '{current.Proto.Strings.Name.TranslatedString}'");
	}

	/// <summary>
	/// Promotes a queue item to active research. The old active research (if any)
	/// is placed at the front of the queue so it's next in line.
	/// </summary>
	private void PromoteToActive(int queueIndex) {
		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
		if (queueIndex < 0 || queueIndex >= queue.Count) return;

		// Safety check: don't promote items whose prerequisites aren't met.
		// The UI hides the promote button for locked items, but this guards
		// against edge cases (e.g., state changing between render and click).
		var candidate = queue[queueIndex];
		if (candidate.IsLocked) {
			Log.Warning($"ResearchQueue: Cannot promote '{candidate.Proto.Strings.Name.TranslatedString}' — prerequisites not met");
			RefreshEmbeddedPanel();
			return;
		}

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
		Log.Info($"ResearchQueue: Promoted '{promoted.Proto.Strings.Name.TranslatedString}' to active research");
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
		Log.Info($"ResearchQueue: Removed '{removed.Proto.Strings.Name.TranslatedString}' from queue");
	}

	/// <summary>
	/// Moves a queue item from one position to another using PopAt + EnqueueAt,
	/// then refreshes the embedded panel display.
	/// </summary>
	private void MoveItem(int fromIndex, int toIndex) {
		if (_queueField == null) return;

		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);

		if (fromIndex < 0 || fromIndex >= queue.Count || toIndex < 0 || toIndex >= queue.Count) {
			Log.Warning($"ResearchQueue: Invalid move {fromIndex} -> {toIndex} (queue size {queue.Count})");
			return;
		}

		var item = queue.PopAt(fromIndex);
		queue.EnqueueAt(item, toIndex);
		Log.Info($"ResearchQueue: Moved '{item.Proto.Strings.Name.TranslatedString}' from {fromIndex} to {toIndex}");

		_refreshQueueMethod?.Invoke(_researchMgr, null);
		RefreshEmbeddedPanel();
	}

	/// <summary>
	/// Reads queue items into a list of ResearchNode objects.
	/// </summary>
	private List<ResearchNode> ReadQueueNodes() {
		var items = new List<ResearchNode>();
		if (_queueField == null) return items;

		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
		foreach (ResearchNode node in queue) {
			items.Add(node);
		}
		return items;
	}

	/// <summary>
	/// Returns the name of the first unresearched parent for a node, or null if all
	/// parents are researched (i.e., the node can be started).
	/// </summary>
	private static string GetFirstUnresearchedParentName(ResearchNode node) {
		foreach (var parent in node.Parents) {
			if (parent.TimesResearched <= 0) {
				return parent.Proto.Strings.Name.TranslatedString;
			}
		}
		return null;
	}

	/// <summary>
	/// Checks if a queue item is "out of order" — meaning it sits above an
	/// unresearched prerequisite in the queue. When the game processes the queue
	/// on research completion, out-of-order items get dequeued and silently
	/// discarded because they can't be started yet (see issue #3).
	/// Returns the name of the blocking prerequisite found later in the queue,
	/// or null if the item is in a safe position.
	/// </summary>
	private static string GetOutOfOrderPrereqName(
		ResearchNode node, int queueIndex, List<ResearchNode> queueNodes) {
		foreach (var parent in node.Parents) {
			if (parent.TimesResearched > 0) continue; // already researched, safe
			for (int j = queueIndex + 1; j < queueNodes.Count; j++) {
				if (ReferenceEquals(queueNodes[j], parent)) {
					return parent.Proto.Strings.Name.TranslatedString;
				}
			}
		}
		return null;
	}

	// ── Drag constraint helpers (issue #8) ──────────────────────────
	// These three static methods compute the valid index range for a
	// queue item during drag-and-drop. Together they prevent the player
	// from placing a research above its unresearched prerequisites or
	// below items that depend on it — which would cause the game to
	// silently discard the out-of-order item on research completion.

	/// <summary>
	/// Returns the earliest (lowest) queue index a node can occupy without
	/// sitting above any of its unresearched prerequisites in the queue.
	/// Example: if node C depends on B (at index 2, unresearched), the
	/// earliest valid index for C is 3 (one below B).
	/// </summary>
	private static int GetEarliestValidIndex(ResearchNode node, List<ResearchNode> queueNodes) {
		int earliest = 0;
		foreach (var parent in node.Parents) {
			if (parent.TimesResearched > 0) continue; // already researched, not blocking
			for (int j = 0; j < queueNodes.Count; j++) {
				if (ReferenceEquals(queueNodes[j], parent)) {
					earliest = Math.Max(earliest, j + 1); // must be below this parent
					break;
				}
			}
		}
		return earliest;
	}

	/// <summary>
	/// Returns the latest (highest) post-pop insert index a node can occupy
	/// without ending up after any of its dependents. All indices are in
	/// tempQueue (post-pop) coordinate space, where inserting at position i
	/// shifts the item currently at i to i+1. This means a dependent at
	/// tempQueue index i is still safe when we insert at i (dependent becomes
	/// i+1), so the constraint is latest = i, NOT i-1.
	/// ResearchNode has no Children property, so we derive dependents by
	/// scanning all queue items' Parents for references to this node.
	/// Starting value is Count (not Count-1) so that appending to the end
	/// of the queue is always a valid insert position when no dependents exist.
	/// Example: if A is needed by B (at tempQueue index 2), the latest valid
	/// insert index for A is 2 (inserting at 2 pushes B to index 3).
	/// </summary>
	private static int GetLatestValidIndex(ResearchNode node, List<ResearchNode> queueNodes) {
		int latest = queueNodes.Count; // Count (not Count-1) so appending to end is a valid position
		if (node.TimesResearched > 0) return latest; // already researched, nothing depends on us
		for (int i = 0; i < queueNodes.Count; i++) {
			foreach (var parent in queueNodes[i].Parents) {
				if (ReferenceEquals(parent, node) && parent.TimesResearched <= 0) {
					latest = Math.Min(latest, i); // inserting at i shifts dependent to i+1, so i is still valid
					break;
				}
			}
		}
		return latest;
	}

	/// <summary>
	/// Computes the clamped target index for a drag operation. Builds a
	/// temporary queue with the dragged item removed (simulating PopAt)
	/// so that index math is correct relative to the post-removal state.
	/// </summary>
	private static int ClampMoveIndex(int fromIndex, int requestedToIndex, List<ResearchNode> queueNodes) {
		var draggedNode = queueNodes[fromIndex];

		// Build queue snapshot without the dragged item (PopAt shifts indices)
		var tempQueue = new List<ResearchNode>(queueNodes.Count - 1);
		for (int i = 0; i < queueNodes.Count; i++) {
			if (i != fromIndex) tempQueue.Add(queueNodes[i]);
		}

		int earliest = GetEarliestValidIndex(draggedNode, tempQueue);
		int latest = GetLatestValidIndex(draggedNode, tempQueue);
		return Math.Max(earliest, Math.Min(requestedToIndex, latest));
	}

	/// <summary>
	/// Drag callback that clamps the target index to the nearest valid position
	/// before applying the move. Plays the game's native error sound when the
	/// drop position was constrained. If the item can't move at all, the panel
	/// refreshes to snap the visual state back to the original order.
	/// </summary>
	private void MoveItemClamped(int fromIndex, int toIndex) {
		if (_queueField == null) return;

		var queueNodes = ReadQueueNodes();
		if (fromIndex < 0 || fromIndex >= queueNodes.Count || toIndex < 0 || toIndex >= queueNodes.Count) {
			RefreshEmbeddedPanel();
			return;
		}

		int clampedTo = ClampMoveIndex(fromIndex, toIndex, queueNodes);

		// Item couldn't move — snap back and play error cue
		if (clampedTo == fromIndex) {
			_invalidOpSound?.Play();
			RefreshEmbeddedPanel();
			return;
		}

		// Item moved but was clamped to a different slot than requested
		if (clampedTo != toIndex) {
			_invalidOpSound?.Play();
		}

		MoveItem(fromIndex, clampedTo);
	}

	/// <summary>
	/// Builds queue rows with drag handles for reordering, a promote button (▶)
	/// to start researching that item, and a remove button (✕) to dequeue.
	/// Items whose prerequisites aren't met show a "Needs: X" label instead of
	/// the promote button, so the player knows why it can't be started.
	/// </summary>
	private void BuildQueueRows(
		UiComponent container,
		List<UiComponent> trackingList,
		List<ResearchNode> queueNodes) {

		if (queueNodes.Count == 0) {
			var emptyLabel = new Label(new LocStrFormatted("Empty"));
			emptyLabel.FontSize(14).TextCenterMiddle();
			container.Add(emptyLabel);
			trackingList.Add(emptyLabel);
			return;
		}

		for (int i = 0; i < queueNodes.Count; i++) {
			int index = i; // capture for closure
			var node = queueNodes[i];
			bool isLocked = node.IsLocked;
			string outOfOrderPrereq = GetOutOfOrderPrereqName(node, i, queueNodes);

			var row = new Row(0.pt());
			row.MarginBottom(3.px());
			row.JustifyItemsCenter();
			row.StyleGroup(); // Native dark background + border (same as recipe rows)

			// Drag handle — styled with native CSS classes and SVG icon
			var dragCol = new Column();
			dragCol.Width(24.px()).AlignSelfStretch().JustifyItemsCenter()
				.Class(Cls.reorderHandle, Cls.reorderHandleAlphaHover)
				.Background(3224115)
				.BorderRight(1.px(), 2763306)
				.BorderRadiusLeft(4)
				.Padding(1.pt());
			var dragIcon = new Icon("Assets/Unity/UserInterface/General/Drag.svg");
			dragIcon.Opacity(0.6f).Size(10.px()).AlignSelfCenter();
			dragCol.Add(dragIcon);
			row.Add(dragCol);

			// Content area — tinted for out-of-order items, flush against drag handle
			var contentRow = new Row(1.pt());
			contentRow.FlexGrow(1f).AlignSelfStretch().JustifyItemsCenter();
			if (outOfOrderPrereq != null) {
				contentRow.Background(new ColorRgba(15166315, 40)); // LOCKED_COLOR
			}

			// Research name label
			var label = new Label(new LocStrFormatted(
				node.Proto.Strings.Name.TranslatedString));
			label.FontSize(15).FlexGrow(1f).Margin(2.px());
			contentRow.Add(label);

			if (outOfOrderPrereq != null) {
				// Out-of-order warning: this item sits above a prerequisite in the queue.
				// If the game processes the queue before that prereq finishes, this item
				// will be silently removed (issue #3).
				var warnLabel = new Label(new LocStrFormatted($"Move below: {outOfOrderPrereq}"));
				warnLabel.FontSize(12).Margin(2.px());
				contentRow.Add(warnLabel);
			} else if (isLocked) {
				// Locked but not out-of-order — prereqs exist but aren't in the queue
				var blockerName = GetFirstUnresearchedParentName(node) ?? "prerequisites";
				var needsLabel = new Label(new LocStrFormatted($"Needs: {blockerName}"));
				needsLabel.FontSize(12).Opacity(0.6f).Margin(2.px());
				contentRow.Add(needsLabel);
			} else {
				// Promote button — start researching this item now
				var promoteBtn = new ButtonIcon(Button.Primary,
					"Assets/Unity/UserInterface/General/ResearchEfficiency.svg",
					() => PromoteToActive(index));
				contentRow.Add(promoteBtn);
			}

			// Remove button — compact red X icon matching the current research cancel button
			var removeBtn = new ButtonIcon(Button.Danger,
				"Assets/Unity/UserInterface/General/Cancel.svg",
				() => RemoveFromQueue(index));
			contentRow.Add(removeBtn);
			row.Add(contentRow);

			// Wire up drag-and-drop reordering via the game's Reorderable manipulator
			var reorderable = new Reorderable(dragCol.RootElement);
			reorderable.OnOrderChanged += (oldIdx, newIdx) => MoveItemClamped(oldIdx, newIdx);
			row.AddManipulator(reorderable);

			container.Add(row);
			trackingList.Add(row);
		}
	}
}
