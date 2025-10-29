using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.OpenFga;

public static class OpenFgaBuilderPostgres
{
    public static IResourceBuilder<OpenFgaResource> WithDatastore(this IResourceBuilder<OpenFgaResource> builder, IResourceBuilder<PostgresDatabaseResource> database)
    {
        return builder.WithDatastore(database, static _ => { });
    }

    public static IResourceBuilder<OpenFgaResource> WithDatastore(
        this IResourceBuilder<OpenFgaResource> builder,
        IResourceBuilder<PostgresDatabaseResource> database,
        Action<IResourceBuilder<OpenFgaDatastoreResource>> configureDatastore)
    {
        var datastore = OpenFgaDatastoreResource.CreateDatastore(builder)
            .WaitFor(database)
            .WithArgs("migrate")
            .WithEnvironment("OPENFGA_DATASTORE_ENGINE", "postgres")
            .WithEnvironment("OPENFGA_DATASTORE_USERNAME", database.Resource.Parent.UserNameReference)
            .WithEnvironment("OPENFGA_DATASTORE_PASSWORD", database.Resource.Parent.PasswordParameter)
            .WithEnvironment("OPENFGA_DATASTORE_URI",
                $"postgres://{database.Resource.Parent.PrimaryEndpoint.Property(EndpointProperty.HostAndPort)}/{database.Resource.DatabaseName}");

        builder.WaitForCompletion(datastore)
            .WithReferenceRelationship(database)
            .WithEnvironment("OPENFGA_DATASTORE_ENGINE", "postgres")
            .WithEnvironment("OPENFGA_DATASTORE_USERNAME", database.Resource.Parent.UserNameReference)
            .WithEnvironment("OPENFGA_DATASTORE_PASSWORD", database.Resource.Parent.PasswordParameter)
            .WithEnvironment("OPENFGA_DATASTORE_URI",
                $"postgres://{database.Resource.Parent.PrimaryEndpoint.Property(EndpointProperty.HostAndPort)}/{database.Resource.DatabaseName}");

        configureDatastore(datastore);

        return builder;
    }
}