namespace WinterMP.Net.Messages
{
    /// <summary>Host Corris engine inputs; does not transfer or alter guest assemblies.</summary>
    public sealed class EngineBlockState : IMessage
    {
        public const byte Available = 1, Installed = 2, Damaged = 4, HeadInstalled = 8, CarburettorInstalled = 16, AirCleanerInstalled = 32;
        public const int ExhaustPartCount = 4, ExhaustValueCount = 12;
        public const int ValveCount = 8, CoolantHoseCount = 4;
        public bool CoolingAmbientAvailable;
        public float CoolingAmbientTemperature;
        public byte CoolingAirflowFlags;
        public float GrilleAirflow, HoodAirflow, FiberglassHoodAirflow;
        public byte CoolantHoseFlags;
        public float[] CoolantHoseTightness = new float[CoolantHoseCount];
        public float CarburettorTightness;
        public bool ValvesAvailable;
        public float[] ValveSettings = new float[ValveCount];
        public byte ExhaustFlags;
        public bool OilpanInstalled, RockerCoverInstalled, RadiatorInstalled;
        public float RadiatorWear, RadiatorCoolant, RadiatorPressureCap, RadiatorFlectEfficiency;
        public float RockerCoverTightness;
        public float OilpanWear, OilpanTightness, Oil, OilContamination, OilViscosity;
        public float[] ExhaustPerformance = new float[ExhaustValueCount];
        public uint Revision;
        public byte Flags;
        public float Wear, FuelChamber, CarbReserve, SettingMixture;
        public float CarburettorPower, CarburettorTorque, CarburettorPowerAdd, AirCleanerPower, AirCleanerTorque, AirCleanerPowerAdd;
        public MessageId Id => MessageId.EngineBlockState;
        public bool Valid => (Flags == 0 || Flags == 1 || Flags == 3 || Flags == 7 || Flags == 11 || Flags == 15
            || Flags == 27 || Flags == 31 || Flags == 43 || Flags == 47 || Flags == 59 || Flags == 63)
            && Finite(Wear) && ((Flags & Installed) != 0 || Wear == 0)
            && Finite(FuelChamber) && Finite(CarbReserve) && Finite(SettingMixture)
            && Finite(CarburettorPower) && Finite(CarburettorTorque) && Finite(CarburettorPowerAdd)
            && Finite(AirCleanerPower) && Finite(AirCleanerTorque) && Finite(AirCleanerPowerAdd)
            && ((Flags & CarburettorInstalled) != 0 || FuelChamber == 0 && CarbReserve == 0 && SettingMixture == 0
                && CarburettorPower == 0 && CarburettorTorque == 0 && CarburettorPowerAdd == 0)
            && ((Flags & AirCleanerInstalled) != 0 || AirCleanerPower == 0 && AirCleanerTorque == 0 && AirCleanerPowerAdd == 0) && ValidExhaust && ValidValves
            && (!OilpanInstalled || (Flags & Installed) != 0)
            && Finite(OilpanWear) && Finite(OilpanTightness) && Finite(Oil) && Finite(OilContamination) && Finite(OilViscosity)
            && (OilpanInstalled || OilpanWear == 0 && OilpanTightness == 0 && Oil == 0 && OilContamination == 0 && OilViscosity == 0)
            && (!RockerCoverInstalled || (Flags & HeadInstalled) != 0)
            && Finite(RockerCoverTightness) && (RockerCoverInstalled || RockerCoverTightness == 0)
            && Finite(RadiatorWear) && Finite(RadiatorCoolant) && Finite(RadiatorPressureCap) && Finite(RadiatorFlectEfficiency)
            && (RadiatorInstalled || RadiatorWear == 0 && RadiatorCoolant == 0 && RadiatorPressureCap == 0 && RadiatorFlectEfficiency == 0)
            && Finite(CoolingAmbientTemperature) && (CoolingAmbientAvailable || CoolingAmbientTemperature == 0)
            && CoolingAirflowFlags <= 15 && Finite(GrilleAirflow) && Finite(HoodAirflow) && Finite(FiberglassHoodAirflow)
            && ((CoolingAirflowFlags & 1) != 0 || GrilleAirflow == 0) && ((CoolingAirflowFlags & 4) != 0 || HoodAirflow == 0)
            && ((CoolingAirflowFlags & 8) != 0 || FiberglassHoodAirflow == 0)
            && ValidCoolantHoses && Finite(CarburettorTightness) && ((Flags & CarburettorInstalled) != 0 || CarburettorTightness == 0);
        private bool ValidCoolantHoses
        {
            get
            {
                if (CoolantHoseFlags > 15 || CoolantHoseTightness == null || CoolantHoseTightness.Length != CoolantHoseCount) return false;
                for (int i = 0; i < CoolantHoseCount; i++)
                    if (!Finite(CoolantHoseTightness[i]) || (CoolantHoseFlags & (1 << i)) == 0 && CoolantHoseTightness[i] != 0) return false;
                return true;
            }
        }
        private bool ValidValves
        {
            get
            {
                if (ValveSettings == null || ValveSettings.Length != ValveCount || ValvesAvailable && (Flags & HeadInstalled) == 0) return false;
                foreach (float value in ValveSettings) if (!Finite(value) || !ValvesAvailable && value != 0) return false;
                return true;
            }
        }
        private bool ValidExhaust
        {
            get
            {
                if (ExhaustFlags > 15 || ExhaustPerformance == null || ExhaustPerformance.Length != ExhaustValueCount
                    || (ExhaustFlags & 1) != 0 && (Flags & HeadInstalled) == 0) return false;
                for (int i = 0; i < ExhaustValueCount; i++)
                    if (!Finite(ExhaustPerformance[i]) || (ExhaustFlags & (1 << (i / 3))) == 0 && ExhaustPerformance[i] != 0) return false;
                return true;
            }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid engine block state.");
            w.WriteUInt32(Revision); w.WriteByte(Flags); w.WriteSingle(Wear);
            w.WriteSingle(FuelChamber); w.WriteSingle(CarbReserve); w.WriteSingle(SettingMixture);
            w.WriteSingle(CarburettorPower); w.WriteSingle(CarburettorTorque); w.WriteSingle(CarburettorPowerAdd);
            w.WriteSingle(AirCleanerPower); w.WriteSingle(AirCleanerTorque); w.WriteSingle(AirCleanerPowerAdd);
            w.WriteByte(ExhaustFlags); foreach (float value in ExhaustPerformance) w.WriteSingle(value);
            w.WriteBool(ValvesAvailable); foreach (float value in ValveSettings) w.WriteSingle(value);
            w.WriteBool(OilpanInstalled); w.WriteSingle(OilpanWear); w.WriteSingle(OilpanTightness); w.WriteSingle(Oil); w.WriteSingle(OilContamination); w.WriteSingle(OilViscosity);
            w.WriteBool(RockerCoverInstalled); w.WriteSingle(RockerCoverTightness);
            w.WriteBool(RadiatorInstalled); w.WriteSingle(RadiatorWear); w.WriteSingle(RadiatorCoolant); w.WriteSingle(RadiatorPressureCap); w.WriteSingle(RadiatorFlectEfficiency);
            w.WriteByte(CoolantHoseFlags); foreach (float value in CoolantHoseTightness) w.WriteSingle(value); w.WriteSingle(CarburettorTightness);
            w.WriteByte(CoolingAirflowFlags); w.WriteSingle(GrilleAirflow); w.WriteSingle(HoodAirflow); w.WriteSingle(FiberglassHoodAirflow);
            w.WriteBool(CoolingAmbientAvailable); w.WriteSingle(CoolingAmbientTemperature);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); Flags = r.ReadByte(); Wear = r.ReadSingle();
            FuelChamber = r.ReadSingle(); CarbReserve = r.ReadSingle(); SettingMixture = r.ReadSingle();
            CarburettorPower = r.ReadSingle(); CarburettorTorque = r.ReadSingle(); CarburettorPowerAdd = r.ReadSingle();
            AirCleanerPower = r.ReadSingle(); AirCleanerTorque = r.ReadSingle(); AirCleanerPowerAdd = r.ReadSingle();
            ExhaustFlags = r.ReadByte(); ExhaustPerformance = new float[ExhaustValueCount];
            for (int i = 0; i < ExhaustValueCount; i++) ExhaustPerformance[i] = r.ReadSingle();
            byte valves = r.ReadByte(); if (valves > 1) throw new ProtocolException("Invalid valve availability.");
            ValvesAvailable = valves != 0; ValveSettings = new float[ValveCount];
            for (int i = 0; i < ValveCount; i++) ValveSettings[i] = r.ReadSingle();
            byte oilpan = r.ReadByte(); if (oilpan > 1) throw new ProtocolException("Invalid oilpan installation.");
            OilpanInstalled = oilpan != 0; OilpanWear = r.ReadSingle(); OilpanTightness = r.ReadSingle(); Oil = r.ReadSingle(); OilContamination = r.ReadSingle(); OilViscosity = r.ReadSingle();
            byte cover = r.ReadByte(); if (cover > 1) throw new ProtocolException("Invalid rocker-cover installation.");
            RockerCoverInstalled = cover != 0; RockerCoverTightness = r.ReadSingle();
            byte radiator = r.ReadByte(); if (radiator > 1) throw new ProtocolException("Invalid radiator installation.");
            RadiatorInstalled = radiator != 0; RadiatorWear = r.ReadSingle(); RadiatorCoolant = r.ReadSingle(); RadiatorPressureCap = r.ReadSingle(); RadiatorFlectEfficiency = r.ReadSingle();
            CoolantHoseFlags = r.ReadByte(); CoolantHoseTightness = new float[CoolantHoseCount];
            for (int i = 0; i < CoolantHoseCount; i++) CoolantHoseTightness[i] = r.ReadSingle();
            CarburettorTightness = r.ReadSingle();
            CoolingAirflowFlags = r.ReadByte(); GrilleAirflow = r.ReadSingle(); HoodAirflow = r.ReadSingle(); FiberglassHoodAirflow = r.ReadSingle();
            byte ambient = r.ReadByte(); if (ambient > 1) throw new ProtocolException("Invalid cooling ambient availability.");
            CoolingAmbientAvailable = ambient != 0; CoolingAmbientTemperature = r.ReadSingle();
            if (!Valid) throw new ProtocolException("Invalid engine block state.");
        }
        internal bool SameInputs(EngineBlockState other) => Flags == other.Flags && Wear == other.Wear
            && FuelChamber == other.FuelChamber && CarbReserve == other.CarbReserve && SettingMixture == other.SettingMixture
            && CarburettorPower == other.CarburettorPower && CarburettorTorque == other.CarburettorTorque && CarburettorPowerAdd == other.CarburettorPowerAdd
            && AirCleanerPower == other.AirCleanerPower && AirCleanerTorque == other.AirCleanerTorque && AirCleanerPowerAdd == other.AirCleanerPowerAdd && SameAttachedInputs(other);
        private bool SameAttachedInputs(EngineBlockState other)
        {
            if (CoolingAmbientAvailable != other.CoolingAmbientAvailable || CoolingAmbientTemperature != other.CoolingAmbientTemperature) return false;
            if (CoolingAirflowFlags != other.CoolingAirflowFlags || GrilleAirflow != other.GrilleAirflow || HoodAirflow != other.HoodAirflow || FiberglassHoodAirflow != other.FiberglassHoodAirflow) return false;
            if (CoolantHoseFlags != other.CoolantHoseFlags || CarburettorTightness != other.CarburettorTightness) return false;
            for (int i = 0; i < CoolantHoseCount; i++) if (CoolantHoseTightness[i] != other.CoolantHoseTightness[i]) return false;
            if (RadiatorInstalled != other.RadiatorInstalled || RadiatorWear != other.RadiatorWear || RadiatorCoolant != other.RadiatorCoolant
                || RadiatorPressureCap != other.RadiatorPressureCap || RadiatorFlectEfficiency != other.RadiatorFlectEfficiency
                || ExhaustFlags != other.ExhaustFlags || ValvesAvailable != other.ValvesAvailable || OilpanInstalled != other.OilpanInstalled
                || RockerCoverInstalled != other.RockerCoverInstalled || RockerCoverTightness != other.RockerCoverTightness
                || OilpanWear != other.OilpanWear || OilpanTightness != other.OilpanTightness || Oil != other.Oil || OilContamination != other.OilContamination || OilViscosity != other.OilViscosity) return false;
            for (int i = 0; i < ValveCount; i++) if (ValveSettings[i] != other.ValveSettings[i]) return false;
            for (int i = 0; i < ExhaustValueCount; i++) if (ExhaustPerformance[i] != other.ExhaustPerformance[i]) return false;
            return true;
        }
        public EngineBlockState Copy()
        {
            // Avoid allocating the constructor's default arrays only to replace
            // them. Each mutable array still belongs exclusively to this copy.
            var copy = (EngineBlockState)MemberwiseClone();
            copy.CoolantHoseTightness = CoolantHoseTightness == null ? new float[0] : (float[])CoolantHoseTightness.Clone();
            copy.ValveSettings = ValveSettings == null ? new float[0] : (float[])ValveSettings.Clone();
            copy.ExhaustPerformance = ExhaustPerformance == null ? new float[0] : (float[])ExhaustPerformance.Clone();
            return copy;
        }
    }
}
