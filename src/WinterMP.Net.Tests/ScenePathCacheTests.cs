using System.Collections.Generic;
using WinterMP.Net;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class ScenePathCacheTests
{
    [Theory]
    [InlineData("Root/Mount", "Mount", true)]
    [InlineData("Root/Mount", "Other", false)]
    [InlineData("Root/Mount", "mount", false)]
    [InlineData("Root/Mount[0]", "Mount", true)]
    [InlineData("Root/Mount[2147483647]", "Mount", true)]
    [InlineData("Root/Mount[0]", "Mount[0]", true)]
    [InlineData("Root/Mount[0][12]", "Mount[0]", true)]
    [InlineData("Root/Mount[0]", "Mount[1]", false)]
    [InlineData("Root/Mount[]", "Mount", false)]
    [InlineData("Root/Mount[-1]", "Mount", false)]
    [InlineData("Root/Mount[1]tail", "Mount", false)]
    [InlineData("Root/Literal/Leaf[2]", "Literal/Leaf", true)]
    [InlineData("Root/ä日本[2]", "ä日本", true)]
    [InlineData("Root/", "", true)]
    [InlineData("Root/[0]", "", true)]
    [InlineData("", "", true)]
    [InlineData("", "Mount", false)]
    [InlineData("Root/OtherMount", "Mount", true)]
    public void LeafNameFilterRejectsOnlyImpossiblePathSuffixes(string path, string name, bool possible)
        => Assert.Equal(possible, ScenePathLookup.MayMatchLeafName(path, name));

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void LeafNameFilterNeverRejectsRenderedRootsAndDuplicateLiteralNames(string cultureName)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
            foreach (string name in new[] { "", "Mount", "Mount[0]", "[", "]", "[]", "Mount[-1]", "one/two", "trailing/", "/", "ä日本" })
            {
                var root = new Node(name);
                Assert.True(ScenePathLookup.MayMatchLeafName(NewScan().Of(root), root.Name));
                for (int i = 0; i < 12; i++) root.Add(name);
                var scan = NewScan();
                foreach (var child in root.Children)
                    Assert.True(ScenePathLookup.MayMatchLeafName(scan.Of(child), child.Name));
                root.Children[0].Name = "Changed";
                Assert.True(ScenePathLookup.MayMatchLeafName(NewScan().Of(root.Children[0]), "Changed"));
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RelativeMountResolutionUsesTheSameDuplicateSiblingIdentityAsTheSender()
    {
        var root = new Node("Guest car"); var mounts = root.Add("Assemblies");
        var first = mounts.Add("Mount"); var second = mounts.Add("Mount");
        var socket = second.Add("Socket"); var scan = NewScan();
        Assert.Same(socket, scan.FindRelative(root, scan.RelativeTo(socket, root)!));
        Assert.Same(root, scan.FindRelative(root, ""));
        Assert.Same(first, scan.FindRelative(root, "Assemblies/Mount[0]"));
        Assert.Null(scan.FindRelative(root, "Assemblies/Mount"));
        Assert.Null(scan.FindRelative(root, "Assemblies/../Mount[1]"));
        Assert.Null(scan.FindRelative(root, "Assemblies/Mount[2]"));
        mounts.Add("Mount[0]");
        Assert.Null(NewScan().FindRelative(root, "Assemblies/Mount[0]"));
    }

    private sealed class Node(string name)
    {
        public string Name = name;
        public Node? Parent;
        public readonly List<Node> Children = new();

        public Node Add(string name)
        {
            var child = new Node(name) { Parent = this };
            Children.Add(child);
            return child;
        }
    }

    private static ScenePathCache<Node> NewScan() => new(
        n => n.Parent, n => n.Name, n => n.Children.Count, (n, i) => n.Children[i]);

    private static ScenePathLookup<Node> NewLookup() => new(
        n => n.Name, n => n.Children.Count, (n, i) => n.Children[i]);

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void LiveLookupAgreesWithIndexedResolverForLiteralAndDuplicateNames(string cultureName)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
            var root = new Node("Root");
            string[] names = { "Bolt", "Bolt", "Bolt[0]", "Bolt[1]", "Bolt[1]", "Bolt[1][0]",
                "Bolt[00]", "Bolt[-1]", "Bolt[+0]", "Bolt[2147483648]", "[]", "", "", "å日本", "one/two", "x[" };
            var paths = new List<string> { "", "missing", "Bolt[2]", "Bolt[01]", "[2]", "å日本[0]", "../Bolt", "Bolt//Child", "/Bolt", "Bolt/" };
            foreach (string name in names)
            {
                var node = root.Add(name);
                node.Add("Child"); node.Add("Child"); node.Add("Child[0]");
                paths.Add(name);
                for (int index = 0; index < 3; index++)
                {
                    paths.Add(name + "[" + index + "]");
                    paths.Add(name + "[" + index + "]/Child[1]");
                }
                paths.Add(name + "/Child"); paths.Add(name + "/Child[0]"); paths.Add(name + "/Child[1]");
            }
            var lookup = NewLookup();
            foreach (string path in paths) Assert.Same(NewScan().FindRelative(root, path), lookup.FindRelative(root, path));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void ReusedLookupSeesRenamesReorderingReparentingAndNewAmbiguityImmediately()
    {
        var root = new Node("Root"); var first = root.Add("Bolt"); var second = root.Add("Bolt");
        var lookup = NewLookup();
        Assert.Same(second, lookup.FindRelative(root, "Bolt[1]"));
        root.Children.Reverse();
        Assert.Same(first, lookup.FindRelative(root, "Bolt[1]"));
        second.Name = "Unique";
        Assert.Null(lookup.FindRelative(root, "Bolt[0]"));
        Assert.Same(first, lookup.FindRelative(root, "Bolt"));
        var parent = root.Add("Moved"); root.Children.Remove(first); parent.Children.Add(first); first.Parent = parent;
        Assert.Null(lookup.FindRelative(root, "Bolt"));
        Assert.Same(first, lookup.FindRelative(root, "Moved/Bolt"));
        parent.Add("Bolt");
        Assert.Same(first, lookup.FindRelative(root, "Moved/Bolt[0]"));
        var literal = parent.Add("Bolt[0]");
        Assert.Null(lookup.FindRelative(root, "Moved/Bolt[0]"));
        parent.Children.Remove(literal);
        Assert.Same(first, lookup.FindRelative(root, "Moved/Bolt[0]"));
    }

    [Fact]
    public void LookupReadsEachSiblingOnceAndDoesNotRetainEarlierNames()
    {
        var root = new Node("Root");
        for (int i = 0; i < 8000; i++) root.Add("Item");
        int children = 0, names = 0;
        var lookup = new ScenePathLookup<Node>(n => { names++; return n.Name; }, n => n.Children.Count,
            (n, i) => { children++; return n.Children[i]; });
        Assert.Same(root.Children[7999], lookup.FindRelative(root, "Item[7999]"));
        Assert.Equal(8000, children); Assert.Equal(8000, names);
        root.Children[7999].Name = "Other";
        Assert.Null(lookup.FindRelative(root, "Item[7999]"));
        Assert.Same(root.Children[7999], lookup.FindRelative(root, "Other"));
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    [InlineData("a\nb")]
    [InlineData("./Bolt")]
    [InlineData("../Bolt")]
    public void InvalidLookupPathDoesNotInspectTheHierarchy(string path)
    {
        var lookup = new ScenePathLookup<Node>(_ => throw new System.InvalidOperationException(),
            _ => throw new System.InvalidOperationException(), (_, _) => throw new System.InvalidOperationException());
        Assert.Null(lookup.FindRelative(new Node("Root"), path));
    }

    [Fact]
    public void PartRelativeBoltIdsSurviveRootRenameInstallationAndDifferentPeerInventory()
    {
        var hostGround = new Node("HOST_GARAGE");
        var guestEngine = new Node("CORRIS").Add("Engine");
        var hostPart = hostGround.Add("Alternator(VINXX)");
        hostGround.Add("Alternator(VINXX)");
        var guestPart = guestEngine.Add("Saved alternator");
        var hostBolts = hostPart.Add("Bolts"); var guestBolts = guestPart.Add("Bolts");
        hostBolts.Add("BoltPM"); guestBolts.Add("BoltPM");
        var hostBolt = hostBolts.Add("BoltPM"); var guestBolt = guestBolts.Add("BoltPM");
        var scan = NewScan();
        Assert.NotEqual(scan.Of(hostBolt), scan.Of(guestBolt));
        Assert.Equal("Bolts/BoltPM[1]", scan.RelativeTo(hostBolt, hostPart));
        Assert.Equal(scan.RelativeTo(hostBolt, hostPart), scan.RelativeTo(guestBolt, guestPart));
        Assert.True(PartIdentity.TryItemId("VIN1331", out uint itemId));
        Assert.True(PartIdentity.TryFsmId(itemId, scan.RelativeTo(hostBolt, hostPart)!, "Screw", out uint id));
        hostPart.Name = "Installed alternator";
        hostGround.Children.Remove(hostPart); guestEngine.Children.Add(hostPart); hostPart.Parent = guestEngine;
        var afterInstallation = NewScan();
        Assert.True(PartIdentity.TryFsmId(itemId, afterInstallation.RelativeTo(hostBolt, hostPart)!, "Screw", out uint installedId));
        Assert.Equal(id, installedId);
        Assert.Equal("", afterInstallation.RelativeTo(hostPart, hostPart));
        Assert.Null(afterInstallation.RelativeTo(hostBolt, guestPart));
    }

    [Fact]
    public void PathsAndIdsMatchExistingFormatIncludingDuplicateAncestors()
    {
        var root = new Node("CORRIS");
        var parts = root.Add("Parts");
        var first = parts.Add("Part").Add("Bolts");
        var second = parts.Add("Part").Add("Bolts");
        var bolt0 = first.Add("BoltPM");
        first.Add("Unique");
        var bolt1 = first.Add("BoltPM");
        var bolt2 = second.Add("BoltPM");
        var scan = NewScan();

        // Ask for a later duplicate first; numbering must come from sibling order.
        Assert.Equal("CORRIS/Parts/Part[0]/Bolts/BoltPM[1]", scan.Of(bolt1));
        Assert.Equal("CORRIS/Parts/Part[0]/Bolts/BoltPM[0]", scan.Of(bolt0));
        Assert.Equal("CORRIS/Parts/Part[1]/Bolts/BoltPM", scan.Of(bolt2));
        AssertPaths(root, scan);
    }

    private static void AssertPaths(Node node, ScenePathCache<Node> scan)
    {
        string original = OriginalPath(node);
        Assert.Equal(original, scan.Of(node));
        Assert.Equal(StableHash.Fnv1a32(original), StableHash.Fnv1a32(scan.Of(node)));
        foreach (var child in node.Children) AssertPaths(child, scan);
    }

    private static string OriginalPath(Node node)
    {
        var segments = new List<string>();
        for (Node? current = node; current != null; current = current.Parent)
        {
            int count = 0, index = -1;
            if (current.Parent != null)
                foreach (var sibling in current.Parent.Children)
                {
                    if (sibling.Name != current.Name) continue;
                    if (sibling == current) index = count;
                    count++;
                }
            segments.Add(count > 1 ? current.Name + "[" + index + "]" : current.Name);
        }
        segments.Reverse();
        return string.Join("/", segments);
    }

    [Fact]
    public void FreshScanSeesRenameReparentAndDuplicateRemoval()
    {
        var root = new Node("ROOT");
        var first = root.Add("Bolt");
        var second = root.Add("Bolt");
        var scan = NewScan();
        Assert.Equal("ROOT/Bolt[1]", scan.Of(second));
        root.Children.Remove(first);
        Assert.Equal("ROOT/Bolt", NewScan().Of(second));

        var parent = root.Add("Moved");
        root.Children.Remove(second);
        parent.Children.Add(second);
        second.Parent = parent;
        second.Name = "Renamed";
        Assert.Equal("ROOT/Moved/Renamed", NewScan().Of(second));
        parent.Add("Renamed");
        Assert.Equal("ROOT/Moved/Renamed[0]", NewScan().Of(second));
    }

    [Fact]
    public void RootsKeepPlainNamesEvenWhenOtherRootsShareTheirName()
    {
        var scan = NewScan();
        Assert.Equal("Root", scan.Of(new Node("Root")));
        Assert.Equal("Root", scan.Of(new Node("Root")));
    }

    [Fact]
    public void InterleavedDuplicateGroupsKeepIndependentOrdinalsAndLiteralAmbiguity()
    {
        var root = new Node("Root");
        foreach (string name in new[] { "Bolt", "bolt", "Bolt[0]", "Bolt", "", "bolt", "Bolt", "" })
            root.Add(name);
        var scan = NewScan();
        string[] expected = { "Bolt[0]", "bolt[0]", "Bolt[0]", "Bolt[1]", "[0]", "bolt[1]", "Bolt[2]", "[1]" };
        for (int i = root.Children.Count - 1; i >= 0; i--)
        {
            Assert.Equal("Root/" + expected[i], scan.Of(root.Children[i]));
            Assert.Equal(expected[i], scan.RelativeTo(root.Children[i], root));
        }
        Assert.Null(scan.FindRelative(root, "Bolt[0]"));
        Assert.Same(root.Children[6], scan.FindRelative(root, "Bolt[2]"));
        root.Children.RemoveAt(2);
        Assert.Same(root.Children[0], scan.FindRelative(root, "Bolt[0]"));
        root.Children.Reverse();
        AssertPaths(root, NewScan());
    }

    [Fact]
    public void WideHierarchyReadsSiblingsOnceForTheWholeScan()
    {
        const int count = 8000;
        var root = new Node("Root");
        for (int i = 0; i < count; i++) root.Add("Item");
        int childReads = 0;
        var scan = new ScenePathCache<Node>(n => n.Parent, n => n.Name,
            n => n.Children.Count, (n, i) => { childReads++; return n.Children[i]; });

        foreach (var child in root.Children)
        {
            scan.Of(child);
            scan.Of(child); // Another FSM/catalog rule on the same object.
        }

        // The old path walker performed 128 million sibling reads in this case.
        Assert.Equal(count, childReads);
        Assert.Equal("Root/Item[7999]", scan.Of(root.Children[count - 1]));
    }
}
