using System;
using System.Collections.Generic;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.UiToolkit.Component;

namespace ResearchReorder;

/// <summary>
/// The Research Queue reorder window panel (standalone F9 window).
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
		foreach (var row in _rows) {
			row.RemoveFromHierarchy();
		}
		_rows.Clear();

		BuildQueueRows(_scrollColumn, _rows, queueItems, OnMoveRequested);
	}

	/// <summary>
	/// Builds numbered queue rows with arrow buttons into a target container.
	/// Uses Label (not Display) and native game styling patterns to match
	/// the look of ResearchDetailUi.
	/// </summary>
	public static void BuildQueueRows(
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
