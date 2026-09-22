using System;
using System.Linq;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace PaneScrapeBridge.Tests
{
    public sealed partial class BridgeTests
    {
        [Theory]
        [InlineData(ScraperOperation.Pickup, false)][InlineData(ScraperOperation.Equip, false)]
        [InlineData(ScraperOperation.Off, false)][InlineData(ScraperOperation.Drop, false)]
        [InlineData(ScraperOperation.KeepAlive, false)][InlineData(ScraperOperation.Stroke, false)]
        [InlineData(ScraperOperation.Pickup, true)][InlineData(ScraperOperation.Equip, true)]
        [InlineData(ScraperOperation.Off, true)][InlineData(ScraperOperation.Drop, true)]
        [InlineData(ScraperOperation.KeepAlive, true)][InlineData(ScraperOperation.Stroke, true)]
        public void ResultCallbacksCannotReenterTheAcceptedStrokeTransaction(ScraperOperation nestedOperation, bool personalEffect)
        {
            using var host = new Rig();
            byte actor = personalEffect ? (byte)0 : (byte)2;
            Physics.Contact = host.ToolContact;
            Assert.Equal(PaneScrapeStatus.Accepted, host.Act(host.Request(1, ScraperOperation.Pickup, actor), actor).Status);
            Assert.Equal(PaneScrapeStatus.Accepted, host.Act(host.Request(2, ScraperOperation.Equip, actor), actor).Status);
            Physics.Contact = host.Pane;
            var replica = new PaneScrapeReplica(9, host.Epoch, actor);
            var initial = host.Session.Sent.OfType<PaneScrapeUpdate>().Last();
            Assert.True(replica.ReceiveSnapshot(true, initial.Snapshot()));
            int sent = host.Session.Sent.Count, callbacks = 0;
            var nested = host.Request(4, nestedOperation, actor);
            Action reenter = () => {
                callbacks++;
                host.Effect.Callback = null;
                host.Session.Sending = null;
                host.Bridge.OnAction(Assert.IsType<ScraperAction>(PacketCodec.Decode(PacketCodec.Encode(nested))), actor);
            };
            if (personalEffect) host.Effect.Callback = reenter;
            else host.Session.Sending = _ => reenter();

            var accepted = host.Act(host.Request(3, ScraperOperation.Stroke, actor), actor);
            Assert.Equal(1, callbacks);
            Assert.False(Get<bool>(host.Bridge, "_failed"));
            Assert.Equal(PaneScrapeStatus.Accepted, accepted.Status);
            Assert.Equal(3u, accepted.Sequence);
            Assert.Equal(sent + 1, host.Session.Sent.Count); // no nested result sent ahead of/after outer result
            Assert.Equal(1, host.Glass.Calls); Assert.Equal(1, host.Material.Writes);
            Assert.Equal(personalEffect ? 1 : 0, host.Effect.Calls);
            Assert.Equal(.25f + .005f, host.Cutoff.Value);
            Assert.Equal(host.Cutoff.Value, host.Material.Cutoff);
            Assert.Equal(host.Cutoff.Value, accepted.Cutoff);
            Assert.Equal(actor, accepted.Holder); Assert.True(accepted.Equipped);
            var lease = Get<ScraperLease>(host.Bridge, "_lease");
            Assert.Equal(actor, lease.Holder); Assert.True(lease.Equipped); Assert.Equal(4u, lease.Seen(actor));
            Assert.Null(Get<ScraperAction?>(host.Bridge, "_request"));
            Assert.False(host.Car.LocallyOwned); Assert.Equal((byte)255, host.Car.RemoteOwner);
            Assert.Equal(.25f, replica.Current!.Cutoff); // no guest prediction while awaiting the absolute result
            Assert.True(replica.ReceiveDecision(true, accepted.Decision(), out bool effect)); Assert.True(effect);
            Assert.Equal(host.Cutoff.Value, replica.Current.Cutoff);

            var denied = host.Act(nested, actor);
            Assert.NotEqual(PaneScrapeStatus.Accepted, denied.Status);
            Assert.Equal(accepted.Cutoff, denied.Cutoff); Assert.Equal(accepted.Revision, denied.Revision);
            Assert.Equal(4u, denied.HighWater);
            bool received = denied.IsDecision ? replica.ReceiveDecision(true, denied.Decision(), out effect)
                : replica.ReceiveSnapshot(true, denied.Snapshot());
            Assert.True(received);
            if (denied.IsDecision) Assert.False(effect);
            Assert.Equal(accepted.Cutoff, replica.Current.Cutoff);
            Assert.Equal(1, host.Glass.Calls); Assert.Equal(1, host.Material.Writes);
            Assert.Equal(personalEffect ? 1 : 0, host.Effect.Calls);
            Assert.Equal(PaneScrapeStatus.Accepted, host.Act(host.Request(5, ScraperOperation.Stroke, actor), actor).Status);
            Assert.Equal(2, host.Glass.Calls); // guard releases for a fresh ordinary request
        }
    }
}
