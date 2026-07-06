using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace Hale_Marketing_International
{
    public partial class CompanyLedgerWindow : Window
    {
        private string _dbPath = AppConfig.ConnectionString;

        // ── Models ────────────────────────────────────────────────────────────────
        public class LedgerRow
        {
            public int Id { get; set; }
            public string Date { get; set; }
            public string Type { get; set; }
            public string Party { get; set; }
            public string Reference { get; set; }
            public string Description { get; set; }
            public decimal Amount { get; set; }
            public bool CanDelete { get; set; }
        }

        public class LedgerDrCrRow
        {
            public string Date { get; set; }
            public string EntryType { get; set; }
            public string DrCr { get; set; }
            public string Reference { get; set; }
            public string Party { get; set; }
            public string Description { get; set; }
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
            public decimal Balance { get; set; }
        }

        // ── Full expense list (unfiltered) for client-side filtering ─────────────
        private List<LedgerRow> _allExpenses = new();

        // ── P&L date filter state ─────────────────────────────────────────────────
        private DateTime? _plFrom = null;
        private DateTime? _plTo = null;

        // ── Constructor ───────────────────────────────────────────────────────────
        public CompanyLedgerWindow()
        {
            InitializeComponent();
            EnsureTables();

            DtExpenseDate.SelectedDate = DateTime.Today;
            DtAssetDate.SelectedDate = DateTime.Today;
            DtLiabilityDate.SelectedDate = DateTime.Today;

            DtLedgerFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DtLedgerTo.SelectedDate = DateTime.Today;

            DtPLFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DtPLTo.SelectedDate = DateTime.Today;

            // Expense filter defaults
            DtExpFilterFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DtExpFilterTo.SelectedDate = DateTime.Today;

            LoadAll();
        }

        // ── Ensure Tables ─────────────────────────────────────────────────────────
        private void EnsureTables()
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(con);

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ManualExpenses (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT, Category TEXT, Amount REAL,
                    Description TEXT, PaymentMode TEXT, CreatedAt TEXT);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ManualAssets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT, AssetType TEXT, Value REAL,
                    Description TEXT, CreatedAt TEXT);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ManualLiabilities (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT, LiabilityType TEXT, Amount REAL,
                    Description TEXT, CreatedAt TEXT);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { MessageBox.Show("EnsureTables: " + ex.Message); }
        }

        // ── Load All ──────────────────────────────────────────────────────────────
        private void LoadAll()
        {
            LoadIncome();
            LoadExpenses();
            LoadAssets();
            LoadLiabilities();
            LoadProfitAndLoss(null, null);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadAll();

        // ════════════════════════════════════════════════════════════════════════
        // INCOME TAB
        // ════════════════════════════════════════════════════════════════════════
        private void LoadIncome()
        {
            var rows = new List<LedgerRow>();
            using var con = new SQLiteConnection(_dbPath);
            con.Open();

            // Sales invoices
            using (var cmd = new SQLiteCommand(@"
                SELECT s.InvoiceDate, s.InvoiceNo, s.NetTotal, p.Name
                FROM Sales s LEFT JOIN Parties p ON p.Id = s.PartyId
                ORDER BY s.InvoiceDate", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(new LedgerRow { Date = r["InvoiceDate"].ToString(), Type = "Sales Invoice", Reference = r["InvoiceNo"].ToString(), Party = r["Name"]?.ToString() ?? "", Description = "Sales Revenue", Amount = Convert.ToDecimal(r["NetTotal"]), CanDelete = false });

            // Cash receipts — correct columns: ReceiptNo, ReceiptDate
            using (var cmd = new SQLiteCommand(@"
                SELECT cr.Date, cr.VoucherNo, cr.Amount, p.Name
                FROM CashReceipt cr LEFT JOIN Parties p ON p.Id = cr.PartyId
                ORDER BY cr.Date", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(new LedgerRow { Date = r["Date"].ToString(), Type = "Cash Receipt", Reference = r["VoucherNo"].ToString(), Party = r["Name"]?.ToString() ?? "", Description = "Cash Received", Amount = r["Amount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Amount"]), CanDelete = false });

            // Sales returns (contra — negative)
            using (var cmd = new SQLiteCommand(@"
                SELECT sr.ReturnDate, sr.ReturnNo, sr.NetAmount, p.Name
                FROM SalesReturn sr LEFT JOIN Parties p ON p.Id = sr.PartyId
                ORDER BY sr.ReturnDate", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(new LedgerRow { Date = r["ReturnDate"].ToString(), Type = "Sales Return", Reference = r["ReturnNo"].ToString(), Party = r["Name"]?.ToString() ?? "", Description = "(-) Sales Return", Amount = -(r["NetAmount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["NetAmount"])), CanDelete = false });

            rows = rows.OrderBy(x => x.Date).ToList();
            DgIncome.ItemsSource = rows;
            TxtTotalIncome.Text = rows.Sum(x => x.Amount).ToString("N2");
        }

        // ════════════════════════════════════════════════════════════════════════
        // EXPENSES TAB  — with filter
        // ════════════════════════════════════════════════════════════════════════
        private void LoadExpenses()
        {
            _allExpenses.Clear();
            using var con = new SQLiteConnection(_dbPath);
            con.Open();

            // Purchase invoices (auto)
            using (var cmd = new SQLiteCommand(@"
                SELECT pi.InvoiceDate, pi.InvoiceNo, pi.NetTotal, pi.Tax, pi.Freight, pi.Supplier
                FROM PurchaseInvoice pi ORDER BY pi.InvoiceDate", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    decimal net = r["NetTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(r["NetTotal"]);
                    decimal tax = r["Tax"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Tax"]);
                    decimal freight = r["Freight"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Freight"]);
                    decimal purchase = net - tax - freight;
                    string inv = r["InvoiceNo"].ToString();
                    string sup = r["Supplier"]?.ToString() ?? "";
                    string date = r["InvoiceDate"].ToString();

                    if (purchase > 0) _allExpenses.Add(new LedgerRow { Date = date, Type = "Purchase", Reference = inv, Party = sup, Description = "Purchase Cost", Amount = purchase, CanDelete = false });
                    if (freight > 0) _allExpenses.Add(new LedgerRow { Date = date, Type = "Freight", Reference = inv, Party = sup, Description = "Freight Charge", Amount = freight, CanDelete = false });
                    if (tax > 0) _allExpenses.Add(new LedgerRow { Date = date, Type = "Tax", Reference = inv, Party = sup, Description = "Tax Paid", Amount = tax, CanDelete = false });
                }

            // Purchase returns (contra)
            using (var cmd = new SQLiteCommand(@"
                SELECT pr.ReturnDate, pr.ReturnNo, pr.NetAmount, p.Name
                FROM PurchaseReturn pr LEFT JOIN Parties p ON p.Id = pr.SupplierId
                ORDER BY pr.ReturnDate", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    _allExpenses.Add(new LedgerRow { Date = r["ReturnDate"].ToString(), Type = "Purchase Return", Reference = r["ReturnNo"].ToString(), Party = r["Name"]?.ToString() ?? "", Description = "(-) Purchase Return", Amount = -(r["NetAmount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["NetAmount"])), CanDelete = false });

            // Payment receipts — correct columns: ReceiptNo, ReceiptDate
            using (var cmd = new SQLiteCommand(@"
                SELECT pr.Date, pr.VoucherNo, pr.Amount, p.Name
                FROM PaymentReceipt pr LEFT JOIN Parties p ON p.Id = pr.PartyId
                ORDER BY pr.Date", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    _allExpenses.Add(new LedgerRow { Date = r["Date"].ToString(), Type = "Payment Receipt", Reference = r["VoucherNo"].ToString(), Party = r["Name"]?.ToString() ?? "", Description = "Payment Made", Amount = r["Amount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Amount"]), CanDelete = false });

            // Manual expenses
            using (var cmd = new SQLiteCommand("SELECT Id, Date, Category, Amount, Description FROM ManualExpenses ORDER BY Date", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    _allExpenses.Add(new LedgerRow { Id = Convert.ToInt32(r["Id"]), Date = r["Date"].ToString(), Type = "Manual", Reference = "", Party = r["Category"].ToString(), Description = r["Description"].ToString(), Amount = Convert.ToDecimal(r["Amount"]), CanDelete = true });

            _allExpenses = _allExpenses.OrderBy(x => x.Date).ToList();
            ApplyExpenseFilter();
        }

        // ── Expense filter logic ──────────────────────────────────────────────────
        private void ApplyExpenseFilter()
        {
            var filtered = _allExpenses.ToList();

            // Date range filter
            if (DtExpFilterFrom.SelectedDate != null)
                filtered = filtered.Where(x => DateTime.TryParse(x.Date, out var d) && d >= DtExpFilterFrom.SelectedDate.Value.Date).ToList();
            if (DtExpFilterTo.SelectedDate != null)
                filtered = filtered.Where(x => DateTime.TryParse(x.Date, out var d) && d <= DtExpFilterTo.SelectedDate.Value.Date).ToList();

            // Type filter
            string typeFilter = (CmbExpTypeFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(typeFilter) && typeFilter != "All Types")
                filtered = filtered.Where(x => x.Type == typeFilter).ToList();

            // Search text filter
            string search = TxtExpSearch.Text.Trim().ToLower();
            if (!string.IsNullOrWhiteSpace(search) && search != "search party / reference...")
                filtered = filtered.Where(x =>
                    (x.Party?.ToLower().Contains(search) == true) ||
                    (x.Reference?.ToLower().Contains(search) == true) ||
                    (x.Description?.ToLower().Contains(search) == true)).ToList();

            DgExpenses.ItemsSource = filtered;
            TxtTotalExpenses.Text = filtered.Sum(x => x.Amount).ToString("N2");
            TxtExpFilterCount.Text = $"({filtered.Count} records)";
        }

        private void BtnExpFilter_Click(object sender, RoutedEventArgs e) => ApplyExpenseFilter();
        private void BtnExpClearFilter_Click(object sender, RoutedEventArgs e)
        {
            DtExpFilterFrom.SelectedDate = null;
            DtExpFilterTo.SelectedDate = null;
            CmbExpTypeFilter.SelectedIndex = 0;
            TxtExpSearch.Text = "Search party / reference...";
            ApplyExpenseFilter();
        }
        private void TxtExpSearch_TextChanged(object sender, TextChangedEventArgs e)
        { if (IsLoaded) ApplyExpenseFilter(); }
        private void CmbExpTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (IsLoaded) ApplyExpenseFilter(); }
        private void TxtExpSearch_GotFocus(object sender, RoutedEventArgs e)
        { if (TxtExpSearch.Text == "Search party / reference...") TxtExpSearch.Text = ""; }
        private void TxtExpSearch_LostFocus(object sender, RoutedEventArgs e)
        { if (string.IsNullOrWhiteSpace(TxtExpSearch.Text)) TxtExpSearch.Text = "Search party / reference..."; }

        // ════════════════════════════════════════════════════════════════════════
        // ASSETS TAB — MANUAL ENTRIES ONLY (no auto-pulled stock/receivables)
        // ════════════════════════════════════════════════════════════════════════
        private void LoadAssets()
        {
            var rows = new List<LedgerRow>();
            using var con = new SQLiteConnection(_dbPath);
            con.Open();

            // ONLY manual assets — no stock inventory, no receivables
            using var cmd = new SQLiteCommand(
                "SELECT Id, Date, AssetType, Value, Description FROM ManualAssets ORDER BY Date", con);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(new LedgerRow
                {
                    Id = Convert.ToInt32(r["Id"]),
                    Date = r["Date"].ToString(),
                    Type = "Manual",
                    Party = r["AssetType"].ToString(),
                    Reference = "",
                    Description = r["Description"].ToString(),
                    Amount = Convert.ToDecimal(r["Value"]),
                    CanDelete = true
                });

            DgAssets.ItemsSource = rows;
            TxtTotalAssets.Text = rows.Sum(x => x.Amount).ToString("N2");
        }

        // ════════════════════════════════════════════════════════════════════════
        // LIABILITIES TAB
        // ════════════════════════════════════════════════════════════════════════
        private void LoadLiabilities()
        {
            var rows = new List<LedgerRow>();
            using var con = new SQLiteConnection(_dbPath);
            con.Open();

            // Payable party balances (auto)
            using (var cmd = new SQLiteCommand(@"
                SELECT p.Name,
                       IFNULL((SELECT Balance FROM CustomerLedger
                               WHERE PartyId=p.Id ORDER BY Id DESC LIMIT 1), 0) AS Balance
                FROM Parties p WHERE p.Type='Payable'", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    decimal bal = Convert.ToDecimal(r["Balance"]);
                    if (bal > 0)
                        rows.Add(new LedgerRow { Id = 0, Date = DateTime.Today.ToString("yyyy-MM-dd"), Type = "Payable", Party = "Accounts Payable", Reference = r["Name"].ToString(), Description = $"Balance owed to {r["Name"]}", Amount = bal, CanDelete = false });
                }

            // Manual liabilities
            using (var cmd = new SQLiteCommand("SELECT Id, Date, LiabilityType, Amount, Description FROM ManualLiabilities ORDER BY Date", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(new LedgerRow { Id = Convert.ToInt32(r["Id"]), Date = r["Date"].ToString(), Type = "Manual", Party = r["LiabilityType"].ToString(), Reference = "", Description = r["Description"].ToString(), Amount = Convert.ToDecimal(r["Amount"]), CanDelete = true });

            DgLiabilities.ItemsSource = rows;
            TxtTotalLiabilities.Text = rows.Sum(x => x.Amount).ToString("N2");
        }

        // ════════════════════════════════════════════════════════════════════════
        // PROFIT & LOSS
        // ════════════════════════════════════════════════════════════════════════
        private void BtnPLFilter_Click(object sender, RoutedEventArgs e)
        {
            if (DtPLFrom.SelectedDate == null || DtPLTo.SelectedDate == null)
            { MessageBox.Show("Please select both From and To dates."); return; }
            _plFrom = DtPLFrom.SelectedDate.Value.Date;
            _plTo = DtPLTo.SelectedDate.Value.Date;
            LoadProfitAndLoss(_plFrom, _plTo);
            TxtPLFilterLabel.Text = $"Showing: {_plFrom:dd-MM-yyyy}  →  {_plTo:dd-MM-yyyy}";
            TxtPLPeriodLabel.Text = $"NET PROFIT / (LOSS)  ·  {_plFrom:dd MMM yyyy} – {_plTo:dd MMM yyyy}";
            BorderPLAllTime.Visibility = Visibility.Visible;
            LoadAllTimeNetProfit();
        }

        private void BtnPLAllTime_Click(object sender, RoutedEventArgs e)
        {
            _plFrom = null; _plTo = null;
            LoadProfitAndLoss(null, null);
            TxtPLFilterLabel.Text = "Showing: All Time";
            TxtPLPeriodLabel.Text = "NET PROFIT / (LOSS) — ALL TIME";
            BorderPLAllTime.Visibility = Visibility.Collapsed;
        }

        private void LoadAllTimeNetProfit()
        {
            using var con = new SQLiteConnection(_dbPath);
            con.Open();
            decimal sales = GetScalar(con, "SELECT IFNULL(SUM(NetTotal),0) FROM Sales");
            decimal cashIn = GetScalar(con, "SELECT IFNULL(SUM(Amount),0)   FROM CashReceipt");
            decimal salesReturn = GetScalar(con, "SELECT IFNULL(SUM(NetAmount),0) FROM SalesReturn");
            decimal netIncome = sales + cashIn - salesReturn;
            decimal purchase = GetScalar(con, "SELECT IFNULL(SUM(NetTotal - IFNULL(Tax,0) - IFNULL(Freight,0)),0) FROM PurchaseInvoice");
            decimal purchaseReturn = GetScalar(con, "SELECT IFNULL(SUM(NetAmount),0) FROM PurchaseReturn");
            decimal freight = GetScalar(con, "SELECT IFNULL(SUM(IFNULL(Freight,0)),0) FROM PurchaseInvoice");
            decimal tax = GetScalar(con, "SELECT IFNULL(SUM(IFNULL(Tax,0)),0) FROM PurchaseInvoice");
            decimal otherExp = GetScalar(con, "SELECT IFNULL(SUM(Amount),0) FROM ManualExpenses");
            decimal payments = GetScalar(con, "SELECT IFNULL(SUM(Amount),0) FROM PaymentReceipt");
            decimal totalExp = purchase - purchaseReturn + freight + tax + otherExp + payments;
            decimal netProfit = netIncome - totalExp;
            TxtPL_AllTimeNetProfit.Text = netProfit.ToString("N2");
            TxtPL_AllTimeNetProfit.Foreground = NetColor(netProfit);
        }

        private void LoadProfitAndLoss(DateTime? from, DateTime? to)
        {
            string f = from?.ToString("yyyy-MM-dd");
            string t = to?.ToString("yyyy-MM-dd");
            string W(string col)
            {
                if (from == null && to == null) return "";
                if (from != null && to != null) return $" AND {col} BETWEEN '{f}' AND '{t}'";
                if (from != null) return $" AND {col} >= '{f}'";
                return $" AND {col} <= '{t}'";
            }

            using var con = new SQLiteConnection(_dbPath);
            con.Open();

            // Income — CashReceipt uses ReceiptDate
            decimal sales = GetScalar(con, $"SELECT IFNULL(SUM(NetTotal),0)     FROM Sales          WHERE 1=1{W("InvoiceDate")}");
            decimal cashIn = GetScalar(con, $"SELECT IFNULL(SUM(Amount),0)        FROM CashReceipt    WHERE 1=1{W("Date")}");
            decimal salesReturn = GetScalar(con, $"SELECT IFNULL(SUM(NetAmount),0)     FROM SalesReturn    WHERE 1=1{W("ReturnDate")}");
            decimal netIncome = sales + cashIn - salesReturn;

            // Expenses — PaymentReceipt uses ReceiptDate
            decimal purchase = GetScalar(con, $"SELECT IFNULL(SUM(NetTotal - IFNULL(Tax,0) - IFNULL(Freight,0)),0) FROM PurchaseInvoice WHERE 1=1{W("InvoiceDate")}");
            decimal purchaseReturn = GetScalar(con, $"SELECT IFNULL(SUM(NetAmount),0)      FROM PurchaseReturn  WHERE 1=1{W("ReturnDate")}");
            decimal freight = GetScalar(con, $"SELECT IFNULL(SUM(IFNULL(Freight,0)),0) FROM PurchaseInvoice WHERE 1=1{W("InvoiceDate")}");
            decimal tax = GetScalar(con, $"SELECT IFNULL(SUM(IFNULL(Tax,0)),0)    FROM PurchaseInvoice WHERE 1=1{W("InvoiceDate")}");
            decimal otherExp = GetScalar(con, $"SELECT IFNULL(SUM(Amount),0) FROM ManualExpenses  WHERE 1=1{W("Date")}");
            decimal payments = GetScalar(con, $"SELECT IFNULL(SUM(Amount),0) FROM PaymentReceipt  WHERE 1=1{W("Date")}");
            decimal totalExpenses = purchase - purchaseReturn + freight + tax + otherExp + payments;
            decimal netProfit = netIncome - totalExpenses;

            // Balance sheet — manual assets only, manual+payable liabilities
            decimal assets = GetScalar(con, "SELECT IFNULL(SUM(Value),0)  FROM ManualAssets");
            decimal liabilities = GetScalar(con, "SELECT IFNULL(SUM(Amount),0) FROM ManualLiabilities");
            // Add payable balances to liabilities
            decimal payableBal = GetScalar(con, @"
                SELECT IFNULL(SUM(IFNULL((
                    SELECT Balance FROM CustomerLedger
                    WHERE PartyId=p.Id ORDER BY Id DESC LIMIT 1),0)),0)
                FROM Parties p WHERE p.Type='Payable'");
            liabilities += payableBal;
            decimal netWorth = assets - liabilities;

            // Update P&L UI
            TxtPL_Sales.Text = sales.ToString("N2");
            TxtPL_SReturn.Text = salesReturn.ToString("N2");
            TxtPL_NetIncome.Text = netIncome.ToString("N2");
            TxtPL_Purchase.Text = purchase.ToString("N2");
            TxtPL_PReturn.Text = purchaseReturn.ToString("N2");
            TxtPL_Freight.Text = freight.ToString("N2");
            TxtPL_Tax.Text = tax.ToString("N2");
            TxtPL_OtherExp.Text = (otherExp + payments).ToString("N2");
            TxtPL_TotalExpenses.Text = totalExpenses.ToString("N2");
            TxtPL_NetProfit.Text = netProfit.ToString("N2");
            TxtPL_Assets.Text = assets.ToString("N2");
            TxtPL_Liabilities.Text = liabilities.ToString("N2");
            TxtPL_NetWorth.Text = netWorth.ToString("N2");

            // Also update asset/liability totals so balance sheet summary stays in sync
            TxtTotalAssets.Text = assets.ToString("N2");
            TxtTotalLiabilities.Text = liabilities.ToString("N2");

            TxtPL_NetProfit.Foreground = NetColor(netProfit);
            TxtPL_NetWorth.Foreground = NetColor(netWorth);
        }

        private static Brush NetColor(decimal v) =>
            v >= 0
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BFA5"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF5350"));

        private decimal GetScalar(SQLiteConnection con, string sql)
        {
            try
            {
                using var cmd = new SQLiteCommand(sql, con);
                var res = cmd.ExecuteScalar();
                return (res == null || res == DBNull.Value) ? 0m : Convert.ToDecimal(res);
            }
            catch { return 0m; }
        }

        // ════════════════════════════════════════════════════════════════════════
        // LEDGER TAB — all column names fixed for updated receipt tables
        // ════════════════════════════════════════════════════════════════════════
        private void BtnShowLedger_Click(object sender, RoutedEventArgs e)
        {
            if (DtLedgerFrom.SelectedDate == null || DtLedgerTo.SelectedDate == null)
            { MessageBox.Show("Please select both From and To dates."); return; }

            var from = DtLedgerFrom.SelectedDate.Value.Date;
            var to = DtLedgerTo.SelectedDate.Value.Date;

            bool showDebit = RbLedgerDebit.IsChecked == true;
            bool showCredit = RbLedgerCredit.IsChecked == true;
            bool showBoth = RbLedgerBoth.IsChecked == true;

            var rows = BuildLedgerRows(from, to);

            if (showDebit) rows = rows.Where(r => r.DrCr == "DR").ToList();
            else if (showCredit) rows = rows.Where(r => r.DrCr == "CR").ToList();

            decimal running = 0;
            foreach (var row in rows) { running += row.Credit - row.Debit; row.Balance = running; }

            DgLedger.ItemsSource = rows;

            decimal td = rows.Sum(r => r.Debit);
            decimal tc = rows.Sum(r => r.Credit);
            decimal nb = tc - td;

            TxtLedgerTotalDebit.Text = td.ToString("N2");
            TxtLedgerTotalCredit.Text = tc.ToString("N2");
            TxtLedgerNetBalance.Text = nb.ToString("N2");

            string filter = showBoth ? "Debit + Credit" : showDebit ? "Debit only" : "Credit only";
            TxtLedgerInfo.Text = $"{rows.Count} entries  ·  {from:dd-MM-yyyy} → {to:dd-MM-yyyy}  ·  {filter}";
        }

        private List<LedgerDrCrRow> BuildLedgerRows(DateTime from, DateTime to)
        {
            var rows = new List<LedgerDrCrRow>();
            string f = from.ToString("yyyy-MM-dd");
            string t = to.ToString("yyyy-MM-dd");

            using var con = new SQLiteConnection(_dbPath);
            con.Open();

            // ── DEBIT (money out / expenses) ─────────────────────────────────────

            // Purchase Invoices
            using (var cmd = new SQLiteCommand($@"
                SELECT pi.InvoiceDate, pi.InvoiceNo,
                       pi.NetTotal - IFNULL(pi.Tax,0) - IFNULL(pi.Freight,0) AS PurchAmt,
                       IFNULL(pi.Freight,0) AS FreightAmt,
                       IFNULL(pi.Tax,0)     AS TaxAmt,
                       pi.Supplier
                FROM PurchaseInvoice pi
                WHERE pi.InvoiceDate BETWEEN '{f}' AND '{t}'
                ORDER BY pi.InvoiceDate", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    string date = r["InvoiceDate"].ToString();
                    string inv = r["InvoiceNo"].ToString();
                    string sup = r["Supplier"]?.ToString() ?? "";
                    decimal pa = Convert.ToDecimal(r["PurchAmt"]);
                    decimal fa = Convert.ToDecimal(r["FreightAmt"]);
                    decimal ta = Convert.ToDecimal(r["TaxAmt"]);
                    if (pa > 0) rows.Add(MakeDr("Purchase", inv, sup, "Purchase Cost", date, pa));
                    if (fa > 0) rows.Add(MakeDr("Freight", inv, sup, "Freight Charge", date, fa));
                    if (ta > 0) rows.Add(MakeDr("Tax", inv, sup, "Tax Paid", date, ta));
                }

            // Payment Receipts → DR — FIXED: ReceiptDate, ReceiptNo
            using (var cmd = new SQLiteCommand($@"
                SELECT pr.Date, pr.VoucherNo, pr.Amount, p.Name
                FROM PaymentReceipt pr
                LEFT JOIN Parties p ON p.Id = pr.PartyId
                WHERE pr.Date BETWEEN '{f}' AND '{t}'
                ORDER BY pr.Date", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(MakeDr("Payment Receipt", r["VoucherNo"].ToString(),
                        r["Name"]?.ToString() ?? "", "Payment Made",
                        r["Date"].ToString(), r["Amount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Amount"])));

            // Manual Expenses → DR
            using (var cmd = new SQLiteCommand($@"
                SELECT Id, Date, Category, Amount, Description
                FROM ManualExpenses WHERE Date BETWEEN '{f}' AND '{t}'
                ORDER BY Date", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(MakeDr("Manual Expense", "",
                        r["Category"].ToString(), r["Description"].ToString(),
                        r["Date"].ToString(), Convert.ToDecimal(r["Amount"])));

            // Sales Returns → DR (contra income)
            using (var cmd = new SQLiteCommand($@"
                SELECT sr.ReturnDate, sr.ReturnNo, sr.NetAmount, p.Name
                FROM SalesReturn sr LEFT JOIN Parties p ON p.Id = sr.PartyId
                WHERE sr.ReturnDate BETWEEN '{f}' AND '{t}'
                ORDER BY sr.ReturnDate", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(MakeDr("Sales Return", r["ReturnNo"].ToString(),
                        r["Name"]?.ToString() ?? "", "(-) Sales Return",
                        r["ReturnDate"].ToString(), r["NetAmount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["NetAmount"])));

            // ── CREDIT (money in / income) ───────────────────────────────────────

            // Sales Invoices → CR
            using (var cmd = new SQLiteCommand($@"
                SELECT s.InvoiceDate, s.InvoiceNo, s.NetTotal, p.Name
                FROM Sales s LEFT JOIN Parties p ON p.Id = s.PartyId
                WHERE s.InvoiceDate BETWEEN '{f}' AND '{t}'
                ORDER BY s.InvoiceDate", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(MakeCr("Sales Invoice", r["InvoiceNo"].ToString(),
                        r["Name"]?.ToString() ?? "", "Sales Revenue",
                        r["InvoiceDate"].ToString(), r["NetTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(r["NetTotal"])));

            // Cash Receipts → CR — FIXED: ReceiptDate, ReceiptNo
            using (var cmd = new SQLiteCommand($@"
                SELECT cr.Date, cr.VoucherNo, cr.Amount, p.Name
                FROM CashReceipt cr LEFT JOIN Parties p ON p.Id = cr.PartyId
                WHERE cr.Date BETWEEN '{f}' AND '{t}'
                ORDER BY cr.Date", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(MakeCr("Cash Receipt", r["VoucherNo"].ToString(),
                        r["Name"]?.ToString() ?? "", "Cash Received",
                        r["Date"].ToString(), r["Amount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Amount"])));

            // Purchase Returns → CR (contra expense)
            using (var cmd = new SQLiteCommand($@"
                SELECT pr.ReturnDate, pr.ReturnNo, pr.NetAmount, p.Name
                FROM PurchaseReturn pr LEFT JOIN Parties p ON p.Id = pr.SupplierId
                WHERE pr.ReturnDate BETWEEN '{f}' AND '{t}'
                ORDER BY pr.ReturnDate", con))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    rows.Add(MakeCr("Purchase Return", r["ReturnNo"].ToString(),
                        r["Name"]?.ToString() ?? "", "(-) Purchase Return",
                        r["ReturnDate"].ToString(), r["NetAmount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["NetAmount"])));

            // Sort and compute running balance
            rows = rows.OrderBy(r => r.Date).ToList();
            decimal bal = 0;
            foreach (var row in rows) { bal += row.Credit - row.Debit; row.Balance = bal; }
            return rows;
        }

        private LedgerDrCrRow MakeDr(string type, string ref_, string party, string desc, string date, decimal amt) =>
            new() { Date = date, EntryType = type, DrCr = "DR", Reference = ref_, Party = party, Description = desc, Debit = amt, Credit = 0 };

        private LedgerDrCrRow MakeCr(string type, string ref_, string party, string desc, string date, decimal amt) =>
            new() { Date = date, EntryType = type, DrCr = "CR", Reference = ref_, Party = party, Description = desc, Debit = 0, Credit = amt };

        // ════════════════════════════════════════════════════════════════════════
        // ADD MANUAL ENTRIES
        // ════════════════════════════════════════════════════════════════════════
        private void BtnAddExpense_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TxtExpenseAmount.Text, out decimal amt) || amt <= 0)
            { MessageBox.Show("Enter valid amount."); return; }
            string cat = (CmbExpenseCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? CmbExpenseCategory.Text;
            if (string.IsNullOrWhiteSpace(cat)) { MessageBox.Show("Select or type a category."); return; }
            string pm = (CmbExpensePayMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";

            using var con = new SQLiteConnection(_dbPath);
            con.Open();
            using var cmd = new SQLiteCommand(@"INSERT INTO ManualExpenses (Date,Category,Amount,Description,PaymentMode,CreatedAt) VALUES (@d,@cat,@amt,@desc,@pm,@now)", con);
            cmd.Parameters.AddWithValue("@d", DtExpenseDate.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@cat", cat);
            cmd.Parameters.AddWithValue("@amt", amt);
            cmd.Parameters.AddWithValue("@desc", TxtExpenseDesc.Text.Trim());
            cmd.Parameters.AddWithValue("@pm", pm);
            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();

            TxtExpenseAmount.Clear(); TxtExpenseDesc.Clear();
            LoadExpenses(); LoadProfitAndLoss(_plFrom, _plTo);
        }

        private void BtnAddAsset_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TxtAssetValue.Text, out decimal val) || val <= 0)
            { MessageBox.Show("Enter valid value."); return; }
            string atype = (CmbAssetType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? CmbAssetType.Text;
            if (string.IsNullOrWhiteSpace(atype)) { MessageBox.Show("Select or type an asset type."); return; }

            using var con = new SQLiteConnection(_dbPath);
            con.Open();
            using var cmd = new SQLiteCommand(@"INSERT INTO ManualAssets (Date,AssetType,Value,Description,CreatedAt) VALUES (@d,@type,@val,@desc,@now)", con);
            cmd.Parameters.AddWithValue("@d", DtAssetDate.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@type", atype);
            cmd.Parameters.AddWithValue("@val", val);
            cmd.Parameters.AddWithValue("@desc", TxtAssetDesc.Text.Trim());
            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();

            TxtAssetValue.Clear(); TxtAssetDesc.Clear();
            LoadAssets(); LoadProfitAndLoss(_plFrom, _plTo);
        }

        private void BtnAddLiability_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TxtLiabilityAmount.Text, out decimal amt) || amt <= 0)
            { MessageBox.Show("Enter valid amount."); return; }
            string ltype = (CmbLiabilityType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? CmbLiabilityType.Text;
            if (string.IsNullOrWhiteSpace(ltype)) { MessageBox.Show("Select or type a liability type."); return; }

            using var con = new SQLiteConnection(_dbPath);
            con.Open();
            using var cmd = new SQLiteCommand(@"INSERT INTO ManualLiabilities (Date,LiabilityType,Amount,Description,CreatedAt) VALUES (@d,@type,@amt,@desc,@now)", con);
            cmd.Parameters.AddWithValue("@d", DtLiabilityDate.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@type", ltype);
            cmd.Parameters.AddWithValue("@amt", amt);
            cmd.Parameters.AddWithValue("@desc", TxtLiabilityDesc.Text.Trim());
            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();

            TxtLiabilityAmount.Clear(); TxtLiabilityDesc.Clear();
            LoadLiabilities(); LoadProfitAndLoss(_plFrom, _plTo);
        }

        // ── Delete Manual Entries ────────────────────────────────────────────────
        private void BtnDeleteExpense_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int id) && id > 0)
            {
                if (MessageBox.Show("Delete this expense?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                using var con = new SQLiteConnection(_dbPath); con.Open();
                new SQLiteCommand($"DELETE FROM ManualExpenses WHERE Id={id}", con).ExecuteNonQuery();
                LoadExpenses(); LoadProfitAndLoss(_plFrom, _plTo);
            }
        }

        private void BtnDeleteAsset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int id) && id > 0)
            {
                if (MessageBox.Show("Delete this asset?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                using var con = new SQLiteConnection(_dbPath); con.Open();
                new SQLiteCommand($"DELETE FROM ManualAssets WHERE Id={id}", con).ExecuteNonQuery();
                LoadAssets(); LoadProfitAndLoss(_plFrom, _plTo);
            }
        }

        private void BtnDeleteLiability_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int id) && id > 0)
            {
                if (MessageBox.Show("Delete this liability?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                using var con = new SQLiteConnection(_dbPath); con.Open();
                new SQLiteCommand($"DELETE FROM ManualLiabilities WHERE Id={id}", con).ExecuteNonQuery();
                LoadLiabilities(); LoadProfitAndLoss(_plFrom, _plTo);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // PRINT
        // ════════════════════════════════════════════════════════════════════════
        private void BtnPrintPL_Click(object sender, RoutedEventArgs e) => ShowPreview(BuildPLFlowDocument());
        private void BtnPrintLedger_Click(object sender, RoutedEventArgs e)
        {
            var rows = DgLedger.ItemsSource as List<LedgerDrCrRow>;
            if (rows == null || rows.Count == 0) { MessageBox.Show("No ledger data. Click 'Show Ledger' first."); return; }
            ShowPreview(BuildLedgerFlowDocument(rows));
        }

        private FlowDocument BuildPLFlowDocument()
        {
            string period = (_plFrom == null && _plTo == null) ? "All Time" : $"{_plFrom:dd-MM-yyyy} – {_plTo:dd-MM-yyyy}";
            var doc = new FlowDocument { PagePadding = new Thickness(50), ColumnWidth = double.PositiveInfinity, FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };

            var hb = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 16) };
            var hp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            hp.Children.Add(new TextBlock { Text = "Hale Marketing International - Multan", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hp.Children.Add(new TextBlock { Text = "Phone: 92-306-1917073", FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hb.Child = hp; doc.Blocks.Add(new BlockUIContainer(hb));

            var title = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
            title.Inlines.Add(new Run($"PROFIT & LOSS STATEMENT — {period.ToUpper()}") { FontSize = 16, FontWeight = FontWeights.Bold });
            title.Inlines.Add(new Run($"\nPrinted: {DateTime.Now:dd-MM-yyyy hh:mm tt}") { FontSize = 11 });
            doc.Blocks.Add(title);

            void AddSection(string heading, Brush color, string[] labels, string[] values)
            {
                var p = new Paragraph { Margin = new Thickness(0, 8, 0, 4) };
                p.Inlines.Add(new Run(heading) { FontSize = 13, FontWeight = FontWeights.Bold, Foreground = color });
                doc.Blocks.Add(p);
                var table = new Table { CellSpacing = 0, BorderThickness = new Thickness(1), BorderBrush = Brushes.LightGray };
                table.Columns.Add(new TableColumn { Width = new GridLength(300) });
                table.Columns.Add(new TableColumn { Width = new GridLength(150) });
                var grp = new TableRowGroup();
                for (int i = 0; i < labels.Length; i++)
                {
                    var row = new TableRow();
                    bool isTot = labels[i].StartsWith("─") || labels[i].StartsWith("Net") || labels[i].StartsWith("Total");
                    var bg = isTot ? Brushes.WhiteSmoke : Brushes.White;
                    var fw = isTot ? FontWeights.Bold : FontWeights.Normal;
                    row.Cells.Add(new TableCell(new Paragraph(new Run(labels[i])) { FontWeight = fw }) { Padding = new Thickness(6, 4, 6, 4), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brushes.WhiteSmoke, Background = bg });
                    row.Cells.Add(new TableCell(new Paragraph(new Run(values[i])) { TextAlignment = TextAlignment.Right, FontWeight = fw }) { Padding = new Thickness(6, 4, 6, 4), BorderThickness = new Thickness(1, 0, 0, 1), BorderBrush = Brushes.WhiteSmoke, Background = bg });
                    grp.Rows.Add(row);
                }
                table.RowGroups.Add(grp); doc.Blocks.Add(table);
            }

            AddSection("INCOME", Brushes.DarkGreen,
                new[] { "Sales Revenue", "(-) Sales Return", "Cash Receipt", "──────────────", "Net Income" },
                new[] { TxtPL_Sales.Text, TxtPL_SReturn.Text, "included", "", TxtPL_NetIncome.Text });

            AddSection("EXPENSES", Brushes.DarkRed,
                new[] { "Purchase Cost", "(-) Purchase Returns", "Freight Paid", "Tax Paid", "Other Expenses + Payments", "──────────────", "Total Expenses" },
                new[] { TxtPL_Purchase.Text, TxtPL_PReturn.Text, TxtPL_Freight.Text, TxtPL_Tax.Text, TxtPL_OtherExp.Text, "", TxtPL_TotalExpenses.Text });

            decimal.TryParse(TxtPL_NetProfit.Text, out decimal np);
            var pb = new Border { Background = np >= 0 ? Brushes.DarkGreen : Brushes.DarkRed, CornerRadius = new CornerRadius(4), Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 4) };
            var pg = new Grid();
            pg.Children.Add(new TextBlock { Text = "NET PROFIT / (LOSS)", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14 });
            pg.Children.Add(new TextBlock { Text = TxtPL_NetProfit.Text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Right });
            pb.Child = pg; doc.Blocks.Add(new BlockUIContainer(pb));

            if (_plFrom != null)
            {
                var ab = new Border { Background = Brushes.LightGray, CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) };
                var ag = new Grid();
                ag.Children.Add(new TextBlock { Text = "NET PROFIT / (LOSS) — ALL TIME", Foreground = Brushes.DarkSlateGray, FontWeight = FontWeights.Bold, FontSize = 12 });
                ag.Children.Add(new TextBlock { Text = TxtPL_AllTimeNetProfit.Text, Foreground = Brushes.DarkSlateGray, FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Right });
                ab.Child = ag; doc.Blocks.Add(new BlockUIContainer(ab));
            }

            AddSection("BALANCE SHEET SUMMARY (Manual Assets Only)", Brushes.DarkBlue,
                new[] { "Total Manual Assets", "Total Liabilities", "──────────────", "Net Worth" },
                new[] { TxtPL_Assets.Text, TxtPL_Liabilities.Text, "", TxtPL_NetWorth.Text });

            return doc;
        }

        private FlowDocument BuildLedgerFlowDocument(List<LedgerDrCrRow> rows)
        {
            string from = DtLedgerFrom.SelectedDate?.ToString("dd-MM-yyyy") ?? "";
            string to = DtLedgerTo.SelectedDate?.ToString("dd-MM-yyyy") ?? "";
            string filterLabel = RbLedgerDebit.IsChecked == true ? "Debit Only" : RbLedgerCredit.IsChecked == true ? "Credit Only" : "Debit + Credit";

            var doc = new FlowDocument { PagePadding = new Thickness(40), ColumnWidth = double.PositiveInfinity, FontFamily = new FontFamily("Segoe UI"), FontSize = 11 };

            var hb = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) };
            var hp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            hp.Children.Add(new TextBlock { Text = "Hale Marketing International - Multan", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hp.Children.Add(new TextBlock { Text = "Phone: 92-306-1917073", FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hb.Child = hp; doc.Blocks.Add(new BlockUIContainer(hb));

            var title = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
            title.Inlines.Add(new Run($"GENERAL LEDGER — {from} to {to}  [{filterLabel}]") { FontSize = 14, FontWeight = FontWeights.Bold });
            title.Inlines.Add(new Run($"\nPrinted: {DateTime.Now:dd-MM-yyyy hh:mm tt}") { FontSize = 10 });
            doc.Blocks.Add(title);

            var table = new Table { CellSpacing = 0, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
            foreach (var w in new double[] { 72, 70, 36, 80, 80, 110, 80, 80, 70 })
                table.Columns.Add(new TableColumn { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

            var hg = new TableRowGroup();
            var hr = new TableRow { Background = Brushes.DimGray };
            foreach (var h in new[] { "DATE", "TYPE", "DR/CR", "REF", "PARTY", "DESCRIPTION", "DEBIT", "CREDIT", "BALANCE" })
                hr.Cells.Add(new TableCell(new Paragraph(new Run(h)) { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center })
                { Foreground = Brushes.White, Padding = new Thickness(4, 3, 4, 3), BorderThickness = new Thickness(0, 0, 1, 1), BorderBrush = Brushes.Gray });
            hg.Rows.Add(hr); table.RowGroups.Add(hg);

            var dg = new TableRowGroup();
            bool alt = false;
            foreach (var row in rows)
            {
                var tr = new TableRow { Background = alt ? new SolidColorBrush(Color.FromRgb(248, 248, 248)) : Brushes.White };
                alt = !alt;
                Brush drcBrush = row.DrCr == "DR" ? Brushes.DarkRed : Brushes.DarkGreen;

                void Cell(string txt, TextAlignment a = TextAlignment.Left, Brush fg = null, bool bold = false)
                    => tr.Cells.Add(new TableCell(new Paragraph(new Run(txt ?? "")) { TextAlignment = a, Foreground = fg ?? Brushes.Black, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal })
                    { Padding = new Thickness(4, 2, 4, 2), BorderThickness = new Thickness(0, 0, 1, 1), BorderBrush = Brushes.LightGray });

                Cell(row.Date); Cell(row.EntryType); Cell(row.DrCr, TextAlignment.Center, drcBrush, true);
                Cell(row.Reference); Cell(row.Party); Cell(row.Description);
                Cell(row.Debit > 0 ? row.Debit.ToString("N2") : "", TextAlignment.Right, Brushes.DarkRed);
                Cell(row.Credit > 0 ? row.Credit.ToString("N2") : "", TextAlignment.Right, Brushes.DarkGreen);
                Cell(row.Balance.ToString("N2"), TextAlignment.Right, row.Balance >= 0 ? Brushes.DarkGreen : Brushes.DarkRed, true);
                dg.Rows.Add(tr);
            }
            table.RowGroups.Add(dg);

            decimal.TryParse(TxtLedgerTotalDebit.Text, out decimal td);
            decimal.TryParse(TxtLedgerTotalCredit.Text, out decimal tc);
            decimal.TryParse(TxtLedgerNetBalance.Text, out decimal nb);

            var tg = new TableRowGroup();
            var totRow = new TableRow { Background = Brushes.LightGray };
            foreach (var cell in new[] { "", "", "", "", "", "TOTALS", td.ToString("N2"), tc.ToString("N2"), nb.ToString("N2") })
                totRow.Cells.Add(new TableCell(new Paragraph(new Run(cell)) { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right })
                { Padding = new Thickness(4, 3, 4, 3), BorderThickness = new Thickness(0, 1, 1, 0), BorderBrush = Brushes.Gray });
            tg.Rows.Add(totRow); table.RowGroups.Add(tg);
            doc.Blocks.Add(table);
            return doc;
        }

        private void ShowPreview(FlowDocument doc)
        {
            var ms = new MemoryStream();
            var pkg = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite);
            string uri = "pack://doc-" + Guid.NewGuid().ToString("N") + ".xps";
            PackageStore.AddPackage(new Uri(uri), pkg);
            var xps = new XpsDocument(pkg, CompressionOption.Maximum, uri);
            XpsDocument.CreateXpsDocumentWriter(xps).Write(((IDocumentPaginatorSource)doc).DocumentPaginator);
            var fd = xps.GetFixedDocumentSequence();

            var viewer = new DocumentViewer { Document = fd };
            var btnPrint = new Button { Content = "🖨 Print", Width = 100, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(0, 191, 165)), Foreground = new SolidColorBrush(Color.FromRgb(12, 21, 32)), FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            var btnPDF = new Button { Content = "📄 Save PDF", Width = 110, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnFit = new Button { Content = "⬛ Fit", Width = 80, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnZoomIn = new Button { Content = "＋", Width = 36, Margin = new Thickness(4, 6, 0, 6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnZoomOut = new Button { Content = "－", Width = 36, Margin = new Thickness(0, 6, 6, 6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };

            btnPrint.Click += (s, ev) => { var d = new PrintDialog(); if (d.ShowDialog() == true) d.PrintDocument(fd.DocumentPaginator, "Document"); };
            btnPDF.Click += (s, ev) => { var d = new PrintDialog(); try { d.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), "Microsoft Print to PDF"); } catch { } if (d.ShowDialog() == true) d.PrintDocument(fd.DocumentPaginator, "Document"); };
            btnFit.Click += (s, ev) => viewer.FitToWidth();
            btnZoomIn.Click += (s, ev) => viewer.IncreaseZoom();
            btnZoomOut.Click += (s, ev) => viewer.DecreaseZoom();

            var tb = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(12, 21, 32)), HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var b in new[] { btnZoomOut, btnZoomIn, btnFit, btnPDF, btnPrint }) tb.Children.Add(b);

            var panel = new DockPanel();
            DockPanel.SetDock(tb, Dock.Top);
            panel.Children.Add(tb);
            panel.Children.Add(viewer);

            var wnd = new Window { Title = "Print Preview", Width = 1050, Height = 820, Content = panel, Background = new SolidColorBrush(Color.FromRgb(17, 29, 43)), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            wnd.Closed += (s, ev) => { xps.Close(); PackageStore.RemovePackage(new Uri(uri)); ms.Close(); };
            wnd.ShowDialog();
        }
    }
}