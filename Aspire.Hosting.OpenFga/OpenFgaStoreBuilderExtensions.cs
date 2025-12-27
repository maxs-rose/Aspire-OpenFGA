using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
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

    public static IResourceBuilder<T> WithAuthorizationModelId<T>(this IResourceBuilder<T> builder, string name, IResourceBuilder<OpenFgaStoreResource> store)
        where T : IResourceWithEnvironment
    {
        return builder
            .WithReferenceRelationship(store)
            .WithEnvironment(async context => { context.EnvironmentVariables[name] = await store.Resource.GetAuthorizationModel(context.CancellationToken); });
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

            builder.Resource.AuthorizationModelReadyTcs = new TaskCompletionSource();
            builder.ApplicationBuilder.Eventing.Subscribe<AuthorizationModelWrittenEvent>(builder.Resource, (ctx, _) =>
            {
                var resource = (OpenFgaStoreResource)ctx.Resource;
                resource.AuthorizationModel = ctx.AuthorizationModel;
                resource.AuthorizationModelReadyTcs!.SetResult();

                return Task.CompletedTask;
            });
            builder.WithAnnotation(annotation);

            res.ApplicationBuilder.Eventing.Subscribe<ResourceStoppedEvent>(res.Resource, static async (ctx, _) =>
            {
                var annotation = ctx.Resource.Annotations.OfType<StoreModelWriteAnnotation>().Single();
                var logger = ctx.Services.GetRequiredService<ResourceLoggerService>().GetLogger(annotation.Store);

                if (ctx.ResourceEvent.Snapshot.ExitCode is not (null or 0))
                {
                    logger.LogError("Failed to import {Models} from file", annotation.ImportName);
                    return;
                }

                logger.LogInformation("Imported {Models} from file", annotation.ImportName);
                await ctx.Services.GetRequiredService<ResourceNotificationService>().PublishUpdateAsync(
                    ctx.Resource,
                    static snap => snap with
                    {
                        IsHidden = true
                    });

                var storeClient = await annotation.Store.StoreClient();

                var model = await storeClient.ReadLatestAuthorizationModel(cancellationToken: _);
                var modelId = model?.AuthorizationModel?.Id;

                Debug.Assert(modelId is not null, "Model ID should not be null here");

                await ctx.Services.GetRequiredService<IDistributedApplicationEventing>().PublishAsync(
                    new AuthorizationModelWrittenEvent(annotation.Store, modelId, ctx.Services),
                    _
                );
            });

            return builder;
        }
    }
}