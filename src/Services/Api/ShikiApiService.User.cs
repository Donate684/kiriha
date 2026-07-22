using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Services.Api;

public partial class ShikiApiService
{
    private async Task<int?> GetCurrentUserIdAsync(CancellationToken ct)
    {
        var response = await GetAsync("users/whoami", ct);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct);
        return doc.RootElement.GetProperty("id").GetInt32();
    }
}
