using System;

namespace WinterMP.Core.Sync
{
    internal partial class UpdateDouble
    {
        internal Action OnFixed = () => { };
        internal void FixedUpdate() => OnFixed();
    }

    public sealed partial class WorldSyncManager
    {
        internal void BindTrainFixed(Action callback) => _train.OnFixed = callback;
        internal void TickFixed(float now) { UnityEngine.Time.unscaledTime = now; FixedUpdate(); }
    }
}
