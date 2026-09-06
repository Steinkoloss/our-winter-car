using System.Collections.Generic;
using WinterMP.Net;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        internal uint ComputeItemCrc()
        {
            uint crc = StableHash.OffsetBasis;
            var ids = new List<uint>(_items.Keys);
            ids.Sort();
            foreach (uint id in ids)
            {
                if (!_items.TryGetValue(id, out var item) || item.Body == null || item.IsVehicle || !CanSyncItemMotion(item)) continue;
                if (item.LocallyOwned || item.RemoteOwner != WorldSyncIds.NoOwner) continue;
                // Cargo mid-ride is streamed state sampled at different instants per peer;
                // like owned/remote items it never checksums identically, so skip it. (The
                // old weld left welded items IN the CRC — kinematic, zero velocity — while
                // the authority excluded them as moving: resync spam on every drive.)
                if (item.RemoteCargoVehicleId != 0 || item.LocalCargoVehicleId != 0) continue;

                var body = item.Body;
                if (!body.IsSleeping() && body.velocity.sqrMagnitude > 0.04f) continue;

                var pos = body.transform.position;
                var rot = body.transform.rotation;
                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, (uint)Quantize(pos.x));
                crc = StableHash.Combine(crc, (uint)Quantize(pos.y));
                crc = StableHash.Combine(crc, (uint)Quantize(pos.z));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.x * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.y * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.z * 1000f));
                crc = StableHash.Combine(crc, (uint)Quantize(rot.w * 1000f));
            }

            return crc;
        }

        internal uint ComputeVehicleCrc()
        {
            uint crc = StableHash.OffsetBasis;
            var ids = new List<uint>();
            foreach (var pair in _items)
            {
                if (pair.Value.IsVehicle) ids.Add(pair.Key);
            }

            ids.Sort();
            foreach (uint id in ids)
            {
                if (!_items.TryGetValue(id, out var item)) continue;

                // Skip vehicles that are actively driven/streamed (locally or remotely):
                // their rpm/fuel/coolant change every frame and are sampled at different
                // instants on each peer, so they never match and would trigger endless
                // false "checksum mismatch" resyncs. Mirrors the ComputeItemCrc guard.
                if (item.LocallyOwned || item.RemoteOwner != WorldSyncIds.NoOwner) continue;

                crc = _vehicles.FoldVehicleChecksum(crc, item);
            }

            return crc;
        }
    }
}
