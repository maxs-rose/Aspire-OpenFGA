using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.OpenFga.Models;

internal sealed class FgaHostUrl(string url) : IValueProvider
{
    private readonly HostUrl _hostUrl = new(url);


    public ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        return GetValueAsync(new ValueProviderContext(), cancellationToken);
    }

    public async ValueTask<string?> GetValueAsync(ValueProviderContext context, CancellationToken cancellationToken = default)
    {
        var result = await ((IValueProvider)_hostUrl).GetValueAsync(context, cancellationToken);

        if (result is null)
            return result;

        return result
            .Replace("http://", string.Empty)
            .Replace("https://", string.Empty);
    }
}