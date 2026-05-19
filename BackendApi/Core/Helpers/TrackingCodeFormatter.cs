namespace BackendApi.Core.Helpers
{
    public static class TrackingCodeFormatter
    {
        public static string Format(string prefix, long refNumber)
        {
            return $"{prefix}-{refNumber:D6}";
        }
    }
}
