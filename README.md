# DotSee Reports

A small ASP.NET Core (Razor Pages, .NET 10) web app that shows how much time the team
has logged, by pulling time entries from **Teamwork** and **Productive** and combining
them per person. It is an internal reporting tool — read‑only, no database.

## What it does

The app exposes two reports (the home page `/` redirects to the Daily Time Ledger):

- **Daily Time Ledger** (`/TimelogDashboard`) — for a single selected day, lists each
  tracked member's **billable**, **non‑billable** and **total** logged time, with team
  totals at the bottom. Optionally shows a **Goal** column (green check / red ✗) marking
  who reached the configured daily goal, plus the day's total goal and how many hours the
  team is behind (orange) or over (green) that goal.

- **Behind Pace** (`/BehindPace`) — over a trailing window of *N* working days, lists only
  the members who fell **short of their goal** (daily goal × working days), sorted by last
  name. Shows the total team goal for the window and the hours behind/over.

Both reports share:

- **Prev / Next day** navigation (skips weekends unless configured otherwise) and a date picker.
- An **Exclude Management** toggle that hides members flagged as management from the rows and totals.
- A **tracked‑members** panel listing each person's Teamwork / Productive IDs and Team/Management flag.

### How it works

The two time‑tracking APIs are called **server‑side**: each report page renders its results
on the server, and the page's controls refresh them through the page's own `?handler=Results`
endpoint, which returns the rendered results HTML. This keeps the API keys off the browser
and avoids the browser CORS block on the Productive API. If only one source has a
key configured, the report is built from that source alone; if neither is configured, the
report shows a "no data source is configured" message.

## Requirements

- .NET 10 SDK
- API credentials for Teamwork and/or Productive (at least one)

## Getting started

