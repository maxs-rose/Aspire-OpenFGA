using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.OpenFga.Events;
using Aspire.Hosting.OpenFga.Models;
using Microsoft.Extensions.Logging;
using OpenFga.Sdk.Client;
using OpenFga.Sdk.Client.Model;

namespace Aspire.Hosting.OpenFga;

public sealed class OpenFgaContainerResource(OpenFgaResource parent, string name)
    : Resource(name), IResourceWithParent<OpenFgaResource>, IValueProvider
{
    private readonly List<ContainerClientCallback> _clientCallbacks = [];
    private string _containerId = string.Empty;
    internal TaskCompletionSource StoreReadyTcs { get; } = new();

    public OpenFgaResource Parent { get; } = parent;

    public async ValueTask<string?> GetValueAsync(CancellationToken ct = new())
    {
        await StoreReadyTcs.Task.WaitAsync(ct).ConfigureAwait(false);

        return _containerId;
    }

    internal async Task Initialize(InitializeResourceEvent ctx, CancellationToken ct)
    {
        var result = await CreateContainer(ctx.Logger, ct);

        if (result)
        {
            Debug.Assert(_containerId is not null, "Container ID should not be null here");

            await ctx.Notifications.PublishUpdateAsync(this, snap => snap with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.Active, KnownResourceStateStyles.Success),
                Properties = [..snap.Properties, new ResourcePropertySnapshot(CustomResourceKnownProperties.Source, _containerId)]
            });

            await ctx.Eventing.PublishAsync(new ContainerCreatedEvent(this, _containerId, ctx.Services), EventDispatchBehavior.NonBlockingConcurrent, ct);
        }
        else
        {
            await ctx.Notifications.PublishUpdateAsync(this, snap => snap with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error)
            });
        }
    }

    private async Task<bool> CreateContainer(ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Creating container");

        var reference = ReferenceExpression.Create($"{Parent.HttpEndpoint}");

        var client = new OpenFgaClient(new ClientConfiguration
        {
            ApiUrl = $"{await reference.GetValueAsync(ct)}"
        });

        try
        {
            _containerId = await GetOrCreateContainer(client, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create container");
        }
        finally
        {
            StoreReadyTcs.SetResult();
        }

        return !string.IsNullOrEmpty(_containerId);
    }

    private async Task<string> GetOrCreateContainer(OpenFgaClient client, ILogger logger, CancellationToken ct)
    {
        var result = await client.ListStores(new ClientListStoresRequest
        {
            Name = Name
        }, cancellationToken: ct);

        switch (result.Stores)
        {
            case { Count: 1 }:
            {
                logger.LogInformation("Found existing container {Name} with ID {Id}", Name, _containerId);

                return result.Stores[0].Id;
            }
            case { Count: > 1 }:
            {
                logger.LogWarning("Found multiple containers with name {Name}: {@Stores}", Name, result.Stores.Select(s => s.Id));

                var store = result.Stores[0];
                logger.LogWarning("Using store {Id}", store.Id);

                return store.Id;
            }
            case { Count: 0 }:
            {
                var response = await client.CreateStore(new ClientCreateStoreRequest { Name = Name }, cancellationToken: ct);

                logger.LogInformation("Created container {Name} with ID {Id}", response.Name, response.Id);

                return response.Id;
            }
            default:
                throw new InvalidOperationException();
        }
    }

    internal void AddClientCallback(ContainerClientCallback callback)
    {
        _clientCallbacks.Add(callback);
    }

    internal async Task RunClientCallbacks(ILogger logger, CancellationToken ct)
    {
        if (_clientCallbacks.Count == 0)
        {
            logger.LogInformation("No client callbacks to run");
            return;
        }

        var context = new ContainerClientContext(new OpenFgaClient(new ClientConfiguration
        {
            ApiUrl = $"{await ReferenceExpression.Create($"{Parent.HttpEndpoint}").GetValueAsync(ct)}",
            StoreId = await GetValueAsync(ct)
        }));

        foreach (var callback in _clientCallbacks)
            try
            {
                await callback.Invoke(context, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to run client callback");
            }
    }
}