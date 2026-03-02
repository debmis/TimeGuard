using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using System.Linq;
using System.Windows;
using TimeGuard.Services;

namespace TimeGuard.UI;

public partial class DashboardWindow : Window
{
    private readonly DatabaseService _db;
    private IReadOnlyList<(DateOnly Date, string ProcessName, double UsageMins, bool IsPassive)> _chartData = [];

    private record DrillRow(string ProcessName, string WindowTitle, string Start, string End, string Duration);

    public DashboardWindow(DatabaseService db)
    {
        InitializeComponent();
        _db = db;
        BuildChart();
    }

    private void BuildChart()
    {
        _chartData = _db.LoadChartData(7);

        var model = new PlotModel
        {
            Background          = OxyColors.Transparent,
            TextColor           = OxyColor.FromRgb(240, 240, 240),
            PlotAreaBorderColor = OxyColor.FromRgb(60, 60, 80)
        };

        model.Legends.Add(new Legend
        {
            LegendTextColor       = OxyColor.FromRgb(160, 160, 184),
            LegendBackground      = OxyColors.Transparent,
            LegendBorderThickness = 0
        });

        // Y axis — dates as categories (BarSeries in OxyPlot 2.x requires CategoryAxis on Left)
        var dates = _chartData.Select(r => r.Date).Distinct().OrderBy(d => d).ToList();
        var yAxis = new CategoryAxis
        {
            Position           = AxisPosition.Left,
            ItemsSource        = dates.Select(d => d.ToString("MMM d")).ToList(),
            TextColor          = OxyColor.FromRgb(160, 160, 184),
            TicklineColor      = OxyColors.Transparent,
            MajorGridlineStyle = LineStyle.None,
            GapWidth           = 0.3
        };
        model.Axes.Add(yAxis);

        // X axis — minutes
        var xAxis = new LinearAxis
        {
            Position           = AxisPosition.Bottom,
            Title              = "Minutes",
            TitleColor         = OxyColor.FromRgb(160, 160, 184),
            TextColor          = OxyColor.FromRgb(160, 160, 184),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(60, 60, 80),
            TicklineColor      = OxyColors.Transparent,
            Minimum            = 0
        };
        model.Axes.Add(xAxis);

        // One ColumnSeries per app (vertical bars, categories on X)
        var apps = _chartData.Select(r => r.ProcessName).Distinct().OrderBy(x => x).ToList();
        var palette = new[]
        {
            OxyColor.FromRgb(224, 90,  43),
            OxyColor.FromRgb(86,  156, 214),
            OxyColor.FromRgb(78,  201, 176),
            OxyColor.FromRgb(220, 220, 100),
            OxyColor.FromRgb(180, 100, 220)
        };
        var passiveColor = OxyColor.FromRgb(80, 80, 100);

        int ruledIdx = 0;
        for (int i = 0; i < apps.Count; i++)
        {
            var app       = apps[i];
            var isPassive = _chartData.Any(r => r.ProcessName == app && r.IsPassive);
            var color     = isPassive ? passiveColor : palette[ruledIdx++ % palette.Length];

            var series = new BarSeries
            {
                Title           = isPassive ? $"{app} (untracked)" : app,
                FillColor       = color,
                StrokeThickness = 0
            };

            foreach (var date in dates)
            {
                var val = _chartData
                    .FirstOrDefault(r => r.Date == date && r.ProcessName == app)
                    .UsageMins;
                series.Items.Add(new BarItem(val));
            }

            model.Series.Add(series);
        }

        // Click to drilldown — map click Y position to date index via category axis
        model.MouseDown += (s, e) =>
        {
            if (e.ChangedButton != OxyMouseButton.Left) return;
            var catAxis = model.Axes.OfType<CategoryAxis>().FirstOrDefault();
            if (catAxis == null) return;
            var catIdx = (int)Math.Round(catAxis.InverseTransform(e.Position.Y));
            if (catIdx >= 0 && catIdx < dates.Count)
                Dispatcher.Invoke(() => LoadDrilldown(dates[catIdx]));
        };

        BarChart.Model = model;
    }

    private void LoadDrilldown(DateOnly date)
    {
        DrilldownHeader.Text = $"📅 {date:dddd, MMMM d, yyyy}";
        var sessions = _db.LoadSessionsForDay(date);
        DrilldownGrid.ItemsSource = sessions
            .Select(s => new DrillRow(s.ProcessName, s.WindowTitle, s.StartDisplay, s.EndDisplay, s.DurationDisplay))
            .ToList();
    }
}
