using OpenFga.Sdk.Client;

namespace Aspire.Hosting.OpenFga.Models;

public delegate Task ContainerClientCallback(ContainerClientContext context, CancellationToken ct);

public sealed record ContainerClientContext(OpenFgaClient Client);

public delegate Task ResourceClientCallback(ContainerClientContext context, CancellationToken ct);

public sealed record ResourceClientContext(OpenFgaClient Client);