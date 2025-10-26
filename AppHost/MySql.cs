using Aspire.Hosting.OpenFga;

namespace AppHost;

public static class MySql
{
    public static void AddMySqlFga(this IDistributedApplicationBuilder builder)
    {
        var database = builder.AddMySql("mysql")
            .AddDatabase("mysql-db");

        builder.AddOpenFga("openfga-mysql")
            .WithPlayground()
            .WithDatastore(database)
            .AddStore("mysql-test-store")
            .WithModelDefinition("mysql-models", Path.Join(builder.AppHostDirectory, "Test"), "fga.mod");
    }
}