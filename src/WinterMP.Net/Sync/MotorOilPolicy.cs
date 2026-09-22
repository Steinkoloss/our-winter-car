using WinterMP.Net.Messages;
namespace WinterMP.Net.Sync
{
    public static class MotorOilPolicy
    {
        public const string NativePrefix="motormoil1";
        public const string Factory="Spawner/CreateItemsSeparate/MotorOil::MotorOil";
        public static uint ItemId(string nativeId) => FactoryItemIdentity.ItemId(StableHash.Fnv1a32(Factory),nativeId);
        public static bool Valid(MotorOilBottleState s) => s!=null && s.Revision!=0 && s.NativeId!=null
            && s.NativeId.Length<=64 && FactoryItemIdentity.IsNativeId(s.NativeId,NativePrefix) && s.ItemId==ItemId(s.NativeId)
            && s.ItemId!=0 && s.Fluid>=0 && s.Fluid<=4 && s.Viscosity>=0 && s.Viscosity<=10 && s.Grade<=2
            && (!s.Empty || s.Fluid<.1f) && AdvertPolicy.Pose(s.Position,s.Rotation);
        public static bool Same(MotorOilBottleState a,MotorOilBottleState b) => a.ItemId==b.ItemId && a.NativeId==b.NativeId
            && a.Fluid==b.Fluid && a.Viscosity==b.Viscosity && a.Grade==b.Grade && a.Empty==b.Empty;
        public static bool Accept(MotorOilBottleState? old,MotorOilBottleState next) => Valid(next)
            && (old==null || old.ItemId==next.ItemId && old.NativeId==next.NativeId
                && (AdvertPolicy.Newer(next.Revision,old.Revision) || next.Revision==old.Revision && Same(old,next)));
    }
}
