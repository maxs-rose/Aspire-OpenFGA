using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.OpenFga.Events;
using Aspire.Hosting.OpenFga.Models;
using Microsoft.Extensions.Logging;
using OpenFga.Sdk.Client;
using OpenFga.Sdk.Client.Model;

namespace Aspire.Hosting.OpenFga;

public sealed class OpenFgaStoreResource(OpenFgaResource parent, string name)
    : Resource(name), IResourceWithParent<OpenFgaResource>, IValueProvider
{
    private readonly List<StoreClientCallback> _clientCallbacks = [];
    private readonly TaskCompletionSource _storeReadyTcs = new();
    private string _storeId = string.Empty;
    internal string AuthorizationModel { private get; set; } = string.Empty;
    internal TaskCompletionSource? AuthorizationModelReadyTcs { get; set; }

    public OpenFgaResource Parent { get; } = parent;

    public async ValueTask<string?> GetValueAsync(CancellationToken ct = new())
    {
        await _storeReadyTcs.Task.WaitAsync(ct).ConfigureAwait(false);

        return _storeId;
    }

    public async ValueTask<string> GetAuthorizationModel(CancellationToken ct = new())
    {
        if (AuthorizationModelReadyTcs is null)
            return AuthorizationModel;

        await AuthorizationModelReadyTcs.Task.WaitAsync(ct).ConfigureAwait(false);

        return AuthorizationModel;
    }

    internal async Task Initialize(InitializeResourceEvent ctx, CancellationToken ct)
    {
        var result = await CreateStore(ctx.Logger, ct);

        if (result)
        {
            Debug.Assert(_storeId is not null, "Store ID should not be null here");

            await ctx.Notifications.PublishUpdateAsync(this, snap => snap with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.Active, KnownResourceStateStyles.Success),
                Properties = [..snap.Properties, new ResourcePropertySnapshot(CustomResourceKnownProperties.Source, _storeId)]
            });

            await ctx.Eventing.PublishAsync(new StoreCreatedEvent(this, _storeId, ctx.Services), EventDispatchBehavior.NonBlockingConcurrent, ct);
        }
        else
        {
            await ctx.Notifications.PublishUpdateAsync(this, snap => snap with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error)
            });
        }
    }

    internal async Task<OpenFgaClient> StoreClient()
    {
        return new OpenFgaClient(new ClientConfiguration
        {
            ApiUrl = $"{await ReferenceExpression.Create($"{Parent.HttpEndpoint}").GetValueAsync(CancellationToken.None)}",
            StoreId = await GetValueAsync()
        });
    }

    private async Task<bool> CreateStore(ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Creating store");

        var reference = ReferenceExpression.Create($"{Parent.HttpEndpoint}");

        var client = new OpenFgaClient(new ClientConfiguration
        {
            ApiUrl = $"{await reference.GetValueAsync(ct)}"
        });

        try
        {
            _storeId = await GetOrCreateStore(client, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create container");
        }
        finally
        {
            _storeReadyTcs.SetResult();
        }

        return !string.IsNullOrEmpty(_storeId);
    }

    private async Task<string> GetOrCreateStore(OpenFgaClient client, ILogger logger, CancellationToken ct)
    {
        var result = await client.ListStores(new ClientListStoresRequest
        {
            Name = Name
        }, cancellationToken: ct);

        switch (result.Stores)
        {
            case { Count: 1 }:
            {
                logger.LogInformation("Found existing store {Name} with ID {Id}", Name, _storeId);

                return result.Stores[0].Id;
            }
            case { Count: > 1 }:
            {
                logger.LogWarning("Found multiple stores with name {Name}: {@Stores}", Name, result.Stores.Select(s => s.Id));

                var store = result.Stores[0];
                logger.LogWarning("Using store {Id}", store.Id);

                return store.Id;
            }
            case { Count: 0 }:
            {
                var response = await client.CreateStore(new ClientCreateStoreRequest { Name = Name }, cancellationToken: ct);

                logger.LogInformation("Created store {Name} with ID {Id}", response.Name, response.Id);

                return response.Id;
            }
            default:
                throw new InvalidOperationException();
        }
    }

    internal void AddClientCallback(StoreClientCallback callback)
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

        var context = new StoreClientContext(new OpenFgaClient(new ClientConfiguration
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