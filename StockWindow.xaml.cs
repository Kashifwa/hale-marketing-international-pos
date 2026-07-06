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
    public partial class StockWindow : Window
    {
        private string _dbPath = AppConfig.ConnectionString;
        private List<StockRow> _allRows = new();
        private List<StockRow> _filtered = new();
        private bool _searchPlaceholder = true;

        // ── Data model ───────────────────────────────────────────────────────────
        public class StockRow
        {
            public int RowNum { get; set; }
            public int ProductId { get; set; }
            public string Code { get; set; }
            public string ProductName { get; set; }
            public string Company { get; set; }
            public decimal PurchasePrice { get; set; }
            public decimal SalePrice { get; set; }

            // Transaction totals
            public decimal TotalPurchased { get; set; }  // +
            public decimal TotalSold { get; set; }  // -
            public decimal ReturnIn { get; set; }  // + (sales return restores stock)
            public decimal ReturnOut { get; set; }  // - (purchase return removes stock)

            public decimal CurrentStock => TotalPurchased - TotalSold + ReturnIn - ReturnOut;
            public decimal StockValue => CurrentStock > 0 ? CurrentStock * PurchasePrice : 0;

            public bool IsOutOfStock => CurrentStock <= 0;
            public bool IsLowStock => CurrentStock > 0 && CurrentStock <= 5;

            // Color coding for the Qty badge
            public Brush StockBackground
            {
                get
                {
                    if (IsOutOfStock) return new SolidColorBrush(Color.FromRgb(60, 10, 10));
                    if (IsLowStock) return new SolidColorBrush(Color.FromRgb(50, 38, 0));
                    return new SolidColorBrush(Color.FromRgb(0, 50, 40));
                }
            }

            public Brush StockForeground
            {
                get
                {
                    if (IsOutOfStock) return new SolidColorBrush(Color.FromRgb(231, 76, 60));
                    if (IsLowStock) return new SolidColorBrush(Color.FromRgb(255, 179, 0));
                    return new SolidColorBrush(Color.FromRgb(0, 191, 165));
                }
            }

            // Display as integer if whole number
            public string CurrentStockDisplay =>
                CurrentStock == Math.Floor(CurrentStock)
                    ? ((int)CurrentStock).ToString()
                    : CurrentStock.ToString("N2");
        }

        // ── Constructor ──────────────────────────────────────────────────────────
        public StockWindow()
        {
            InitializeComponent();
            LoadStock();
        }

        // ── Load Stock ───────────────────────────────────────────────────────────
        private void LoadStock()
        {
            TxtStatus.Text = "Loading stock data...";
            _allRows.Clear();

            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();

                // Get all products
                var products = new List<(int id, string code, string name, string company, decimal pp, decimal sp)>();
                using (var cmd = new SQLiteCommand(
                    "SELECT Id, IFNULL(Code,'') as Code, ProductName, IFNULL(Category,'') as Category, " +
                    "IFNULL(PurchasePrice,0) as PurchasePrice, IFNULL(SalePrice,0) as SalePrice " +
                    "FROM Products ORDER BY ProductName", con))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        products.Add((
                            Convert.ToInt32(r["Id"]),
                            r["Code"].ToString().Trim(),
                            r["ProductName"]?.ToString() ?? "",
                            r["Category"]?.ToString() ?? "",
                            Convert.ToDecimal(r["PurchasePrice"]),
                            Convert.ToDecimal(r["SalePrice"])
                        ));
                }

                int rowNum = 1;
                foreach (var (id, code, name, company, pp, sp) in products)
                {
                    decimal purchased = 0, sold = 0, returnIn = 0, returnOut = 0;

                    // ── PURCHASED ──────────────────────────────────────────────────────
                    // FIX: Sirf non-empty code se match karo, ProductId bhi check karo
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        using var cmd = new SQLiteCommand(
                            "SELECT IFNULL(SUM(Quantity),0) FROM PurchaseItems " +
                            "WHERE TRIM(ProductCode)=@c AND TRIM(ProductCode)!=''", con);
                        cmd.Parameters.AddWithValue("@c", code);
                        purchased = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    // ── SOLD ───────────────────────────────────────────────────────────
                    // FIX: ProductId se primary match, ProductCode sirf tab jab Id match na ho
                    // Aur empty code se kabhi match mat karo
                    using (var cmd = new SQLiteCommand(
                        !string.IsNullOrWhiteSpace(code)
                            ? "SELECT IFNULL(SUM(Quantity),0) FROM SalesItems " +
                              "WHERE ProductId=@id OR (TRIM(ProductCode)=@c AND TRIM(ProductCode)!='')"
                            : "SELECT IFNULL(SUM(Quantity),0) FROM SalesItems " +
                              "WHERE ProductId=@id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        if (!string.IsNullOrWhiteSpace(code))
                            cmd.Parameters.AddWithValue("@c", code);
                        sold = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    // ── RETURN IN (Sales Return) ────────────────────────────────────────
                    using (var cmd = new SQLiteCommand(
                        !string.IsNullOrWhiteSpace(code)
                            ? "SELECT IFNULL(SUM(ReturnQty),0) FROM SalesReturnItems " +
                              "WHERE ProductId=@id OR (TRIM(ProductCode)=@c AND TRIM(ProductCode)!='')"
                            : "SELECT IFNULL(SUM(ReturnQty),0) FROM SalesReturnItems " +
                              "WHERE ProductId=@id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        if (!string.IsNullOrWhiteSpace(code))
                            cmd.Parameters.AddWithValue("@c", code);
                        returnIn = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    // ── RETURN OUT (Purchase Return) ────────────────────────────────────
                    using (var cmd = new SQLiteCommand(
                        !string.IsNullOrWhiteSpace(code)
                            ? "SELECT IFNULL(SUM(ReturnQty),0) FROM PurchaseReturnItems " +
                              "WHERE ProductId=@id OR (TRIM(ProductCode)=@c AND TRIM(ProductCode)!='')"
                            : "SELECT IFNULL(SUM(ReturnQty),0) FROM PurchaseReturnItems " +
                              "WHERE ProductId=@id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        if (!string.IsNullOrWhiteSpace(code))
                            cmd.Parameters.AddWithValue("@c", code);
                        returnOut = Convert.ToDecimal(cmd.ExecuteScalar());
                    }

                    _allRows.Add(new StockRow
                    {
                        RowNum = rowNum++,
                        ProductId = id,
                        Code = code,
                        ProductName = name,
                        Company = company,
                        PurchasePrice = pp,
                        SalePrice = sp,
                        TotalPurchased = purchased,
                        TotalSold = sold,
                        ReturnIn = returnIn,
                        ReturnOut = returnOut
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading stock: " + ex.Message);
                return;
            }

            PopulateCompanyFilter();
            ApplyFilters();
            TxtStatus.Text = $"Stock loaded — {_allRows.Count} products.";
        }

        private void PopulateCompanyFilter()
        {
            CmbCompanyFilter.Items.Clear();
            CmbCompanyFilter.Items.Add("All Companies");
            foreach (var co in _allRows
                .Select(r => r.Company)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .OrderBy(c => c))
                CmbCompanyFilter.Items.Add(co);
            CmbCompanyFilter.SelectedIndex = 0;
        }

        // ── Filters ──────────────────────────────────────────────────────────────
        private void ApplyFilters()
        {
            string search = (_searchPlaceholder || string.IsNullOrWhiteSpace(TxtSearch.Text))
                                    ? "" : TxtSearch.Text.Trim().ToLower();
            string company = CmbCompanyFilter.SelectedItem?.ToString() ?? "All Companies";
            string stockStatus = (CmbStockFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Items";

            _filtered = _allRows.Where(r =>
            {
                // Search
                bool matchSearch = string.IsNullOrEmpty(search) ||
                    r.Code.ToLower().Contains(search) ||
                    r.ProductName.ToLower().Contains(search) ||
                    r.Company.ToLower().Contains(search);

                // Company
                bool matchCompany = company == "All Companies" || r.Company == company;

                // Stock status
                bool matchStock = stockStatus switch
                {
                    "In Stock" => !r.IsOutOfStock,
                    "Out of Stock" => r.IsOutOfStock,
                    "Low Stock (≤5)" => r.IsLowStock,
                    _ => true
                };

                return matchSearch && matchCompany && matchStock;
            }).ToList();

            // Re-number visible rows
            for (int i = 0; i < _filtered.Count; i++)
                _filtered[i].RowNum = i + 1;

            StockGrid.ItemsSource = null;
            StockGrid.ItemsSource = _filtered;

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int total = _allRows.Count;
            int outOfStock = _allRows.Count(r => r.IsOutOfStock);
            decimal totalVal = _allRows.Sum(r => r.StockValue);

            TxtTotalItems.Text = total.ToString();
            TxtTotalValue.Text = totalVal.ToString("N2");
            TxtOutOfStock.Text = outOfStock.ToString();
            TxtStockCount.Text = $"  •  {total} product{(total == 1 ? "" : "s")}";

            // Filtered totals
            decimal shownVal = _filtered.Sum(r => r.StockValue);
            decimal shownUnits = _filtered.Sum(r => r.CurrentStock > 0 ? r.CurrentStock : 0);

            TxtShownCount.Text = _filtered.Count.ToString();
            TxtTotalUnits.Text = shownUnits == Math.Floor(shownUnits)
                                    ? ((int)shownUnits).ToString()
                                    : shownUnits.ToString("N2");
            TxtGrandValue.Text = shownVal.ToString("N2");
        }

        // ── Event Handlers ───────────────────────────────────────────────────────
        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_searchPlaceholder)
            {
                TxtSearch.Text = "";
                TxtSearch.Foreground = new SolidColorBrush(Colors.White);
                _searchPlaceholder = false;
            }
        }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSearch.Text))
            {
                TxtSearch.Text = "Search by product / code / company...";
                TxtSearch.Foreground = new SolidColorBrush(Color.FromRgb(107, 143, 168));
                _searchPlaceholder = true;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchPlaceholder) return;
            BtnClearSearch.Visibility =
                string.IsNullOrWhiteSpace(TxtSearch.Text) ? Visibility.Collapsed : Visibility.Visible;
            TxtSearch.Foreground = new SolidColorBrush(Colors.White);
            ApplyFilters();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            TxtSearch.Focus();
            BtnClearSearch.Visibility = Visibility.Collapsed;
        }

        private void CmbCompanyFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (IsLoaded) ApplyFilters(); }

        private void CmbStockFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (IsLoaded) ApplyFilters(); }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadStock();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Print ────────────────────────────────────────────────────────────────
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_filtered.Count == 0) { MessageBox.Show("No data to print."); return; }

            string companyFilter = CmbCompanyFilter.SelectedItem?.ToString() ?? "All Companies";
            string stockFilter = (CmbStockFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Items";

            var doc = BuildFlowDocument(_filtered, companyFilter, stockFilter);
            ShowPreview(doc);
        }

        private FlowDocument BuildFlowDocument(
            List<StockRow> rows, string companyFilter, string stockFilter)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(30),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                PageWidth = 1050
            };

            // Header
            var hb = new Border
            {
                Background = Brushes.Black,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var hp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            hp.Children.Add(new TextBlock
            {
                Text = "Hale Marketing International - Multan",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            hp.Children.Add(new TextBlock
            {
                Text = "STOCK REPORT",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 191, 165)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            hp.Children.Add(new TextBlock
            {
                Text = $"Date: {DateTime.Now:dd-MM-yyyy HH:mm}    Filter: {companyFilter} | {stockFilter}",
                FontSize = 11,
                Foreground = Brushes.LightGray,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            hb.Child = hp;
            doc.Blocks.Add(new BlockUIContainer(hb));

            // Table
            var table = new Table
            {
                CellSpacing = 0,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.DarkSlateGray
            };

            double[] widths = { 30, 70, 190, 110, 85, 80, 75, 60, 60, 60, 80, 90 };
            foreach (var w in widths)
                table.Columns.Add(new TableColumn { Width = new GridLength(w) });

            // Header row
            var hg = new TableRowGroup();
            var hr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(20, 31, 43)) };
            foreach (var h in new[] { "#", "CODE", "PRODUCT NAME", "COMPANY",
                "PURCHASE", "SALE", "PURCHASED", "SOLD", "RTN IN", "RTN OUT", "STOCK QTY", "STOCK VALUE" })
                hr.Cells.Add(HCell(h));
            hg.Rows.Add(hr);
            table.RowGroups.Add(hg);

            // Data rows
            var bg = new TableRowGroup();
            int idx = 0;
            decimal totalUnits = 0, totalValue = 0;

            foreach (var r in rows)
            {
                idx++;
                var row = new TableRow
                {
                    Background = r.IsOutOfStock
                        ? new SolidColorBrush(Color.FromRgb(45, 10, 10))
                        : r.IsLowStock
                            ? new SolidColorBrush(Color.FromRgb(45, 35, 0))
                            : idx % 2 == 0
                                ? new SolidColorBrush(Color.FromRgb(20, 31, 43))
                                : new SolidColorBrush(Color.FromRgb(25, 36, 47))
                };

                decimal cur = r.CurrentStock;
                totalUnits += cur > 0 ? cur : 0;
                totalValue += r.StockValue;

                Brush qtyColor = r.IsOutOfStock
                    ? new SolidColorBrush(Color.FromRgb(231, 76, 60))
                    : r.IsLowStock
                        ? new SolidColorBrush(Color.FromRgb(255, 179, 0))
                        : new SolidColorBrush(Color.FromRgb(0, 191, 165));

                row.Cells.Add(DCell(idx.ToString()));
                row.Cells.Add(DCell(r.Code));
                row.Cells.Add(DCell(r.ProductName, TextAlignment.Left));
                row.Cells.Add(DCell(r.Company, TextAlignment.Left));
                row.Cells.Add(DCell(r.PurchasePrice.ToString("N2")));
                row.Cells.Add(DCell(r.SalePrice.ToString("N2")));
                row.Cells.Add(DCell(r.TotalPurchased.ToString("N0")));
                row.Cells.Add(DCell(r.TotalSold.ToString("N0")));
                row.Cells.Add(DCell(r.ReturnIn.ToString("N0")));
                row.Cells.Add(DCell(r.ReturnOut.ToString("N0")));
                // Qty cell with color
                row.Cells.Add(new TableCell(
                    new Paragraph(new Run(cur == Math.Floor(cur) ? ((int)cur).ToString() : cur.ToString("N2"))
                    { Foreground = qtyColor, FontWeight = FontWeights.Bold })
                    { TextAlignment = TextAlignment.Center })
                { Padding = new Thickness(4), BorderThickness = new Thickness(0, 0, 1, 1), BorderBrush = Brushes.DarkSlateGray });
                row.Cells.Add(DCell(r.StockValue.ToString("N2")));

                bg.Rows.Add(row);
            }
            table.RowGroups.Add(bg);

            // Totals row
            var tg = new TableRowGroup();
            var tr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(20, 31, 43)) };
            tr.Cells.Add(SumCell("", 4));      // span first 4 cols
            tr.Cells.Add(SumCell(""));         // purchase
            tr.Cells.Add(SumCell(""));         // sale
            tr.Cells.Add(SumCell(""));         // purchased
            tr.Cells.Add(SumCell(""));         // sold
            tr.Cells.Add(SumCell(""));         // rtn in
            tr.Cells.Add(SumCell(""));         // rtn out
            tr.Cells.Add(SumCell(totalUnits == Math.Floor(totalUnits)
                ? ((int)totalUnits).ToString() : totalUnits.ToString("N2"),
                isTotal: true));
            tr.Cells.Add(SumCell(totalValue.ToString("N2"), isTotal: true));
            tg.Rows.Add(tr);
            table.RowGroups.Add(tg);

            doc.Blocks.Add(table);

            // Summary
            var sum = new Paragraph { Margin = new Thickness(0, 12, 0, 0) };
            sum.Inlines.Add(new Run($"Products shown: {rows.Count}   |   " +
                $"Out of stock: {rows.Count(r => r.IsOutOfStock)}   |   " +
                $"Low stock: {rows.Count(r => r.IsLowStock)}   |   " +
                $"Total value: {totalValue:N2}"));
            doc.Blocks.Add(sum);

            // ═══════════════════════════════════════════════
            // Developer Branding Footer (Premium)
            // ═══════════════════════════════════════════════

            var footerLine = new Paragraph
            {
                Margin = new Thickness(0, 40, 0, 5),
                TextAlignment = TextAlignment.Center
            };

            footerLine.Inlines.Add(new Run(new string('─', 45))
            {
                Foreground = Brushes.Gray
            });

            doc.Blocks.Add(footerLine);

            var devFooter = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 0),
                TextAlignment = TextAlignment.Center,
                FontSize = 10
            };

            devFooter.Inlines.Add(new Run("Powered by ")
            {
                Foreground = Brushes.Gray
            });

            devFooter.Inlines.Add(new Run("KW Soft Solutions")
            {
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black
            });

            doc.Blocks.Add(devFooter);

            var contactFooter = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 0),
                TextAlignment = TextAlignment.Center,
                FontSize = 9
            };

            contactFooter.Inlines.Add(new Run("📞 0335-2255510")
            {
                Foreground = Brushes.Gray
            });

            doc.Blocks.Add(contactFooter);

            return doc;
        }

        private static TableCell HCell(string t, int colSpan = 1) => new TableCell(
            new Paragraph(new Run(t))
            {
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 191, 165))
            })
        {
            ColumnSpan = colSpan,
            Background = new SolidColorBrush(Color.FromRgb(14, 21, 32)),
            Padding = new Thickness(4, 5, 4, 5),
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = Brushes.DarkSlateGray
        };

        private static TableCell DCell(string t, TextAlignment align = TextAlignment.Center) => new TableCell(
            new Paragraph(new Run(t)) { TextAlignment = align, Foreground = Brushes.LightGray })
        {
            Padding = new Thickness(4),
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = Brushes.DarkSlateGray
        };

        private static TableCell SumCell(string t, int colSpan = 1, bool isTotal = false) => new TableCell(
            new Paragraph(new Run(t))
            {
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = isTotal
                    ? new SolidColorBrush(Color.FromRgb(255, 179, 0))
                    : Brushes.Gray
            })
        {
            ColumnSpan = colSpan,
            Background = new SolidColorBrush(Color.FromRgb(14, 21, 32)),
            Padding = new Thickness(4, 5, 4, 5),
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = Brushes.DarkSlateGray
        };

        // ── Preview ───────────────────────────────────────────────────────────────
        private void ShowPreview(FlowDocument doc)
        {
            var ms = new MemoryStream();
            var pkg = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite);
            string uri = "pack://stock-" + Guid.NewGuid().ToString("N") + ".xps";
            PackageStore.AddPackage(new Uri(uri), pkg);
            var xps = new XpsDocument(pkg, CompressionOption.Maximum, uri);
            XpsDocument.CreateXpsDocumentWriter(xps).Write(((IDocumentPaginatorSource)doc).DocumentPaginator);
            var fd = xps.GetFixedDocumentSequence();

            var viewer = new DocumentViewer { Document = fd };
            var btnPrint = new Button { Content = "🖨 Print", Width = 100, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(0, 191, 165)), Foreground = Brushes.Black, FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            var btnPDF = new Button { Content = "📄 Save PDF", Width = 110, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnFit = new Button { Content = "⬛ Fit", Width = 80, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnZoomIn = new Button { Content = "＋", Width = 36, Margin = new Thickness(4, 6, 0, 6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnZoomOut = new Button { Content = "－", Width = 36, Margin = new Thickness(0, 6, 6, 6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };

            btnPrint.Click += (s, ev) => { var d = new PrintDialog(); if (d.ShowDialog() == true) d.PrintDocument(fd.DocumentPaginator, "Stock Report"); };
            btnPDF.Click += (s, ev) => { var d = new PrintDialog(); try { d.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), "Microsoft Print to PDF"); } catch { } if (d.ShowDialog() == true) d.PrintDocument(fd.DocumentPaginator, "Stock Report"); };
            btnFit.Click += (s, ev) => viewer.FitToWidth();
            btnZoomIn.Click += (s, ev) => viewer.IncreaseZoom();
            btnZoomOut.Click += (s, ev) => viewer.DecreaseZoom();

            var tb = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(12, 21, 32)), HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var btn in new[] { btnZoomOut, btnZoomIn, btnFit, btnPDF, btnPrint }) tb.Children.Add(btn);

            var panel = new DockPanel();
            DockPanel.SetDock(tb, Dock.Top);
            panel.Children.Add(tb);
            panel.Children.Add(viewer);

            var wnd = new Window
            {
                Title = "Stock Report — Hale Marketing International",
                Width = 1150,
                Height = 850,
                Content = panel,
                Background = new SolidColorBrush(Color.FromRgb(17, 29, 43)),
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            wnd.Closed += (s, ev) => { xps.Close(); PackageStore.RemovePackage(new Uri(uri)); ms.Close(); };
            wnd.ShowDialog();
        }
    }
}