using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.OpenFga;

public static class OpenFgaBuilderMySql
{
    public static IResourceBuilder<OpenFgaResource> WithDatastore(this IResourceBuilder<OpenFgaResource> builder, IResourceBuilder<MySqlDatabaseResource> database)
    {
        var datastore = OpenFgaDatastoreResource.CreateDatastore(builder)
            .WaitFor(database)
            .WithArgs("migrate")
            .WithEnvironment("OPENFGA_DATASTORE_ENGINE", "mysql")
            .WithEnvironment("OPENFGA_DATASTORE_USERNAME", "root")
            .WithEnvironment("OPENFGA_DATASTORE_PASSWORD", database.Resource.Parent.PasswordParameter)
            .WithEnvironment("OPENFGA_DATASTORE_URI",
                $"tcp@({database.Resource.Parent.PrimaryEndpoint.Property(EndpointProperty.HostAndPort)})/{database.Resource.DatabaseName}?parseTime=true");

        builder.WaitForCompletion(datastore)
            .WithReferenceRelationship(database)
            .WithEnvironment("OPENFGA_DATASTORE_ENGINE", "mysql")
            .WithEnvironment("OPENFGA_DATASTORE_USERNAME", "root")
            .WithEnvironment("OPENFGA_DATASTORE_PASSWORD", database.Resource.Parent.PasswordParameter)
            .WithEnvironment("OPENFGA_DATASTORE_URI",
                $"tcp@({database.Resource.Parent.PrimaryEndpoint.Property(EndpointProperty.HostAndPort)})/{database.Resource.DatabaseName}?parseTime=true");

        return builder;
    }
}