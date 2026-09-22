using WinterMP.Net.Messages;

namespace WinterMP.Net
{
    /// <summary>Admission rules shared by session transports before a decoded message reaches game state.</summary>
    public static class SessionMessagePolicy
    {
        /// <summary>
        /// Hosts may receive an initial handshake from a transport-connected peer;
        /// every other message requires the peer's completed handshake. Before a
        /// guest accepts its handshake response, its selected host may not mutate
        /// local world state.
        /// </summary>
        public static bool IsSenderAllowed(MessageId messageId, bool receiverIsHost,
            bool senderIsAuthenticated, bool senderIsSelectedHost, bool receiverHandshakeComplete)
        {
            if (messageId == MessageId.PissAreaIntent) return receiverIsHost && senderIsAuthenticated;
            if (messageId == MessageId.SaunaTimerIntent) return receiverIsHost && senderIsAuthenticated;
            if (messageId == MessageId.SaunaTimerState) return !receiverIsHost && senderIsSelectedHost && receiverHandshakeComplete;
            if (messageId == MessageId.PissAreaState) return !receiverIsHost && senderIsSelectedHost && receiverHandshakeComplete;
            if (messageId == MessageId.ContainerFuelIntent) return receiverIsHost && senderIsAuthenticated;
            if (messageId == MessageId.ContainerFuelResult) return !receiverIsHost && senderIsSelectedHost && receiverHandshakeComplete;
            if (messageId == MessageId.BeerCaseExtractIntent || messageId == MessageId.ScraperAction || messageId == MessageId.WoodstoveFeedIntent || messageId == MessageId.VendorCoffeeIntent)
                return receiverIsHost && senderIsAuthenticated;
            if (messageId == MessageId.BeerCaseUpdate || messageId == MessageId.PaneScrapeUpdate || messageId == MessageId.WoodstoveFuelUpdate || messageId == MessageId.VendorCoffeeState || messageId == MessageId.VendorCoffeeResult)
                return !receiverIsHost && senderIsSelectedHost && receiverHandshakeComplete;
            if (receiverIsHost)
                return messageId != MessageId.AdvertPhoneResult && messageId != MessageId.MotorOilFillerState && messageId != MessageId.MotorOilBottleState && messageId != MessageId.AdvertJobState && messageId != MessageId.AdvertSheetState && messageId != MessageId.TrainState && messageId != MessageId.CoffeeState && messageId != MessageId.CoffeeDrinkResult && messageId != MessageId.SausageState && messageId != MessageId.TractorTrailerState && messageId != MessageId.HouseholdFuseState && messageId != MessageId.HouseholdFuseResult && messageId != MessageId.TaxiFareState && messageId != MessageId.TaxiMeterState && messageId != MessageId.TaxiServiceState && messageId != MessageId.WiringInstallReceipt && messageId != MessageId.ApplianceState && messageId != MessageId.VehicleDamage && messageId != MessageId.WiringState && messageId != MessageId.BatteryState && messageId != MessageId.EngineBlockState && messageId != MessageId.GearboxState && messageId != MessageId.VehicleCoolantState && messageId != MessageId.HeaterState && messageId != MessageId.VehicleConditionReleaseAck
                    && messageId != MessageId.VehicleDrivetrainWearState
                    && messageId != MessageId.VehicleWheelHealthState
                    && messageId != MessageId.ValveAdjustmentState
                    && messageId != MessageId.FirewoodLoadState
                    && messageId != MessageId.FirewoodBuyerState
                    && messageId != MessageId.MooseMeatState
                    && messageId != MessageId.MooseCorpseState
                    && messageId != MessageId.MilkConditionState
                    && messageId != MessageId.SupplyItemState
                    && messageId != MessageId.AtfBottleState
                    && messageId != MessageId.AtfFillerState
                    && messageId != MessageId.UtilityBillState
                    && messageId != MessageId.FleaSaleResult
                    && messageId != MessageId.FleaListingState && messageId != MessageId.FleaListingResult
                    && messageId != MessageId.FleaSaleState
                    && messageId != MessageId.UtilityPaymentResult
                    && messageId != MessageId.SessionSettings
                    && messageId != MessageId.CylinderHeadState
                    && (messageId == MessageId.HandshakeRequest || senderIsAuthenticated);

            if (!senderIsSelectedHost || messageId == MessageId.AdvertPhoneIntent || messageId == MessageId.MotorOilRefillIntent || messageId == MessageId.AdvertIntent || messageId == MessageId.CoffeeIntent || messageId == MessageId.SausageOpenIntent || messageId == MessageId.TractorTrailerIntent || messageId == MessageId.HouseholdFuseIntent || messageId == MessageId.TaxiFareIntent || messageId == MessageId.TaxiMeterIntent || messageId == MessageId.TaxiCallIntent || messageId == MessageId.TaxiPaydayReadIntent || messageId == MessageId.WiringInstallRequest || messageId == MessageId.FleaListingIntent || messageId == MessageId.FleaSaleIntent || messageId == MessageId.AtfRefillIntent || messageId == MessageId.StoveKnobIntent || messageId == MessageId.WheelPunctureRequest || messageId == MessageId.FirewoodUnloadIntent || messageId == MessageId.MooseChopIntent || messageId == MessageId.UtilityPaymentIntent || messageId == MessageId.StarterDrawRequest || messageId == MessageId.StarterWearRequest || messageId == MessageId.GearboxOilUseRequest || messageId == MessageId.GearboxWearRequest) return false;
            return receiverHandshakeComplete || messageId == MessageId.HandshakeResponse;
        }

        /// <summary>Only the three transport channels defined by the protocol are valid.</summary>
        public static bool IsKnownChannel(Channel channel)
        {
            return channel == Channel.ReliableOrdered
                || channel == Channel.UnreliableSequenced
                || channel == Channel.ReliableBulk;
        }

        /// <summary>
        /// Channel contract for the current wire messages. Live transform streams may
        /// also use reliable ordered for their final/snapshot state; all other current
        /// messages are ordered events. ReliableBulk remains reserved until a message
        /// explicitly opts into it.
        /// </summary>
        public static bool IsChannelAllowed(MessageId messageId, Channel channel)
        {
            if (!IsKnownChannel(channel)) return false;
            if (messageId == MessageId.TractorTrailerMotion) return channel == Channel.UnreliableSequenced;

            switch (messageId)
            {
                case MessageId.PlayerTransform:
                    return channel == Channel.UnreliableSequenced;

                case MessageId.ItemTransform:
                case MessageId.VehicleState:
                case MessageId.VehicleClimate:
                case MessageId.VehicleCargo:
                case MessageId.NpcTransform:
                case MessageId.TrainState:
                case MessageId.VenttiSceneState:
                    return channel == Channel.ReliableOrdered || channel == Channel.UnreliableSequenced;

                default:
                    return channel == Channel.ReliableOrdered;
            }
        }

        /// <summary>Soft-resync requests must name at least one defined state group.</summary>
        public static bool IsValidResyncFlags(byte flags)
        {
            return flags != 0 && (flags & ~WorldResyncRequest.AllFlags) == 0;
        }
    }
}
