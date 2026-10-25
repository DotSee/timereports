namespace DotSeeReports;

// Formats a minute count the same way across the reports (e.g. 75 -> "1h 15m").
public static class Format
{
    public static string Minutes(int mins)
    {
        if (mins < 0) mins = 0;
        int h = mins / 60, m = mins % 60;
        if (h == 0 && m == 0) return "0m";
        if (h == 0) return $"{m}m";
        if (m == 0) return $"{h}h";
        return $"{h}h {m}m";
    }
}

// One rendered row: a tracked member's aggregated time for the period.
public class MemberRow
{
    public string Name { get; init; } = "";
    public bool IsManagement { get; init; }
    public int BillableMinutes { get; init; }
    public int NonBillableMinutes { get; init; }
    public int TotalMinutes => BillableMinutes + NonBillableMinutes;
    public int TeamworkBillable { get; init; }
    public int TeamworkNonBillable { get; init; }
    public int ProductiveBillable { get; init; }
    public int ProductiveNonBillable { get; init; }
    public bool GoalMet { get; init; }
}

// Shared bits both report partials need (sources, goal summary, status).
public abstract class ReportResults
{
    public bool AnySourceConfigured { get; init; }
    public bool TeamworkEnabled { get; init; }
    public bool ProductiveEnabled { get; init; }
    public string SourcesLabel { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = System.Array.Empty<string>();
    public string UpdatedAt { get; init; } = "";
    public string StampText { get; init; } = "";

    public bool GoalBillable { get; init; }
    public int MemberCount { get; init; }
    public int TotalGoalMinutes { get; init; }
    public int LoggedForGoalMinutes { get; init; }
    public int TotalBillable { get; init; }
    public int TotalNonBillable { get; init; }
    public int GrandTotalMinutes => TotalBillable + TotalNonBillable;
    public int ShortfallMinutes => TotalGoalMinutes - LoggedForGoalMinutes;

    // Tooltip limited to the configured sources (no word for a source that's off).
    public string Breakdown(int teamworkMinutes, int productiveMinutes)
    {
        var parts = new List<string>();
        if (TeamworkEnabled) parts.Add($"Teamwork: {Format.Minutes(teamworkMinutes)}");
        if (ProductiveEnabled) parts.Add($"Productive: {Format.Minutes(productiveMinutes)}");
        return string.Join(" · ", parts);
    }

    public bool StatusIsError => !AnySourceConfigured || Warnings.Count > 0;
    public string StatusText()
    {
        if (!AnySourceConfigured) return "No API keys configured.";
        if (Warnings.Count > 0) return $"Updated with issues · {UpdatedAt} — {string.Join(" | ", Warnings)}";
        return $"Updated · {UpdatedAt} · {SourcesLabel}";
    }
}

public class LedgerResults : ReportResults
{
    public IReadOnlyList<MemberRow> Rows { get; init; } = System.Array.Empty<MemberRow>();
    public bool ShowGoals { get; init; }
    public int DailyGoalMinutes { get; init; }
    public int ColumnCount => ShowGoals ? 5 : 4;
    public int TotalColspan => ShowGoals ? 2 : 1;
}

public class BehindPaceResults : ReportResults
{
    public IReadOnlyList<MemberRow> Rows { get; init; } = System.Array.Empty<MemberRow>();  // only members behind
    public int PeriodDays { get; init; }
    public bool AllClear => AnySourceConfigured && Rows.Count == 0;
}

public class MembersPanel
{
    public IReadOnlyList<Member> Members { get; init; } = System.Array.Empty<Member>();
    public bool TeamworkEnabled { get; init; }
    public bool ProductiveEnabled { get; init; }
}
