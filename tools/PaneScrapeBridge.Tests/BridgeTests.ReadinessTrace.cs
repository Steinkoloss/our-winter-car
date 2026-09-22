using System;
using System.Linq;
using System.Text.Json;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Sync;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace PaneScrapeBridge.Tests
{
    public sealed partial class BridgeTests
    {
        // Structured observations of the existing injected fixture, not native operands.
        // Authentication here is the session-double actor passed to production OnAction.
        private void FixtureRecord(string kind, string stage, object data)
        {
            _output.WriteLine("V11_FIXTURE " + JsonSerializer.Serialize(new {
                kind, stage, data,
                run_token = Environment.GetEnvironmentVariable("WINTERMP_V11_PORTABLE_TOKEN") ?? "",
                evidence_level = "portable-engine-session-physics-fixture"
            }));
        }

        private void TracePreconditions(string stage, Rig host, Rig guest, PlayerSyncManager hostPose, PlayerSyncManager guestPose)
        {
            Assert.True(hostPose.TryReadLocalPose(out var hostFeet, out _));
            Assert.True(guestPose.TryReadLocalPose(out var guestFeet, out _));
            var actor = host.Session.Players.Single(p => p.PlayerId == guest.Session.LocalPlayerId);
            var inside = Get<Collider>(host.Bridge, "_insideCollider");
            var guestInside = Get<FsmBool>(guest.Bridge, "_inside");
            var hostLease = Get<ScraperLease>(host.Bridge, "_lease");
            Assert.False(host.Session.IsPassengerInVehicle(actor.PlayerId, host.Car.Id));
            Assert.False(inside.bounds.Contains(actor.Position + Vector3.up * .4f));
            Assert.False(guestInside.Value);
            FixtureRecord("preconditions", stage, new {
                host_is_host = host.Session.IsHost, guest_is_host = guest.Session.IsHost,
                host_id = (int)host.Session.LocalPlayerId, guest_id = (int)guest.Session.LocalPlayerId,
                observed_actor_id = (int)actor.PlayerId, pose_now = Time.unscaledTime,
                pose_timestamp = actor.LastTransformTime, guest_alive = !actor.IsDead,
                guest_pose_matches = guestFeet.Equals(actor.Position),
                host_guest_distance_squared = (hostFeet - guestFeet).sqrMagnitude,
                host_car_distance_squared = (hostFeet - host.Car.Body!.transform.position).sqrMagnitude,
                host_has_tool = host.Picked.Value != null, host_lease_holder = (int)hostLease.Holder,
                guest_move_state = (int)actor.MoveState,
                guest_passenger = host.Session.IsPassengerInVehicle(actor.PlayerId, host.Car.Id),
                guest_inside_trigger = inside.bounds.Contains(actor.Position + Vector3.up * .4f),
                guest_local_inside = guestInside.Value,
                host_vehicle_id = host.Car.Id, guest_vehicle_id = guest.Car.Id,
                host_bound_vehicle_id = Get<SyncedItem>(host.Bridge, "_vehicle").Id,
                guest_bound_vehicle_id = Get<SyncedItem>(guest.Bridge, "_vehicle").Id,
                host_vehicle_path = host.Car.Path, guest_vehicle_path = guest.Car.Path,
                host_vehicle_body = host.Car.Body.name, guest_vehicle_body = guest.Car.Body!.name,
                host_is_vehicle = host.Car.IsVehicle, guest_is_vehicle = guest.Car.IsVehicle,
                host_local_owner = host.Car.LocallyOwned, guest_local_owner = guest.Car.LocallyOwned,
                host_remote_owner = (int)host.Car.RemoteOwner, guest_remote_owner = (int)guest.Car.RemoteOwner,
                host_linear_squared = host.Car.Body.velocity.sqrMagnitude,
                guest_linear_squared = guest.Car.Body.velocity.sqrMagnitude,
                host_angular_squared = host.Car.Body.angularVelocity.sqrMagnitude,
                guest_angular_squared = guest.Car.Body.angularVelocity.sqrMagnitude,
                host_tool_id = host.Tool.Id, guest_tool_id = guest.Tool.Id,
                host_tool_name = host.Tool.Body!.name, guest_tool_name = guest.Tool.Body!.name
            });
        }

        private void TraceFixtureDecision(string stage, Rig host, Rig guest, PaneScrapeUpdate result)
        {
            var decisions = host.Session.Sent.OfType<PaneScrapeUpdate>().Where(x => x.IsDecision).ToArray();
            FixtureRecord("decision", stage, new {
                stage, status = result.Status.ToString(), actor = (int)result.Actor, is_decision = result.IsDecision,
                sequence = result.Sequence, high_water = result.HighWater,
                lease_seen = Get<ScraperLease>(host.Bridge, "_lease").Seen(result.Actor),
                guest_sequence = Get<uint>(guest.Bridge, "_sequence"),
                revision = result.Revision, cutoff = result.Cutoff, epoch = result.Epoch,
                vehicle_id = result.VehicleId, tool_id = result.ToolId,
                holder = (int)result.Holder, equipped = result.Equipped,
                host_decisions = decisions.Length,
                accepted_for_sequence = decisions.Count(x => x.Actor == result.Actor && x.Sequence == result.Sequence && x.Status == PaneScrapeStatus.Accepted),
                host_glass = host.Glass.Calls, guest_glass = guest.Glass.Calls,
                host_material_writes = host.Material.Writes,
                host_effects = host.Effect.Calls, guest_effects = guest.Effect.Calls,
                host_heat = host.Heat.Value, guest_heat = guest.Heat.Value,
                host_cutoff = host.Cutoff.Value, guest_cutoff = guest.Cutoff.Value,
                host_revision = Get<PaneScrapeReplica>(host.Bridge, "_replica").Current!.Revision,
                guest_revision = Get<PaneScrapeReplica>(guest.Bridge, "_replica").Current!.Revision,
                host_material = host.Material.Cutoff, guest_material = guest.Material.Cutoff
            });
        }
    }
}
