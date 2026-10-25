using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DotSeeReports.Services;

public class ApiSettings
{
    public TeamworkSettings Teamwork { get; set; } = new();
    public ProductiveSettings Productive { get; set; } = new();
}

public class TeamworkSettings
{
    public string Host { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public class ProductiveSettings
{
    public string Host { get; set; } = "api.productive.io";
    public string OrganizationId { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

// Shapes match what the page scripts expect (serialized as camelCase).
public record TeamworkEntry(int PersonId, string DateStr, int Minutes, bool Billable);
public record ProductiveEntry(int? PersonId, string DateStr, int BillableMinutes, int NonBillableMinutes);

public class TimelogService
{
    private const int MaxPages = 5;

    private readonly HttpClient _http;
    private readonly ApiSettings _settings;

    public TimelogService(HttpClient http, IOptions<ApiSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    // Fetches every time entry in the (inclusive) date range. Person filtering is
    // left to the caller/page, matching the original client-side behaviour.
    public async Task<List<TeamworkEntry>> GetTeamworkAsync(string fromIso, string toIso, CancellationToken ct = default)
    {
        var tw = _settings.Teamwork;
        // No key configured → skip this source silently (caller carries on with the other).
        if (string.IsNullOrWhiteSpace(tw.ApiKey))
            return new List<TeamworkEntry>();
        if (string.IsNullOrWhiteSpace(tw.Host))
            throw new InvalidOperationException("Teamwork host is not configured (ApiSettings:Teamwork:Host).");

        var fromdate = fromIso.Replace("-", "");
        var todate = toIso.Replace("-", "");
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(tw.ApiKey + ":X"));

        var result = new List<TeamworkEntry>();
        var page = 1;
        while (true)
        {
            var url = $"https://{tw.Host}/time_entries.json?fromdate={fromdate}&todate={todate}&pageSize=500&page={page}&sortby=date";
            using var resp = await SendWithRetryAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return req;
            }, ct);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Teamwork API returned {(int)resp.StatusCode} ({resp.ReasonPhrase}).");

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("time-entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in entries.EnumerateArray())
                {
                    var personId = GetInt(e, "person-id");
                    var date = e.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                    var dateStr = date.Length >= 10 ? date[..10] : date;
                    var minutes = GetInt(e, "hours") * 60 + GetInt(e, "minutes");
                    var billable = e.TryGetProperty("isbillable", out var b) && b.GetString() == "1";
                    result.Add(new TeamworkEntry(personId, dateStr, minutes, billable));
                }
            }

            var totalPages = 1;
            if (resp.Headers.TryGetValues("x-pages", out var pageValues)
                && int.TryParse(pageValues.FirstOrDefault(), out var tp) && tp > 0)
                totalPages = tp;

            if (page >= totalPages || page >= MaxPages) break;
            page++;
        }

        return result;
    }

    // Fetches Productive time entries for the given persons (comma-separated ids) in the range.
    public async Task<List<ProductiveEntry>> GetProductiveAsync(string fromIso, string toIso, string? persons, CancellationToken ct = default)
    {
        var pr = _settings.Productive;
        // No key configured → skip this source silently (caller carries on with the other).
        if (string.IsNullOrWhiteSpace(pr.ApiKey))
            return new List<ProductiveEntry>();
        if (string.IsNullOrWhiteSpace(pr.OrganizationId))
            throw new InvalidOperationException("Productive organization id is not configured (ApiSettings:Productive:OrganizationId).");
        if (string.IsNullOrWhiteSpace(pr.Host))
            throw new InvalidOperationException("Productive host is not configured (ApiSettings:Productive:Host).");
        if (string.IsNullOrWhiteSpace(persons))
            return new List<ProductiveEntry>();

        var result = new List<ProductiveEntry>();
        var page = 1;
        while (true)
        {
            // include=person is required — otherwise the person relationship comes back
            // as {"meta":{"included":false}} with no id, so entries can't be attributed.
            var url = $"https://{pr.Host}/api/v2/time_entries"
                + "?filter[after]=" + Uri.EscapeDataString(fromIso)
                + "&filter[before]=" + Uri.EscapeDataString(toIso)
                + "&filter[person_id]=" + Uri.EscapeDataString(persons)
                + "&include=person"
                + "&page[size]=200&page[number]=" + page;

            using var resp = await SendWithRetryAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("X-Auth-Token", pr.ApiKey);
                req.Headers.Add("X-Organization-Id", pr.OrganizationId);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
                return req;
            }, ct);

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Productive API returned {(int)resp.StatusCode} ({resp.ReasonPhrase}).");

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in data.EnumerateArray())
                {
                    if (!e.TryGetProperty("attributes", out var attrs)) continue;
                    var time = GetInt(attrs, "time");
                    var billableTime = GetInt(attrs, "billable_time");
                    var dateStr = attrs.TryGetProperty("date", out var dd) ? dd.GetString() ?? "" : "";

                    int? personId = null;
                    if (e.TryGetProperty("relationships", out var rel)
                        && rel.TryGetProperty("person", out var person)
                        && person.TryGetProperty("data", out var pdata)
                        && pdata.ValueKind == JsonValueKind.Object
                        && pdata.TryGetProperty("id", out var pid)
                        && int.TryParse(pid.GetString(), out var v))
                        personId = v;

                    result.Add(new ProductiveEntry(personId, dateStr, billableTime, time - billableTime));
                }
            }

            var totalPages = 1;
            if (root.TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("total_pages", out var tpEl)
                && tpEl.ValueKind == JsonValueKind.Number)
                totalPages = tpEl.GetInt32();

            if (page >= totalPages || page >= MaxPages) break;
            page++;
        }

        return result;
    }

    // Retries only on 429 (rate limit), with backoff — mirrors the old client behaviour.
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct, int maxRetries = 2)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            using var request = requestFactory();
            response = await _http.SendAsync(request, ct);
            if ((int)response.StatusCode != 429) return response;
            if (attempt < maxRetries)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)), ct);
            }
        }
        return response!;
    }

    // Reads an integer that the source JSON may encode as either a number or a string.
    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p)) return 0;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out var i) ? i : (int)p.GetDouble(),
            JsonValueKind.String => int.TryParse(p.GetString(), out var s) ? s : 0,
            _ => 0
        };
    }
}
