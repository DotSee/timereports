using System.Globalization;
using DotSeeReports.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace DotSeeReports.Pages
{
    public class TimelogDashboardModel : PageModel
    {
        private readonly ReportService _svc;
        private readonly ReportSettings _settings;
        private readonly ApiSettings _api;

        public TimelogDashboardModel(ReportService svc, IOptions<ReportSettings> settings, IOptions<ApiSettings> api)
        {
            _svc = svc;
            _settings = settings.Value;
            _api = api.Value;
        }

        public string CompanyName => _settings.CompanyName;
        public string ApplicationName => _settings.ApplicationName;
        public string DailyGoalHours => _settings.DailyGoalHours.ToString(CultureInfo.InvariantCulture);
        public bool ShowGoals => _settings.ShowGoals;
        public bool IncludeWeekends => _settings.IncludeWeekends;
        public bool GoalIsBillable => _settings.GoalBasis == GoalBasisKind.Billable;
        public bool TeamworkEnabled => !string.IsNullOrWhiteSpace(_api.Teamwork.ApiKey);
        public bool ProductiveEnabled => !string.IsNullOrWhiteSpace(_api.Productive.ApiKey);

        public string SourcesLabel
        {
            get
            {
                var names = new List<string>();
                if (TeamworkEnabled) names.Add("Teamwork");
                if (ProductiveEnabled) names.Add("Productive");
                return names.Count > 0 ? string.Join(" + ", names) : "no data source configured";
            }
        }

        public string DefaultDate { get; private set; } = "";
        public LedgerResults Results { get; private set; } = default!;
        public MembersPanel MembersPanel { get; private set; } = default!;

        public async Task OnGetAsync(CancellationToken ct)
        {
            DefaultDate = DateTime.Today.ToString("yyyy-MM-dd");
            MembersPanel = _svc.BuildMembersPanel();
            Results = await _svc.BuildLedgerAsync(DefaultDate, _settings.DailyGoalHours, exclude: false, ct);
        }

        // AJAX: returns just the results partial for the current controls.
        public async Task<IActionResult> OnGetResultsAsync(string? date, double? goal, bool exclude, CancellationToken ct)
        {
            var d = string.IsNullOrWhiteSpace(date) ? DateTime.Today.ToString("yyyy-MM-dd") : date;
            var vm = await _svc.BuildLedgerAsync(d, goal ?? _settings.DailyGoalHours, exclude, ct);
            return Partial("_LedgerResults", vm);
        }
    }
}
