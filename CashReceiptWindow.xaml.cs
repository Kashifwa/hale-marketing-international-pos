using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace Hale_Marketing_International
{
    public partial class CashReceiptWindow : Window
    {
        private string _dbPath = AppConfig.ConnectionString;
        private ObservableCollection<CashReceiptItem> _receiptItems = new ObservableCollection<CashReceiptItem>();

        // Pending attachment for current entry row
        private byte[] _pendingAttachBytes = null;
        private string _pendingAttachName = null;

        // Side panel state
        private bool _listSearchPlaceholder = true;
        private int _editingVoucherDbId = -1; // -1 = new mode, >0 = editing saved receipt group

        // ViewModel for the side panel list
        public class ReceiptListRow
        {
            public int Id { get; set; } // first row Id for this voucher
            public string VoucherNo { get; set; }
            public string Date { get; set; }
            public string PartyName { get; set; }
            public decimal TotalAmount { get; set; }
        }

        private List<ReceiptListRow> _allSavedReceipts = new();

        // ── Constructor ─────────────────────────────────────────────────────────
        public CashReceiptWindow()
        {
            InitializeComponent();
            dgCashReceipts.ItemsSource = _receiptItems;
            EnsureTable();
            LoadParties();
            LoadNextVoucherNo();
            DtReceiptDate.SelectedDate = DateTime.Today;
        }

        // ── Ensure Table ─────────────────────────────────────────────────────────
        private void EnsureTable()
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS CashReceipt (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    VoucherNo   TEXT,
                    Date        TEXT,
                    PartyId     INTEGER,
                    PaymentMode TEXT,
                    Remarks     TEXT,
                    Amount      REAL,
                    AttachName  TEXT,
                    AttachData  BLOB
                )", con).ExecuteNonQuery();

                foreach (var sql in new[]
                {
                    "ALTER TABLE CashReceipt ADD COLUMN AttachName TEXT",
                    "ALTER TABLE CashReceipt ADD COLUMN AttachData BLOB"
                })
                { try { new SQLiteCommand(sql, con).ExecuteNonQuery(); } catch { } }
            }
            catch (Exception ex) { MessageBox.Show("EnsureTable (CashReceipt): " + ex.Message); }
        }

        // ── Load Next Voucher No ──────────────────────────────────────────────────
        private void LoadNextVoucherNo()
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT IFNULL(MAX(CAST(SUBSTR(VoucherNo,4) AS INTEGER)),0)+1 FROM CashReceipt", con);
                TxtVoucherNo.Text = "CR-" + Convert.ToInt64(cmd.ExecuteScalar()).ToString().PadLeft(6, '0');
            }
            catch { TxtVoucherNo.Text = "CR-000001"; }
        }

        // ── Load Parties ──────────────────────────────────────────────────────────
        private void LoadParties()
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT Id, Name, Contact, Address FROM Parties ORDER BY Name", con);
                using var r = cmd.ExecuteReader();
                var list = new List<Party>();
                while (r.Read())
                    list.Add(new Party
                    {
                        Id = r.GetInt32(0),
                        Name = r.GetString(1),
                        Contact = r.IsDBNull(2) ? "" : r.GetString(2),
                        Address = r.IsDBNull(3) ? "" : r.GetString(3)
                    });
                cmbParty.ItemsSource = list;
            }
            catch (Exception ex) { MessageBox.Show("Error loading parties: " + ex.Message); }
        }

        // ── Party Selected ────────────────────────────────────────────────────────
        private void cmbParty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbParty.SelectedItem is Party p)
            {
                TxtPhone.Text = p.Contact;
                TxtAddress.Text = p.Address;
            }
        }

        // ── Attachment: Pick File ─────────────────────────────────────────────────
        private void BtnAttach_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Select Attachment", Filter = "All Files (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                _pendingAttachBytes = File.ReadAllBytes(dlg.FileName);
                _pendingAttachName = Path.GetFileName(dlg.FileName);
                BtnClearAttach.Visibility = Visibility.Visible;
                TxtAttachHint.Text = $"📎  {_pendingAttachName}  ({FormatBytes(_pendingAttachBytes.Length)})";
                TxtAttachHint.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { MessageBox.Show("Could not read file: " + ex.Message); }
        }

        private void BtnClearAttach_Click(object sender, RoutedEventArgs e)
        {
            _pendingAttachBytes = null;
            _pendingAttachName = null;
            BtnClearAttach.Visibility = Visibility.Collapsed;
            TxtAttachHint.Visibility = Visibility.Collapsed;
            TxtAttachHint.Text = "";
        }

        // ── Attachment: Open From Grid ────────────────────────────────────────────
        private void BtnOpenAttach_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out int srNo)) return;
            var item = _receiptItems.FirstOrDefault(x => x.SrNo == srNo);
            if (item?.AttachBytes == null || item.AttachBytes.Length == 0) return;
            OpenBytesWithDefaultApp(item.AttachBytes, item.AttachName ?? "attachment");
        }

        private static void OpenBytesWithDefaultApp(byte[] data, string filename)
        {
            try
            {
                string ext = Path.GetExtension(filename);
                string tmp = Path.Combine(Path.GetTempPath(), $"hmi_cr_{Guid.NewGuid():N}{ext}");
                File.WriteAllBytes(tmp, data);
                Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("Cannot open attachment: " + ex.Message); }
        }

        private static string FormatBytes(long b)
        {
            if (b < 1024) return $"{b} B";
            if (b < 1048576) return $"{b / 1024.0:N1} KB";
            return $"{b / 1048576.0:N1} MB";
        }

        // ── Add Item ──────────────────────────────────────────────────────────────
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cmbParty.SelectedItem == null)
                { MessageBox.Show("Please select a party first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                if (string.IsNullOrWhiteSpace(TxtAmount.Text) || !decimal.TryParse(TxtAmount.Text, out decimal amount))
                { MessageBox.Show("Please enter a valid amount.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                string paymentMode = ((ComboBoxItem)cmbPaymentMode.SelectedItem).Content.ToString();
                string remarks = TxtRemarks.Text.Trim();
                var party = (Party)cmbParty.SelectedItem;

                _receiptItems.Add(new CashReceiptItem
                {
                    SrNo = _receiptItems.Count + 1,
                    PartyId = party.Id,
                    PartyName = party.Name,
                    PaymentMode = paymentMode,
                    Remarks = remarks,
                    Amount = amount,
                    AttachBytes = _pendingAttachBytes,
                    AttachName = _pendingAttachName
                });

                UpdateTotals();
                ClearEntryFields();
            }
            catch (Exception ex) { MessageBox.Show("Error adding item: " + ex.Message); }
        }

        // ── Delete Item ───────────────────────────────────────────────────────────
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int srNo)
            {
                var item = _receiptItems.FirstOrDefault(x => x.SrNo == srNo);
                if (item == null) return;
                _receiptItems.Remove(item);
                ReIndexItems();
                UpdateTotals();
            }
        }

        private void ReIndexItems() { int c = 1; foreach (var i in _receiptItems) i.SrNo = c++; }

        // ── Totals ────────────────────────────────────────────────────────────────
        private void UpdateTotals()
        {
            TxtNetAmount.Text = _receiptItems.Sum(x => x.Amount).ToString("N2");
            TxtEntriesCount.Text = _receiptItems.Count.ToString();
        }

        // ── Clear Entry Fields ────────────────────────────────────────────────────
        private void ClearEntryFields()
        {
            TxtAmount.Clear();
            TxtRemarks.Clear();
            cmbPaymentMode.SelectedIndex = 0;
            BtnClearAttach_Click(null, null);
        }

        // ── Clear Whole Form ──────────────────────────────────────────────────────
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _receiptItems.Clear();
            _editingVoucherDbId = -1;
            EditModeBadge.Visibility = Visibility.Collapsed;
            TxtNetAmount.Text = "0.00";
            TxtEntriesCount.Text = "0";
            TxtPhone.Clear();
            TxtAddress.Clear();
            cmbParty.SelectedIndex = -1;
            ClearEntryFields();
        }

        // ── New ───────────────────────────────────────────────────────────────────
        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            BtnClear_Click(null, null);
            LoadNextVoucherNo();
            DtReceiptDate.SelectedDate = DateTime.Today;
        }

        // ── Save ──────────────────────────────────────────────────────────────────
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_receiptItems.Count == 0)
                { MessageBox.Show("Please add at least one record.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var tran = con.BeginTransaction();

                // If editing, delete old rows first
                if (_editingVoucherDbId > 0)
                    new SQLiteCommand($"DELETE FROM CashReceipt WHERE VoucherNo=@v",
                        con, tran)
                    { Parameters = { new SQLiteParameter("@v", TxtVoucherNo.Text) } }
                        .ExecuteNonQuery();

                foreach (var item in _receiptItems)
                {
                    using var cmd = new SQLiteCommand(@"
                        INSERT INTO CashReceipt
                            (VoucherNo, Date, PartyId, PaymentMode, Remarks, Amount, AttachName, AttachData)
                        VALUES
                            (@VoucherNo,@Date,@PartyId,@PaymentMode,@Remarks,@Amount,@AttachName,@AttachData)",
                        con, tran);
                    cmd.Parameters.AddWithValue("@VoucherNo", TxtVoucherNo.Text);
                    cmd.Parameters.AddWithValue("@Date", DtReceiptDate.SelectedDate ?? DateTime.Now);
                    cmd.Parameters.AddWithValue("@PartyId", item.PartyId);
                    cmd.Parameters.AddWithValue("@PaymentMode", item.PaymentMode);
                    cmd.Parameters.AddWithValue("@Remarks", item.Remarks ?? "");
                    cmd.Parameters.AddWithValue("@Amount", item.Amount);
                    cmd.Parameters.AddWithValue("@AttachName", (object)item.AttachName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@AttachData", (object)item.AttachBytes ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
                tran.Commit();

                MessageBox.Show(_editingVoucherDbId > 0
                    ? "Cash receipt updated successfully."
                    : "Cash receipt saved successfully.",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Refresh side panel if open
                if (BorderReceiptList.Visibility == Visibility.Visible)
                    LoadReceiptList();

                BtnNew_Click(null, null);
            }
            catch (Exception ex) { MessageBox.Show("Error saving receipt: " + ex.Message); }
        }

        // ── Print (current form) ──────────────────────────────────────────────────
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_receiptItems.Count == 0)
            { MessageBox.Show("No items to print.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            string partyName = (cmbParty.SelectedItem as Party)?.Name ?? "";
            string phone = TxtPhone.Text;
            string address = TxtAddress.Text;
            decimal.TryParse(TxtNetAmount.Text, out decimal net);

            var doc = BuildFlowDocument(
                TxtVoucherNo.Text,
                DtReceiptDate.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd"),
                partyName, phone, address, _receiptItems.ToList(), net);

            var preview = new PrintPreviewWindow(doc);
            preview.Owner = this;
            preview.ShowDialog();
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  SAVED RECEIPTS SIDE PANEL
        // ══════════════════════════════════════════════════════════════════════════

        private void BtnShowReceiptList_Click(object sender, RoutedEventArgs e)
        {
            BorderReceiptList.Visibility = Visibility.Visible;
            LoadReceiptList();
        }

        private void BtnCloseReceiptList_Click(object sender, RoutedEventArgs e)
            => BorderReceiptList.Visibility = Visibility.Collapsed;

        // ── Load all saved receipts grouped by VoucherNo ──────────────────────────
        private void LoadReceiptList(string search = "", DateTime? from = null, DateTime? to = null)
        {
            _allSavedReceipts.Clear();
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(@"
                    SELECT cr.VoucherNo,
                           cr.Date,
                           IFNULL(p.Name,'Unknown') AS PartyName,
                           SUM(cr.Amount)           AS TotalAmount,
                           MIN(cr.Id)               AS FirstId
                    FROM CashReceipt cr
                    LEFT JOIN Parties p ON p.Id = cr.PartyId
                    GROUP BY cr.VoucherNo
                    ORDER BY cr.Date DESC, cr.VoucherNo DESC", con);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                    _allSavedReceipts.Add(new ReceiptListRow
                    {
                        Id = r.GetInt32(r.GetOrdinal("FirstId")),
                        VoucherNo = r["VoucherNo"].ToString(),
                        Date = r["Date"].ToString()[..10],
                        PartyName = r["PartyName"].ToString(),
                        TotalAmount = Convert.ToDecimal(r["TotalAmount"])
                    });
            }
            catch (Exception ex) { MessageBox.Show("Error loading receipts: " + ex.Message); return; }

            ApplyListFilter(search, from, to);
        }

        private void ApplyListFilter(string search, DateTime? from, DateTime? to)
        {
            var filtered = _allSavedReceipts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
                filtered = filtered.Where(r =>
                    r.VoucherNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    r.PartyName.Contains(search, StringComparison.OrdinalIgnoreCase));

            if (from.HasValue)
                filtered = filtered.Where(r => DateTime.TryParse(r.Date, out var d) && d >= from.Value);
            if (to.HasValue)
                filtered = filtered.Where(r => DateTime.TryParse(r.Date, out var d) && d <= to.Value);

            var list = filtered.ToList();
            DgReceiptList.ItemsSource = null;
            DgReceiptList.ItemsSource = list;
            TxtListStatus.Text = $"{list.Count} receipt{(list.Count == 1 ? "" : "s")}";
        }

        // ── Side panel search / filter events ─────────────────────────────────────
        private void TxtListSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_listSearchPlaceholder)
            {
                TxtListSearch.Text = "";
                TxtListSearch.Foreground = new SolidColorBrush(Colors.White);
                _listSearchPlaceholder = false;
            }
        }
        private void TxtListSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtListSearch.Text))
            {
                TxtListSearch.Text = "Search voucher / party...";
                TxtListSearch.Foreground = new SolidColorBrush(Color.FromRgb(107, 143, 168));
                _listSearchPlaceholder = true;
            }
        }
        private void TxtListSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_listSearchPlaceholder) return;
            ApplyListFilter(TxtListSearch.Text, DtListFrom.SelectedDate, DtListTo.SelectedDate);
        }
        private void BtnListFilter_Click(object sender, RoutedEventArgs e)
        {
            string search = _listSearchPlaceholder ? "" : TxtListSearch.Text;
            ApplyListFilter(search, DtListFrom.SelectedDate, DtListTo.SelectedDate);
        }

        // ── Row selected in side panel ────────────────────────────────────────────
        private void DgReceiptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = DgReceiptList.SelectedItem != null;
            BtnLoadEdit.IsEnabled = hasSelection;
            BtnPrintSaved.IsEnabled = hasSelection;
            BtnDeleteSaved.IsEnabled = hasSelection;
        }

        // ── Load & Edit ───────────────────────────────────────────────────────────
        private void BtnLoadEdit_Click(object sender, RoutedEventArgs e)
        {
            if (DgReceiptList.SelectedItem is not ReceiptListRow sel) return;

            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(@"
                    SELECT cr.*, IFNULL(p.Name,'') AS PartyName,
                           IFNULL(p.Contact,'') AS PartyContact,
                           IFNULL(p.Address,'') AS PartyAddress
                    FROM CashReceipt cr
                    LEFT JOIN Parties p ON p.Id = cr.PartyId
                    WHERE cr.VoucherNo = @v
                    ORDER BY cr.Id", con);
                cmd.Parameters.AddWithValue("@v", sel.VoucherNo);

                _receiptItems.Clear();
                int srNo = 1;
                string date = "";

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    date = r["Date"].ToString()[..10];
                    var item = new CashReceiptItem
                    {
                        SrNo = srNo++,
                        PartyId = Convert.ToInt32(r["PartyId"]),
                        PartyName = r["PartyName"].ToString(),
                        PaymentMode = r["PaymentMode"].ToString(),
                        Remarks = r["Remarks"].ToString(),
                        Amount = Convert.ToDecimal(r["Amount"])
                    };
                    if (!(r["AttachData"] is DBNull))
                    {
                        item.AttachBytes = (byte[])r["AttachData"];
                        item.AttachName = r["AttachName"].ToString();
                    }
                    _receiptItems.Add(item);

                    // Set party in combo on first row
                    if (srNo == 2)
                    {
                        int pid = Convert.ToInt32(r["PartyId"]);
                        foreach (var p in (IEnumerable<Party>)cmbParty.ItemsSource)
                        {
                            if (p.Id == pid)
                            {
                                cmbParty.SelectedItem = p;
                                TxtPhone.Text = r["PartyContact"].ToString();
                                TxtAddress.Text = r["PartyAddress"].ToString();
                                break;
                            }
                        }
                    }
                }

                TxtVoucherNo.Text = sel.VoucherNo;
                DtReceiptDate.SelectedDate = DateTime.TryParse(date, out var dt) ? dt : DateTime.Today;
                _editingVoucherDbId = sel.Id;
                EditModeBadge.Visibility = Visibility.Visible;

                UpdateTotals();
                BorderReceiptList.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { MessageBox.Show("Error loading receipt: " + ex.Message); }
        }

        // ── Print Saved ───────────────────────────────────────────────────────────
        private void BtnPrintSaved_Click(object sender, RoutedEventArgs e)
        {
            if (DgReceiptList.SelectedItem is not ReceiptListRow sel) return;

            try
            {
                var items = new List<CashReceiptItem>();
                string partyName = "", phone = "", address = "", date = "";

                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(@"
                    SELECT cr.*, IFNULL(p.Name,'')    AS PartyName,
                                 IFNULL(p.Contact,'') AS PartyContact,
                                 IFNULL(p.Address,'') AS PartyAddress
                    FROM CashReceipt cr
                    LEFT JOIN Parties p ON p.Id = cr.PartyId
                    WHERE cr.VoucherNo = @v
                    ORDER BY cr.Id", con);
                cmd.Parameters.AddWithValue("@v", sel.VoucherNo);

                int srNo = 1;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    partyName = r["PartyName"].ToString();
                    phone = r["PartyContact"].ToString();
                    address = r["PartyAddress"].ToString();
                    date = r["Date"].ToString()[..10];

                    var item = new CashReceiptItem
                    {
                        SrNo = srNo++,
                        PartyId = Convert.ToInt32(r["PartyId"]),
                        PartyName = partyName,
                        PaymentMode = r["PaymentMode"].ToString(),
                        Remarks = r["Remarks"].ToString(),
                        Amount = Convert.ToDecimal(r["Amount"])
                    };
                    if (!(r["AttachData"] is DBNull))
                    {
                        item.AttachBytes = (byte[])r["AttachData"];
                        item.AttachName = r["AttachName"].ToString();
                    }
                    items.Add(item);
                }

                decimal total = items.Sum(i => i.Amount);
                var doc = BuildFlowDocument(sel.VoucherNo, date, partyName, phone, address, items, total);
                var preview = new PrintPreviewWindow(doc);
                preview.Owner = this;
                preview.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show("Print error: " + ex.Message); }
        }

        // ── Delete Saved ──────────────────────────────────────────────────────────
        private void BtnDeleteSaved_Click(object sender, RoutedEventArgs e)
        {
            if (DgReceiptList.SelectedItem is not ReceiptListRow sel) return;

            if (MessageBox.Show($"Delete receipt {sel.VoucherNo}?", "Confirm Delete",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                var cmd = new SQLiteCommand("DELETE FROM CashReceipt WHERE VoucherNo=@v", con);
                cmd.Parameters.AddWithValue("@v", sel.VoucherNo);
                cmd.ExecuteNonQuery();
                LoadReceiptList();
            }
            catch (Exception ex) { MessageBox.Show("Error deleting receipt: " + ex.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  FLOW DOCUMENT (Print)
        // ══════════════════════════════════════════════════════════════════════════
        private FlowDocument BuildFlowDocument(
            string voucherNo, string receiptDate,
            string partyName, string phone, string address,
            List<CashReceiptItem> items, decimal netAmount)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(40),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12
            };

            // Header banner
            var hb = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) };
            var hp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            hp.Children.Add(new TextBlock { Text = "Hale Marketing International", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hp.Children.Add(new TextBlock { Text = "Phone: 92-306-1917073", FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hb.Child = hp;
            doc.Blocks.Add(new BlockUIContainer(hb));

            var info = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            info.Inlines.Add(new Run($"Cash Receipt #: {voucherNo}    Date: {receiptDate}") { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(info);

            // Party info
            var pp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var ph = new Border { Background = Brushes.Black, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 4) };
            ph.Child = new TextBlock { Text = "Received From:", Foreground = Brushes.White, FontWeight = FontWeights.Bold };
            pp.Children.Add(ph);
            pp.Children.Add(new TextBlock { Text = partyName, FontSize = 13, Foreground = Brushes.Black });
            pp.Children.Add(new TextBlock { Text = $"Phone: {(string.IsNullOrWhiteSpace(phone) ? "N/A" : phone)}", FontSize = 12, Foreground = Brushes.Black });
            pp.Children.Add(new TextBlock { Text = $"Address: {(string.IsNullOrWhiteSpace(address) ? "N/A" : address)}", FontSize = 12, Foreground = Brushes.Black });
            doc.Blocks.Add(new BlockUIContainer(CloneUIPanel(pp)));

            // Table
            var table = new Table { CellSpacing = 0, BorderThickness = new Thickness(1), BorderBrush = Brushes.Black };
            foreach (var w in new double[] { 40, 200, 120, 100, 160, 50 })
                table.Columns.Add(new TableColumn { Width = new GridLength(w) });

            var hg = new TableRowGroup();
            var hr = new TableRow();
            foreach (var h in new[] { "Sr#", "Party", "Payment Mode", "Amount", "Remarks", "📎" })
                hr.Cells.Add(HCell(h));
            hg.Rows.Add(hr);
            table.RowGroups.Add(hg);

            var bg = new TableRowGroup();
            int sr = 1;
            foreach (var it in items)
            {
                var row = new TableRow();
                foreach (var v in new[] { sr.ToString(), it.PartyName ?? "", it.PaymentMode ?? "", it.Amount.ToString("N2"), it.Remarks ?? "", it.HasAttachment ? "📎" : "" })
                    row.Cells.Add(DCell(v));
                bg.Rows.Add(row);
                sr++;
            }
            table.RowGroups.Add(bg);
            doc.Blocks.Add(table);

            var totals = new Paragraph { Margin = new Thickness(0, 12, 0, 0), TextAlignment = TextAlignment.Right };
            totals.Inlines.Add(new Run($"Net Amount: {netAmount:N2}") { FontWeight = FontWeights.Bold, FontSize = 14 });
            doc.Blocks.Add(totals);

            var withAttach = items.Where(i => i.HasAttachment).ToList();
            if (withAttach.Count > 0)
            {
                var note = new Paragraph { Margin = new Thickness(0, 8, 0, 0), FontSize = 10 };
                note.Inlines.Add(new Run($"📎  Attachments: {string.Join(", ", withAttach.Select(i => i.AttachName ?? $"#{i.SrNo}"))}"));
                doc.Blocks.Add(note);
            }

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

        private static TableCell HCell(string text) => new TableCell(
            new Paragraph(new Run(text)) { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center })
        { Background = Brushes.LightGray, Padding = new Thickness(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Black };

        private static TableCell DCell(string text) => new TableCell(
            new Paragraph(new Run(text)) { TextAlignment = TextAlignment.Center })
        { Padding = new Thickness(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Black };

        private static UIElement CloneUIPanel(Panel original)
        {
            var clone = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 5, 0, 10) };
            foreach (var child in original.Children)
            {
                if (child is TextBlock tb)
                    clone.Children.Add(new TextBlock { Text = tb.Text, FontSize = tb.FontSize, FontWeight = tb.FontWeight, Foreground = tb.Foreground, HorizontalAlignment = tb.HorizontalAlignment });
                else if (child is Border b && b.Child is TextBlock inner)
                {
                    var nb = new Border { Background = b.Background, Padding = b.Padding, Margin = b.Margin };
                    nb.Child = new TextBlock { Text = inner.Text, Foreground = inner.Foreground, FontWeight = inner.FontWeight };
                    clone.Children.Add(nb);
                }
            }
            return clone;
        }

        // ── Model Classes ─────────────────────────────────────────────────────────
        public class CashReceiptItem : System.ComponentModel.INotifyPropertyChanged
        {
            private int _srNo;
            public int SrNo { get => _srNo; set { _srNo = value; OnPropertyChanged(nameof(SrNo)); } }
            public int PartyId { get; set; }
            public string PartyName { get; set; }
            public string PaymentMode { get; set; }
            public string Remarks { get; set; }
            public decimal Amount { get; set; }
            public byte[] AttachBytes { get; set; }
            public string AttachName { get; set; }
            public bool HasAttachment => AttachBytes != null && AttachBytes.Length > 0;
            public string HasAttachmentVisibility => HasAttachment ? "Visible" : "Collapsed";

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string p) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(p));
        }

        public class Party
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Contact { get; set; }
            public string Address { get; set; }
        }
    }
}