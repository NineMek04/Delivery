using Xunit;

namespace BackendApi.IntegrationTests;

[CollectionDefinition("SharedTestDatabase")]
public class SharedTestCollection : ICollectionFixture<DeliveryWebApplicationFactory>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
