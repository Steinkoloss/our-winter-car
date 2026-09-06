using System;
using WinterMP.Net.Sync;

namespace WinterMP.Net.Messages
{
    public struct VenttiPose
    {
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public byte Active;
    }

    /// <summary>Three world poses followed by catalog-ordered local NPC poses; no FSM events.</summary>
    public sealed class VenttiSceneState : IMessage
    {
        public const int WorldPoseCount = 3;
        // 15-byte header + 21 bytes/pose stays within classic Steam P2P's 1200-byte UDP limit.
        public const int MaxPoses = 56;
        public uint TableId, LayoutId, Sequence;
        public VenttiPose[] Poses = {
            new VenttiPose { Rotation = NetQuaternion.Identity },
            new VenttiPose { Rotation = NetQuaternion.Identity },
            new VenttiPose { Rotation = NetQuaternion.Identity },
        };
        public MessageId Id => MessageId.VenttiSceneState;

        public void Write(NetWriter w)
        {
            if (!VenttiSceneReplica.IsValid(this)) throw new ProtocolException("Invalid Ventti scene pose.");
            w.WriteUInt32(TableId); w.WriteUInt32(LayoutId); w.WriteUInt32(Sequence); w.WriteByte((byte)Poses.Length);
            foreach (var pose in Poses)
            {
                w.WriteVector3(pose.Position);
                WriteRotation(w, pose.Rotation.X); WriteRotation(w, pose.Rotation.Y);
                WriteRotation(w, pose.Rotation.Z); WriteRotation(w, pose.Rotation.W);
                w.WriteByte(pose.Active);
            }
        }

        public void Read(NetReader r)
        {
            TableId = r.ReadUInt32(); LayoutId = r.ReadUInt32(); Sequence = r.ReadUInt32();
            int count = r.ReadByte();
            if (count < WorldPoseCount || count > MaxPoses || count * 21 > r.Remaining)
                throw new ProtocolException("Invalid Ventti scene length.");
            var poses = new VenttiPose[count];
            for (int i = 0; i < count; i++)
                poses[i] = new VenttiPose { Position = r.ReadVector3(),
                    Rotation = new NetQuaternion(ReadRotation(r), ReadRotation(r), ReadRotation(r), ReadRotation(r)), Active = r.ReadByte() };
            Poses = poses;
            if (!VenttiSceneReplica.IsValid(this)) throw new ProtocolException("Invalid Ventti scene pose.");
        }

        private static void WriteRotation(NetWriter w, float value)
        {
            value = Math.Max(-1, Math.Min(1, value));
            w.WriteUInt16(unchecked((ushort)(short)Math.Round(value * 32767.0)));
        }
        private static float ReadRotation(NetReader r)
        {
            short value = unchecked((short)r.ReadUInt16());
            if (value == short.MinValue) throw new ProtocolException("Reserved Ventti quaternion value.");
            return value / 32767f;
        }
    }

    /// <summary>Live-only sound chosen by the host. Never included in join snapshots.</summary>
    public sealed class VenttiSoundCue : IMessage
    {
        public uint TableId, LayoutId, Sequence;
        public byte Sound;
        public NetVector3 Position;
        public float Delay;
        public MessageId Id => MessageId.VenttiSoundCue;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(TableId); w.WriteUInt32(LayoutId); w.WriteUInt32(Sequence);
            w.WriteByte(Sound); w.WriteVector3(Position); w.WriteSingle(Delay);
        }
        public void Read(NetReader r)
        {
            TableId = r.ReadUInt32(); LayoutId = r.ReadUInt32(); Sequence = r.ReadUInt32();
            Sound = r.ReadByte(); Position = r.ReadVector3(); Delay = r.ReadSingle();
        }
    }
}
