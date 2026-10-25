using System.Globalization;
using Microsoft.Extensions.Options;

namespace DotSeeReports.Services;

// Builds the report view models: fetches from Teamwork/Productive (via TimelogService),
// aggregates per member, and computes goals — so the Razor partials just render data.
public class ReportService
{
    private readonly TimelogService _timelogs;
    private readonly ReportSettings _report;
    private readonly ApiSettings _api;

    public ReportService(TimelogService timelogs, IOptions<ReportSettings> report, IOptions<ApiSettings> api)
    {
        _timelogs = timelogs;
        _report = report.Value;
        _api = api.Value;
    }

    private bool TeamworkEnabled => !string.IsNullOrWhiteSpace(_api.Teamwork.ApiKey);
    private bool ProductiveEnabled => !string.IsNullOrWhiteSpace(_api.Productive.ApiKey);
    private bool GoalBillable => _report.GoalBasis == GoalBasisKind.Billable;

    private string SourcesLabel()
    {
        var names = new List<string>();
        if (TeamworkEnabled) names.Add("Teamwork");
        if (ProductiveEnabled) names.Add("Productive");
        return names.Count > 0 ? string.Join(" + ", names) : "no data source configured";
    }

    private string PersonsCsv() =>
        string.Join(",", _report.Members.Where(m => m.ProductiveId != null).Select(m => m.ProductiveId!.Value));

    private static string LastName(string name)
    {
        var parts = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : "";
    }

    private IEnumerable<Member> ShownMembers(bool exclude) =>
        _report.Members
            .Where(m => !(exclude && m.IsManagement))
            .OrderBy(m => LastName(m.Name), StringComparer.OrdinalIgnoreCase);

    // teamworkId -> [twBillable, twNonBillable, prBillable, prNonBillable]
    private async Task<(Dictionary<int, int[]> per, List<string> warnings)> AggregateAsync(
        string fromIso, string toIso, HashSet<string>? dates, CancellationToken ct)
    {
        var per = new Dictionary<int, int[]>();
        foreach (var m in _report.Members) per[m.TeamworkId] = new int[4];
        var warnings = new List<string>();

        List<TeamworkEntry> tw = new();
        List<ProductiveEntry> pr = new();
        try { tw = await _timelogs.GetTeamworkAsync(fromIso, toIso, ct); }
        catch (Exception ex) { warnings.Add($"Teamwork: {ex.Message}"); }
        try { pr = await _timelogs.GetProductiveAsync(fromIso, toIso, PersonsCsv(), ct); }
        catch (Exception ex) { warnings.Add($"Productive: {ex.Message}"); }

        foreach (var e in tw)
        {
            if (dates != null && !dates.Contains(e.DateStr)) continue;
            if (!per.TryGetValue(e.PersonId, out var b)) continue;
            if (e.Billable) b[0] += e.Minutes; else b[1] += e.Minutes;
        }
        var byProductive = _report.Members
            .Where(m => m.ProductiveId != null)
            .ToDictionary(m => m.ProductiveId!.Value, m => m.TeamworkId);
        foreach (var e in pr)
        {
            if (dates != null && !dates.Contains(e.DateStr)) continue;
            if (e.PersonId == null || !byProductive.TryGetValue(e.PersonId.Value, out var twId)) continue;
            var b = per[twId]; b[2] += e.BillableMinutes; b[3] += e.NonBillableMinutes;
        }
        return (per, warnings);
    }

    public MembersPanel BuildMembersPanel() => new()
    {
        Members = ShownMembers(exclude: false).ToList(),
        TeamworkEnabled = TeamworkEnabled,
        ProductiveEnabled = ProductiveEnabled,
    };

