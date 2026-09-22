using System;
using System.Collections.Generic;
namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static AdvertPhoneData ParseAdvertPhone(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid advert phone catalog.");
            var data = new AdvertPhoneData();
            foreach (string key in new[] { "number", "listings", "job", "audio", "subtitle", "phone0", "phone1", "phone2", "keypad0", "keypad1", "keypad2", "handle0", "handle1", "handle2", "ring0", "ring1", "ring2", "bill0", "bill1" })
                data.Names.Add(key, key == "subtitle" ? RequiredString(fields, key) : EngineInputPath(fields, key));
            if (data["subtitle"].Length > 512) throw new FormatException("Advert subtitle too long.");
            if (data["number"] != "08231206") throw new FormatException("Advert number differs from protocol.");
            return data;
        }
        private static AdvertsData ParseAdverts(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid advert catalog.");
            var data = new AdvertsData();
            foreach (string key in new[] { "root", "data", "reset", "pile", "use", "scale", "spawn", "open", "ready", "empty", "mailboxFsm", "mailboxRoot", "lod", "mailboxOpen", "mailboxClose", "mailboxIdle", "mailboxReset", "mailboxRetry", "db", "index", "placed", "sheets", "delivered", "stage", "day", "salary", "sheetName" })
                data.Names.Add(key, EngineInputPath(fields, key));
            if (!fields.TryGetValue("boxes", out var rawBoxes) || rawBoxes is not List<object?> boxes || boxes.Count != 27) throw new FormatException("Expected 27 native advert mailboxes.");
            foreach (var row in boxes)
            {
                if (row is not Dictionary<string, object?> box) throw new FormatException("Invalid advert mailbox.");
                if (!box.TryGetValue("index", out var rawIndex)) throw new FormatException("Missing advert mailbox index.");
                int index = SlotNumber(rawIndex, "index", 0, 27);
                if (index == 22 || data.Boxes.ContainsKey(index)) throw new FormatException("Duplicate or unavailable advert mailbox index.");
                data.Boxes.Add(index, EngineInputPath(box, "path"));
            }
            return data;
        }
    }
    internal sealed class AdvertPhoneData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }
    internal sealed class AdvertsData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly Dictionary<int, string> Boxes = new Dictionary<int, string>();
        internal string this[string key] => Names[key];
    }
}
