using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.OpenFga.Sqlite;

public static class OpenFgaBuilderSqlite
{
    private static async Task<FileInfo> GetFile(SqliteResource resource, CancellationToken ct)
    {
        var connectionString = await resource.ConnectionStringExpression.GetValueAsync(ct);

        if (connectionString is null)
            throw new InvalidOperationException("Cannot get file path from null connection string");

        var filePath = connectionString.Split(";")
            .Single(p => p.StartsWith("Data Source="))
            .Split("=")
            .Last();

        return new FileInfo(filePath);
    }

    extension(IResourceBuilder<OpenFgaResource> builder)
    {
        public IResourceBuilder<OpenFgaResource> WithDatastore(IResourceBuilder<SqliteResource> database)
        {
            return builder.WithDatastore(database, static _ => { });
        }

        public IResourceBuilder<OpenFgaResource> WithDatastore(IResourceBuilder<SqliteResource> database,
            Action<IResourceBuilder<OpenFgaDatastoreResource>> configureDatastore)
        {
            var datastore = OpenFgaDatastoreResource.CreateDatastore(builder)
                .WaitFor(database)
                .WithArgs("migrate")
                .WithEnvironment("OPENFGA_DATASTORE_ENGINE", "sqlite")
                .WithEnvironment(async ctx =>
                {
                    var file = await GetFile(database.Resource, ctx.CancellationToken);

                    ctx.EnvironmentVariables["OPENFGA_DATASTORE_URI"] = $"file:/database/{file.Name}";
                })
                .OnInitializeResource(async (res, _, ct) =>
                {
                    var file = await GetFile(database.Resource, ct);

                    res.Annotations.Add(new ContainerMountAnnotation(file.DirectoryName, "/database", ContainerMountType.BindMount, false));
                });

            builder.WaitForCompletion(datastore)
                .WithReferenceRelationship(database)
                .WithEnvironment("OPENFGA_DATASTORE_ENGINE", "sqlite")
                .WithEnvironment(async ctx =>
                {
                    var file = await GetFile(database.Resource, ctx.CancellationToken);

                    ctx.EnvironmentVariables["OPENFGA_DATASTORE_URI"] = $"file:/database/{file.Name}";
                })
                .OnInitializeResource(async (res, _, ct) =>
                {
                    var file = await GetFile(database.Resource, ct);

                    res.Annotations.Add(new ContainerMountAnnotation(file.DirectoryName, "/database", ContainerMountType.BindMount, false));
                });

            configureDatastore(datastore);

            return builder;
        }
    }
}