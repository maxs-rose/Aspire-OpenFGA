using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.OpenFga.Events;
using Aspire.Hosting.OpenFga.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.OpenFga;

public static class OpenFgaBuilderExtensions
{
    public static IResourceBuilder<OpenFgaResource> AddOpenFga(this IDistributedApplicationBuilder builder, string name,
        int httpPort = 8080, int grpcPort = 8081, bool proxy = true)
    {
        var openFgaResource = new OpenFgaResource(name);

        var res = builder.AddResource(openFgaResource)
            .WithImage("openfga/openfga", "latest")
            .WithHttpEndpoint(name: "http", targetPort: httpPort, port: proxy ? null : httpPort, isProxied: proxy)
            .WithHttpEndpoint(name: "grpc", targetPort: grpcPort, port: proxy ? null : grpcPort, isProxied: proxy)
            .WithEnvironment("OPENFGA_GRPC_ADDR", $"[::]:{grpcPort}".ToString)
            .WithEnvironment("OPENFGA_HTTP_ADDR", $"0.0.0.0:{httpPort}".ToString())
            .WithArgs("run", "--playground-enabled=false")
            .WithIconName("LockClosedRibbon");

        res.ApplicationBuilder.Eventing.Subscribe<ResourceReadyEvent>(res.Resource, static (ctx, ct) =>
        {
            var logger = ctx.Services.GetRequiredService<ResourceLoggerService>().GetLogger(ctx.Resource);

            return ((OpenFgaResource)ctx.Resource).RunClientCallbacks(logger, ct);
        });

        return res;
    }

    public static IResourceBuilder<OpenFgaResource> WithPlayground(this IResourceBuilder<OpenFgaResource> builder)
    {
        builder.Resource.Annotations.OfType<EndpointAnnotation>().Single(e => e.Name == "http").IsProxied = false;

        return builder.WithArgs("--playground-enabled", "--playground-port", "3001")
            .WithArgs(ctx => ctx.Args.Remove("--playground-enabled=false"))
            .WithHttpEndpoint(name: "playground", targetPort: 3001)
            .WithUrlForEndpoint("playground", x =>
            {
                x.Url = "/playground";
                x.DisplayText = "🛝 Playground";
            });
    }

    public static IResourceBuilder<OpenFgaResource> WithClientCallback(this IResourceBuilder<OpenFgaResource> builder,
        ResourceClientCallback callback)
    {
        builder.Resource.AddClientCallback(callback);
        return builder;
    }

    public static IResourceBuilder<OpenFgaDatastoreResource> WithDatastore(
        this IResourceBuilder<OpenFgaResource> builder,
        string engine,
        in ReferenceExpression.ExpressionInterpolatedStringHandler datastoreUri)
    {
        return OpenFgaDatastoreResource.CreateDatastore(builder, engine, datastoreUri);
    }

    public static IResourceBuilder<OpenFgaContainerResource> AddContainer(
        this IResourceBuilder<OpenFgaResource> builder, string name)
    {
        var container = new OpenFgaContainerResource(builder.Resource, name);

        var parentReady = new TaskCompletionSource();

        builder.ApplicationBuilder.Eventing.Subscribe<ResourceReadyEvent>(builder.Resource, (_, ct) =>
        {
            if (ct.IsCancellationRequested)
            {
                parentReady.SetCanceled(ct);
                return Task.CompletedTask;
            }

            parentReady.SetResult();
            return Task.CompletedTask;
        });

        builder.ApplicationBuilder.Eventing.Subscribe<ContainerCreatedEvent>(container, (e, ct) =>
        {
            if (e.Resource is OpenFgaContainerResource containerResource)
                return containerResource.RunClientCallbacks(
                    e.ServiceProvider.GetRequiredService<ResourceLoggerService>().GetLogger(e.Resource), ct);

            return Task.CompletedTask;
        });

        return builder.ApplicationBuilder.AddResource(container)
            .WithIconName("Database")
            .WithInitialState(new CustomResourceSnapshot
            {
                State = new ResourceStateSnapshot(KnownResourceStates.Waiting, KnownResourceStateStyles.Info),
                ResourceType = "OpenFgaStore",
                Properties = []
            })
            .OnInitializeResource(async (r, ctx, ct) =>
            {
                await parentReady.Task.WaitAsync(ct).ConfigureAwait(false);

                await r.Initialize(ctx, ct);
            });
    }

    public static IResourceBuilder<OpenFgaContainerResource> WithClientCallback(
        this IResourceBuilder<OpenFgaContainerResource> builder, ContainerClientCallback callback)
    {
        builder.Resource.AddClientCallback(callback);
        return builder;
    }

    public static IResourceBuilder<OpenFgaContainerResource> WithModelDefinition(
        this IResourceBuilder<OpenFgaContainerResource> builder, string name, string pathToDefinition, string modelFile)
    {
        var annotation = new StoreModelWriteAnnotation(builder.Resource, name);

        var res = builder.ApplicationBuilder.AddContainer(name, "openfga/cli", "latest")
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

    public static IResourceBuilder<T> WithEnvironment<T>(this IResourceBuilder<T> builder, string name,
        IResourceBuilder<OpenFgaContainerResource> container)
        where T : IResourceWithEnvironment
    {
        builder.WithReferenceRelationship(container);
        return builder.WithEnvironment(ctx => ctx.EnvironmentVariables[name] = container.Resource);
    }
}