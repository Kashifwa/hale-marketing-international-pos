using System.Data.SQLite;
using System;
using System.Collections.Generic;
namespace Hale_Marketing_International.Services
{
    public static class DashboardService
    {
        static string conn = "Data Source=posdata.db;Version=3;";
        // ---------- KPI ----------
        public static double TodaySales()
        {
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText =
            @"SELECT IFNULL(SUM(NetTotal),0)
              FROM Sales
              WHERE DATE(InvoiceDate)=DATE('now')";
            return Convert.ToDouble(cmd.ExecuteScalar());
        }
        public static double YesterdaySales()
        {
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText =
            @"SELECT IFNULL(SUM(NetTotal),0)
              FROM Sales
              WHERE DATE(InvoiceDate)=DATE('now','-1 day')";
            return Convert.ToDouble(cmd.ExecuteScalar());
        }
        public static double TodayPurchase()
        {
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText =
            @"SELECT IFNULL(SUM(NetTotal),0)
              FROM PurchaseInvoice
              WHERE DATE(InvoiceDate)=DATE('now')";
            return Convert.ToDouble(cmd.ExecuteScalar());
        }
        public static int LowStockCount()
        {
            // Stock is derived from transactions (Purchase - Sold + ReturnIn - ReturnOut),
            // same logic as StockWindow — Stock table doesn't exist / isn't authoritative.
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText =
            @"SELECT COUNT(*) FROM (
                SELECT p.Id,
                    (IFNULL((SELECT SUM(Quantity) FROM PurchaseItems pi WHERE pi.ProductCode=p.Code),0)
                   - IFNULL((SELECT SUM(Quantity) FROM SalesItems si WHERE si.ProductId=p.Id),0)
                   + IFNULL((SELECT SUM(ReturnQty) FROM SalesReturnItems sr WHERE sr.ProductId=p.Id),0)
                   - IFNULL((SELECT SUM(ReturnQty) FROM PurchaseReturnItems pr WHERE pr.ProductId=p.Id),0)
                    ) AS CurrentStock
                FROM Products p
              ) WHERE CurrentStock <= 5";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        public static int TotalProducts()
        {
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Products";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static int TotalParties()
        {
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Parties";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static double TotalDueAmount()
        {
            // Approx receivable due = Total Sales - Cash Received - Sales Returns
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText =
            @"SELECT
        IFNULL((SELECT SUM(NetTotal) FROM Sales),0)
      - IFNULL((SELECT SUM(Amount) FROM CashReceipt),0)
      - IFNULL((SELECT SUM(NetAmount) FROM SalesReturn),0)";
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? 0 : Convert.ToDouble(result);
        }
        // ---------- CHART DATA ----------
        public static List<double> GetSalesTrend()
        {
            var list = new List<double>();
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText =
            @"SELECT SUM(NetTotal)
              FROM Sales
              GROUP BY DATE(InvoiceDate)
              ORDER BY DATE(InvoiceDate) DESC
              LIMIT 7";
            var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(r.IsDBNull(0) ? 0 : r.GetDouble(0));
            list.Reverse(); // oldest -> newest, left to right
            return list;
        }
        public static List<(string Item, double Qty)> TopItems()
        {
            var list = new List<(string, double)>();
            using var c = new SQLiteConnection(conn);
            c.Open();
            var cmd = c.CreateCommand();
            cmd.CommandText =
            @"SELECT ProductName, SUM(Quantity)
              FROM SalesItems
              GROUP BY ProductName
              ORDER BY SUM(Quantity) DESC
              LIMIT 5";
            var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((r.GetString(0), r.GetDouble(1)));
            return list;
        }
    }
}