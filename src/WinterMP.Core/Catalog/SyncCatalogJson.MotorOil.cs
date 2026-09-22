using System;
using System.Collections.Generic;
namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static MotorOilData ParseMotorOil(object? value)
        {
            if(value is not Dictionary<string,object?> fields)throw new FormatException("Invalid motor-oil catalog.");
            var data=new MotorOilData();
            foreach(string key in new[]{"factoryPath","factoryFsm","prefab","prefix","itemName","emptyName","use","data","trigger","particle","id","fluid","grade","viscosity","consumed","copy","empty","save","material","cap","capFsm","fill","fillFsm","gaugePath","gaugeFsm"})
                data.Names.Add(key,EngineInputPath(fields,key));
            if(data["factoryPath"]+"::"+data["factoryFsm"]!=WinterMP.Net.Sync.MotorOilPolicy.Factory || data["prefix"]!=WinterMP.Net.Sync.MotorOilPolicy.NativePrefix)
                throw new FormatException("Motor-oil saved identity differs from protocol.");
            return data;
        }
    }
    internal sealed class MotorOilData
    {
        internal readonly Dictionary<string,string> Names=new Dictionary<string,string>();
        internal string this[string key] => Names[key];
    }
}
