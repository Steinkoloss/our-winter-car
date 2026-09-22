using System;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TrainDouble
    {
        internal uint NetId => ReadId();
        internal Func<uint> ReadId = () => 123;
    }
    public sealed partial class WorldSyncManager
    {
        internal TrainState? TrainSnapshot(uint? id = null) => BuildTrainSnapshot(id);
    }
}
