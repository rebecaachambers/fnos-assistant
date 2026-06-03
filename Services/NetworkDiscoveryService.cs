using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FnosAssistant.Models;

namespace FnosAssistant.Services;

public class NetworkDiscoveryService
{
    private static readonly int[] DefaultPorts = [80, 443];

    private readonly ConcurrentDictionary<string, DeviceInfo> _devices = new();
    private CancellationTokenSource? _cts;

    public event Action<DeviceInfo>? DeviceDiscovered;
    public event Action? DiscoveryCompleted;

    public static int[] LoadPorts()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("FnosAssistant.appsettings.json");
            if (stream != null)
            {
                using var reader = new System.IO.StreamReader(stream);
                var json = reader.ReadToEnd();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("scanPorts", out var arr))
                {
                    var ports = new List<int>();
                    foreach (var el in arr.EnumerateArray())
                        if (el.TryGetInt32(out var p)) ports.Add(p);
                    if (ports.Count > 0) return ports.ToArray();
                }
            }
        }
        catch { }
        return DefaultPorts;
    }

    public async Task StartDiscoveryAsync()
    {
        _cts = new CancellationTokenSource();
        _devices.Clear();

        await Task.WhenAll(
            DiscoverSsdpAsync(_cts.Token),
            DiscoverByHttpScanAsync(_cts.Token)
        );

        DiscoveryCompleted?.Invoke();
    }

    public void StopDiscovery() => _cts?.Cancel();

    // ---- SSDP ----

    private async Task DiscoverSsdpAsync(CancellationToken ct)
    {
        try
        {
            using var client = new UdpClient();
            client.EnableBroadcast = true;
            client.Client.ReceiveTimeout = 3000;
            client.Client.SendTimeout = 1000;
            var endpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);

            foreach (var st in new[] { "ssdp:all", "urn:schemas-upnp-org:device:NetworkStorage:1", "urn:schemas-upnp-org:device:Basic:1" })
            {
                if (ct.IsCancellationRequested) return;
                var req = $"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {st}\r\n\r\n";
                await client.SendAsync(Encoding.ASCII.GetBytes(req), req.Length, endpoint);
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { }
            }
            
            // Receive SSDP responses for a few seconds then stop
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { }
            _cts?.Cancel();
        }
        catch { }
    }

    private async Task ReceiveSsdpAsync(UdpClient client, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await client.ReceiveAsync(ct);
                var resp = Encoding.ASCII.GetString(result.Buffer);
                ProcessSsdpResponse(resp, result.RemoteEndPoint.Address.ToString());
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private void ProcessSsdpResponse(string response, string ip)
    {
        string? location = null, server = null;
        foreach (var line in response.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                location = line["LOCATION:".Length..].Trim();
            else if (line.StartsWith("SERVER:", StringComparison.OrdinalIgnoreCase))
                server = line["SERVER:".Length..].Trim();
        }

        if (location == null || !Uri.TryCreate(location, UriKind.Absolute, out var uri)) return;
        if (!IsFnosIndicator(server) && !IsFnosIndicator(location)) return;

        UpsertDevice(new DeviceInfo
        {
            IpAddress = ip,
            Port = uri.Port > 0 ? uri.Port : 80,
            Hostname = uri.Host,
            DeviceName = server ?? ip,
            DiscoverMethod = "SSDP",
            IsFnos = true
        });
    }

    // ---- HTTP Scan ----

    private async Task DiscoverByHttpScanAsync(CancellationToken ct)
    {
        var subnet = GetCurrentSubnet();
        if (subnet == null) return;

        var ports = LoadPorts();
        var semaphore = new SemaphoreSlim(200);
        var tasks = new List<Task>();

        for (int host = 1; host <= 255; host++)
        {
            if (ct.IsCancellationRequested) return;
            var ip = $"{subnet}.{host}";
            foreach (var port in ports)
            {
                await semaphore.WaitAsync(ct);
                var cIp = ip; var cP = port;
                tasks.Add(Task.Run(async () =>
                {
                    try { await ScanIpPortAsync(cIp, cP, ct); }
                    finally { semaphore.Release(); }
                }, ct));
            }
        }

        await Task.WhenAll(tasks);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(500) };
    }

    private async Task ScanIpPortAsync(string ip, int port, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient();
            var baseUrl = port == 443 ? $"https://{ip}" : $"http://{ip}:{port}";

            var response = await client.GetAsync(baseUrl, ct);
            if (!response.IsSuccessStatusCode) return;

            var html = await response.Content.ReadAsStringAsync(ct);
            if (!IsFnosHtml(html) && !IsFnosHeader(response)) return;

            var deviceName = ExtractTitle(html) ?? ip;
            var version = ExtractVersionFromHtml(html);

            if (string.IsNullOrEmpty(version))
                version = await TryFetchVersionSync(client, baseUrl, ct);

            UpsertDevice(new DeviceInfo
            {
                IpAddress = ip,
                Port = port,
                DeviceName = deviceName,
                FnosVersion = version ?? "",
                DiscoverMethod = "HTTP",
                IsFnos = true
            });
        }
        catch { }
    }

    private static async Task<string?> TryFetchVersionSync(HttpClient client, string baseUrl, CancellationToken ct)
    {
        foreach (var api in new[]
        {
            "/api/version", "/api/system/version", "/api/v1/system/version",
            "/api/info", "/api/system/info", "/api/v1/system/info",
            "/api/device/info", "/api/system/status", "/api/status",
            "/v2/api/version", "/v1/api/version", "/v1/settings/version"
        })
        {
            try
            {
                var resp = await client.GetAsync($"{baseUrl}{api}", ct);
                if (!resp.IsSuccessStatusCode) continue;
                var json = await resp.Content.ReadAsStringAsync();
                var ver = TryExtractVersionFromJson(json);
                if (!string.IsNullOrEmpty(ver)) return ver;
            }
            catch { }
        }
        return null;
    }

    // ---- Detection ----

    private static bool IsFnosIndicator(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lower = text.ToLowerInvariant();
        return lower.Contains("fnos") || lower.Contains("f.nos")
            || lower.Contains("飞牛") || lower.Contains("feiniu");
    }

    private static bool IsFnosHtml(string html)
    {
        var lower = html.ToLowerInvariant();
        return lower.Contains("fnos") || lower.Contains("飞牛")
            || lower.Contains("f.n os") || lower.Contains("fn os");
    }

    private static bool IsFnosHeader(HttpResponseMessage response)
    {
        foreach (var header in response.Headers)
            if (IsFnosIndicator($"{header.Key}:{string.Join(" ", header.Value)}")) return true;
        foreach (var header in response.Content.Headers)
            if (IsFnosIndicator($"{header.Key}:{string.Join(" ", header.Value)}")) return true;
        return IsFnosIndicator(response.Headers.Server?.ToString());
    }

    // ---- Version Extraction ----

    private static string? TryExtractVersionFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var versionKeys = new[]
            {
                "version", "Version", "firmware_version", "firmwareVersion",
                "os_version", "osVersion", "system_version", "systemVersion",
                "fnos_version", "fnosVersion", "currentVersion", "current_version",
                "app_version", "appVersion"
            };

            foreach (var key in versionKeys)
            {
                if (root.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
                {
                    var v = p.GetString();
                    if (!string.IsNullOrEmpty(v) && v.Length < 30) return v;
                }
            }

            foreach (var wrapper in new[] { "data", "result", "content", "info", "system" })
            {
                if (root.TryGetProperty(wrapper, out var inner) && inner.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in versionKeys)
                    {
                        if (inner.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
                        {
                            var v = p.GetString();
                            if (!string.IsNullOrEmpty(v) && v.Length < 30) return v;
                        }
                    }
                }
            }

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var v = prop.Value.GetString();
                    if (v != null && Regex.IsMatch(v, @"^\d+\.\d+") && v.Length < 30)
                        return v;
                }
            }
        }
        catch { }
        return null;
    }

    private static string ExtractVersionFromHtml(string html)
    {
        foreach (var pattern in new[]
        {
            @"版本[：:]\s*v?([\d]+\.[\d]+\.[\d]+)", @"version[:\s]*v?([\d]+\.[\d]+\.[\d]+)",
            @"FN.?OS[:\s]*v?([\d]+\.[\d]+\.[\d]+)", @"飞牛[^\d]*v?([\d]+\.[\d]+\.[\d]+)",
            @"v([\d]+\.[\d]+\.[\d]+)",
        })
        {
            var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
        }
        return string.Empty;
    }

    // ---- Helpers ----

    private static string? ExtractTitle(string html)
    {
        var idx = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += 7;
        var end = html.IndexOf("</title>", idx, StringComparison.OrdinalIgnoreCase);
        return end > idx ? html[idx..end].Trim() : null;
    }

    private static string? GetCurrentSubnet()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            var props = ni.GetIPProperties();
            if (props.GatewayAddresses.Count == 0) continue;

            foreach (var ip in props.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var parts = ip.Address.ToString().Split('.');
                    if (parts.Length == 4) return $"{parts[0]}.{parts[1]}.{parts[2]}";
                }
            }
        }

        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    var parts = ip.Address.ToString().Split('.');
                    if (parts.Length == 4) return $"{parts[0]}.{parts[1]}.{parts[2]}";
                }
            }
        }
        return null;
    }

    private bool UpsertDevice(DeviceInfo device)
    {
        var key = device.IpAddress;
        if (_devices.TryGetValue(key, out var existing))
        {
            if (!string.IsNullOrEmpty(device.DeviceName) && device.DeviceName != device.IpAddress)
                existing.DeviceName = device.DeviceName;
            if (!string.IsNullOrEmpty(device.FnosVersion) && string.IsNullOrEmpty(existing.FnosVersion))
                existing.FnosVersion = device.FnosVersion;
            if (device.Port != 0 && existing.Port != device.Port)
                existing.Port = device.Port;
            return false;
        }

        if (_devices.TryAdd(key, device))
        {
            DeviceDiscovered?.Invoke(device);
            return true;
        }
        return false;
    }
}
