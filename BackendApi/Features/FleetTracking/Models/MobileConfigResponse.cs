using System;

namespace BackendApi.Features.FleetTracking.Models
{
    public class MobileConfigResponse
    {
        public int IntervalSeconds { get; set; }
        public DateTime ServerTime { get; set; }
    }
}
