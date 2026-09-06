using System;
using System.IO;
using System.Linq;
using System.Text;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class PartAttachmentTests
    {
        private static readonly ReplacementPartRule Rule = new ReplacementPartRule(71, "VIN133", 2, 1);
        private static ReplacementPartReplica Replica(ItemSpawnLifecycle? life = null) =>
            new ReplacementPartReplica(new[] { Rule }, life ?? new ItemSpawnLifecycle());
        private static ReplacementPartState State(int suffix = 1) => new ReplacementPartState {
            NativeId = "VIN133" + suffix, FactoryId = 71, Revision = 1, Scalars = new[] { 98.5f, 0f },
            Rotation = NetQuaternion.Identity, AssemblyId = 1, Installed = true,
            ParentKind = PartParentKind.Vehicle, ParentId = 999, ParentPath = "Assemblies/Slot[1]",
            LocalPosition = new NetVector3(.25f, -.5f, .75f), LocalScale = new NetVector3(1, 2, 3) };

        [Fact]
        public void AttachmentIsAppendedAfterTheCompleteV114PrefixAndSurvivesWireRoundTrip()
        {
            var state = State(); state.ParentPath = "Assemblies/Kiinnitys ä";
            byte[] bytes = PacketCodec.Encode(state);
            int prefixLength = 46 + Encoding.UTF8.GetByteCount(state.NativeId) + 4 * state.Scalars.Length;
            Assert.Equal(prefixLength + 48 + Encoding.UTF8.GetByteCount(state.ParentPath), bytes.Length);
            using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            reader.BaseStream.Position = prefixLength;
            Assert.Equal(2, reader.ReadByte()); Assert.Equal(999u, reader.ReadUInt32());
            Assert.Equal(state.ParentPath, Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16())));
            foreach (float value in new[] { .25f, -.5f, .75f, 0, 0, 0, 1, 1, 2, 3 }) Assert.Equal(value, reader.ReadSingle());
            Assert.False(reader.ReadBoolean());
            Assert.Equal(bytes.Length, reader.BaseStream.Position);
            var decoded = Assert.IsType<ReplacementPartState>(PacketCodec.Decode(bytes));
            Assert.True(Replica().Receive(decoded, out _)); Assert.True(PartAttachmentPolicy.Same(state, decoded));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(prefixLength).ToArray()));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
        }

        [Theory]
        [InlineData("kind")] [InlineData("loose-parent")] [InlineData("self-parent")]
        [InlineData("absolute")] [InlineData("traversal")] [InlineData("empty-segment")]
        [InlineData("backslash")] [InlineData("colon")] [InlineData("control")]
        [InlineData("long-path")] [InlineData("long-utf8")] [InlineData("nan-position")]
        [InlineData("bad-rotation")] [InlineData("infinite-scale")] [InlineData("zero-scale")]
        [InlineData("negative-scale")] [InlineData("none-with-id")] [InlineData("none-with-pose")]
        public void InvalidAttachmentCannotReplaceTheLastAcceptedState(string fault)
        {
            var replica = Replica(); Assert.True(replica.Receive(State(), out uint id));
            var bad = State(); bad.Revision = 2;
            switch (fault)
            {
                case "kind": bad.ParentKind = (PartParentKind)3; break;
                case "loose-parent": bad.AssemblyId = 0; bad.Installed = false; break;
                case "self-parent": bad.ParentKind = PartParentKind.NativePart; bad.ParentId = id; break;
                case "absolute": bad.ParentPath = "/Assemblies"; break;
                case "traversal": bad.ParentPath = "Mount/../Save"; break;
                case "empty-segment": bad.ParentPath = "Mount//Socket"; break;
                case "backslash": bad.ParentPath = "Mount\\Socket"; break;
                case "colon": bad.ParentPath = "Mount::Data"; break;
                case "control": bad.ParentPath = "Mount\nSocket"; break;
                case "long-path": bad.ParentPath = new string('x', 513); break;
                case "long-utf8": bad.ParentPath = new string('ä', 257); break;
                case "nan-position": bad.LocalPosition.X = float.NaN; break;
                case "bad-rotation": bad.LocalRotation = default; break;
                case "infinite-scale": bad.LocalScale.Y = float.PositiveInfinity; break;
                case "zero-scale": bad.LocalScale.Y = 0; break;
                case "negative-scale": bad.LocalScale.Z = -1; break;
                case "none-with-id": bad.ParentKind = PartParentKind.None; break;
                case "none-with-pose": bad.ParentKind = PartParentKind.None; bad.ParentId = 0; bad.ParentPath = ""; break;
            }
            Assert.False(replica.Receive(bad, out _)); Assert.Equal(1u, replica.Get(id)!.Revision);
        }

        [Fact]
        public void FittedCopiesStayOutOfLooseMotionAndCanReturnWithoutAStaleAttachment()
        {
            var replica = Replica(); var fitted = State();
            Assert.True(replica.Receive(fitted, out uint id)); Assert.True(Rule.CanCreateFitted(replica.Get(id)!));
            Assert.False(Rule.CanCreate(replica.Get(id)!)); Assert.False(replica.AllowsLooseMotion(id));
            var loose = new ReplacementPartState { FactoryId = fitted.FactoryId, NativeId = fitted.NativeId, Revision = 2,
                Scalars = new[] { 98.5f, 0f }, Position = new NetVector3(20, 2, 30), Rotation = NetQuaternion.Identity };
            Assert.True(replica.Receive(loose, out _)); Assert.True(replica.AllowsLooseMotion(id));
            Assert.False(replica.Receive(fitted, out _)); Assert.True(replica.AllowsLooseMotion(id));
            Assert.False(Rule.CanCreateFitted(replica.Get(id)!));
        }

        [Fact]
        public void UnresolvedMountsRemainPendingAndRetirementPreventsTheirLaterCreation()
        {
            var life = new ItemSpawnLifecycle(); var replica = Replica(life); var state = State();
            state.ParentKind = PartParentKind.None; state.ParentId = 0; state.ParentPath = "";
            state.LocalPosition = default; state.LocalScale = new NetVector3(1, 1, 1);
            Assert.True(replica.Receive(state, out uint id)); Assert.False(Rule.CanCreateFitted(replica.Get(id)!));
            var ready = State(); ready.Revision = 2; Assert.True(replica.Receive(ready, out _));
            Assert.True(Rule.CanCreateFitted(replica.Get(id)!));
            life.Retire(id); Assert.Null(replica.Get(id)); Assert.False(replica.Receive(ready, out _));
        }

        [Fact]
        public void ADelayedParentCannotCloseAnAttachmentCycle()
        {
            var replica = Replica(); var a = State(1); var b = State(2); var c = State(3);
            Rule.TryId(a.NativeId, out uint aid); Rule.TryId(b.NativeId, out uint bid); Rule.TryId(c.NativeId, out uint cid);
            a.ParentKind = b.ParentKind = c.ParentKind = PartParentKind.NativePart;
            a.ParentId = bid; b.ParentId = cid; c.ParentId = aid;
            Assert.True(replica.Receive(a, out _)); Assert.True(replica.Receive(b, out _));
            Assert.False(replica.Receive(c, out _)); Assert.Null(replica.Get(cid));
            c.ParentKind = PartParentKind.Vehicle; c.ParentId = 999; Assert.True(replica.Receive(c, out _));
            c.ParentKind = PartParentKind.NativePart; c.ParentId = aid; c.Revision++;
            Assert.False(replica.Receive(c, out _)); Assert.Equal(PartParentKind.Vehicle, replica.Get(cid)!.ParentKind);
            replica.Clear(); Assert.True(replica.Receive(c, out _));
        }

        [Fact]
        public void AttachmentChangesRequireANewRevisionButWorldMovementDoesNot()
        {
            var state = State(); var publication = new ReplacementPartPublication(); var replica = Replica();
            state.Revision = publication.Observe(state); Assert.True(replica.Receive(state, out uint id));
            publication.MarkBroadcast(state.Revision);
            state.Position.X = 100; Assert.Equal(1u, publication.Observe(state)); Assert.False(publication.NeedsBroadcast);
            Assert.True(replica.Receive(state, out _));
            foreach (Action<ReplacementPartState> change in new Action<ReplacementPartState>[] {
                s => s.ParentId++, s => s.ParentPath += "/Inner", s => s.LocalPosition.X += .1f,
                s => s.LocalScale.Z = 2, s => s.LocalRotation = new NetQuaternion(0, 0, 1, 0) })
            {
                change(state); Assert.False(replica.Receive(state, out _));
                state.Revision = publication.Observe(state); Assert.True(publication.NeedsBroadcast);
                Assert.True(replica.Receive(state, out _)); publication.MarkBroadcast(state.Revision);
                Assert.True(PartAttachmentPolicy.Same(state, replica.Get(id)!));
            }
            replica.Get(id)!.ParentPath = "Changed";
            Assert.Equal(state.ParentPath, replica.Get(id)!.ParentPath);
        }
    }
}
