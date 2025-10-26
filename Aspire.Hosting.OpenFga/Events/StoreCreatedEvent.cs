using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;

namespace Aspire.Hosting.OpenFga.Events;

internal sealed class StoreCreatedEvent(OpenFgaStoreResource resource, string containerId, IServiceProvider serviceProvider) : IDistributedApplicationResourceEvent
{
    public string ContainerId { get; } = containerId;
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public IResource Resource { get; } = resource;
}

internal sealed record StoreModelWriteAnnotation(IResource Store, string ImportName) : IResourceAnnotation;