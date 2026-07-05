using System;
using System.Collections.Generic;
using System.Linq;
using Hale_Marketing_International.Services;

namespace Hale_Marketing_International.Services
{
    public static class InsightEngine
    {
        public static string GetSalesInsight()
        {
            double today = DashboardService.TodaySales();
            double yesterday = DashboardService.YesterdaySales();

            if (yesterday == 0) return "No previous data available";

            double change = ((today - yesterday) / yesterday) * 100;

            if (change < -10)
                return $"⚠ Sales dropped {Math.Round(change)}% today";

            if (change > 10)
                return $"📈 Sales increased {Math.Round(change)}% today";

            return "📊 Sales are stable";
        }

        public static string GetFastMovingInsight()
        {
            var items = DashboardService.TopItems();

            if (items.Count == 0)
                return "No sales data";

            return $"🔥 Fast moving item: {items.First().Item}";
        }

        public static string GetStockInsight()
        {
            int low = DashboardService.LowStockCount();

            if (low > 10)
                return $"🚨 Critical: {low} items low in stock";

            if (low > 0)
                return $"⚠ {low} items need restocking";

            return "✅ Stock levels normal";
        }

        public static string PredictStockRisk()
        {
            var items = DashboardService.TopItems();

            if (items.Count == 0)
                return "No prediction available";

            return $"🔮 Forecast: {items.First().Item} may run out soon";
        }
    }
}