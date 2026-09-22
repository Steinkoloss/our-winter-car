using System;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization=true)]
namespace ContainerFuelPortable
{
    public sealed class TransferTests
    {
        private sealed class World
        {
            internal ItemWorldSync Items=new(); internal FluidContainerSync Fluids;
            internal SyncedItem Can,Car; internal SessionManager Session;
            internal World(bool host)
            {
                Time.unscaledTime=10; Input.Press=false;
                Session=new SessionManager { IsHost=host,LocalPlayerId=host?(byte)0:(byte)2 };
                Session.Players.Add(new Player { PlayerId=2 }); Session.Players.Add(new Player { PlayerId=3 });
                SessionManager.Instance=Session; GameObject.Player=new GameObject();
                Can=new SyncedItem { Id=10,Path=FluidContainerSync.GasolinePath,RemoteOwner=host?(byte)2:(byte)255,
                    LocallyOwned=!host,Held=!host,LastRemoteAt=10 };
                Car=new SyncedItem { Id=20,Path=FluidContainerSync.SorbetPath,IsVehicle=true,GaugeFuelLevelVar=new FsmFloat() };
                Can.Body.transform.Path=Can.Path; Car.Body.transform.Path=Car.Path;
                Add(Can,"FluidTrigger","Data",("Fluid",10));
                Add(Can,"Triggers/CapTrigger_FuelJerryGasoline","Trigger",("FuelLevel",99),("MaxCapacity",20));
                Add(Car,"Simulation/FuelTankSorbett","Data",("FuelLevel",20),("MaxCapacity",40));
                Items.Items.Add(10,Can); Items.Items.Add(20,Car); Fluids=new FluidContainerSync(Items);
                Fluids.BuildSnapshots(0).ToArray(); Assert.True(VehicleWorldSync.PrepareContainerTank(Car));
            }
            internal void Use()=>SessionManager.Instance=Session;
            internal float Source=>Can.FluidLevelVar!.Value;
            internal float Tank=>Car.FuelTankLevelVar!.Value;
            private static void Add(SyncedItem item,string suffix,string name,params (string,float)[] variables)
            {
                var fsm=new PlayMakerFSM { FsmName=name,transform=new Transform { Path=item.Path+"/"+suffix } };
                foreach(var v in variables) fsm.FsmVariables.Floats[v.Item1]=new FsmFloat { Value=v.Item2 };
                item.Body.transform.Children.Add(suffix,fsm.transform);
                item.Body.Fsms=item.Body.Fsms.Concat(new[]{fsm}).ToArray();
            }
        }
        private static ContainerFuelIntent Request(uint seq=1,float amount=2)=>new ContainerFuelIntent {
            SourceId=10,VehicleId=20,PlayerId=2,Sequence=seq,Amount=amount };

