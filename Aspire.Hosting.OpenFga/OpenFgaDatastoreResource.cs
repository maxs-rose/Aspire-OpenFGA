using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.OpenFga;

public sealed class OpenFgaDatastoreResource(string name) : ContainerResource(name)
{
    internal static IResourceBuilder<OpenFgaDatastoreResource> CreateDatastore(
        IResourceBuilder<OpenFgaResource> server,
        string engine,
        in ReferenceExpression.ExpressionInterpolatedStringHandler engineUri)
    {
        var datastore = CreateDatastore(server)
            .WithArgs("migrate")
            .WithEnvironment("OPENFGA_DATASTORE_ENGINE", engine)
            .WithEnvironment("OPENFGA_DATASTORE_URI", engineUri);

        server.WaitForCompletion(datastore)
            .WithEnvironment("OPENFGA_DATASTORE_ENGINE", engine)
            .WithEnvironment("OPENFGA_DATASTORE_URI", engineUri);

        return datastore;
    }

    internal static IResourceBuilder<OpenFgaDatastoreResource> CreateDatastore(IResourceBuilder<OpenFgaResource> server)
    {
        var serverImageAnnotation = server.Resource.Annotations.OfType<ContainerImageAnnotation>().Last();

        var datastore = server.ApplicationBuilder.AddResource(new OpenFgaDatastoreResource($"{server.Resource.Name}-engine-setup"))
            .WithParentRelationship(server)
            .WithImage(serverImageAnnotation.Image);

        if (serverImageAnnotation.Tag is not null)
            datastore.WithImageTag(serverImageAnnotation.Tag);

        datastore.ApplicationBuilder.Eventing.Subscribe<ResourceStoppedEvent>(datastore.Resource, static (ctx, _) =>
        {
            // Keep the migration resource visible if its not healthy after we finished
            if (ctx.ResourceEvent.Snapshot.ExitCode is not (null or 0))
                return Task.CompletedTask;

            return ctx.Services.GetRequiredService<ResourceNotificationService>().PublishUpdateAsync(
                ctx.Resource,
                static snap => snap with
                {
                    IsHidden = true
                });
        });

        return datastore;
    }
}