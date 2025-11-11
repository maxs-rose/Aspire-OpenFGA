using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.OpenFga;

public static class OpenFgaBuilderMySql
{
    extension(IResourceBuilder<OpenFgaResource> builder)
    {
        public IResourceBuilder<OpenFgaResource> WithDatastore(IResourceBuilder<MySqlDatabaseResource> database)
        {
            return builder.WithDatastore(database, static _ => { });
        }

        public IResourceBuilder<OpenFgaResource> WithDatastore(IResourceBuilder<MySqlDatabaseResource> database,
            Action<IResourceBuilder<OpenFgaDatastoreResource>> configureDatastore)
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

            configureDatastore(datastore);

            return builder;
        }
    }
}