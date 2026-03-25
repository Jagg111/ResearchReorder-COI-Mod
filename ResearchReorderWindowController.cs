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
/// Reads the research queue and populates the view on activation.
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
