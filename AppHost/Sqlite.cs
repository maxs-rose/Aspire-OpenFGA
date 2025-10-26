using Aspire.Hosting.OpenFga;
using Aspire.Hosting.OpenFga.Sqlite;

namespace AppHost;

public static class Sqlite
{
    public static void AddSqliteFga(this IDistributedApplicationBuilder builder)
    {
        var sqlite = builder.AddSqlite("sqlite");

        builder.AddOpenFga("openfga-sqlite")
            .WithPlayground()
            .WithDatastore(sqlite)
            .AddStore("sqlite-test-store")
            .WithModelDefinition("sqlite-models", Path.Join(builder.AppHostDirectory, "Test"), "fga.mod");
    }
}