using System;

namespace BackendApi.Features.FleetTracking.Telemetry
{
    /// <summary>
    /// Represents a single GPS track point from a rider.
    /// </summary>
    public record TrackPoint(string RiderId, double Lat, double Lng, DateTime Timestamp);
}
