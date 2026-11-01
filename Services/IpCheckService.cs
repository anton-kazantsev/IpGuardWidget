using System.Net.Http;
using System.Text.Json;

namespace IpGuard;

public sealed record GeoResult(
    string Ip,
    string Family,
    string CountryCode,
    string CountryName,
    string City,
    string Source);

public sealed class IpCheckService
{
    /// <summary>
    /// A fresh client per check with no connection reuse: when a VPN is toggled, pooled
    /// TCP connections keep using the old route and the widget would see the old IP forever.
    /// </summary>
    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IpGuardWidget/1.0 (Windows 10/11)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.ConnectionClose = true;
        return client;
    }

    public async Task<GeoResult> CheckAsync(string? simulate, GeoResult? previous, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(simulate))
        {
            await Task.Delay(650, ct).ConfigureAwait(false);
            return simulate.Trim().ToUpperInvariant() switch
            {
                "RU" => new GeoResult("95.163.248.14", "IPv4", "RU", "Russia", "Moscow", "simulate"),
                "NL" => new GeoResult("185.42.12.87", "IPv4", "NL", "Netherlands", "Amsterdam", "simulate"),
                "DE" => new GeoResult("89.163.243.17", "IPv4", "DE", "Germany", "Frankfurt am Main", "simulate"),
                "US" => new GeoResult("23.22.19.44", "IPv4", "US", "United States", "New York", "simulate"),
                "IPV6" => new GeoResult("2a02:8071:50:4a80::1", "IPv6", "DE", "Germany", "Berlin", "simulate"),
                "SLOW" => await SlowAsync(ct),
                "OFFLINE" => throw new HttpRequestException("simulate: network is unreachable"),
                _ => new GeoResult("185.42.12.87", "IPv4", "NL", "Netherlands", "Amsterdam", "simulate")
            };
        }

        using var client = CreateClient();
        var (ip, family) = await GetIpAsync(client, ct).ConfigureAwait(false);

        // The country can only change together with the IP: skip the geo lookup while the
        // IP is the same (the widget re-checks every 20 s while blocked).
        if (previous is not null &&
            string.Equals(previous.Ip, ip, StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(previous.CountryCode))
        {
            return previous with { Source = "ip-only" };
        }

        var (code, name, city) = await GetGeoAsync(client, ip, ct).ConfigureAwait(false);
        return new GeoResult(ip, family, code, name, city, "api");
    }

    private static async Task<GeoResult> SlowAsync(CancellationToken ct)
    {
        await Task.Delay(3500, ct).ConfigureAwait(false);
        return new GeoResult("185.42.12.87", "IPv4", "NL", "Netherlands", "Amsterdam", "simulate");
    }

    private static async Task<(string Ip, string Family)> GetIpAsync(HttpClient client, CancellationToken ct)
    {
        foreach (var url in new[]
                 {
                     "https://api.ipify.org?format=json",
                     "https://api64.ipify.org?format=json"
                 })
        {
            try
            {
                var json = await client.GetStringAsync(url, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var ip = GetString(doc.RootElement, "ip");
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return (ip, ip.Contains(':') ? "IPv6" : "IPv4");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"{url}: {ex.Message}");
            }
        }

        try
        {
            var text = (await client.GetStringAsync("https://ifconfig.me/ip", ct).ConfigureAwait(false)).Trim();
            if (text.Length is > 3 and < 64 && (text.Contains('.') || text.Contains(':')))
            {
                return (text, text.Contains(':') ? "IPv6" : "IPv4");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("ifconfig.me: " + ex.Message);
        }

        throw new HttpRequestException("Не удалось получить публичный IP");
    }

    private static async Task<(string Code, string Name, string City)> GetGeoAsync(HttpClient client, string ip, CancellationToken ct)
    {
        try
        {
            var json = await client.GetStringAsync($"https://ipapi.co/{ip}/json/", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out _))
            {
                throw new InvalidOperationException("ipapi.co returned an error");
            }

            var code = GetString(doc.RootElement, "country_code");
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("ipapi.co returned no country");
            }

            return (code.ToUpperInvariant(),
                GetString(doc.RootElement, "country_name"),
                GetString(doc.RootElement, "city"));
        }
        catch (Exception ex)
        {
            Log.Warn("ipapi.co: " + ex.Message);
        }

        try
        {
            var json = await client.GetStringAsync($"https://ipwho.is/{ip}", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            {
                throw new InvalidOperationException("ipwho.is returned an error");
            }

            return (GetString(root, "country_code").ToUpperInvariant(),
                GetString(root, "country"),
                GetString(root, "city"));
        }
        catch (Exception ex)
        {
            Log.Warn("ipwho.is: " + ex.Message);
        }

        return ("", "", "");
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
