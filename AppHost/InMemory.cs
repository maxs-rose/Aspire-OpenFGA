using Aspire.Hosting.OpenFga;

namespace AppHost;

public static class InMemory
{
    public static void AddInMemoryFga(this IDistributedApplicationBuilder builder)
    {
        builder.AddOpenFga("openfga-inmemory")
            .WithPlayground()
            .AddStore("inmemory-test-store")
            .WithModelDefinition("inmemory-models", Path.Join(builder.AppHostDirectory, "Test"), "fga.mod");
    }
}