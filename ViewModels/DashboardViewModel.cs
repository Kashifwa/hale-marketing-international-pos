using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Linq;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Hale_Marketing_International.Services;

namespace Hale_Marketing_International.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        void Notify([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // KPI
        public double TodaySales => DashboardService.TodaySales();
        public double TodayPurchase => DashboardService.TodayPurchase();
        public double Profit => TodaySales - TodayPurchase;
        public int LowStock => DashboardService.LowStockCount();

        // NEW: bottom stat row
        public int TotalProducts => DashboardService.TotalProducts();
        public int TotalParties => DashboardService.TotalParties();
        public double DueAmount => DashboardService.TotalDueAmount();

        // AI INSIGHTS
        public string SalesInsight => InsightEngine.GetSalesInsight();
        public string FastMoving => InsightEngine.GetFastMovingInsight();
        public string StockInsight => InsightEngine.GetStockInsight();
        public string Prediction => InsightEngine.PredictStockRisk();

        // CHARTS
        public ObservableCollection<ISeries> SalesSeries { get; set; }
        public ObservableCollection<ISeries> TopSeries { get; set; }

        public DashboardViewModel()
        {
            Load();
            DashboardHub.RefreshEvent += Refresh;
        }

        public void Refresh()
        {
            Load();
            Notify("");
        }

        void Load()
        {
            var sales = DashboardService.GetSalesTrend();
            SalesSeries = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = sales,
                    Name = "Sales Trend"
                }
            };

            var top = DashboardService.TopItems();
            TopSeries = new ObservableCollection<ISeries>(
                top.Select(x => new PieSeries<double>
                {
                    Values = new double[] { x.Qty },
                    Name = x.Item,
                    InnerRadius = 60,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle
                })
            );
        }
    }
}