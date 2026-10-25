using System.Text.Json;

namespace DotSeeReports;

// Whether goals are measured against all logged hours or billable hours only.
public enum GoalBasisKind { All, Billable }

// Report defaults, bound from the "ReportSettings" section of configuration.
public class ReportSettings
{
    // Company name shown in report titles/headings.
    public string CompanyName { get; set; } = "DotSee";

    // Application name shown as the nav brand and in the layout title/footer.
    public string ApplicationName { get; set; } = "DotSeeReports";

    // Number of trailing working days the Behind Pace report looks back over.
    public int WorkingDays { get; set; } = 5;

    // Per-day goal in hours. The daily ledger flags each person against this;
    // Behind Pace multiplies it by WorkingDays for its period goal.
    public double DailyGoalHours { get; set; } = 4;

    // Whether the goal counts all logged hours or billable hours only.
    public GoalBasisKind GoalBasis { get; set; } = GoalBasisKind.All;

    // When true, the Daily Time Ledger shows the "Goal" column (met/missed per person).
    public bool ShowGoals { get; set; } = true;

    // When false (default), the Prev/Next day buttons skip Saturdays and Sundays,
    // jumping to the previous/next working day instead.
    public bool IncludeWeekends { get; set; } = false;

    // Tracked team members. Configured via appsettings (placeholder) and overridden
    // with real values in user secrets.
    public List<Member> Members { get; set; } = new();

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Serialized for the page scripts as [{ name, teamworkId, productiveId, isManagement }].
    public string MembersJson() => JsonSerializer.Serialize(Members, CamelCase);
}

public class Member
{
    public string Name { get; set; } = "";
    public int TeamworkId { get; set; }
    public int? ProductiveId { get; set; }
    public bool IsManagement { get; set; }
}
