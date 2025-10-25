using Aspire.Hosting.OpenFga;

namespace AppHost;

public static class Postgres
{
    public static void AddPostgresFga(this IDistributedApplicationBuilder builder)
    {
        var database = builder.AddPostgres("postgres")
            .AddDatabase("pg-db");

        builder.AddOpenFga("openfga-portgres")
            .WithPlayground()
            .WithDatastore(database)
            .AddContainer("postgres-test-store")
            .WithModelDefinition("postgres-models", Path.Join(builder.AppHostDirectory, "Test"), "fga.mod");
    }
}