        [Fact]
        public void ProductionGuestInputHostCommitResultAndPeriodicSnapshotsConverge()
        {
            var host=new World(true); var guest=new World(false); var observer=new World(false);
            guest.Use(); Input.Press=true; guest.Fluids.Update(guest.Session); Input.Press=false;
            var request=Assert.IsType<ContainerFuelIntent>(Assert.Single(guest.Session.Sent));
            Assert.Equal(.25f,request.Amount); Assert.Equal(10,guest.Source); Assert.Equal(20,guest.Tank);
            host.Use(); host.Fluids.OnFuelIntent(request,2);
            var result=Assert.IsType<ContainerFuelResult>(Assert.Single(host.Session.Sent));
            Assert.Equal(9.75f,host.Source); Assert.Equal(20.25f,host.Tank);
            Assert.Equal(30d,(double)host.Source+host.Tank);
            foreach(var peer in new[]{guest,observer}) {
                peer.Use(); peer.Fluids.OnFuelResult(result); peer.Fluids.OnFuelResult(result);
                Assert.Equal(host.Source,peer.Source); Assert.Equal(host.Tank,peer.Tank);
                // Delayed reliable source snapshot and delayed unreliable tank state
                // were sampled before the committed transfer.
                peer.Fluids.OnRemoteState(new FluidContainerState { ItemId=10,OwnerPlayerId=0,Sequence=100,Level=10,Capacity=20,FuelRevision=0 });
                Assert.True(VehicleWorldSync.ReceiveContainerTank(peer.Car,new VehicleState { FuelRevision=0,FuelLiters=20 },false));
                Assert.Equal(host.Source,peer.Source); Assert.Equal(host.Tank,peer.Tank);
                peer.Can.FluidLevelVar!.Value=19;
                peer.Fluids.Update(peer.Session); Assert.Equal(host.Source,peer.Source);
            }
            host.Use(); Assert.False(host.Fluids.TryAcceptGuestState(new FluidContainerState {
                ItemId=10,OwnerPlayerId=2,Sequence=99,Level=19,Capacity=20,FuelRevision=result.Revision },2));
            Assert.Equal(9.75f,host.Source);
            var sourceSnapshot=host.Fluids.BuildSnapshots(0).Single();
            var tankSnapshot=new VehicleState { VehicleId=20,OwnerPlayerId=0,Sequence=VehicleState.SnapshotSequence };
            VehicleWorldSync.CaptureContainerTank(host.Car,tankSnapshot);
            tankSnapshot=Assert.IsType<VehicleState>(PacketCodec.Decode(PacketCodec.Encode(tankSnapshot)));
            Assert.Equal(result.Revision,tankSnapshot.FuelRevision); Assert.Equal(host.Tank,tankSnapshot.FuelLiters);
            guest.Use(); guest.Fluids.OnRemoteState(sourceSnapshot);
            Assert.True(VehicleWorldSync.ReceiveContainerTank(guest.Car,tankSnapshot,false));
            Assert.Equal(host.Source,guest.Source); Assert.Equal(host.Tank,guest.Tank);
            host.Use(); host.Fluids.OnFuelIntent(request,2); Assert.Single(host.Session.Sent);
        }

        [Theory]
        [InlineData("actor")] [InlineData("owner")] [InlineData("source")] [InlineData("target")]
        [InlineData("diesel")] [InlineData("other-car")] [InlineData("moving")] [InlineData("driving")]
        [InlineData("local-driving")] [InlineData("engine")] [InlineData("lease")] [InlineData("stale-pose")]
        [InlineData("future-pose")] [InlineData("far-player")] [InlineData("far-can")]
        [InlineData("source-empty")] [InlineData("full")] [InlineData("zero")] [InlineData("nan")]
        [InlineData("infinity")] [InlineData("negative")] [InlineData("destroyed")] [InlineData("setter")]
        public void RealCoreValidationRejectsWithoutAnyLevelOrSequenceMutation(string failure)
        {
            var w=new World(true); var r=Request(); byte actor=2;
            switch(failure) {
                case "actor":actor=3;break; case "owner":w.Can.RemoteOwner=3;break;
                case "source":r.SourceId=999;break; case "target":r.VehicleId=999;break;
                case "diesel":w.Can.Path="EQUIPMENTS/diesel(itemx)";break;
                case "other-car":w.Car.Path="CORRIS";break;
                case "moving":w.Car.Body.velocity=new Vector3(1,0,0);break;
                case "driving":w.Car.RemoteIsDriver=true;break;
                case "local-driving":w.Items.Driving=true;break;
                case "engine":w.Car.Ignition=true;break;
                case "lease":w.Can.LastRemoteAt=1;break;
                case "stale-pose":w.Session.Players[0].LastTransformTime=1;break;
                case "future-pose":w.Session.Players[0].LastTransformTime=20;break;
                case "far-player":w.Session.Players[0].Position=new Vector3(50,0,0);break;
                case "far-can":w.Can.TargetPosition=new Vector3(50,0,0);break;
                case "source-empty":w.Can.FluidLevelVar!.Value=0;break;
                case "full":w.Car.FuelTankLevelVar!.Value=40;break;
                case "zero":r.Amount=0;break; case "nan":r.Amount=float.NaN;break;
                case "infinity":r.Amount=float.PositiveInfinity;break;case "negative":r.Amount=-2;break;
                case "destroyed":w.Can.DespawnSent=true;break;
                case "setter":w.Car.FuelTankLevelVar!.FailNextSet=true;break;
            }
            float source=w.Source,tank=w.Tank;
            w.Fluids.OnFuelIntent(r,actor); Assert.Empty(w.Session.Sent);
            Assert.Equal(source,w.Source);Assert.Equal(tank,w.Tank);
            Assert.Equal(0u,w.Can.FuelRevision);Assert.Equal(0u,w.Car.FuelRevision);
            w.Can.Path=FluidContainerSync.GasolinePath;w.Car.Path=FluidContainerSync.SorbetPath;
            w.Can.RemoteOwner=2;w.Can.LastRemoteAt=10;w.Can.TargetPosition=Vector3.zero;
            w.Can.DespawnSent=false;w.Car.RemoteIsDriver=w.Items.Driving=w.Car.Ignition=false;
            w.Car.Body.velocity=Vector3.zero; w.Session.Players[0].LastTransformTime=10;
            w.Session.Players[0].Position=Vector3.zero;w.Can.FluidLevelVar!.Value=10;w.Car.FuelTankLevelVar!.Value=20;
            w.Fluids.OnFuelIntent(Request(),2);Assert.Single(w.Session.Sent);
            Assert.Equal(8,w.Source);Assert.Equal(22,w.Tank);
        }