    public async Task<LedgerResults> BuildLedgerAsync(string dateIso, double goalHours, bool exclude, CancellationToken ct)
    {
        var (per, warnings) = await AggregateAsync(dateIso, dateIso, null, ct);
        var dailyGoalMin = (int)Math.Round(goalHours * 60);

        var rows = new List<MemberRow>();
        int tb = 0, tn = 0, logged = 0, count = 0;
        foreach (var m in ShownMembers(exclude))
        {
            var b = per[m.TeamworkId];
            int bill = b[0] + b[2], non = b[1] + b[3];
            int forGoal = GoalBillable ? bill : bill + non;
            tb += bill; tn += non; logged += forGoal; count++;
            rows.Add(new MemberRow
            {
                Name = m.Name, IsManagement = m.IsManagement,
                BillableMinutes = bill, NonBillableMinutes = non,
                TeamworkBillable = b[0], TeamworkNonBillable = b[1],
                ProductiveBillable = b[2], ProductiveNonBillable = b[3],
                GoalMet = forGoal >= dailyGoalMin,
            });
        }

        return new LedgerResults
        {
            AnySourceConfigured = TeamworkEnabled || ProductiveEnabled,
            TeamworkEnabled = TeamworkEnabled, ProductiveEnabled = ProductiveEnabled,
            SourcesLabel = SourcesLabel(), Warnings = warnings, UpdatedAt = Now(),
            StampText = FormatDateStamp(dateIso),
            GoalBillable = GoalBillable, MemberCount = count,
            DailyGoalMinutes = dailyGoalMin, TotalGoalMinutes = dailyGoalMin * count, LoggedForGoalMinutes = logged,
            TotalBillable = tb, TotalNonBillable = tn,
            ShowGoals = _report.ShowGoals, Rows = rows,
        };
    }

    public async Task<BehindPaceResults> BuildBehindPaceAsync(string selectedDateIso, int days, double goalHours, bool exclude, CancellationToken ct)
    {
        if (days < 1) days = _report.WorkingDays;
        var working = WorkingDays(selectedDateIso, days);
        var set = new HashSet<string>(working);
        string oldest = working.Count > 0 ? working[^1] : selectedDateIso;
        string newest = working.Count > 0 ? working[0] : selectedDateIso;

        var (per, warnings) = await AggregateAsync(oldest, newest, set, ct);
        var dailyGoalMin = (int)Math.Round(goalHours * 60);
        var periodGoalMin = dailyGoalMin * days;

        var behind = new List<MemberRow>();
        int tb = 0, tn = 0, logged = 0, count = 0;
        foreach (var m in ShownMembers(exclude))
        {
            var b = per[m.TeamworkId];
            int bill = b[0] + b[2], non = b[1] + b[3];
            int forGoal = GoalBillable ? bill : bill + non;
            logged += forGoal; count++;
            if (forGoal < periodGoalMin)
            {
                behind.Add(new MemberRow
                {
                    Name = m.Name, IsManagement = m.IsManagement,
                    BillableMinutes = bill, NonBillableMinutes = non,
                    TeamworkBillable = b[0], TeamworkNonBillable = b[1],
                    ProductiveBillable = b[2], ProductiveNonBillable = b[3],
                });
                tb += bill; tn += non;
            }
        }

        return new BehindPaceResults
        {
            AnySourceConfigured = TeamworkEnabled || ProductiveEnabled,
            TeamworkEnabled = TeamworkEnabled, ProductiveEnabled = ProductiveEnabled,
            SourcesLabel = SourcesLabel(), Warnings = warnings, UpdatedAt = Now(),
            StampText = $"{oldest} → {newest} ({days}d, goal {goalHours.ToString(CultureInfo.InvariantCulture)}h/day per member)",
            GoalBillable = GoalBillable, MemberCount = count,
            TotalGoalMinutes = periodGoalMin * count, LoggedForGoalMinutes = logged,
            TotalBillable = tb, TotalNonBillable = tn,
            PeriodDays = days, Rows = behind,
        };
    }

    // `count` working days (Mon-Fri) strictly before the given date, most-recent-first.
    private static List<string> WorkingDays(string beforeIso, int count)
    {
        var list = new List<string>();
        if (!DateTime.TryParse(beforeIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return list;
        d = d.AddDays(-1);
        while (list.Count < count)
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                list.Add(d.ToString("yyyy-MM-dd"));
            d = d.AddDays(-1);
        }
        return list;
    }

    private static string FormatDateStamp(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) : iso;

    private static string Now() => DateTime.Now.ToString("t", CultureInfo.InvariantCulture);
}
