using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;

namespace Aspire.Hosting.OpenFga.Events;

internal sealed class StoreCreatedEvent(OpenFgaStoreResource resource, string containerId, IServiceProvider serviceProvider) : IDistributedApplicationResourceEvent
{
    public string ContainerId { get; } = containerId;
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public IResource Resource { get; } = resource;
}

internal sealed record StoreModelWriteAnnotation(OpenFgaStoreResource Store, string ImportName) : IResourceAnnotation;

internal sealed class AuthorizationModelWrittenEvent(OpenFgaStoreResource resource, string authorizationModel, IServiceProvider serviceProvider) : IDistributedApplicationResourceEvent
{
    public string AuthorizationModel { get; } = authorizationModel;
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public IResource Resource { get; } = resource;
}