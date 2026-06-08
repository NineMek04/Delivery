using System;

namespace BackendApi.Core.Helpers;

public static class GeoMath
{
    public static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000; // Earth's radius in meters
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    public static double HaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        return HaversineDistanceMeters(lat1, lon1, lat2, lon2) / 1000.0;
    }

    private static double ToRadians(double val) => Math.PI / 180.0 * val;
}
