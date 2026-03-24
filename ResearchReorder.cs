using Mafi;
using Mafi.Core.Mods;

namespace ResearchReorder;

public sealed class ResearchReorderMod : DataOnlyMod {

	public ResearchReorderMod(ModManifest manifest) : base(manifest) {
		Log.Info("ResearchReorder: constructed");
	}

	public override void RegisterPrototypes(ProtoRegistrator registrator) {
		Log.Info("ResearchReorder: registering prototypes");
	}
}
