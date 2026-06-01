using System;

namespace BackendApi.Features.FleetTracking.Models
{
    public class GpsBatchPointRequest
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Accuracy { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