        [Fact]
        public void DuplicateOutOfOrderAndCompetingGuestCannotSpendASecondTime()
        {
            var w=new World(true);
            w.Fluids.OnFuelIntent(Request(5),2);
            w.Fluids.OnFuelIntent(Request(5),2);w.Fluids.OnFuelIntent(Request(4),2);
            var competing=Request(100);competing.PlayerId=3;w.Fluids.OnFuelIntent(competing,3);
            Assert.Single(w.Session.Sent);Assert.Equal(8,w.Source);Assert.Equal(22,w.Tank);
            w.Fluids.OnFuelIntent(Request(6),2);Assert.Equal(2,w.Session.Sent.Count);
            Assert.Equal(6,w.Source);Assert.Equal(24,w.Tank);
        }
        [Fact]
        public void HostInputUsesExactlyTheSameRequestPath()
        {
            var w=new World(true);w.Can.LocallyOwned=w.Can.Held=true;w.Can.RemoteOwner=255;
            w.Fluids.RequestFuelTransfer(10,20,2);
            Assert.Single(w.Session.Sent);Assert.Equal(8,w.Source);Assert.Equal(22,w.Tank);
        }
        [Fact]
        public void PendingResultWaitsForRealBindingsAndDoesNotReplayOlderLevels()
        {
            var w=new World(false);w.Items.Items.Remove(10);
            var r=new ContainerFuelResult { SourceId=10,VehicleId=20,PlayerId=2,Sequence=1,Revision=1,
                AcceptedAmount=2,SourceLevel=8,DestinationLevel=22 };
            w.Fluids.OnFuelResult(r);Assert.Equal(20,w.Tank);
            w.Items.Items.Add(10,w.Can);w.Fluids.Update(w.Session);
            Assert.Equal(8,w.Source);Assert.Equal(22,w.Tank);
            r.Revision=2;r.SourceLevel=6;r.DestinationLevel=24;w.Fluids.OnFuelResult(r);
            w.Fluids.OnFuelResult(new ContainerFuelResult { SourceId=10,VehicleId=20,PlayerId=2,Sequence=1,Revision=1,
                AcceptedAmount=2,SourceLevel=8,DestinationLevel=22 });
            Assert.Equal(6,w.Source);Assert.Equal(24,w.Tank);
        }
        [Fact]
        public void DriverWithCurrentBarrierCanConsumeFuelButStaleDriverCannotOverwrite()
        {
            var w=new World(true);w.Fluids.OnFuelIntent(Request(),2);
            w.Car.AcceptedVehicleState=new VehicleState { FuelRevision=0,FuelLiters=0,FuelLevel=0 };
            w.Car.RemoteFuelLevel=0;
            Assert.True(VehicleWorldSync.ReceiveContainerTank(w.Car,new VehicleState { FuelRevision=0,FuelLiters=2 },true));
            Assert.Equal(22,w.Tank);
            Assert.Equal(1u,w.Car.AcceptedVehicleState.FuelRevision);
            Assert.Equal(22,w.Car.AcceptedVehicleState.FuelLiters);
            Assert.NotEqual((byte)0,w.Car.RemoteFuelLevel);
            Assert.True(VehicleWorldSync.ReceiveContainerTank(w.Car,new VehicleState { FuelRevision=1,FuelLiters=21.75f },true));
            Assert.Equal(21.75f,w.Tank);
        }
        [Fact]
        public void HeldReparentingDoesNotLoseTheSavedFluidBinding()
        {
            var w=new World(false);w.Can.FluidLevelVar=null;w.Can.NextFluidProbeAt=0;
            foreach(var fsm in w.Can.Body.Fsms) fsm.transform.Path="PLAYER/hand/"+fsm.transform.Path;
            Assert.Single(w.Fluids.BuildSnapshots(0));Assert.Equal(10,w.Source);
        }

