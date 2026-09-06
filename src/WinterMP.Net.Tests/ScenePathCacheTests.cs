using System.Collections.Generic;
using WinterMP.Net;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class ScenePathCacheTests
{
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
