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
    public partial class PartyLedgerWindow : Window
    {
        private string _dbPath = AppConfig.ConnectionString;
        private Party _selectedParty;

        public PartyLedgerWindow()
        {
            InitializeComponent();
        }

        public PartyLedgerWindow(Party selectedParty)
        {
            InitializeComponent();
            _selectedParty = selectedParty;
            TxtPartyName.Text = _selectedParty.Name;
        }

        private void BtnSelectParty_Click(object sender, RoutedEventArgs e)
        {
            var popup = new PartySearchWindow { Owner = this };
            if (popup.ShowDialog() == true)
            {
                _selectedParty = popup.SelectedParty;
                TxtPartyName.Text = _selectedParty.Name;
            }
        }

        private void BtnShowLedger_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedParty == null)
            {
                MessageBox.Show("Please select a party first.");
                return;
            }

            DateTime fromDate = DtFrom.SelectedDate ?? DateTime.MinValue;
            DateTime toDate = DtTo.SelectedDate ?? DateTime.MaxValue;

            var ledger = GetPartyLedger(_selectedParty.Id, _selectedParty.Name, fromDate, toDate);
            dgLedger.ItemsSource = ledger;
        }

        private List<LedgerEntry> GetPartyLedger(int partyId, string partyName, DateTime from, DateTime to)
        {
            var ledger = new List<LedgerEntry>();
            string fromStr = from.ToString("yyyy-MM-dd");
            string toStr = to.ToString("yyyy-MM-dd");

            using (var con = new SQLiteConnection(_dbPath))
            {
                con.Open();

                // ── 1. Sales Invoice → Debit (customer owes us) ──────────────────
                // Table: Sales  Columns: InvoiceDate, InvoiceNo, NetTotal, PartyId
                using (var cmd = new SQLiteCommand(@"
                    SELECT InvoiceDate, InvoiceNo, NetTotal
                    FROM Sales
                    WHERE PartyId = @pid
                      AND InvoiceDate BETWEEN @from AND @to", con))
                {
                    cmd.Parameters.AddWithValue("@pid", partyId);
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            ledger.Add(new LedgerEntry
                            {
                                Date = r["InvoiceDate"].ToString(),
                                Type = "Sale",
                                InvoiceNo = r["InvoiceNo"].ToString(),
                                Description = "Sale Invoice",
                                Debit = Convert.ToDecimal(r["NetTotal"]),
                                Credit = 0
                            });
                }

                // ── 2. Sales Return → Credit (we owe customer back) ──────────────
                // Table: SalesReturn  Columns: ReturnDate, ReturnNo, NetAmount, PartyId
                using (var cmd = new SQLiteCommand(@"
                    SELECT ReturnDate, ReturnNo, NetAmount
                    FROM SalesReturn
                    WHERE PartyId = @pid
                      AND ReturnDate BETWEEN @from AND @to", con))
                {
                    cmd.Parameters.AddWithValue("@pid", partyId);
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            ledger.Add(new LedgerEntry
                            {
                                Date = r["ReturnDate"].ToString(),
                                Type = "Sales Return",
                                InvoiceNo = r["ReturnNo"].ToString(),
                                Description = "Sales Return",
                                Debit = 0,
                                Credit = Convert.ToDecimal(r["NetAmount"])
                            });
                }

                // ── 3. Purchase Invoice → Credit (we owe supplier) ───────────────
                // Table: PurchaseInvoice  Columns: InvoiceDate, InvoiceNo, NetTotal
                // PartyId column exists — use it first, fall back to Supplier name match
                using (var cmd = new SQLiteCommand(@"
                    SELECT InvoiceDate, InvoiceNo, NetTotal
                    FROM PurchaseInvoice
                    WHERE (PartyId = @pid OR (PartyId IS NULL AND Supplier = @name))
                      AND InvoiceDate BETWEEN @from AND @to", con))
                {
                    cmd.Parameters.AddWithValue("@pid", partyId);
                    cmd.Parameters.AddWithValue("@name", partyName);
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            ledger.Add(new LedgerEntry
                            {
                                Date = r["InvoiceDate"].ToString(),
                                Type = "Purchase",
                                InvoiceNo = r["InvoiceNo"].ToString(),
                                Description = "Purchase Invoice",
                                Debit = 0,
                                Credit = Convert.ToDecimal(r["NetTotal"])
                            });
                }

                // ── 4. Purchase Return → Debit (supplier owes us back) ───────────
                // Table: PurchaseReturn  Columns: ReturnDate, ReturnNo, NetAmount, SupplierId
                using (var cmd = new SQLiteCommand(@"
                    SELECT ReturnDate, ReturnNo, NetAmount
                    FROM PurchaseReturn
                    WHERE SupplierId = @pid
                      AND ReturnDate BETWEEN @from AND @to", con))
                {
                    cmd.Parameters.AddWithValue("@pid", partyId);
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            ledger.Add(new LedgerEntry
                            {
                                Date = r["ReturnDate"].ToString(),
                                Type = "Purchase Return",
                                InvoiceNo = r["ReturnNo"].ToString(),
                                Description = "Purchase Return",
                                Debit = Convert.ToDecimal(r["NetAmount"]),
                                Credit = 0
                            });
                }

                // ── 5. Cash Receipt → Credit (customer paid us) ──────────────────
                // Table: CashReceipt  Columns: ReceiptDate, ReceiptNo, Amount, PartyId
                using (var cmd = new SQLiteCommand(@"
                    SELECT Date, VoucherNo, Amount
                    FROM CashReceipt
                    WHERE PartyId = @pid
                      AND Date BETWEEN @from AND @to", con))
                {
                    cmd.Parameters.AddWithValue("@pid", partyId);
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            ledger.Add(new LedgerEntry
                            {
                                Date = r["Date"].ToString(),
                                Type = "Cash Receipt",
                                InvoiceNo = r["VoucherNo"].ToString(),
                                Description = "Cash Receipt",
                                Debit = 0,
                                Credit = Convert.ToDecimal(r["Amount"])
                            });
                }

                // ── 6. Payment Receipt → Debit (we paid supplier) ────────────────
                // Table: PaymentReceipt  Columns: ReceiptDate, ReceiptNo, Amount, PartyId
                using (var cmd = new SQLiteCommand(@"
                    SELECT Date, VoucherNo, Amount
                    FROM PaymentReceipt
                    WHERE PartyId = @pid
                      AND Date BETWEEN @from AND @to", con))
                {
                    cmd.Parameters.AddWithValue("@pid", partyId);
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                            ledger.Add(new LedgerEntry
                            {
                                Date = r["Date"].ToString(),
                                Type = "Payment Receipt",
                                InvoiceNo = r["VoucherNo"].ToString(),
                                Description = "Payment Receipt",
                                Debit = Convert.ToDecimal(r["Amount"]),
                                Credit = 0
                            });
                }
            }

            // Sort by date then compute running balance
            ledger = ledger.OrderBy(x =>
            {
                DateTime.TryParse(x.Date, out DateTime d);
                return d;
            }).ToList();

            decimal balance = 0;
            foreach (var entry in ledger)
            {
                balance += entry.Debit - entry.Credit;
                entry.Balance = balance;
            }

            return ledger;
        }

        public class LedgerEntry
        {
            public string Date { get; set; }
            public string Type { get; set; }
            public string InvoiceNo { get; set; }
            public string Description { get; set; }
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
            public decimal Balance { get; set; }
        }

        // ── Print Preview ─────────────────────────────────────────────────────────
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            var entries = dgLedger.ItemsSource as List<LedgerEntry>;
            if (entries == null || !entries.Any())
            {
                MessageBox.Show("No data to print.");
                return;
            }

            FlowDocument doc = BuildLedgerFlowDocument(entries);
            ShowFlowDocumentPreview(doc);
        }

        private FlowDocument BuildLedgerFlowDocument(List<LedgerEntry> entries)
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

            // Party + date info
            var info = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
            info.Inlines.Add(new Run($"Party Ledger:  {_selectedParty?.Name}")
            { FontSize = 14, FontWeight = FontWeights.Bold });
            doc.Blocks.Add(info);

            var dateInfo = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
            dateInfo.Inlines.Add(new Run(
                $"From: {DtFrom.SelectedDate?.ToString("dd-MM-yyyy") ?? "All"}    " +
                $"To: {DtTo.SelectedDate?.ToString("dd-MM-yyyy") ?? "All"}    " +
                $"Printed: {DateTime.Now:dd-MM-yyyy hh:mm tt}"));
            doc.Blocks.Add(dateInfo);

            // Ledger table
            var table = new Table
            {
                CellSpacing = 0,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Black
            };
            foreach (var w in new double[] { 75, 110, 90, 180, 90, 90, 95 })
                table.Columns.Add(new TableColumn { Width = new GridLength(w) });

            // Header row
            var hGroup = new TableRowGroup();
            var hRow = new TableRow();
            foreach (var h in new[] { "Date", "Type", "Invoice No", "Description", "Debit", "Credit", "Balance" })
            {
                var cell = new TableCell(
                    new Paragraph(new Run(h) { Foreground = Brushes.White })
                    { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center })
                {
                    Background = Brushes.Black,
                    Padding = new Thickness(4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Black
                };
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
                    entry.Description,
                    entry.Debit  > 0 ? entry.Debit.ToString("N2")  : "-",
                    entry.Credit > 0 ? entry.Credit.ToString("N2") : "-",
                    entry.Balance.ToString("N2")
                };

                for (int i = 0; i < vals.Length; i++)
                {
                    var align = (i >= 4) ? TextAlignment.Right : TextAlignment.Left;
                    var run = new Run(vals[i]);

                    if (i == 6)
                    {
                        run.Foreground = entry.Balance >= 0 ? Brushes.DarkRed : Brushes.DarkGreen;
                        run.FontWeight = FontWeights.Bold;
                    }

                    var cell = new TableCell(new Paragraph(run) { TextAlignment = align })
                    {
                        Background = rowBg,
                        Padding = new Thickness(4),
                        BorderThickness = new Thickness(1),
                        BorderBrush = Brushes.LightGray
                    };
                    row.Cells.Add(cell);
                }
                bGroup.Rows.Add(row);
            }
            table.RowGroups.Add(bGroup);
            doc.Blocks.Add(table);

            // Summary footer
            decimal totalDebit = entries.Sum(x => x.Debit);
            decimal totalCredit = entries.Sum(x => x.Credit);
            decimal closing = entries.LastOrDefault()?.Balance ?? 0;

            var summary = new Paragraph
            {
                Margin = new Thickness(0, 12, 0, 0),
                TextAlignment = TextAlignment.Right
            };
            summary.Inlines.Add(new Run($"Total Debit:  {totalDebit:N2}\n"));
            summary.Inlines.Add(new Run($"Total Credit: {totalCredit:N2}\n"));
            summary.Inlines.Add(new Run(
                $"Closing Balance: {Math.Abs(closing):N2}  " +
                $"({(closing >= 0 ? "Receivable" : "Payable")})\n")
            {
                FontWeight = FontWeights.Bold,
                Foreground = closing >= 0 ? Brushes.DarkRed : Brushes.DarkGreen
            });
            doc.Blocks.Add(summary);

            return doc;
        }

        private void ShowFlowDocumentPreview(FlowDocument doc)
        {
            var ms = new MemoryStream();
            var package = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite);
            string uri = "pack://ledger-" + Guid.NewGuid().ToString("N") + ".xps";
            PackageStore.AddPackage(new Uri(uri), package);

            var xpsDoc = new XpsDocument(package, CompressionOption.Maximum, uri);
            var writer = XpsDocument.CreateXpsDocumentWriter(xpsDoc);
            writer.Write(((IDocumentPaginatorSource)doc).DocumentPaginator);
            var fixedDoc = xpsDoc.GetFixedDocumentSequence();

            var viewer = new DocumentViewer { Document = fixedDoc };
            var btnPrint = new Button { Content = "Print", Width = 90, Margin = new Thickness(6) };
            var btnPDF = new Button { Content = "Save as PDF", Width = 110, Margin = new Thickness(6) };
            var btnFit = new Button { Content = "Fit to Width", Margin = new Thickness(6) };
            var btnZoomIn = new Button { Content = "+", Width = 30, Margin = new Thickness(6) };
            var btnZoomOut = new Button { Content = "-", Width = 30, Margin = new Thickness(6) };

            btnPrint.Click += (s, ev) => { var d = new PrintDialog(); if (d.ShowDialog() == true) d.PrintDocument(fixedDoc.DocumentPaginator, $"Ledger - {_selectedParty?.Name}"); };
            btnPDF.Click += (s, ev) => { var d = new PrintDialog(); try { d.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), "Microsoft Print to PDF"); } catch { } if (d.ShowDialog() == true) d.PrintDocument(fixedDoc.DocumentPaginator, $"Ledger - {_selectedParty?.Name}"); };
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

            var panel = new DockPanel();
            DockPanel.SetDock(toolbar, Dock.Top);
            panel.Children.Add(toolbar);
            panel.Children.Add(viewer);

            var wnd = new Window
            {
                Title = $"Ledger Preview - {_selectedParty?.Name}",
                Width = 1100,
                Height = 850,
                Content = panel,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            wnd.Closed += (s, ev) => { xpsDoc.Close(); PackageStore.RemovePackage(new Uri(uri)); ms.Close(); };
            wnd.ShowDialog();
        }
    }
}