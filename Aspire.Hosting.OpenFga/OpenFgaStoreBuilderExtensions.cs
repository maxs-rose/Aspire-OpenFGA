using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.OpenFga.Events;
using Aspire.Hosting.OpenFga.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.OpenFga;

public static class OpenFgaStoreBuilderExtensions
{
    public static IResourceBuilder<T> WithEnvironment<T>(this IResourceBuilder<T> builder, string name,
        IResourceBuilder<OpenFgaStoreResource> store)
        where T : IResourceWithEnvironment
    {
        builder.WithReferenceRelationship(store);
        return builder.WithEnvironment(ctx => ctx.EnvironmentVariables[name] = store.Resource);
    }

    extension(IResourceBuilder<OpenFgaStoreResource> builder)
    {
        public IResourceBuilder<OpenFgaStoreResource> WithClientCallback(StoreClientCallback callback)
        {
            builder.Resource.AddClientCallback(callback);
            return builder;
        }

        public IResourceBuilder<OpenFgaStoreResource> WithModelDefinition(string name, string pathToDefinition, string modelFile)
        {
            var annotation = new StoreModelWriteAnnotation(builder.Resource, name);

            var res = builder.ApplicationBuilder.AddContainer(name, OpenFgaContainerImageTags.CliImage, OpenFgaContainerImageTags.CliTag)
                .WithImageRegistry(OpenFgaContainerImageTags.Registry)
                .WithEnvironment("FGA_STORE_ID", builder)
                .WithEnvironment("FGA_API_URL", builder.Resource.Parent.HttpEndpoint)
                .WithBindMount(pathToDefinition, "/schema", true)
                .WithArgs("model", "write", "--file", $"/schema/{modelFile}")
                .WithParentRelationship(builder)
                .WithAnnotation(annotation);

            builder.WithAnnotation(annotation);

            res.ApplicationBuilder.Eventing.Subscribe<ResourceStoppedEvent>(res.Resource, static (ctx, _) =>
            {
                var annotation = ctx.Resource.Annotations.OfType<StoreModelWriteAnnotation>().Single();
                var logger = ctx.Services.GetRequiredService<ResourceLoggerService>().GetLogger(annotation.Store);

                if (ctx.ResourceEvent.Snapshot.ExitCode is not (null or 0))
                {
                    logger.LogError("Failed to import {Models} from file", annotation.ImportName);
                    return Task.CompletedTask;
                }

                logger.LogInformation("Imported {Models} from file", annotation.ImportName);
                return ctx.Services.GetRequiredService<ResourceNotificationService>().PublishUpdateAsync(
                    ctx.Resource,
                    static snap => snap with
                    {
                        IsHidden = true
                    });
            });

            return builder;
        }
    }
}