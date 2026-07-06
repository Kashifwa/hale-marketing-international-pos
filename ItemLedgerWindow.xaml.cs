using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace Hale_Marketing_International
{
    public partial class ItemLedgerWindow : Window
    {
        private string _dbPath = AppConfig.ConnectionString;

        // Currently selected product
        private int _selectedProductId = 0;
        private string _selectedProductName = "";
        private string _selectedProductCode = "";
        private decimal _selectedPurchasePrice = 0;
        private decimal _selectedSalePrice = 0;

        public ItemLedgerWindow()
        {
            InitializeComponent();
            DtFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DtTo.SelectedDate = DateTime.Today;
            LoadProducts();
        }

        // ── Product Model ────────────────────────────────────────────────────────
        private class ProductItem
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ProductCode { get; set; }
            public decimal PurchasePrice { get; set; }
            public decimal SalePrice { get; set; }
            public override string ToString() => ProductName;
        }

        // ── Ledger Row Model ─────────────────────────────────────────────────────
        public class ItemLedgerEntry
        {
            public string Date { get; set; }
            public string Type { get; set; }
            public string InvoiceNo { get; set; }
            public string PartyName { get; set; }
            public string StockIn { get; set; }
            public string StockOut { get; set; }
            public decimal BalanceQty { get; set; }
            public string Rate { get; set; }
            public string Amount { get; set; }
        }

        // ── Load Products into ComboBox ──────────────────────────────────────────
        private void LoadProducts(string filter = "")
        {
            try
            {
                CmbProduct.Items.Clear();
                using (var conn = new SQLiteConnection(_dbPath))
                {
                    conn.Open();
                    string query = @"SELECT Id, ProductName, Code, PurchasePrice, SalePrice 
                                     FROM Products 
                                     WHERE LOWER(ProductName) LIKE @f 
                                        OR LOWER(Code) LIKE @f
                                     ORDER BY ProductName";
                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@f", "%" + filter.ToLower() + "%");
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                CmbProduct.Items.Add(new ProductItem
                                {
                                    ProductId = Convert.ToInt32(r["Id"]),
                                    ProductName = r["ProductName"]?.ToString() ?? "",
                                    ProductCode = r["Code"]?.ToString() ?? "",
                                    PurchasePrice = r["PurchasePrice"] == DBNull.Value ? 0 : Convert.ToDecimal(r["PurchasePrice"]),
                                    SalePrice = r["SalePrice"] == DBNull.Value ? 0 : Convert.ToDecimal(r["SalePrice"])
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading products: " + ex.Message);
            }
        }

        private void CmbProduct_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbProduct.SelectedItem is ProductItem p)
            {
                _selectedProductId = p.ProductId;
                _selectedProductName = p.ProductName;
                _selectedProductCode = p.ProductCode;
                _selectedPurchasePrice = p.PurchasePrice;
                _selectedSalePrice = p.SalePrice;

                // Update info strip
                TxtProductName.Text = p.ProductName;
                TxtProductCode.Text = p.ProductCode;
                TxtPurchasePrice.Text = p.PurchasePrice.ToString("N2");
                TxtSalePrice.Text = p.SalePrice.ToString("N2");
            }
        }

        private void CmbProduct_KeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                string filter = CmbProduct.Text?.Trim() ?? "";
                LoadProducts(filter);
                CmbProduct.IsDropDownOpen = true;
            }
            catch { }
        }

        // ── Show Ledger Button ───────────────────────────────────────────────────
        private void BtnShowLedger_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProductId == 0)
            {
                MessageBox.Show("Please select a product first.");
                return;
            }

            DateTime from = DtFrom.SelectedDate ?? DateTime.MinValue;
            DateTime to = DtTo.SelectedDate ?? DateTime.MaxValue;

            var entries = GetItemLedger(_selectedProductId, _selectedProductName, from, to);
            DgItemLedger.ItemsSource = entries;

            // ✅ FIXED: skip "-" strings when summing
            decimal totalIn = entries.Sum(x => x.StockIn != "-" && decimal.TryParse(x.StockIn, out decimal v1) ? v1 : 0);
            decimal totalOut = entries.Sum(x => x.StockOut != "-" && decimal.TryParse(x.StockOut, out decimal v2) ? v2 : 0);
            decimal closing = entries.LastOrDefault()?.BalanceQty ?? 0;
            decimal netAmt = entries.Sum(x => decimal.TryParse(x.Amount, out decimal v3) ? v3 : 0);

            TxtTotalIn.Text = totalIn.ToString("N0");
            TxtTotalOut.Text = totalOut.ToString("N0");
            TxtClosingBalance.Text = closing.ToString("N0");
            TxtNetAmount.Text = netAmt.ToString("N2");
        }

        // ── Core Ledger Query ────────────────────────────────────────────────────
        private List<ItemLedgerEntry> GetItemLedger(int productId, string productName, DateTime from, DateTime to)
        {
            var entries = new List<ItemLedgerEntry>();
            string fromStr = from.ToString("yyyy-MM-dd");
            string toStr = to.ToString("yyyy-MM-dd");

            using var con = new SQLiteConnection(_dbPath);
            con.Open();

            // ── 1. Purchase Invoice → Stock IN ──────────────────────────────────────
            // Match by BOTH ProductId OR ProductName to catch all records
            // (older records may have ProductId=0 if product wasn't found by code)
            using (var cmd = new SQLiteCommand(@"
        SELECT pi.InvoiceDate, pi.InvoiceNo, pi.Supplier,
               pitem.Quantity, pitem.Rate, pitem.Amount
        FROM PurchaseItems pitem
        INNER JOIN PurchaseInvoice pi ON pi.InvoiceNo = pitem.InvoiceNo
        WHERE (LOWER(pitem.ProductName) = LOWER(@name))
          AND pi.InvoiceDate BETWEEN @from AND @to
        ORDER BY pi.InvoiceDate", con))
            {
                //cmd.Parameters.AddWithValue("@pid", productId);
                cmd.Parameters.AddWithValue("@name", productName);
                cmd.Parameters.AddWithValue("@from", fromStr);
                cmd.Parameters.AddWithValue("@to", toStr);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    entries.Add(new ItemLedgerEntry
                    {
                        Date = r["InvoiceDate"].ToString(),
                        Type = "Purchase",
                        InvoiceNo = r["InvoiceNo"].ToString(),
                        PartyName = r["Supplier"]?.ToString() ?? "",
                        StockIn = r["Quantity"] == DBNull.Value ? "-" : Convert.ToDecimal(r["Quantity"]).ToString("N0"),
                        StockOut = "-",
                        Rate = r["Rate"] == DBNull.Value ? "0.00" : Convert.ToDecimal(r["Rate"]).ToString("N2"),
                        Amount = r["Amount"] == DBNull.Value ? "0.00" : Convert.ToDecimal(r["Amount"]).ToString("N2")
                    });
            }

            // ── 2. Purchase Return → Stock OUT ──────────────────────────────────────
            // Match by BOTH ProductId OR ProductName
            using (var cmd = new SQLiteCommand(@"
        SELECT pr.ReturnDate, pr.ReturnNo, p.Name AS SupplierName,
               pri.ReturnQty, pri.Rate, pri.Amount
        FROM PurchaseReturnItems pri
        INNER JOIN PurchaseReturn pr ON pr.Id  = pri.ReturnId
        INNER JOIN Parties p         ON p.Id   = pr.SupplierId
        WHERE (LOWER(pri.ProductName) = LOWER(@name))
          AND pr.ReturnDate BETWEEN @from AND @to
        ORDER BY pr.ReturnDate", con))
            {
               // cmd.Parameters.AddWithValue("@pid", productId);
                cmd.Parameters.AddWithValue("@name", productName);
                cmd.Parameters.AddWithValue("@from", fromStr);
                cmd.Parameters.AddWithValue("@to", toStr);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    entries.Add(new ItemLedgerEntry
                    {
                        Date = r["ReturnDate"].ToString(),
                        Type = "Purchase Return",
                        InvoiceNo = r["ReturnNo"].ToString(),
                        PartyName = r["SupplierName"]?.ToString() ?? "",
                        StockIn = "-",
                        StockOut = r["ReturnQty"] == DBNull.Value ? "-" : Convert.ToDecimal(r["ReturnQty"]).ToString("N0"),
                        Rate = r["Rate"] == DBNull.Value ? "0.00" : Convert.ToDecimal(r["Rate"]).ToString("N2"),
                        Amount = r["Amount"] == DBNull.Value ? "0.00" : Convert.ToDecimal(r["Amount"]).ToString("N2")
                    });
            }

            // ── 3. Sales Invoice → Stock OUT ────────────────────────────────────────
            // Match by BOTH ProductId OR ProductName
            using (var cmd = new SQLiteCommand(@"
        SELECT s.InvoiceDate, s.InvoiceNo, pa.Name AS CustomerName,
               si.Quantity, si.Rate, si.Amount
        FROM SalesItems si
        INNER JOIN Sales s    ON s.Id  = si.SaleId
        INNER JOIN Parties pa ON pa.Id = s.PartyId
        WHERE (LOWER(si.ProductName) = LOWER(@name))
          AND s.InvoiceDate BETWEEN @from AND @to
        ORDER BY s.InvoiceDate", con))
            {
                //cmd.Parameters.AddWithValue("@pid", productId);
                cmd.Parameters.AddWithValue("@name", productName);
                cmd.Parameters.AddWithValue("@from", fromStr);
                cmd.Parameters.AddWithValue("@to", toStr);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    entries.Add(new ItemLedgerEntry
                    {
                        Date = r["InvoiceDate"].ToString(),
                        Type = "Sale",
                        InvoiceNo = r["InvoiceNo"].ToString(),
                        PartyName = r["CustomerName"]?.ToString() ?? "",
                        StockIn = "-",
                        StockOut = r["Quantity"] == DBNull.Value ? "-" : Convert.ToDecimal(r["Quantity"]).ToString("N0"),
                        Rate = r["Rate"] == DBNull.Value ? "0.00" : Convert.ToDecimal(r["Rate"]).ToString("N2"),
                        Amount = r["Amount"] == DBNull.Value ? "0.00" : Convert.ToDecimal(r["Amount"]).ToString("N2")
                    });
            }

            // ── 4. Sales Return → Stock IN ──────────────────────────────────────────
            // Match by BOTH ProductId OR ProductName
            using (var cmd = new SQLiteCommand(@"
        SELECT sr.ReturnDate, sr.ReturnNo, pa.Name AS CustomerName,
               sri.ReturnQty, sri.Rate, sri.Amount
        FROM SalesReturnItems sri
        INNER JOIN SalesReturn sr ON sr.Id  = sri.ReturnId
        INNER JOIN Parties pa     ON pa.Id  = sr.PartyId
        WHERE (LOWER(sri.ProductName) = LOWER(@name))
          AND sr.ReturnDate BETWEEN @from AND @to
        ORDER BY sr.ReturnDate", con))
            {
               // cmd.Parameters.AddWithValue("@pid", productId);
                cmd.Parameters.AddWithValue("@name", productName);
                cmd.Parameters.AddWithValue("@from", fromStr);
                cmd.Parameters.AddWithValue("@to", toStr);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    entries.Add(new ItemLedgerEntry
                    {
                        Date = r["ReturnDate"].ToString(),
                        Type = "Sales Return",
                        InvoiceNo = r["ReturnNo"].ToString(),
                        PartyName = r["CustomerName"]?.ToString() ?? "",
                        StockIn = r["ReturnQty"] == DBNull.Value ? "-" : Convert.ToDecimal(r["ReturnQty"]).ToString("N0"),
                        StockOut = "-",
                        Rate = r["Rate"] == DBNull.Value ? "0.00" : Convert.ToDecimal(r["Rate"]).ToString("N2"),
                        Amount = r["Amount"] == DBNull.Value ? "0.00" : Convert.ToDecimal(r["Amount"]).ToString("N2")
                    });
            }

            // ── Sort by date then compute running balance ────────────────────────────
            entries = entries.OrderBy(x =>
            {
                DateTime.TryParse(x.Date, out DateTime d);
                return d;
            }).ToList();

            decimal balance = 0;
            foreach (var entry in entries)
            {
                // ✅ FIXED: only parse if not "-", otherwise treat as 0
                decimal stockIn = (entry.StockIn != "-" && decimal.TryParse(entry.StockIn, out decimal si)) ? si : 0;
                decimal stockOut = (entry.StockOut != "-" && decimal.TryParse(entry.StockOut, out decimal so)) ? so : 0;
                balance += stockIn - stockOut;
                entry.BalanceQty = balance;
            }

            return entries;
        }

        // ── Print Button ─────────────────────────────────────────────────────────
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            var entries = DgItemLedger.ItemsSource as List<ItemLedgerEntry>;
            if (entries == null || !entries.Any())
            {
                MessageBox.Show("No data to print. Please show the ledger first.");
                return;
            }

            FlowDocument doc = BuildItemLedgerFlowDocument(entries);
            ShowFlowDocumentPreview(doc);
        }

        // ── FlowDocument Builder ─────────────────────────────────────────────────
        private FlowDocument BuildItemLedgerFlowDocument(List<ItemLedgerEntry> entries)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(40),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11
            };

            // Header banner
            var headerBorder = new Border
            {
                Background = Brushes.Black,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var headerPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Hale Marketing International - Multan",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Phone: 92-306-1917073",
                FontSize = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            headerBorder.Child = headerPanel;
            doc.Blocks.Add(new BlockUIContainer(headerBorder));

            // Product info line
            var info = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            info.Inlines.Add(new Run($"Item Ledger:  {_selectedProductName}  |  Code: {_selectedProductCode}")
            { FontSize = 13, FontWeight = FontWeights.Bold });
            doc.Blocks.Add(info);

            var info2 = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            info2.Inlines.Add(new Run(
                $"Purchase Price: {_selectedPurchasePrice:N2}    " +
                $"Sale Price: {_selectedSalePrice:N2}"));
            doc.Blocks.Add(info2);

            var dateInfo = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
            dateInfo.Inlines.Add(new Run(
                $"From: {DtFrom.SelectedDate?.ToString("dd-MM-yyyy") ?? "All"}    " +
                $"To: {DtTo.SelectedDate?.ToString("dd-MM-yyyy") ?? "All"}    " +
                $"Printed: {DateTime.Now:dd-MM-yyyy hh:mm tt}"));
            doc.Blocks.Add(dateInfo);

            // Table
            var table = new Table
            {
                CellSpacing = 0,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Black
            };

            // Date | Type | Invoice No | Party | Stock In | Stock Out | Balance | Rate | Amount
            foreach (var w in new double[] { 72, 100, 95, 100, 65, 68, 70, 70, 90 })
                table.Columns.Add(new TableColumn { Width = new GridLength(w) });

            // Header row
            var hGroup = new TableRowGroup();
            var hRow = new TableRow();
            foreach (var h in new[] { "Date", "Type", "Invoice No", "Party Name", "Stock In", "Stock Out", "Balance", "Rate", "Amount" })
            {
                var cell = new TableCell(
                    new Paragraph(new Run(h))
                    {
                        FontWeight = FontWeights.Bold,
                        TextAlignment = TextAlignment.Center
                    })
                {
                    Background = Brushes.Black,
                    Padding = new Thickness(4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Black
                };
                ((Paragraph)cell.Blocks.FirstBlock).Inlines.OfType<Run>().First().Foreground = Brushes.White;
                hRow.Cells.Add(cell);
            }
            hGroup.Rows.Add(hRow);
            table.RowGroups.Add(hGroup);

            // Body rows
            var bGroup = new TableRowGroup();
            bool alternate = false;
            foreach (var entry in entries)
            {
                var row = new TableRow();
                var rowBg = alternate ? Brushes.WhiteSmoke : Brushes.White;
                alternate = !alternate;

                string[] vals =
                {
                    entry.Date,
                    entry.Type,
                    entry.InvoiceNo,
                    entry.PartyName,
                    entry.StockIn,
                    entry.StockOut,
                    entry.BalanceQty.ToString("N0"),
                    entry.Rate,
                    entry.Amount
                };

                for (int i = 0; i < vals.Length; i++)
                {
                    var align = (i >= 4) ? TextAlignment.Right : TextAlignment.Left;
                    var cell = new TableCell(
                        new Paragraph(new Run(vals[i])) { TextAlignment = align })
                    {
                        Background = rowBg,
                        Padding = new Thickness(4),
                        BorderThickness = new Thickness(1),
                        BorderBrush = Brushes.LightGray
                    };

                    // Color Stock In green, Stock Out red
                    if (i == 4 && vals[i] != "-")
                        ((Paragraph)cell.Blocks.FirstBlock).Inlines.OfType<Run>().First().Foreground = Brushes.DarkGreen;
                    if (i == 5 && vals[i] != "-")
                        ((Paragraph)cell.Blocks.FirstBlock).Inlines.OfType<Run>().First().Foreground = Brushes.DarkRed;

                    row.Cells.Add(cell);
                }
                bGroup.Rows.Add(row);
            }
            table.RowGroups.Add(bGroup);
            doc.Blocks.Add(table);

            // Summary
            decimal totalIn = entries.Sum(x => x.StockIn != "-" && decimal.TryParse(x.StockIn, out decimal v) ? v : 0);
            decimal totalOut = entries.Sum(x => x.StockOut != "-" && decimal.TryParse(x.StockOut, out decimal v) ? v : 0);
            decimal closing = entries.LastOrDefault()?.BalanceQty ?? 0;
            decimal netAmt = entries.Sum(x => decimal.TryParse(x.Amount, out decimal v) ? v : 0);

            var summary = new Paragraph
            {
                Margin = new Thickness(0, 12, 0, 0),
                TextAlignment = TextAlignment.Right
            };
            summary.Inlines.Add(new Run($"Total Stock In:   {totalIn:N0}\n"));
            summary.Inlines.Add(new Run($"Total Stock Out:  {totalOut:N0}\n"));
            summary.Inlines.Add(new Run($"Closing Balance:  {closing:N0} units\n") { FontWeight = FontWeights.Bold });
            summary.Inlines.Add(new Run($"Net Amount:       {netAmt:N2}\n") { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(summary);

            var footer = new Paragraph
            {
                Margin = new Thickness(0, 20, 0, 0),
                TextAlignment = TextAlignment.Center,
                FontSize = 11
            };
            footer.Inlines.Add(new Run("Hale Marketing International - Item Ledger Report"));
            doc.Blocks.Add(footer);

            return doc;
        }

        // ── XPS Preview Window ───────────────────────────────────────────────────
        private void ShowFlowDocumentPreview(FlowDocument doc)
        {
            MemoryStream ms = new MemoryStream();
            Package package = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite);
            string packUri = "pack://itemledger-" + Guid.NewGuid().ToString("N") + ".xps";
            PackageStore.AddPackage(new Uri(packUri), package);

            XpsDocument xpsDoc = new XpsDocument(package, CompressionOption.Maximum, packUri);
            XpsDocumentWriter writer = XpsDocument.CreateXpsDocumentWriter(xpsDoc);
            writer.Write(((IDocumentPaginatorSource)doc).DocumentPaginator);
            FixedDocumentSequence fixedDoc = xpsDoc.GetFixedDocumentSequence();

            var viewer = new DocumentViewer { Document = fixedDoc };

            var btnPrint = new Button { Content = "Print", Width = 90, Margin = new Thickness(6) };
            var btnPDF = new Button { Content = "Save as PDF", Width = 110, Margin = new Thickness(6) };
            var btnFit = new Button { Content = "Fit to Width", Margin = new Thickness(6) };
            var btnZoomIn = new Button { Content = "+", Width = 30, Margin = new Thickness(6) };
            var btnZoomOut = new Button { Content = "-", Width = 30, Margin = new Thickness(6) };

            btnPrint.Click += (s, ev) =>
            {
                var dlg = new PrintDialog();
                if (dlg.ShowDialog() == true)
                    dlg.PrintDocument(fixedDoc.DocumentPaginator, $"Item Ledger - {_selectedProductName}");
            };

            btnPDF.Click += (s, ev) =>
            {
                var dlg = new PrintDialog();
                try { dlg.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), "Microsoft Print to PDF"); } catch { }
                if (dlg.ShowDialog() == true)
                    dlg.PrintDocument(fixedDoc.DocumentPaginator, $"Item Ledger - {_selectedProductName}");
            };

            btnFit.Click += (s, ev) => viewer.FitToWidth();
            btnZoomIn.Click += (s, ev) => viewer.IncreaseZoom();
            btnZoomOut.Click += (s, ev) => viewer.DecreaseZoom();

            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            toolbar.Children.Add(btnZoomOut);
            toolbar.Children.Add(btnZoomIn);
            toolbar.Children.Add(btnFit);
            toolbar.Children.Add(btnPDF);
            toolbar.Children.Add(btnPrint);

            var mainPanel = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            mainPanel.Children.Add(toolbar);
            mainPanel.Children.Add(viewer);

            var wnd = new Window
            {
                Title = $"Item Ledger Preview - {_selectedProductName}",
                Width = 1150,
                Height = 850,
                Content = mainPanel,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            wnd.Closed += (s, args) =>
            {
                xpsDoc.Close();
                PackageStore.RemovePackage(new Uri(packUri));
                ms.Close();
            };

            wnd.ShowDialog();
        }
    }
}