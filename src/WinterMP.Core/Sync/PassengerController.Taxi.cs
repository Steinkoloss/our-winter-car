using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    public sealed partial class PassengerController
    {
        private static bool IsTaxi(Rigidbody body)
        {
            SyncCatalog.EnsureLoaded();
            var data = SyncCatalog.TaxiPassengers;
            return data != null && ScenePath.Of(body.transform) == data["path"];
        }

        private static VehicleSeats? ResolveTaxiSeats(uint vehicleId, Rigidbody body)
        {
            var data = SyncCatalog.TaxiPassengers;
            if (data == null) return null;
            var root = body.transform;
            var drive = root.Find(data["driveTrigger"]);
            var driver = root.Find(data["driverMass"]);
            var customer = root.Find(data["customerMass"]);
            var tutorial = root.Find(data["tutorial"]);
            if (drive == null || driver == null || customer == null || tutorial == null) return null;

            // Unlike the Sorbet/Corris, MassPassenger is a REAR seat. Mirror the
            // driver point for shotgun and the fare's point for the rear-left seat.
            var front = root.InverseTransformPoint(driver.position);
            var rear = root.InverseTransformPoint(customer.position);
            front.x = -front.x;
            front.z += FrontSeatForwardOffset;
            rear.x = -rear.x;
            return new VehicleSeats
            {
                VehicleId = vehicleId, Body = body, DriveTrigger = drive,
                SeatLocal = new[] { front, Vector3.zero, rear },
                AvailableSeats = PassengerSeatPolicy.TaxiSeats,
                UnavailableWhileActive = tutorial,
            };
        }

        private static bool SeatAvailable(VehicleSeats vehicle, int seat) =>
            PassengerSeatPolicy.Available(vehicle.AvailableSeats, seat,
                vehicle.Body != null && vehicle.Body.gameObject.activeInHierarchy,
                vehicle.UnavailableWhileActive != null && vehicle.UnavailableWhileActive.gameObject.activeInHierarchy);
    }
}
