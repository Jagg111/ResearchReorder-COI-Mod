using System;
using System.Collections.Generic;
using System.Reflection;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Research;
using Mafi.Localization;
using Mafi.Unity.InputControl;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiStatic.Toolbar;
using Mafi.Unity.UiToolkit.Component;
using UnityEngine;

namespace ResearchReorder;

/// <summary>
/// Controller that manages the Research Queue window visibility.
/// Reads the research queue, populates the view, and handles reorder requests.
/// </summary>
[GlobalDependency(RegistrationMode.AsEverything)]
public class ResearchReorderWindowController : IToolbarItemController {

	private readonly ResearchReorderWindowView _view;
	private readonly ResearchManager _researchMgr;
	private readonly FieldInfo _queueField;
	private bool _isVisible;

	public ControllerConfig Config => ControllerConfig.Window;
	public bool IsVisible => _isVisible;
	public bool DeactivateShortcutsIfNotVisible => false;
	public event Action<IToolbarItemController> VisibilityChanged;

	public ResearchReorderWindowController(
		ResearchReorderWindowView view,
		ToolbarHud toolbar,
		ResearchManager researchManager) {
		_view = view;
		_researchMgr = researchManager;

		// Cache the reflection field for queue access
		_queueField = typeof(ResearchManager).GetField(
			"m_researchQueue",
			BindingFlags.NonPublic | BindingFlags.Instance
		);

		if (_queueField == null) {
			Log.Error("ResearchReorder: Could not find m_researchQueue field!");
		}

		// Wire up the view's move callback to our reorder logic
		_view.OnMoveRequested = MoveItem;

		toolbar.AddMainMenuButton(
			new LocStrFormatted("Research Queue"),
			this,
			"",
			1500f,
			sm => KeyBindings.FromKey(KbCategory.Windows, ShortcutMode.Game, KeyCode.F9)
		);

		toolbar.AddToolWindow(_view);

		Log.Info("ResearchReorder: Controller constructed, toolbar button registered");
	}

	public void Activate() {
		_isVisible = true;
		_view.SetVisible(true);
		VisibilityChanged?.Invoke(this);
		RefreshQueueDisplay();
		Log.Info("ResearchReorder: Window activated");
	}

	public void Deactivate() {
		_isVisible = false;
		_view.SetVisible(false);
		VisibilityChanged?.Invoke(this);
		Log.Info("ResearchReorder: Window deactivated");
	}

	public bool InputUpdate() {
		return false;
	}

	/// <summary>
	/// Moves a queue item from one position to another using PopAt + EnqueueAt,
	/// then refreshes the display.
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

		RefreshQueueDisplay();
	}

	private void RefreshQueueDisplay() {
		var items = new List<string>();

		if (_queueField == null) {
			items.Add("[Error: queue field not found]");
			_view.RefreshQueue(items);
			return;
		}

		var queue = (Queueue<ResearchNode>)_queueField.GetValue(_researchMgr);
		foreach (ResearchNode node in queue) {
			items.Add(node.Proto.Strings.Name.TranslatedString);
		}

		_view.RefreshQueue(items);
	}
}
