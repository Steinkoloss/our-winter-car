using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class AtfTests
{
    private static AtfBottleState Bottle(uint revision = 1) => new() { ItemId = 42, Revision = revision,
        NativeId = "atfoil01", Fluid = .625f, Position = new NetVector3(1, 2, -3), Rotation = NetQuaternion.Identity };
    private static AtfFillerState Filler(uint revision = 1) => new() { VehicleId = 43, Revision = revision,
        Rotation = 359, OilLevel = 4.375f, Flags = AtfFillerState.FlagAvailable,
        CapLocalPosition = new NetVector3(.125f, .25f, 1.125f), CapLocalRotation = new NetQuaternion(0, 0, .6f, .8f) };
    private static AtfRefillIntent Intent(ushort sequence = 1, byte action = AtfRefillIntent.Pour) => new() {
        VehicleId = 43, BottleId = action <= AtfRefillIntent.Pour ? 42u : 0u, PlayerId = 1, Sequence = sequence, Action = action };

    [Fact]
    public void BottleWireContainsExactCreationIdentityAndQuantity()
    {
        var original = Bottle(); var bytes = PacketCodec.Encode(original);
        Assert.Equal(53, bytes.Length);
        using var r = new BinaryReader(new MemoryStream(bytes));
        Assert.Equal(220, r.ReadUInt16()); Assert.Equal(42u, r.ReadUInt32()); Assert.Equal(1u, r.ReadUInt32());
        Assert.Equal(8, r.ReadUInt16()); Assert.Equal("atfoil01", new string(r.ReadChars(8)));
        Assert.Equal(.625f, r.ReadSingle()); Assert.Equal(0, r.ReadByte());
        Assert.Equal(1, r.ReadSingle()); Assert.Equal(2, r.ReadSingle()); Assert.Equal(-3, r.ReadSingle());
        Assert.Equal(0, r.ReadSingle()); Assert.Equal(0, r.ReadSingle()); Assert.Equal(0, r.ReadSingle()); Assert.Equal(1, r.ReadSingle());
        var copy = Assert.IsType<AtfBottleState>(PacketCodec.Decode(bytes));
        Assert.True(AtfPolicy.Same(original, copy)); Assert.Equal(original.Position, copy.Position); Assert.Equal(original.Rotation, copy.Rotation);
    }
    [Fact]
    public void FillerAndIntentWireUseFixedLayouts()
    {
        byte[] filler = PacketCodec.Encode(Filler()); Assert.Equal(47, filler.Length);
        using var r = new BinaryReader(new MemoryStream(filler));
        Assert.Equal(221, r.ReadUInt16()); Assert.Equal(43u, r.ReadUInt32()); Assert.Equal(1u, r.ReadUInt32());
        Assert.Equal(359, r.ReadSingle()); Assert.Equal(4.375f, r.ReadSingle()); Assert.Equal(1, r.ReadByte());
        Assert.Equal(.125f, r.ReadSingle()); Assert.Equal(.25f, r.ReadSingle()); Assert.Equal(1.125f, r.ReadSingle());
        Assert.Equal(0, r.ReadSingle()); Assert.Equal(0, r.ReadSingle()); Assert.Equal(.6f, r.ReadSingle()); Assert.Equal(.8f, r.ReadSingle());
        Assert.Equal(r.BaseStream.Length, r.BaseStream.Position);
        Assert.True(AtfPolicy.Same(Filler(), Assert.IsType<AtfFillerState>(PacketCodec.Decode(filler))));
        byte[] intent = PacketCodec.Encode(Intent(513)); Assert.Equal(14, intent.Length);
        using var i = new BinaryReader(new MemoryStream(intent));
        Assert.Equal(222, i.ReadUInt16()); Assert.Equal(43u, i.ReadUInt32()); Assert.Equal(42u, i.ReadUInt32());
        Assert.Equal(1, i.ReadByte()); Assert.Equal(513, i.ReadUInt16()); Assert.Equal(AtfRefillIntent.Pour, i.ReadByte());
    }
    [Fact]
    public void EveryTruncationAndTrailingByteIsRejected()
    {
        foreach (IMessage message in new IMessage[] { Bottle(), Filler(), Intent() })
        {
            byte[] bytes = PacketCodec.Encode(message);
            for (int n = 0; n < bytes.Length; n++) Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes[..n]));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        }
    }
    [Theory]
    [InlineData(0)][InlineData(.02f)][InlineData(.099f)][InlineData(.1f)]
    public void NativeEmptyRemnantRemainsIndependentFromQuantity(float fluid)
    {
        var bottle = Bottle(); bottle.Fluid = fluid; bottle.Empty = true;
        Assert.True(AtfPolicy.Valid(bottle)); Assert.True(Assert.IsType<AtfBottleState>(PacketCodec.Decode(PacketCodec.Encode(bottle))).Empty);
        bottle.Empty = false; Assert.True(AtfPolicy.Valid(bottle));
    }
    [Theory]
    [InlineData("atfoil01")][InlineData("atfoil09")][InlineData("atfoil0123")][InlineData("atfoil02147483647")]
    public void NativeFactoryPrefixAndPositiveCounterArePreserved(string nativeId)
    {
        var bottle = Bottle(); bottle.NativeId = nativeId;
        Assert.Equal(nativeId, Assert.IsType<AtfBottleState>(PacketCodec.Decode(PacketCodec.Encode(bottle))).NativeId);
    }
    [Theory]
    [InlineData("")][InlineData("atfoil0")][InlineData("atfoil00")][InlineData("atfoil001")][InlineData("atfoil1")]
    [InlineData("atfoil0-1")][InlineData("atfoil0+1")][InlineData("atfoil0１")][InlineData("atfoil02147483648")]
    [InlineData("atfoil01 ")][InlineData("ATFOIL01")]
    public void InvalidNativeIdentityIsRejectedOnReadAndWrite(string nativeId)
    {
        var bottle = Bottle(); bottle.NativeId = nativeId;
        Assert.False(AtfPolicy.Valid(bottle)); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(bottle));
        Assert.Throws<ProtocolException>(() => ReadRawBottle(bottle));
    }
    [Fact]
    public void InvalidIdentityLengthAndZeroIdentifiersAreRejected()
    {
        var bottle = Bottle(); bottle.NativeId = "atfoil0" + new string('1', 64);
        Assert.False(AtfPolicy.Valid(bottle)); Assert.Throws<ProtocolException>(() => ReadRawBottle(bottle));
        bottle = Bottle(); bottle.ItemId = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(bottle)); Assert.Throws<ProtocolException>(() => ReadRawBottle(bottle));
        bottle = Bottle(0); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(bottle)); Assert.Throws<ProtocolException>(() => ReadRawBottle(bottle));
        var filler = Filler(); filler.VehicleId = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(filler));
        byte[] bytes = PacketCodec.Encode(Filler()); Array.Clear(bytes, 2, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        filler = Filler(0); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(filler));
        bytes = PacketCodec.Encode(Filler()); Array.Clear(bytes, 6, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
    }
    [Theory]
    [InlineData(float.NaN)][InlineData(float.NegativeInfinity)][InlineData(float.PositiveInfinity)][InlineData(-.001f)][InlineData(1.001f)]
    public void InvalidBottleQuantityIsRejected(float fluid)
    {
        var bottle = Bottle(); bottle.Fluid = fluid;
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(bottle)); Assert.Throws<ProtocolException>(() => ReadRawBottle(bottle));
    }
    [Fact]
    public void EmptyFlagIsStrictAndCannotHideAFullBottle()
    {
        var bottle = Bottle(); bottle.Empty = true;
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(bottle)); Assert.Throws<ProtocolException>(() => ReadRawBottle(bottle));
        Assert.Throws<ProtocolException>(() => ReadRawBottle(Bottle(), 2));
        Assert.Throws<ProtocolException>(() => ReadRawBottle(Bottle(), 255));
    }
    [Theory]
    [InlineData(float.NaN)][InlineData(float.PositiveInfinity)][InlineData(float.NegativeInfinity)]
    public void NonfiniteCreationPoseCannotReachTheGame(float bad)
    {
        for (int component = 0; component < 7; component++)
        {
            var bottle = Bottle(); float[] pose = { 1, 2, 3, 0, 0, 0, 1 }; pose[component] = bad;
            bottle.Position = new NetVector3(pose[0], pose[1], pose[2]); bottle.Rotation = new NetQuaternion(pose[3], pose[4], pose[5], pose[6]);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(bottle)); Assert.Throws<ProtocolException>(() => ReadRawBottle(bottle));
        }
    }
    [Theory]
    [InlineData(0)][InlineData(.8f)][InlineData(1.2f)]
    public void DegenerateCreationRotationIsRejected(float w)
    {
        var bottle = Bottle(); bottle.Rotation = new NetQuaternion(0, 0, 0, w);
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(bottle)); Assert.Throws<ProtocolException>(() => ReadRawBottle(bottle));
    }
    [Theory]
    [InlineData(float.NaN)][InlineData(float.PositiveInfinity)][InlineData(float.NegativeInfinity)][InlineData(0)][InlineData(360)]
    public void InvalidCapRotationIsRejected(float rotation)
    {
        var filler = Filler(); filler.Rotation = rotation; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(filler));
        byte[] bytes = PacketCodec.Encode(Filler()); Array.Copy(BitConverter.GetBytes(rotation), 0, bytes, 10, 4);
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes)); Assert.False(AtfPolicy.CapOpen(rotation));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtfPolicy.TurnCap(rotation, true));
    }
    [Theory]
    [InlineData(float.NaN)][InlineData(float.PositiveInfinity)][InlineData(float.NegativeInfinity)][InlineData(-1.001f)][InlineData(6.301f)]
    public void InvalidOilLevelIsRejected(float oil)
    {
        var filler = Filler(); filler.OilLevel = oil; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(filler));
        byte[] bytes = PacketCodec.Encode(Filler()); Array.Copy(BitConverter.GetBytes(oil), 0, bytes, 14, 4);
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
    }
    [Theory]
    [InlineData(-1)][InlineData(-.0001f)][InlineData(0)][InlineData(6.3f)]
    public void NativeOilBoundariesRemainValid(float oil)
    {
        var filler = Filler(); filler.OilLevel = oil; filler.Flags = 0;
        Assert.False(filler.Available); Assert.Equal(oil, Assert.IsType<AtfFillerState>(PacketCodec.Decode(PacketCodec.Encode(filler))).OilLevel);
    }
    [Theory]
    [InlineData(2)][InlineData(3)][InlineData(255)]
    public void UnknownFillerFlagsAreRejected(byte flags)
    {
        var filler = Filler(); filler.Flags = flags; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(filler));
        byte[] bytes = PacketCodec.Encode(Filler()); bytes[18] = flags; Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
    }
    [Theory]
    [InlineData(float.NaN)][InlineData(float.PositiveInfinity)][InlineData(float.NegativeInfinity)]
    public void NonfiniteCapPoseIsRejectedOnReadAndWrite(float bad)
    {
        for (int component = 0; component < 7; component++)
        {
            var filler = Filler(); float[] pose = { .1f, .25f, 1.1f, 0, 0, 0, 1 }; pose[component] = bad;
            filler.CapLocalPosition = new NetVector3(pose[0], pose[1], pose[2]);
            filler.CapLocalRotation = new NetQuaternion(pose[3], pose[4], pose[5], pose[6]);
            Assert.False(AtfPolicy.Valid(filler));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(filler)); Assert.Throws<ProtocolException>(() => ReadRawFiller(filler));
        }
    }
    [Theory]
    [InlineData(-5.001f)][InlineData(5.001f)][InlineData(float.MaxValue)][InlineData(float.MinValue)]
    public void CapPositionMustRemainWithinFiveMetresOfCorrisOnEveryAxis(float bad)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            var filler = Filler(); float[] position = { 0, 0, 0 }; position[axis] = bad;
            filler.CapLocalPosition = new NetVector3(position[0], position[1], position[2]);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(filler)); Assert.Throws<ProtocolException>(() => ReadRawFiller(filler));
        }
    }
    [Theory]
    [InlineData(-5)][InlineData(0)][InlineData(5)]
    public void CapLocalCoordinateBoundsAreInclusive(float coordinate)
    {
        var filler = Filler(); filler.CapLocalPosition = new NetVector3(coordinate, coordinate, coordinate);
        var copy = Assert.IsType<AtfFillerState>(PacketCodec.Decode(PacketCodec.Encode(filler)));
        Assert.Equal(filler.CapLocalPosition, copy.CapLocalPosition); Assert.Equal(filler.CapLocalRotation, copy.CapLocalRotation);
    }
    [Theory]
    [InlineData(0)][InlineData(.948f)][InlineData(1.05f)]
    public void InvalidCapQuaternionNormIsRejected(float w)
    {
        var filler = Filler(); filler.CapLocalRotation = new NetQuaternion(0, 0, 0, w);
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(filler)); Assert.Throws<ProtocolException>(() => ReadRawFiller(filler));
    }
    [Fact]
    public void FillerRevisionTracksCapMotionWithMillimetreTolerance()
    {
        var old = Filler(); old.CapLocalPosition = new NetVector3(0, 0, 0);
        var next = AtfPolicy.Copy(old); next.CapLocalPosition = new NetVector3(AtfPolicy.CapPositionTolerance, 0, 0);
        Assert.True(AtfPolicy.Same(old, next)); Assert.True(AtfPolicy.CanReceive(old, next));
        next.CapLocalPosition = new NetVector3(.0006f, .0006f, .0006f);
        Assert.False(AtfPolicy.Same(old, next)); Assert.False(AtfPolicy.CanReceive(old, next));
        next.Revision++; Assert.True(AtfPolicy.CanReceive(old, next));
        next.Revision = old.Revision; next.CapLocalPosition = new NetVector3(.001001f, 0, 0);
        Assert.False(AtfPolicy.CanReceive(old, next));
        next.CapLocalPosition = new NetVector3(.099f, 0, 0); Assert.False(AtfPolicy.Same(old, next));
    }
    [Theory]
    [InlineData(0,true)][InlineData(.099f,true)][InlineData(.101f,false)][InlineData(180,false)]
    public void CapRotationEquivalenceUsesPointOneDegreeAndIgnoresQuaternionSign(float degrees, bool same)
    {
        var old = Filler(); old.CapLocalRotation = NetQuaternion.Identity;
        var next = AtfPolicy.Copy(old); double half = degrees * Math.PI / 360;
        foreach (float scale in new[] { 1f, -1f, .95f, -1.04f })
        {
            next.CapLocalRotation = new NetQuaternion(0, scale * (float)Math.Sin(half), 0, scale * (float)Math.Cos(half));
            Assert.True(AtfPolicy.Valid(next)); Assert.Equal(same, AtfPolicy.Same(old, next));
            Assert.Equal(same, AtfPolicy.CanReceive(old, next));
        }
        next.Revision++; Assert.True(AtfPolicy.CanReceive(old, next));
    }
    [Theory]
    [InlineData(0,42,1,1)][InlineData(43,0,1,0)][InlineData(43,0,1,1)][InlineData(43,42,1,2)][InlineData(43,42,1,3)]
    [InlineData(43,42,0,1)][InlineData(43,42,255,1)][InlineData(43,0,1,4)]
    public void MalformedIntentIsRejectedInBothDirections(uint vehicle, uint bottle, byte player, byte action)
    {
        var intent = Intent(); intent.VehicleId = vehicle; intent.BottleId = bottle; intent.PlayerId = player; intent.Action = action;
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
        var w = new NetWriter(); w.WriteUInt32(vehicle); w.WriteUInt32(bottle); w.WriteByte(player); w.WriteUInt16(1); w.WriteByte(action);
        Assert.Throws<ProtocolException>(() => new AtfRefillIntent().Read(new NetReader(w.ToArray())));
    }
    [Fact]
    public void RevisionsRejectConflictsStaleAndOtherIdentitiesButAllowPoseRefresh()
    {
        foreach (uint revision in new[] { 1u, 2u, uint.MaxValue })
        {
            Assert.True(AtfPolicy.CanReceive(null, Bottle(revision))); Assert.True(AtfPolicy.CanReceive(null, Filler(revision)));
            Assert.True(AtfPolicy.CanReceive(Bottle(revision), Bottle(revision))); Assert.True(AtfPolicy.CanReceive(Filler(revision), Filler(revision)));
        }
        Assert.True(AtfPolicy.CanReceive(Bottle(uint.MaxValue), Bottle(1))); Assert.True(AtfPolicy.CanReceive(Filler(uint.MaxValue), Filler(1)));
        Assert.False(AtfPolicy.CanReceive(Bottle(2), Bottle(1))); Assert.False(AtfPolicy.CanReceive(Filler(2), Filler(1)));
        Assert.False(AtfPolicy.CanReceive(Bottle(1), Bottle(0x80000001))); Assert.False(AtfPolicy.CanReceive(Filler(1), Filler(0x80000001)));
        Assert.False(AtfPolicy.CanReceive(null, Bottle(0))); Assert.False(AtfPolicy.CanReceive(null, Filler(0)));
        var next = Bottle(); next.Position = new NetVector3(42, 43, 44); next.Rotation = new NetQuaternion(0, 1, 0, 0);
        Assert.True(AtfPolicy.CanReceive(Bottle(), next)); Assert.True(AtfPolicy.Same(Bottle(), next));
        next = Bottle(); next.Fluid = .5f; Assert.False(AtfPolicy.CanReceive(Bottle(), next));
        next = Bottle(2); next.ItemId++; Assert.False(AtfPolicy.CanReceive(Bottle(), next));
        next = Bottle(2); next.NativeId = "atfoil02"; Assert.False(AtfPolicy.CanReceive(Bottle(), next));
        var empty = Bottle(); empty.Fluid = .01f; next = AtfPolicy.Copy(empty); next.Empty = true;
        Assert.False(AtfPolicy.CanReceive(empty, next)); next.Revision++; Assert.True(AtfPolicy.CanReceive(empty, next));
        var cap = Filler(); cap.Rotation = 1; Assert.False(AtfPolicy.CanReceive(Filler(), cap));
        cap = Filler(); cap.OilLevel = 4; Assert.False(AtfPolicy.CanReceive(Filler(), cap));
        cap = Filler(); cap.Flags = 0; Assert.False(AtfPolicy.CanReceive(Filler(), cap));
        cap = Filler(2); cap.VehicleId++; Assert.False(AtfPolicy.CanReceive(Filler(), cap));
    }
    [Fact]
    public void CopiesPreserveAllFieldsWithoutSharingMutableState()
    {
        var bottle = Bottle(); var copy = AtfPolicy.Copy(bottle); Assert.NotSame(bottle, copy);
        Assert.Equal(bottle.Revision, copy.Revision); Assert.True(AtfPolicy.Same(bottle, copy));
        Assert.Equal(bottle.Position, copy.Position); Assert.Equal(bottle.Rotation, copy.Rotation); copy.Fluid = 0; Assert.Equal(.625f, bottle.Fluid);
        var filler = Filler(); var cap = AtfPolicy.Copy(filler); Assert.NotSame(filler, cap);
        Assert.True(AtfPolicy.Same(filler, cap)); Assert.Equal(filler.Revision, cap.Revision); cap.Rotation = 1; Assert.Equal(359, filler.Rotation);
        Assert.Equal(filler.CapLocalPosition, cap.CapLocalPosition); Assert.Equal(filler.CapLocalRotation, cap.CapLocalRotation);
        cap.CapLocalPosition = new NetVector3(2, 3, 4); cap.CapLocalRotation = NetQuaternion.Identity;
        Assert.Equal(new NetVector3(.125f, .25f, 1.125f), filler.CapLocalPosition);
        Assert.Equal(new NetQuaternion(0, 0, .6f, .8f), filler.CapLocalRotation);
    }
    [Fact]
    public void OrderingIsPerPlayerAndConsumesRejectedGeometryBeforeRetry()
    {
        var ledger = new AtfIntentLedger(); Assert.True(ledger.Accept(Intent(65535), true));
        Assert.False(ledger.Accept(Intent(65535), true)); Assert.True(ledger.Accept(Intent(0), true));
        Assert.False(ledger.Accept(Intent(32768), true)); Assert.False(ledger.Accept(Intent(1), false));
        Assert.False(ledger.Accept(Intent(1), true)); Assert.False(ledger.Accept(Intent(0), true));
        Assert.True(ledger.Accept(Intent(2, AtfRefillIntent.Unscrew), true));
        Assert.False(ledger.Accept(Intent(2), true));
        var other = Intent(1); other.PlayerId = 2; Assert.True(ledger.Accept(other, true));
        ledger.Forget(1); Assert.True(ledger.Accept(Intent(1), true)); Assert.False(ledger.Accept(other, true));
        ledger.Clear(); Assert.True(ledger.Accept(other, true)); Assert.True(ledger.Accept(Intent(1), true));
    }
    [Fact]
    public void DuplicateKeepaliveCannotRefreshLeaseOrAwardTransferTime()
    {
        var ledger = new AtfIntentLedger(); float lastAccepted = -1, amount = 0;
        foreach (float now in new[] { 0f, .2f, .4f, .59f })
            if (ledger.Accept(Intent(1), true)) { lastAccepted = now; amount += AtfPolicy.TransferAmount(1, 0, .1f); }
        Assert.Equal(0, lastAccepted); Assert.Equal(.01f, amount, 6); Assert.False(AtfPolicy.LeaseFresh(.61f, lastAccepted));
        Assert.False(ledger.Accept(Intent(2), false)); Assert.False(ledger.Accept(Intent(2), true));
    }
    [Theory]
    [InlineData(0,0,true)][InlineData(.6f,0,true)][InlineData(.601f,0,false)][InlineData(0,1,false)]
    [InlineData(0,-1,false)][InlineData(float.NaN,0,false)][InlineData(1,float.PositiveInfinity,false)]
    public void LeaseUsesOnlyBoundedForwardHostTime(float now, float accepted, bool expected)
        => Assert.Equal(expected, AtfPolicy.LeaseFresh(now, accepted));
    [Theory]
    [InlineData(359,true,326)][InlineData(34,true,1)][InlineData(2,true,1)][InlineData(1,true,1)]
    [InlineData(1,false,34)][InlineData(326,false,359)][InlineData(358,false,359)][InlineData(359,false,359)]
    public void CapUsesNativeStepAndClamps(float before, bool unscrew, float after)
        => Assert.Equal(after, AtfPolicy.TurnCap(before, unscrew));
    [Fact]
    public void CapMustReachFullyOpen()
    { Assert.True(AtfPolicy.CapOpen(1)); Assert.False(AtfPolicy.CapOpen(1.001f)); Assert.False(AtfPolicy.CapOpen(359)); }
    [Theory]
    [InlineData(1,0,.1f,.01f)][InlineData(1,0,1,.025f)][InlineData(1,0,100,.025f)]
    [InlineData(.004f,0,.25f,.004f)][InlineData(1,6.29f,.25f,.01f)][InlineData(1,6.3f,.25f,0)]
    [InlineData(0,0,.25f,0)][InlineData(1,-1,.25f,.025f)]
    public void TransferConservesSourceAndDestinationWithoutOverfill(float source, float target, float dt, float expected)
    {
        float amount = AtfPolicy.TransferAmount(source, target, dt);
        Assert.Equal(expected, amount, 5); Assert.InRange(amount, 0, source);
        Assert.True(target + amount <= AtfPolicy.Capacity); Assert.True(amount <= .025001f);
        Assert.Equal(source + target, (source - amount) + (target + amount), 5);
    }
    [Theory]
    [InlineData(float.NaN,0,.1f)][InlineData(1,float.PositiveInfinity,.1f)][InlineData(1,0,float.NaN)]
    [InlineData(1,0,float.PositiveInfinity)][InlineData(1,0,-1)][InlineData(1,0,0)][InlineData(1.01f,0,.1f)]
    [InlineData(-.01f,0,.1f)][InlineData(1,6.31f,.1f)][InlineData(1,-1.01f,.1f)]
    public void InvalidTransferInputsGrantNothing(float source, float target, float dt)
        => Assert.Equal(0, AtfPolicy.TransferAmount(source, target, dt));
    [Fact]
    public void AtfMessagesRequireCorrectRoleAdmissionAndReliableOrderedChannel()
    {
        foreach (var id in new[] { MessageId.AtfBottleState, MessageId.AtfFillerState })
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, false));
        }
        Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.AtfRefillIntent, true, true, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.AtfRefillIntent, true, false, false, true));
        Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.AtfRefillIntent, false, true, true, true));
        foreach (var id in new[] { MessageId.AtfBottleState, MessageId.AtfFillerState, MessageId.AtfRefillIntent })
        {
            Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
        }
    }
    [Fact]
    public void CatalogBindsNativeBottleIdentityAndFillerWithoutDisablingOtherSystems()
    {
        var catalog = SyncCatalogJson.Parse(CatalogText()); Assert.Null(catalog.AtfRefillError);
        var atf = Assert.IsType<AtfRefillData>(catalog.AtfRefill);
        Assert.Equal(AtfPolicy.NativePrefix, atf["prefix"]); Assert.Equal("atfoil0", atf["prefabName"]);
        Assert.Equal(FactoryItemIdentity.FactoryId("Spawner/CreateItems", "ATFOil"), atf.FactoryId);
        Assert.Equal("Load 3", atf["emptyCheck"]); Assert.Equal("State 4", atf["empty"]);
        Assert.Equal("CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox", atf["mountPath"]);
        Assert.Equal(atf["mountPath"] + "/OpenCap", atf["capPath"]);
        Assert.Equal(atf["capPath"] + "/CapTrigger_ATFOil", atf["fillPath"]);
        Assert.Equal("Fluid", atf["fluid"]); Assert.Equal("OilLevel", atf["oil"]);
        Assert.NotNull(catalog.Stoves); Assert.NotNull(catalog.MooseMeat);
    }
    [Fact]
    public void EveryRequiredCatalogBindingFailsLocallyWhenMissingOrWrongType()
    {
        string text = CatalogText(); var original = JsonNode.Parse(text)!;
        string[] keys = original["atfRefill"]!.AsObject().Select(pair => pair.Key).ToArray();
        Assert.Contains("emptyCheck", keys);
        foreach (string key in keys)
        {
            var missing = JsonNode.Parse(text)!; missing["atfRefill"]!.AsObject().Remove(key); AssertAtfUnavailable(missing);
            var wrong = JsonNode.Parse(text)!; wrong["atfRefill"]![key] = 123; AssertAtfUnavailable(wrong);
        }
    }
    [Theory]
    [InlineData("")][InlineData("/CORRIS")][InlineData("CORRIS//OpenCap")][InlineData("CORRIS/../OpenCap")]
    [InlineData("CORRIS\\OpenCap")][InlineData("CORRIS::Data")][InlineData("CORRIS/\nOpenCap")]
    public void InvalidCatalogPathsFailOnlyTheAtfAdapter(string path)
    {
        foreach (string key in new[] { "rootPath", "mountPath", "capPath", "fillPath", "gaugePath" })
        {
            var json = JsonNode.Parse(CatalogText())!; json["atfRefill"]![key] = path; AssertAtfUnavailable(json);
        }
    }
    [Theory]
    [InlineData("prefix", "atfoil")][InlineData("prefix", "atfoil00")]
    [InlineData("factoryIdentity", "Spawner/CreateItems::Milk")][InlineData("factoryIdentity", "Spawner/Other::ATFOil")]
    public void CatalogCannotSilentlyChangeTheProtocolNativeIdentity(string key, string value)
    {
        var json = JsonNode.Parse(CatalogText())!; json["atfRefill"]![key] = value; AssertAtfUnavailable(json);
    }
    private static string CatalogText() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json"));
    private static void AssertAtfUnavailable(JsonNode json)
    {
        var data = SyncCatalogJson.Parse(json.ToJsonString()); Assert.Null(data.AtfRefill); Assert.NotNull(data.AtfRefillError);
        Assert.NotNull(data.Stoves); Assert.NotNull(data.MooseMeat);
    }
    private static AtfBottleState ReadRawBottle(AtfBottleState s, byte? empty = null)
    {
        var w = new NetWriter(); w.WriteUInt32(s.ItemId); w.WriteUInt32(s.Revision); w.WriteString(s.NativeId);
        w.WriteSingle(s.Fluid); w.WriteByte(empty ?? (s.Empty ? (byte)1 : (byte)0)); w.WriteVector3(s.Position); w.WriteQuaternion(s.Rotation);
        var read = new AtfBottleState(); read.Read(new NetReader(w.ToArray())); return read;
    }
    private static AtfFillerState ReadRawFiller(AtfFillerState s)
    {
        var w = new NetWriter(); w.WriteUInt32(s.VehicleId); w.WriteUInt32(s.Revision);
        w.WriteSingle(s.Rotation); w.WriteSingle(s.OilLevel); w.WriteByte(s.Flags);
        w.WriteVector3(s.CapLocalPosition); w.WriteQuaternion(s.CapLocalRotation);
        var read = new AtfFillerState(); read.Read(new NetReader(w.ToArray())); return read;
    }
}
