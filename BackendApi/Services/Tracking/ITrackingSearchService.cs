namespace BackendApi.Services.Tracking
{
    public interface ITrackingSearchService
    {
        long? ParseSearchQuery(string? query, string expectedPrefix);
    }
}
