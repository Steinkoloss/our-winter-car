// Action doubles emulate only the audited stain action boundary, not PlayMaker scheduling.
using HutongGames.PlayMaker;
using UnityEngine;
public class GetFsmGameObject : FsmStateAction { public FsmOwnerDefault gameObject = new FsmOwnerDefault(); }
public class GetScale : FsmStateAction { public FsmFloat xScale = null!; }
public class FloatOperator : FsmStateAction { public FsmFloat float1 = null!, float2 = new FsmFloat { Value=3500 }, storeResult = null!; public int operation = 3; public bool everyFrame = true; }
public class FloatAdd : FsmStateAction { public FsmFloat floatVariable = null!, add = null!; public bool everyFrame = true, perSecond = true; }
public class FloatClamp : FsmStateAction { public FsmFloat floatVariable = null!, maxValue = null!; public FsmFloat minValue = new FsmFloat(); public bool everyFrame = true; }
public class SetScale : FsmStateAction
{
    public FsmOwnerDefault gameObject = new FsmOwnerDefault();
    public FsmFloat x = null!, y = null!, z = new FsmFloat { Value=1 };
    public bool everyFrame = true, lateUpdate = true;
    public int Calls;
    public override void OnLateUpdate() { Calls++; gameObject.GameObject.Value!.transform.localScale = new Vector3(x.Value,y.Value,z.Value); }
}
public class GameObjectChanged : FsmStateAction { }
