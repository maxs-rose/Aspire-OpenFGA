using OpenFga.Sdk.Client;

namespace Aspire.Hosting.OpenFga.Models;

public delegate Task StoreClientCallback(StoreClientContext context, CancellationToken ct);

public sealed record StoreClientContext(OpenFgaClient Client);

public delegate Task ResourceClientCallback(StoreClientContext context, CancellationToken ct);

public sealed record ResourceClientContext(OpenFgaClient Client);