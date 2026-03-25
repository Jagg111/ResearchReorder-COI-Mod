using System.Collections.Generic;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.UiToolkit.Component;

namespace ResearchReorder;

/// <summary>
/// The Research Queue reorder window panel.
/// Shows a numbered list of queued research items.
/// </summary>
[GlobalDependency(RegistrationMode.AsEverything)]
public class ResearchReorderWindowView : PanelWithHeader {

	private readonly ScrollColumn _scrollColumn;
	private readonly List<Display> _labels = new List<Display>();

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
		// Remove old labels
		foreach (var label in _labels) {
			label.SetVisible(false);
		}
		_labels.Clear();

		if (queueItems.Count == 0) {
			var emptyLabel = _scrollColumn.AddAndReturn(new Display(new LocStrFormatted("Queue is empty")));
			emptyLabel.FontSize(14);
			_labels.Add(emptyLabel);
			return;
		}

		for (int i = 0; i < queueItems.Count; i++) {
			string text = $"{i + 1}. {queueItems[i]}";
			var label = _scrollColumn.AddAndReturn(new Display(new LocStrFormatted(text)));
			label.FontSize(14);
			label.Margin(new Px(2));
			_labels.Add(label);
		}

		Log.Info($"ResearchReorder: Refreshed queue display with {queueItems.Count} items");
	}
}
