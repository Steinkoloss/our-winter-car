using System;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using WinterMP.Tools;
using Xunit;

public class CatalogTests
{
    private static string Dump(string method)
    {
        var writer = new JsonWriter();
        writer.BeginObject();
        FsmDumperPlugin.Log = new BepInEx.Logging.ManualLogSource();
        typeof(FsmDumperPlugin).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { writer });
        writer.EndObject();
        return writer.ToString();
    }
    private static JsonElement Field(JsonElement record, string name)
    {
        foreach (var field in record.GetProperty("fields").EnumerateArray())
            if (field.GetProperty("name").GetString() == name) return field.GetProperty("data");
        throw new Exception("Missing field " + name);
    }
    private static string Status(JsonElement node) => node.GetProperty("status").GetString()!;
    private static PlayMakerFSM Setup()
    {
        var go = new GameObject("Hand");
        var target = new GameObject("Held tool fixture");
        var targetFsm = new PlayMakerFSM { gameObject = target, FsmName = "Use" };
        target.Components.Add(targetFsm);
        var action = new ActivateGameObject();
        action.gameObject.GameObject.Value = target;
        action.targetProperty.TargetObject.Value = targetFsm;
        action.targets = new[] { target, target };
        var fsm = new PlayMakerFSM { gameObject = go };
        go.Components.Add(fsm);
        fsm.Fsm.States = new object[] {
            new { Name = "Check item", Transitions = new[] { new { EventName = "SAUNADIPPER", ToState = "Sauna dipper" } }, Actions = new object[] { new StringCompare() } },
            new { Name = "Sauna dipper", Transitions = new[] { new { EventName = "FINISHED", ToState = "Hand" } }, Actions = new object?[] { action, null, new StringCompare { string2 = new ThrowingString() } } }
        };
        fsm.Fsm.GlobalTransitions = new[] { new { EventName = "EQUIP", ToState = "Check item" } };
        Resources.Objects = new UnityEngine.Object[] { go, target, fsm, targetFsm };
        return fsm;
    }

    [Fact]
    public void Actual_dumper_preserves_legacy_structure_and_ordered_actions()
    {
        Setup();
        string text = Dump("DumpFsms");
        using var doc = JsonDocument.Parse(text);
        var fsm = doc.RootElement.GetProperty("fsms")[0];
        Assert.Equal("Hand", fsm.GetProperty("path").GetString());
        Assert.Equal("PickUp", fsm.GetProperty("fsmName").GetString());
        Assert.Equal("EQUIP", fsm.GetProperty("globalTransitions")[0].GetProperty("event").GetString());
        Assert.Equal("Item", fsm.GetProperty("variables").GetProperty("GameObjectVariables")[0].GetString());
        var state = fsm.GetProperty("states")[1];
        Assert.Equal("Hand", state.GetProperty("transitions")[0].GetProperty("to").GetString());
        var records = state.GetProperty("actionRecords");
        Assert.Equal("ok", Status(state.GetProperty("actionRecordsStatus")));
        Assert.Equal(3, records.GetArrayLength());
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(i, records[i].GetProperty("index").GetInt32());
            Assert.Equal(state.GetProperty("actionTypes")[i].ToString(), records[i].GetProperty("type").ToString());
        }
        Assert.Equal("unknown", Status(records[1]));
        Assert.Equal("HutongGames.PlayMaker.Actions.StringCompare", records[2].GetProperty("type").GetString());
        var compare = fsm.GetProperty("states")[0].GetProperty("actionRecords")[0];
        Assert.Equal("dipper(itemx)", Field(compare, "string2").GetProperty("value").GetProperty("value").GetString());
        Assert.Equal("withheld", Status(Field(compare, "string1").GetProperty("value")));
        Assert.Equal("SAUNADIPPER", Field(compare, "equalEvent").GetProperty("members").GetProperty("Name").GetProperty("value").GetString());
        Assert.Equal(text, Dump("DumpFsms"));
        string? output = Environment.GetEnvironmentVariable("FSM_CATALOG_FIXTURE_OUT");
        if (!string.IsNullOrEmpty(output))
        {
            using var stream = new FileStream(output, FileMode.CreateNew);
            using var writer = new StreamWriter(stream);
            writer.Write(text);
        }
    }

    [Fact]
    public void Operands_retain_reference_metadata_without_shared_values_or_save_payloads()
    {
        Setup();
        string text = Dump("DumpFsms");
        using var doc = JsonDocument.Parse(text);
        var action = doc.RootElement.GetProperty("fsms")[0].GetProperty("states")[1].GetProperty("actionRecords")[0];
        var target = Field(action, "gameObject").GetProperty("members").GetProperty("GameObject").GetProperty("value");
        Assert.Equal("Held tool fixture", target.GetProperty("path").GetString());
        Assert.Equal("runtime-reference-not-default", target.GetProperty("provenance").GetString());
        var component = Field(action, "targetProperty").GetProperty("members").GetProperty("TargetObject").GetProperty("value");
        Assert.Equal("PlayMakerFSM", component.GetProperty("componentType").GetString());
        Assert.Equal(0, component.GetProperty("componentIndex").GetInt32());
        Assert.Equal("Use", component.GetProperty("fsmName").GetString());
        Assert.Equal("withheld", Status(Field(action, "water").GetProperty("value")));
        Assert.Equal("withheld", Status(Field(action, "saveContents")));
        Assert.Equal("unknown", Status(Field(action, "opaque")));
        Assert.Equal("withheld", Status(Field(action, "cachedResource")));
        var last = doc.RootElement.GetProperty("fsms")[0].GetProperty("states")[1].GetProperty("actionRecords")[2];
        Assert.Equal("unreadable", Status(Field(last, "string2").GetProperty("value")));
        Assert.Equal("ok", Status(Field(action, "resetOnExit")));
        Assert.Equal(2, Field(action, "targets").GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("PRIVATE_", text);
        Assert.DoesNotContain("LIVE_VALUE", text);
        Assert.DoesNotContain("987654", text);
        Assert.DoesNotContain("RuntimeCache", text);
        Assert.DoesNotContain("instanceId", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Throwing_action_array_is_not_misreported_as_no_actions()
    {
        var fsm = Setup();
        fsm.Fsm.States = new object[] { new BrokenState() };
        using var doc = JsonDocument.Parse(Dump("DumpFsms"));
        var state = doc.RootElement.GetProperty("fsms")[0].GetProperty("states")[0];
        Assert.Equal("unreadable", Status(state.GetProperty("actionRecordsStatus")));
    }
    public class BrokenState
    {
        public string Name = "broken";
        public object Actions => throw new Exception("PRIVATE_EXCEPTION_MESSAGE");
    }

    [Fact]
    public void Duplicate_paths_do_not_become_stable_instance_ids()
    {
        Setup();
        var objects = new System.Collections.Generic.List<UnityEngine.Object>(Resources.Objects);
        objects.Add(new GameObject("Held tool fixture"));
        Resources.Objects = objects.ToArray();
        using var doc = JsonDocument.Parse(Dump("DumpFsms"));
        var action = doc.RootElement.GetProperty("fsms")[0].GetProperty("states")[1].GetProperty("actionRecords")[0];
        var target = Field(action, "gameObject").GetProperty("members").GetProperty("GameObject").GetProperty("value");
        Assert.Equal("unknown", Status(target));
        Assert.Equal("ambiguous-path", target.GetProperty("resolution").GetString());
        Assert.Equal(2, target.GetProperty("pathCandidates").GetInt32());
    }

    [Fact]
    public void Limits_nonfinite_and_opaque_cycles_are_explicit_and_do_not_drop_following_fields()
    {
        var fsm = Setup();
        var action = new HutongGames.PlayMaker.Actions.SendEvent();
        action.recursive[0] = action.recursive;
        fsm.Fsm.States = new object[] { new { Name = "Limits", Actions = new[] { action } } };
        using var doc = JsonDocument.Parse(Dump("DumpFsms"));
        var record = doc.RootElement.GetProperty("fsms")[0].GetProperty("states")[0].GetProperty("actionRecords")[0];
        Assert.False(record.GetProperty("enabled").GetProperty("value").GetBoolean());
        Assert.Equal("nonfinite-literal", Field(record, "delay").GetProperty("reason").GetString());
        Assert.Equal("array-limit", Field(record, "many").GetProperty("reason").GetString());
        Assert.Equal(300, Field(record, "many").GetProperty("length").GetInt32());
        Assert.Equal(256, Field(record, "many").GetProperty("items").GetArrayLength());
        Assert.Equal("string-limit", Field(record, "sendEvent").GetProperty("reason").GetString());
        Assert.Contains("depth-limit", Field(record, "recursive").GetRawText());
        Assert.Equal("unknown", Status(Field(record, "opaque")));
        Assert.True(Field(record, "everyFrame").GetProperty("value").GetBoolean());
    }

    [Fact]
    public void Metadata_is_deterministic_across_cultures_and_field_order_is_ordinal()
    {
        var fsm = Setup();
        fsm.Fsm.States = new object[] { new { Name = "Culture", Actions = new[] { new HutongGames.PlayMaker.Actions.SendEvent { delay = 1.25, sendEvent = "GO" } } } };
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string first = Dump("DumpFsms");
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal(first, Dump("DumpFsms"));
            using var doc = JsonDocument.Parse(first);
            var action = doc.RootElement.GetProperty("fsms")[0].GetProperty("states")[0].GetProperty("actionRecords")[0];
            Assert.Equal("1.25", Field(action, "delay").GetProperty("value").GetString());
            string previous = "";
            foreach (var field in action.GetProperty("fields").EnumerateArray())
            {
                string current = field.GetProperty("name").GetString()!;
                Assert.True(string.CompareOrdinal(previous, current) < 0);
                previous = current;
            }
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Empty_null_and_nonarray_action_sources_are_distinct()
    {
        var fsm = Setup();
        fsm.Fsm.States = new object[] { new { Name = "Empty", Actions = Array.Empty<object>() }, new { Name = "Null", Actions = (object?)null }, new { Name = "Opaque", Actions = new HostileObject() } };
        using var doc = JsonDocument.Parse(Dump("DumpFsms"));
        var states = doc.RootElement.GetProperty("fsms")[0].GetProperty("states");
        Assert.Equal("ok", Status(states[0].GetProperty("actionRecordsStatus")));
        Assert.Equal(0, states[0].GetProperty("actionRecords").GetArrayLength());
        Assert.Equal("unknown", Status(states[1].GetProperty("actionRecordsStatus")));
        Assert.Equal("unknown", Status(states[2].GetProperty("actionRecordsStatus")));
    }

    [Fact]
    public void Globals_keep_names_but_never_read_live_resource_values()
    {
        SecretScalar.Reads = 0;
        using var doc = JsonDocument.Parse(Dump("DumpGlobalVariables"));
        var value = doc.RootElement.GetProperty("globalVariables").GetProperty("FloatVariables")[0];
        Assert.Equal("Water", value.GetProperty("name").GetString());
        Assert.Equal(0, SecretScalar.Reads);
        Assert.Equal(JsonValueKind.Null, value.GetProperty("value").ValueKind);
        Assert.Equal("withheld", Status(value.GetProperty("valueStatus")));
    }
}
