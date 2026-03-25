using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.UiToolkit.Component;

namespace ResearchReorder;

/// <summary>
/// The Research Queue reorder window panel.
/// Shows a numbered list of queued research items with up/down reorder buttons.
/// </summary>
[GlobalDependency(RegistrationMode.AsEverything)]
public class ResearchReorderWindowView : PanelWithHeader {

	private readonly ScrollColumn _scrollColumn;
	private readonly List<UiComponent> _rows = new List<UiComponent>();

	/// <summary>
	/// Called when the player clicks a move button.
	/// Args: (fromIndex, toIndex) in queue order.
	/// </summary>
	public Action<int, int> OnMoveRequested;

	public ResearchReorderWindowView()
		: base(new LocStrFormatted("Research Queue")) {
		this.Size(new Px(340), new Px(400));
		this.SetVisible(false);

		_scrollColumn = new ScrollColumn();
		_scrollColumn.FlexGrow(1f);
		this.BodyAdd(_scrollColumn);

		Log.Info("ResearchReorder: WindowView constructed");
	}

	/// <summary>
	/// Refreshes the displayed queue list. Pass the research names in queue order.
	/// </summary>
	public void RefreshQueue(List<string> queueItems) {
		// Remove old rows
		foreach (var row in _rows) {
			row.RemoveFromHierarchy();
		}
		_rows.Clear();

		if (queueItems.Count == 0) {
			var emptyLabel = new Display(new LocStrFormatted("Queue is empty"));
			emptyLabel.FontSize(14);
			_scrollColumn.Add(emptyLabel);
			_rows.Add(emptyLabel);
			return;
		}

		for (int i = 0; i < queueItems.Count; i++) {
			int index = i; // capture for closure

			var row = new Row(new Px(4));
			row.Margin(new Px(2));

			// Numbered label — takes up remaining space
			string text = $"{i + 1}. {queueItems[i]}";
			var label = new Display(new LocStrFormatted(text));
			label.FontSize(14);
			label.FlexGrow(1f);
			row.Add(label);

			// Move Up button (disabled for first item)
			var upBtn = new ButtonText(new LocStrFormatted("\u25b2"), () => {
				OnMoveRequested?.Invoke(index, index - 1);
			});
			upBtn.Size(new Px(30), new Px(24));
			if (i == 0) upBtn.SetVisible(false);
			row.Add(upBtn);

			// Move Down button (disabled for last item)
			var downBtn = new ButtonText(new LocStrFormatted("\u25bc"), () => {
				OnMoveRequested?.Invoke(index, index + 1);
			});
			downBtn.Size(new Px(30), new Px(24));
			if (i == queueItems.Count - 1) downBtn.SetVisible(false);
			row.Add(downBtn);

			_scrollColumn.Add(row);
			_rows.Add(row);
		}

		Log.Info($"ResearchReorder: Refreshed queue display with {queueItems.Count} items");
	}
}
