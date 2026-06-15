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
                if (!_items.TryGetValue(id, out var item) || item.Body == null || item.IsVehicle) continue;
                if (item.LocallyOwned || item.RemoteOwner != WorldSyncIds.NoOwner) continue;

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

                if (!_vehicles.TryReadVehicleChecksum(item, out byte flags,
                        out ushort rpm, out byte fuel, out byte coolant, out byte frost, out byte fog, out byte cabinTemp))
                {
                    continue;
                }

                crc = StableHash.Combine(crc, id);
                crc = StableHash.Combine(crc, flags);
                crc = StableHash.Combine(crc, rpm);
                crc = StableHash.Combine(crc, fuel);
                crc = StableHash.Combine(crc, coolant);
                crc = StableHash.Combine(crc, frost);
                crc = StableHash.Combine(crc, fog);
                crc = StableHash.Combine(crc, cabinTemp);
            }

            return crc;
        }
    }
}