        [Fact]
        public void SessionClearAllowsAFreshHostSnapshotWithoutWritingNativeSaveValues()
        {
            var w=new World(false);
            w.Fluids.OnRemoteState(new FluidContainerState { ItemId=10,OwnerPlayerId=0,Sequence=100,Level=8,Capacity=20,FuelRevision=7 });
            w.Fluids.Clear(); Assert.Equal(8,w.Source); Assert.Equal(20,w.Tank);
            w.Fluids.OnRemoteState(new FluidContainerState { ItemId=10,OwnerPlayerId=0,Sequence=1,Level=12,Capacity=20,FuelRevision=0 });
            Assert.Equal(12,w.Source); Assert.Equal(0u,w.Can.FuelRevision);
        }

        [Fact]
        public void PresentationFailureCannotReplayCommittedFuelAndRejectedHighSequenceDoesNotPoisonHistory()
        {
            var w=new World(true);w.Car.GaugeFuelLevelVar!.FailNextSet=true;
            w.Fluids.OnFuelIntent(Request(1),2);Assert.Single(w.Session.Sent);
            w.Fluids.OnFuelIntent(Request(1),2);Assert.Single(w.Session.Sent);
            w.Fluids.OnFuelIntent(Request(100,0),2);Assert.Single(w.Session.Sent);
            w.Fluids.OnFuelIntent(Request(2),2);Assert.Equal(2,w.Session.Sent.Count);
            Assert.Equal(6,w.Source);Assert.Equal(24,w.Tank);
        }

        [Theory]
        [InlineData("local-drive")] [InlineData("remote-engine")] [InlineData("remote-acc")]
        public void RemainingParkedFlagsDenyThenPermitWithoutSpendingSequence(string flag)
        {
            var w=new World(true);
            w.Car.LocalDriveActive=flag=="local-drive";
            w.Car.RemoteEngineOn=flag=="remote-engine";w.Car.RemoteAccOn=flag=="remote-acc";
            w.Fluids.OnFuelIntent(Request(),2);Assert.Empty(w.Session.Sent);
            Assert.Equal(10,w.Source);Assert.Equal(20,w.Tank);
            w.Car.LocalDriveActive=w.Car.RemoteEngineOn=w.Car.RemoteAccOn=false;
            w.Fluids.OnFuelIntent(Request(),2);Assert.Single(w.Session.Sent);
        }
    }
}
