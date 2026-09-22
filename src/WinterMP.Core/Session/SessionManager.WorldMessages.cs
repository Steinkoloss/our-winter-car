using System;
using System.Collections.Generic;
using UnityEngine;
using WinterMP.Core;
using WinterMP.Core.Catalog;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using WinterMP.Net.Transport;

namespace WinterMP.Core.Session
{
    public sealed partial class SessionManager
    {
        /// <summary>World/economy half of <see cref="HandleMessage"/>; false = unhandled.</summary>
        private bool HandleWorldMessage(PeerId peer, IMessage message)
        {
            switch (message)
            {
                case UtilityPaymentIntent utilityPayment when IsHost:
                    if (IsPeerPlayer(peer, utilityPayment.PlayerId)) Sync.WorldSyncManager.Instance?.OnUtilityPayment(utilityPayment);
                    break;
                case UtilityPaymentResult utilityReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnUtilityPaymentResult(utilityReceipt);
                    break;
                case UtilityBillState utilityBillState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteUtilityBillState(utilityBillState);
                    break;

                case LottoTicketRequest ticketRequest when IsHost:
                    if (IsPeerPlayer(peer, ticketRequest.PlayerId)) Sync.WorldSyncManager.Instance?.OnLottoTicketRequest(ticketRequest);
                    break;
                case LottoTicketReceipt ticketReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnLottoTicketReceipt(ticketReceipt);
                    break;
                case BagState bagState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnBagState(bagState);
                    break;
                case BagOpenRequest bagRequest when IsHost:
                    if (TryGetPlayerId(peer, out byte bagActor) && bagActor == bagRequest.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHostBagOpen(bagRequest, bagActor);
                    break;
                case BagOpenReceipt bagReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnBagOpenReceipt(bagReceipt);
                    break;
                case MotorOilFillerState filler when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnMotorOilFillerState(filler); break;
                case MotorOilRefillIntent oilIntent when IsHost:
                    if(IsPeerPlayer(peer,oilIntent.PlayerId))Sync.WorldSyncManager.Instance?.OnMotorOilRefillIntent(oilIntent); break;
                case MotorOilBottleState motorOil when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnMotorOilState(motorOil); break;
                case AdvertJobState adJob when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnAdvertJob(adJob); break;
                case AdvertSheetState adSheet when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnAdvertSheet(adSheet); break;
                case AdvertPhoneIntent phoneIntent when IsHost:
                    if (TryGetPlayerId(peer, out byte phoneActor)) Sync.WorldSyncManager.Instance?.OnAdvertPhoneIntent(phoneIntent, phoneActor); break;
                case AdvertPhoneResult phoneResult when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnAdvertPhoneResult(phoneResult); break;
                case AdvertIntent adIntent when IsHost:
                    if (TryGetPlayerId(peer, out byte advertActor)) Sync.WorldSyncManager.Instance?.OnAdvertIntent(adIntent, advertActor); break;
                case BulbState bulbState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnBulbState(bulbState); break;
                case SupplyItemState supplyState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnSupplyState(supplyState);
                    break;

                case PackageState packageState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnPackageState(packageState);
                    break;

                case PartFitRequest fitRequest when IsHost:
                    if (TryGetPlayerId(peer, out byte fittingPlayerId))
                        Sync.WorldSyncManager.Instance?.OnHostPartFit(fitRequest, fittingPlayerId);
                    break;

                case PartFitReceipt fitReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnPartFitReceipt(fitReceipt);
                    break;

                case PackageOpenRequest openRequest when IsHost:
                    if (TryGetPlayerId(peer, out byte openingPlayerId) && openingPlayerId == openRequest.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHostPackageOpen(openRequest, openingPlayerId);
                    break;

                case PackageOpenReceipt openReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnPackageOpenReceipt(openReceipt);
                    break;

                case ReplacementPartState replacementState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnReplacementPartState(replacementState);
                    break;
                case WiringState wiringState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnWiringState(wiringState);
                    break;
                case WiringInstallRequest wireRequest when IsHost:
                    if (TryGetPlayerId(peer, out byte wireActor) && wireActor == wireRequest.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHostWireInstall(wireRequest, wireActor);
                    break;
                case WiringInstallReceipt wireReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnWireInstallReceipt(wireReceipt);
                    break;
                case VehicleCoolantState coolantState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnVehicleCoolantState(coolantState);
                    break;
                case HeaterState heaterState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnHeaterState(heaterState);
                    break;
                case GearboxState gearboxState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnGearboxState(gearboxState);
                    break;
                case VehicleDrivetrainWearState drivetrainWear when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnVehicleDrivetrainWearState(drivetrainWear);
                    break;
                case VehicleWheelHealthState wheelHealth when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnVehicleWheelHealthState(wheelHealth);
                    break;
                case EngineBlockState engineBlockState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnEngineBlockState(engineBlockState);
                    break;
                case BatteryState batteryState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnBatteryState(batteryState);
                    break;
                case WheelPunctureRequest puncture when IsHost:
                    if (TryGetPlayerId(peer, out byte punctureActor) && punctureActor == puncture.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHostWheelPuncture(puncture, punctureActor);
                    break;
                case GearboxWearRequest gearboxWear when IsHost:
                    if (TryGetPlayerId(peer, out byte gearboxActor) && gearboxActor == gearboxWear.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHostGearboxWear(gearboxWear, gearboxActor);
                    break;
                case GearboxOilUseRequest oilUse when IsHost:
                    if (TryGetPlayerId(peer, out byte oilActor) && oilActor == oilUse.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHostGearboxOilUse(oilUse, oilActor);
                    break;
                case StarterWearRequest starterWear when IsHost:
                    if (TryGetPlayerId(peer, out byte wearActor) && wearActor == starterWear.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHostStarterWear(starterWear, wearActor);
                    break;
                case StarterDrawRequest starterDraw when IsHost:
                    if (TryGetPlayerId(peer, out byte starterActor) && starterActor == starterDraw.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHostStarterDraw(starterDraw, starterActor);
                    break;
                case LottoTicketState ticketState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnLottoTicketState(ticketState);
                    break;

                case LottoDrawState lottoDrawState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteLottoDrawState(lottoDrawState);
                    break;

                case VenttiRequest venttiRequest when IsHost:
                    if (!IsPeerPlayer(peer, venttiRequest.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning($"Dropped VenttiRequest claiming player {venttiRequest.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnVenttiRequest(venttiRequest);
                    break;

                case VenttiLedgerState venttiState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnVenttiGameState(venttiState);
                    break;

                case VenttiSceneState venttiScene when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnVenttiSceneState(venttiScene);
                    break;

                case VenttiSoundCue venttiSound when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnVenttiSoundCue(venttiSound);
                    break;

                case VenttiReceipt venttiReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnVenttiReceipt(venttiReceipt);
                    break;

                case SlotMachineIntent slotIntent when IsHost:
                    if (IsPeerPlayer(peer, slotIntent.PlayerId))
                        Sync.WorldSyncManager.Instance?.OnSlotIntent(slotIntent);
                    break;
                case SlotMachineState slotState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnSlotState(slotState);
                    break;
                case SlotMachineResult slotResult when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnSlotResult(slotResult);
                    break;
                case PokerIntent pokerIntent when IsHost:
                    if (IsPeerPlayer(peer, pokerIntent.PlayerId)) Sync.WorldSyncManager.Instance?.OnPokerIntent(pokerIntent);
                    break;
                case PokerState pokerState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnPokerState(pokerState);
                    break;
                case PokerResult pokerResult when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnPokerResult(pokerResult);
                    break;

                case FluidContainerState fluidState when IsHost:
                    if (!IsPeerPlayer(peer, fluidState.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped FluidContainerState claiming player {fluidState.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestFluidContainerState(
                            fluidState, fluidState.OwnerPlayerId) == true)
                    {
                        Broadcast(fluidState, Channel.ReliableOrdered, except: peer);
                    }
                    else
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped unauthorized FluidContainerState {fluidState.ItemId:X8} from player {fluidState.OwnerPlayerId}.");
                    }
                    break;

                case FluidContainerState fluidState:
                    Sync.WorldSyncManager.Instance?.OnRemoteFluidContainerState(fluidState);
                    break;

                case BrewState brewState when IsHost:
                    if (!IsPeerPlayer(peer, brewState.OwnerPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped BrewState claiming player {brewState.OwnerPlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostGuestBrewState(brewState, brewState.OwnerPlayerId) == true)
                        Broadcast(brewState, Channel.ReliableOrdered, except: peer);
                    else
                        WinterMPPlugin.Log.LogWarning($"Dropped unauthorized BrewState {brewState.ItemId:X8} from player {brewState.OwnerPlayerId}.");
                    break;

                case BrewState brewState:
                    Sync.WorldSyncManager.Instance?.OnRemoteBrewState(brewState);
                    break;

                case WorldProgressState progressState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWorldProgressState(progressState);
                    break;

                case JobSiteState jobSiteState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteJobSiteState(jobSiteState);
                    break;

                case MailOrderState mailOrderState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteMailOrderState(mailOrderState);
                    break;

                case MailOrderIntent mailOrderIntent when IsHost:
                    if (!IsPeerPlayer(peer, mailOrderIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped MailOrderIntent claiming player {mailOrderIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostMailOrderIntent(mailOrderIntent);
                    break;

                case FleetariOrderState fleetariOrderState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteFleetariOrderState(fleetariOrderState);
                    break;

                case FleetariOrderIntent fleetariOrderIntent when IsHost:
                    if (!IsPeerPlayer(peer, fleetariOrderIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped FleetariOrderIntent claiming player {fleetariOrderIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostFleetariOrderIntent(fleetariOrderIntent);
                    break;

                case FleaListingState fleaListings when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnFleaListingState(fleaListings);
                    break;
                case FleaListingResult fleaListingReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnFleaListingResult(fleaListingReceipt);
                    break;
                case FleaListingIntent fleaListingIntent when IsHost:
                    if (IsPeerPlayer(peer, fleaListingIntent.PlayerId))
                        Sync.WorldSyncManager.Instance?.OnFleaListingIntent(fleaListingIntent);
                    break;
                case FleaSaleResult fleaReceipt when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnFleaSaleResult(fleaReceipt);
                    break;

                case FleaSaleState fleaSaleState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteFleaSaleState(fleaSaleState);
                    break;

                case TrainState trainState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnTrainState(trainState); break;
                case CoffeeState coffeeState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnCoffeeState(coffeeState); break;
                case CoffeeDrinkResult coffeeDrink when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnCoffeeDrink(coffeeDrink); break;
                case CoffeeIntent coffeeIntent when IsHost:
                    if (TryGetPlayerId(peer, out byte coffeeActor)) Sync.WorldSyncManager.Instance?.OnCoffeeIntent(coffeeIntent, coffeeActor);
                    break;
                case SausageState sausageState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnSausageState(sausageState); break;
                case SausageOpenIntent sausageOpen when IsHost:
                    if (TryGetPlayerId(peer, out byte sausageActor)) Sync.WorldSyncManager.Instance?.OnSausageOpen(sausageOpen, sausageActor);
                    break;
                case TractorTrailerState trailerState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnTractorTrailerState(trailerState); break;
                case TractorTrailerIntent trailerIntent when IsHost:
                    if (TryGetPlayerId(peer, out byte trailerActor)) Sync.WorldSyncManager.Instance?.OnTractorTrailerIntent(trailerIntent, trailerActor);
                    break;
                case TractorTrailerMotion trailerMotion:
                    if (!IsHost) Sync.WorldSyncManager.Instance?.OnTractorTrailerMotion(trailerMotion, 0);
                    else if (TryGetPlayerId(peer, out byte trailerOwner)) Sync.WorldSyncManager.Instance?.OnTractorTrailerMotion(trailerMotion, trailerOwner);
                    break;
                case HouseholdFuseState householdFuses when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnHouseholdFuseState(householdFuses); break;
                case HouseholdFuseIntent fuseIntent when IsHost:
                    if (TryGetPlayerId(peer, out byte fuseActor) && fuseActor == fuseIntent.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnHouseholdFuseIntent(fuseIntent, fuseActor);
                    break;
                case HouseholdFuseResult fuseResult when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnHouseholdFuseResult(fuseResult); break;
                case TaxiFareState taxiFare when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnTaxiFareState(taxiFare);
                    break;
                case TaxiFareIntent fareIntent when IsHost:
                    if (TryGetPlayerId(peer, out var fareActor))
                        Sync.WorldSyncManager.Instance?.OnTaxiFareIntent(fareIntent, fareActor);
                    break;
                case TaxiMeterState taxiMeter when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnTaxiMeterState(taxiMeter);
                    break;
                case TaxiMeterIntent meterIntent when IsHost:
                    if (TryGetPlayerId(peer, out byte meterActor) && meterActor == meterIntent.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnTaxiMeterIntent(meterIntent, meterActor);
                    break;
                case TaxiServiceState taxiService when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnTaxiServiceState(taxiService);
                    break;
                case TaxiCallIntent taxiCall when IsHost:
                    if (TryGetPlayerId(peer, out byte taxiActor) && taxiActor == taxiCall.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnTaxiCallIntent(taxiCall, taxiActor);
                    break;
                case TaxiPaydayReadIntent paydayRead when IsHost:
                    if (TryGetPlayerId(peer, out byte paydayActor) && paydayActor == paydayRead.PlayerId)
                        Sync.WorldSyncManager.Instance?.OnTaxiPaydayReadIntent(paydayRead, paydayActor);
                    break;
                case TaxiJobState taxiJobState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteTaxiJobState(taxiJobState);
                    break;

                case WorldScalarsState worldScalarsState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWorldScalarsState(worldScalarsState);
                    break;

                case HockeyBettingState hockeyBettingState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteHockeyBettingState(hockeyBettingState);
                    break;

                case StoveKnobIntent stoveKnob when IsHost:
                    if (IsPeerPlayer(peer, stoveKnob.PlayerId)) Sync.WorldSyncManager.Instance?.OnHostStoveKnob(stoveKnob);
                    break;
                case ApplianceFireReport applianceFireReport when IsHost:
                    if (!IsPeerPlayer(peer, applianceFireReport.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped ApplianceFireReport claiming player {applianceFireReport.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostApplianceFireReport(applianceFireReport);
                    break;

                case WelfareState welfareState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWelfareState(welfareState);
                    break;
                case DebtLetterState debtState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnDebtLetterState(debtState);
                    break;
                case DebtPaymentIntent debtPayment when IsHost:
                    if (IsPeerPlayer(peer, debtPayment.PlayerId)) Sync.WorldSyncManager.Instance?.OnDebtPayment(debtPayment);
                    break;
                case DebtPaymentResult debtResult when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnDebtPaymentResult(debtResult);
                    break;

                case HitchhikerState hitchhikerState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteHitchhikerState(hitchhikerState);
                    break;

                case WantedState wantedState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteWantedState(wantedState);
                    break;

                case JailState jailState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteJailState(jailState);
                    break;

                case JailState jailReport when IsHost:
                    if (!IsPeerPlayer(peer, jailReport.JailedPlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped JailState report claiming player {jailReport.JailedPlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostJailReport(jailReport);
                    break;

                case PursuitState pursuitState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePursuitState(pursuitState);
                    break;

                case RallyResultsState rallyResultsState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteRallyResultsState(rallyResultsState);
                    break;

                case JokkisRaceState jokkisRaceState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteJokkisRaceState(jokkisRaceState);
                    break;

                case ApplianceState applianceState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteApplianceState(applianceState);
                    break;

                case PhoneCallEvent phoneCallEvent when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePhoneCallEvent(phoneCallEvent);
                    break;

                case PissAreaState pissAreaState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePissAreaState(pissAreaState);
                    break;

                case CarRadioState carRadioState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteCarRadioState(carRadioState);
                    break;

                case CrimeReport crimeReport when IsHost:
                    if (!IsPeerPlayer(peer, crimeReport.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped CrimeReport claiming player {crimeReport.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostCrimeReport(crimeReport);
                    break;

                case FleaSaleIntent fleaSaleIntent when IsHost:
                    if (!IsPeerPlayer(peer, fleaSaleIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped FleaSaleIntent claiming player {fleaSaleIntent.PlayerId} from {peer}.");
                        break;
                    }
                    Sync.WorldSyncManager.Instance?.OnHostFleaSaleIntent(fleaSaleIntent);
                    break;

                case InspectionState inspectionState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteInspectionState(inspectionState);
                    break;

                case PoliceState remotePoliceState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePoliceState(remotePoliceState);
                    break;

                case HomeStereoIntent homeStereoIntent when IsHost:
                    if (!IsPeerPlayer(peer, homeStereoIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped HomeStereoIntent claiming player {homeStereoIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostHomeStereoIntent(homeStereoIntent, out var homeStereoState) == true)
                        Broadcast(homeStereoState, Channel.ReliableOrdered);
                    break;

                case HomeStereoState remoteHomeStereoState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteHomeStereoState(remoteHomeStereoState);
                    break;

                case RallyIntent rallyIntent when IsHost:
                    if (!IsPeerPlayer(peer, rallyIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped RallyIntent claiming player {rallyIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostRallyIntent(rallyIntent, out var rallyState) == true)
                        Broadcast(rallyState, Channel.ReliableOrdered);
                    break;

                case RallyState remoteRallyState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteRallyState(remoteRallyState);
                    break;

                case IceRaceIntent iceRaceIntent when IsHost:
                    if (!IsPeerPlayer(peer, iceRaceIntent.PlayerId))
                    {
                        WinterMPPlugin.Log.LogWarning(
                            $"Dropped IceRaceIntent claiming player {iceRaceIntent.PlayerId} from {peer}.");
                        break;
                    }
                    if (Sync.WorldSyncManager.Instance?.OnHostIceRaceIntent(iceRaceIntent, out var iceRaceState) == true)
                        Broadcast(iceRaceState, Channel.ReliableOrdered);
                    break;

                case IceRaceState remoteIceRaceState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceState(remoteIceRaceState);
                    break;

                case IceRaceEventState iceRaceEventState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceEventState(iceRaceEventState);
                    break;

                case IceRaceResultsState iceRaceResultsState when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteIceRaceResultsState(iceRaceResultsState);
                    break;

                case WorldSnapshotRequest snapshotRequest when IsHost:
                    HandleSnapshotRequest(peer, snapshotRequest);
                    break;

                case WorldStateChecksum checksum when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteStateChecksum(checksum);
                    break;

                case WorldResyncRequest resync when IsHost:
                    HandleResyncRequest(peer, resync);
                    break;

                case WorldObjectStateRequest objectRequest when IsHost:
                    HandleObjectStateRequest(peer, objectRequest);
                    break;

                case WorldDoorSnapshot doorSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteDoorSnapshot(doorSnapshot);
                    break;

                case WorldItemSnapshot itemSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemSnapshot(itemSnapshot);
                    break;

                case WorldBoltSnapshot boltSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteBoltSnapshot(boltSnapshot);
                    break;

                case WorldPartSnapshot partSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemotePartSnapshot(partSnapshot);
                    break;

                case WorldItemDespawnSnapshot despawnSnapshot when !IsHost:
                    Sync.WorldSyncManager.Instance?.OnRemoteItemDespawnSnapshot(despawnSnapshot);
                    break;

                default:
                    return false;
            }
            return true;
        }
    }
}
