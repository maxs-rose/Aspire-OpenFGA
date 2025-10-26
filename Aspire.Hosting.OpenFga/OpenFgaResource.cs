using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.OpenFga.Models;
using Microsoft.Extensions.Logging;
using OpenFga.Sdk.Client;

namespace Aspire.Hosting.OpenFga;

public sealed class OpenFgaResource : ContainerResource
{
    private readonly List<ResourceClientCallback> _clientCallbacks = [];

    public OpenFgaResource(string name) : base(name)
    {
        HttpEndpoint = new EndpointReference(this, "http");
        GrpcEndpoint = new EndpointReference(this, "grpc");
    }

    public EndpointReference HttpEndpoint { get; }
    public EndpointReference GrpcEndpoint { get; }


    internal void AddClientCallback(ResourceClientCallback callback)
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
            ApiUrl = $"{await ReferenceExpression.Create($"{HttpEndpoint}").GetValueAsync(ct)}"
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