1. Set the Teamwork host, the API keys and the tracked team in
   [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) — see
   [Configuration](#configuration). For example, from `src/DotSeeReports`:

   ```bash
   dotnet user-secrets set "ApiSettings:Teamwork:Host" "yourcompany.teamwork.com"
   dotnet user-secrets set "ApiSettings:Teamwork:ApiKey" "<teamwork-api-key>"
   dotnet user-secrets set "ReportSettings:Members:0:Name" "Jane Doe"
   dotnet user-secrets set "ReportSettings:Members:0:TeamworkId" "100001"
   ```

2. From `src/DotSeeReports`:

   ```bash
   dotnet restore
   dotnet run
   ```

Then open the URL printed by `dotnet run` (the home page redirects to the Daily Time Ledger).

## Configuration

App settings are organised in two sections: **`ApiSettings`** (how to reach the APIs) and
**`ReportSettings`** (report behaviour and the tracked team). `appsettings.json` holds the
non‑sensitive defaults and `REPLACE_ME` placeholders; it is committed to source control, so
**never put real values in it**. Supply the Teamwork host, the API keys, the Productive
organization id and the `Members` list through
[user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) in development,
or through environment variables when deployed (see
[below](#overriding-via-environment-variables)). The full shape is:

```jsonc
{
  "ApiSettings": {
    "Teamwork": {
      "Host": "yourcompany.teamwork.com", // your Teamwork host (no scheme)
      "ApiKey": "<teamwork-api-key>"     // your Teamwork API key
    },
    "Productive": {
      "Host": "api.productive.io",       // Productive API host (no scheme)
      "OrganizationId": "<productive-org-id>",// your Productive organization id
      "ApiKey": "<productive-api-token>" // your Productive API token
    }
  },
  "ReportSettings": {
    "CompanyName": "DotSee",             // shown in report titles/headings
    "ApplicationName": "DotSeeReports",  // nav brand + page <title> suffix + footer
    "WorkingDays": 5,                     // Behind Pace: trailing working days to look back
    "DailyGoalHours": 4,                  // per-member goal for a single day (hours)
    "GoalBasis": "All",                  // "All" logged hours or "Billable" hours only
    "ShowGoals": true,                    // Daily Ledger: show the Goal column + goal summary
    "IncludeWeekends": false,             // Prev/Next day step onto weekends when true
    "Members": [                          // the tracked team
      { "Name": "Jane Doe",   "TeamworkId": 100001, "ProductiveId": 200001, "IsManagement": false },
      { "Name": "John Smith", "TeamworkId": 100002, "ProductiveId": null,   "IsManagement": true }
    ]
  }
}
```

### `ApiSettings`

| Key | Default | Description |
| --- | --- | --- |
| `Teamwork:Host` | `REPLACE_ME` | Teamwork host name (no `https://`), e.g. `yourcompany.teamwork.com`. |
| `Teamwork:ApiKey` | *(empty)* | Teamwork API key. Empty ⇒ Teamwork is skipped. |
| `Productive:Host` | `api.productive.io` | Productive API host name (no `https://`). |
| `Productive:OrganizationId` | *(empty)* | Productive organization id (sent as the `X-Organization-Id` header). |
| `Productive:ApiKey` | *(empty)* | Productive API token. Empty ⇒ Productive is skipped. |

If **one** key is empty, that source is silently skipped and the report is built from the
other. If **both** are empty, the reports display a "no data source is configured" message.

### `ReportSettings`

| Key | Default | Description |
| --- | --- | --- |
| `CompanyName` | `DotSee` | Company name used in report titles/headings (`{CompanyName} — Behind Pace`). |
| `ApplicationName` | `DotSeeReports` | Application name shown as the nav brand, the page `<title>` suffix, and the footer. |
| `WorkingDays` | `5` | Number of trailing working days the Behind Pace report covers. Also the initial value of its "Working days" field. |
| `DailyGoalHours` | `4` | Per‑member goal for a single day, in hours. Both reports expose an editable **"Daily goal / member (hours)"** field (with an **Apply** button) that defaults to this value: on the Daily Ledger it drives the Goal column and goal summary; on Behind Pace it is multiplied by `WorkingDays` for the period goal. |
| `GoalBasis` | `All` | What counts toward the goal: `All` (every logged hour, i.e. billable + non‑billable) or `Billable` (billable hours only). Applies everywhere the goal is evaluated — the Daily Ledger's Goal column, which members Behind Pace lists as "behind", and the goal summary under the totals. The summary reflects the basis: e.g. "behind the total goal" / "behind the billable goal" and "over the goal (billable + nonbillable hours)" / "over the goal (billable hours only)". |
| `ShowGoals` | `true` | Daily Ledger only: when `false`, the Goal column, its legend, and the goal summary are removed. |
| `IncludeWeekends` | `false` | When `false`, the **Prev / Next day** buttons skip Saturdays/Sundays and jump to the previous/next working day. When `true`, they move one calendar day. |
| `Members` | *(placeholder)* | The tracked team — see [`ReportSettings:Members`](#reportsettingsmembers). |

### `ReportSettings:Members`

Each entry describes one tracked person:

| Field | Type | Description |
| --- | --- | --- |
| `Name` | string | Display name. Members are shown sorted by **last name** (last whitespace‑separated token). |
| `TeamworkId` | int | The person's Teamwork user id. |
| `ProductiveId` | int or `null` | The person's Productive person id, or `null` if they are not tracked in Productive. |
| `IsManagement` | bool | When `true`, the person is hidden by the **Exclude Management** toggle. |

`appsettings.json` ships with `REPLACE_ME` placeholder entries. Supply your real members in
user secrets or environment variables (e.g. `ReportSettings__Members__0__Name=Jane Doe`).
Configuration arrays are merged **by index**, so each real entry overrides the placeholder
at the same position: provide at least as many members as there are placeholders, or
remove the extra placeholders, otherwise the leftover `REPLACE_ME` entries will show up in
the reports.

### Overriding via environment variables

Any setting can also be supplied as an environment variable using `__` (double underscore)
as the separator — useful for deployment:

```
ApiSettings__Teamwork__Host=…
ApiSettings__Teamwork__ApiKey=…
ApiSettings__Productive__OrganizationId=…
ApiSettings__Productive__ApiKey=…
ReportSettings__CompanyName=Acme
ReportSettings__DailyGoalHours=6
ReportSettings__IncludeWeekends=true
```

## Notes

- **No authentication.** The report pages (including their `?handler=Results` endpoints)
  are open to anyone who can reach the app. Run it on a trusted/internal network or put it
  behind an authenticating reverse proxy.
- API keys are used only server‑side; the browser only ever talks to this app, not to
  Teamwork or Productive directly.
- Keep real credentials and team data out of source control. Use user secrets in
  development and environment variables (or your host's secret store) when deployed —
  never edit real values into `appsettings.json`.
