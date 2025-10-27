using Aspire.Hosting.OpenFga;

namespace AppHost;

public static class Postgres
{
    public static void AddPostgresFga(this IDistributedApplicationBuilder builder)
    {
        var database = builder.AddPostgres("postgres")
            .AddDatabase("pg-db");

        builder.AddOpenFga("openfga-portgres", 8084, 8085)
            .WithPlayground()
            .WithDatastore(database)
            .AddStore("postgres-test-store")
            .WithModelDefinition("postgres-models", Path.Join(builder.AppHostDirectory, "Test"), "fga.mod");
    }
}