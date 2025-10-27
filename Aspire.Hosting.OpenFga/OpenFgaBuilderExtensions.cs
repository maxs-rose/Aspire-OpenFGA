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
            .WithImage(OpenFgaContainerImageTags.Image, OpenFgaContainerImageTags.Tag)
            .WithImageRegistry(OpenFgaContainerImageTags.Registry)
            .WithHttpEndpoint(name: "http", targetPort: httpPort, port: proxy ? null : httpPort, isProxied: proxy)
            .WithHttpEndpoint(name: "grpc", targetPort: grpcPort, port: proxy ? null : grpcPort, isProxied: proxy)
            .WithEnvironment("OPENFGA_GRPC_ADDR", $"[::]:{grpcPort}".ToString)
            .WithEnvironment("OPENFGA_HTTP_ADDR", $"0.0.0.0:{httpPort}".ToString())
            .WithArgs("run", "--playground-enabled=false", "--metrics-enabled=false")
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

    public static IResourceBuilder<OpenFgaResource> WithMetrics(this IResourceBuilder<OpenFgaResource> builder, int port = 2112, bool proxy = true)
    {
        return builder.WithHttpEndpoint(name: "metrics", targetPort: port, port: proxy ? null : port, isProxied: proxy)
            .WithEnvironment("OPENFGA_METRICS_ADDR", $"0.0.0.0:{port}".ToString())
            .WithArgs(ctx =>
            {
                ctx.Args.Remove("--metrics-enabled=false");
                ctx.Args.Add("--metrics-enabled=true");
            });
    }

    public static IResourceBuilder<OpenFgaDatastoreResource> WithDatastore(
        this IResourceBuilder<OpenFgaResource> builder,
        string engine,
        in ReferenceExpression.ExpressionInterpolatedStringHandler datastoreUri)
    {
        return OpenFgaDatastoreResource.CreateDatastore(builder, engine, datastoreUri);
    }

    public static IResourceBuilder<OpenFgaStoreResource> AddStore(
        this IResourceBuilder<OpenFgaResource> builder, string name)
    {
        var store = new OpenFgaStoreResource(builder.Resource, name);

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

        builder.ApplicationBuilder.Eventing.Subscribe<StoreCreatedEvent>(store, (e, ct) =>
        {
            if (e.Resource is OpenFgaStoreResource resource)
                return resource.RunClientCallbacks(
                    e.ServiceProvider.GetRequiredService<ResourceLoggerService>().GetLogger(e.Resource), ct);

            return Task.CompletedTask;
        });

        return builder.ApplicationBuilder.AddResource(store)
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

    public static IResourceBuilder<OpenFgaStoreResource> WithClientCallback(
        this IResourceBuilder<OpenFgaStoreResource> builder, StoreClientCallback callback)
    {
        builder.Resource.AddClientCallback(callback);
        return builder;
    }

    public static IResourceBuilder<OpenFgaStoreResource> WithModelDefinition(
        this IResourceBuilder<OpenFgaStoreResource> builder, string name, string pathToDefinition, string modelFile)
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

    public static IResourceBuilder<T> WithEnvironment<T>(this IResourceBuilder<T> builder, string name,
        IResourceBuilder<OpenFgaStoreResource> store)
        where T : IResourceWithEnvironment
    {
        builder.WithReferenceRelationship(store);
        return builder.WithEnvironment(ctx => ctx.EnvironmentVariables[name] = store.Resource);
    }
}