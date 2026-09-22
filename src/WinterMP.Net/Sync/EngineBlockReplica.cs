using System;
using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class EngineBlockReplica
    {
        private EngineBlockSnapshot? _state;
        public bool Receive(EngineBlockState state)
        {
            if (state == null || !state.Valid) return false;
            if (_state != null)
            {
                if (_state.Revision == state.Revision) return _state.SameInputs(state);
                if (unchecked((int)(state.Revision - _state.Revision)) <= 0) return false;
            }
            _state = new EngineBlockSnapshot(state); return true;
        }
        public EngineBlockSnapshot? Inputs => _state;
        public EngineBlockState? Get() => _state?.Copy();
        public void Clear() => _state = null;
    }

    /// <summary>One accepted revision. Readers cannot mutate its owned state or arrays,
    /// and a later Receive/Clear leaves previously acquired snapshots unchanged.</summary>
    public sealed class EngineBlockSnapshot
    {
        private readonly EngineBlockState _state;
        internal EngineBlockSnapshot(EngineBlockState state) { _state = state.Copy(); }
        internal bool SameInputs(EngineBlockState state) => _state.SameInputs(state);
        internal EngineBlockState Copy() => _state.Copy();

        public uint Revision => _state.Revision;
        public byte Flags => _state.Flags;
        public float Wear => _state.Wear;
        public float FuelChamber => _state.FuelChamber;
        public float CarbReserve => _state.CarbReserve;
        public float SettingMixture => _state.SettingMixture;
        public float CarburettorPower => _state.CarburettorPower;
        public float CarburettorTorque => _state.CarburettorTorque;
        public float CarburettorPowerAdd => _state.CarburettorPowerAdd;
        public float CarburettorTightness => _state.CarburettorTightness;
        public float AirCleanerPower => _state.AirCleanerPower;
        public float AirCleanerTorque => _state.AirCleanerTorque;
        public float AirCleanerPowerAdd => _state.AirCleanerPowerAdd;
        public byte ExhaustFlags => _state.ExhaustFlags;
        public float ExhaustPerformanceAt(int index) => _state.ExhaustPerformance[index];
        public bool ValvesAvailable => _state.ValvesAvailable;
        public float ValveSettingAt(int index) => _state.ValveSettings[index];
        public bool OilpanInstalled => _state.OilpanInstalled;
        public float OilpanWear => _state.OilpanWear;
        public float OilpanTightness => _state.OilpanTightness;
        public float Oil => _state.Oil;
        public float OilContamination => _state.OilContamination;
        public float OilViscosity => _state.OilViscosity;
        public bool RockerCoverInstalled => _state.RockerCoverInstalled;
        public float RockerCoverTightness => _state.RockerCoverTightness;
        public bool RadiatorInstalled => _state.RadiatorInstalled;
        public float RadiatorWear => _state.RadiatorWear;
        public float RadiatorCoolant => _state.RadiatorCoolant;
        public float RadiatorPressureCap => _state.RadiatorPressureCap;
        public float RadiatorFlectEfficiency => _state.RadiatorFlectEfficiency;
        public byte CoolantHoseFlags => _state.CoolantHoseFlags;
        public float CoolantHoseTightnessAt(int index) => _state.CoolantHoseTightness[index];
        public byte CoolingAirflowFlags => _state.CoolingAirflowFlags;
        public float GrilleAirflow => _state.GrilleAirflow;
        public float HoodAirflow => _state.HoodAirflow;
        public float FiberglassHoodAirflow => _state.FiberglassHoodAirflow;
        public bool CoolingAmbientAvailable => _state.CoolingAmbientAvailable;
        public float CoolingAmbientTemperature => _state.CoolingAmbientTemperature;
    }

    public sealed class EngineBlockPublication
    {
        private EngineBlockState? _state;
        private uint _sent;
        private bool _hasSent;
        public bool NeedsBroadcast => _state != null && (!_hasSent || _sent != _state.Revision);
        public EngineBlockState Observe(byte flags, float wear, float fuelChamber = 0, float carbReserve = 0, float settingMixture = 0)
            => Observe(new EngineBlockState { Flags = flags, Wear = wear, FuelChamber = fuelChamber, CarbReserve = carbReserve, SettingMixture = settingMixture });
        public EngineBlockState Observe(EngineBlockState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (!state.Valid) throw new ArgumentException("Invalid engine block publication.");
            var next = state.Copy();
            next.Revision = _state == null ? 1 : _state.SameInputs(next) ? _state.Revision : unchecked(_state.Revision + 1);
            _state = next; return next.Copy();
        }
        public void MarkBroadcast(uint revision) { if (_state == null || _state.Revision != revision) return; _sent = revision; _hasSent = true; }
    }
}
