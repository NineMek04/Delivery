namespace BackendApi.Security.Models;

public static class AuthConstants
{
    public const string AccessTokenCookieName = "access_token";
    public const string AdminRole = "Admin";
    public const string DispatcherRole = "Dispatcher";
    public const string RiderRole = "Rider";
    public const string CustomerRole = "Customer";
    public const string StorePartnerRole = "StorePartner";
    
    public const string AdminPolicy = "AdminOnly";
    public const string OperationsPolicy = "Operations";
    public const string RiderPolicy = "Rider";
}

