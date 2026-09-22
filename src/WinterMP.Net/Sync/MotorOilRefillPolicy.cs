using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;
namespace WinterMP.Net.Sync
{
    public static class MotorOilRefillPolicy
    {
        public const float Capacity=3.7f, Rate=.1f;
        public static bool Valid(MotorOilFillerState s) => s!=null && s.Revision!=0 && s.Epoch!=0
            && (s.HeadId==0)==(s.PanId==0) && AtfPolicy.ValidCap(s.Rotation)
            && Pan(s.Oil,s.Contamination,s.Viscosity) && AdvertPolicy.Pose(s.CapPosition,s.CapRotation);
        public static bool Valid(MotorOilRefillIntent s) => s!=null && s.Epoch!=0 && s.PlayerId>0 && s.PlayerId<255
            && s.Action<=3 && (s.Action<=1 ? s.BottleId!=0 : s.BottleId==0);
        public static bool Pan(float oil,float dirt,float viscosity) => Finite(oil) && oil>=-1 && oil<=Capacity
            && Finite(dirt) && dirt>=-1 && dirt<=100 && Finite(viscosity) && viscosity>=0 && viscosity<=100;
        public static float Amount(float source,float oil,float dt) => Finite(source) && source>=0 && source<=4
            && Finite(oil) && oil>=-1 && oil<=Capacity && Finite(dt) && dt>0
            ? Math.Min(source,Math.Min(Capacity-oil,Rate*Math.Min(dt,.25f))) : 0;
        public static float Clean(float dirt,float amount) => Math.Max(.01f,dirt-3.2f*(amount/Rate));
        public static float Mix(float viscosity,float bottle,float amount)
        { float delta=.06f*(amount/Rate); return viscosity<bottle?Math.Min(bottle,viscosity+delta):Math.Max(bottle,viscosity-delta); }
        public static bool Same(MotorOilFillerState a,MotorOilFillerState b) => a.Epoch==b.Epoch && a.HeadId==b.HeadId && a.PanId==b.PanId
            && a.Rotation==b.Rotation && a.Oil==b.Oil && a.Contamination==b.Contamination && a.Viscosity==b.Viscosity;
        public static bool Accept(MotorOilFillerState? old,MotorOilFillerState s) => Valid(s)
            && (old==null || AdvertPolicy.Newer(s.Revision,old.Revision) || s.Revision==old.Revision && Same(old,s));
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }
    public sealed class MotorOilIntentLedger
    {
        private readonly Dictionary<byte,ushort> _seen=new Dictionary<byte,ushort>();
        public bool Accept(MotorOilRefillIntent intent,bool valid)
        {
            if(!MotorOilRefillPolicy.Valid(intent))return false;
            if(_seen.TryGetValue(intent.PlayerId,out ushort old))
            { ushort delta=unchecked((ushort)(intent.Sequence-old));if(delta==0 || delta>=0x8000)return false; }
            _seen[intent.PlayerId]=intent.Sequence;return valid;
        }
        public void Forget(byte actor) { _seen.Remove(actor); }
        public void Clear() { _seen.Clear(); }
    }
}
