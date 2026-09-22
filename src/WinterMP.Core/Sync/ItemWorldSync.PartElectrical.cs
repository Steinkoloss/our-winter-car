using System;
using HutongGames.PlayMaker;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private static void CaptureAlternatorDamage(ReplacementBinding binding, ReplacementPartState state)
        {
            string? variable = binding.Factory.Rule.AlternatorDamageVariable;
            if (variable == null || !PartAttachmentPolicy.HasAttachment(state)) return;
            var c = SyncCatalog.ReplacementParts!;
            var install = binding.Data.FsmVariables.FindFsmGameObject(c["installPointVariable"])?.Value;
            string? source = null;
            foreach (var reference in binding.Factory.Rule.References)
                if (reference.Target == c["installPointVariable"]) source = reference.Source;
            if (source == null || install == null || binding.Factory.Fsm.FsmVariables.FindFsmGameObject(source)?.Value != install)
                throw new InvalidOperationException("Host alternator factory and fitted mount disagree.");
            PlayMakerFSM? mount = null;
            foreach (var fsm in install.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == c["itemFsm"])
                {
                    if (mount != null) throw new InvalidOperationException("Ambiguous host alternator mount Data.");
                    mount = fsm;
                }
            if (mount == null || mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value != true
                || mount.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != binding.Data.gameObject
                || mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value != binding.Data.transform.parent?.gameObject)
                throw new InvalidOperationException("Host alternator attachment changed during capture.");
            // Damaged belongs to the fitted mount, not part Data. Copy the host's
            // actual flag; neither a wear threshold nor a guest saved flag replaces it.
            var damage = mount.FsmVariables.FindFsmBool(variable);
            if (damage == null) throw new InvalidOperationException("Missing host alternator damage flag.");
            state.AlternatorDamaged = damage.Value;
        }
    }
}
