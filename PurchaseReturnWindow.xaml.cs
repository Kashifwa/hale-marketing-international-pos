using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;
using Microsoft.Win32;

namespace Hale_Marketing_International
{
    public partial class PurchaseReturnWindow : Window
    {
        private string _dbPath = "Data Source=posdata.db;Version=3;";
        private ObservableCollection<ReturnItem> _items = new ObservableCollection<ReturnItem>();

        private byte[] _pendingAttachBytes = null;
        private string _pendingAttachName = null;

        // Party state
        private int _selectedPartyId = 0;
        private string _selectedPartyName = "";
        private bool _partyConfirmed = false;
        private bool _suppressPartyTextChanged = false;

        // Return edit mode
        private int _editingReturnId = -1;
        private bool _isEditMode = false;

        // Row-level edit
        private int _editingRowIndex = -1;
        private bool _isEditingRow = false;

        // ── Models ───────────────────────────────────────────────────────────────
        private class PartyItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public string Contact { get; set; }
            public string Address { get; set; }
            public string TypeLabel => Type == "Payable" ? "Supplier" : "Customer";
        }

        private class ReturnListRow
        {
            public int ReturnDbId { get; set; }
            public string ReturnNo { get; set; }
            public string Date { get; set; }
            public string PartyName { get; set; }
            public decimal NetTotal { get; set; }
        }

        private class ProductTag
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public decimal Price { get; set; }
            public string Company { get; set; }
            public override string ToString() => $"{Name}   [{Code}]   {Company}";
        }

        private class ReturnItem : INotifyPropertyChanged
        {
            public int Index { get; set; }
            public int ProductId { get; set; }
            public string ProductCode { get; set; }
            public string ProductName { get; set; }
            public string Company { get; set; }
            public string Details { get; set; }
            public decimal Rate { get; set; }

            // ── Sirf ReturnQty — AvailableQty / PurchasedQty field hata di ──────
            private int _returnQuantity;
            public int ReturnQuantity
            {
                get => _returnQuantity;
                set { _returnQuantity = value; OnPC(nameof(ReturnQuantity)); OnPC(nameof(Amount)); }
            }
            public decimal Amount => Rate * ReturnQuantity;

            public byte[] AttachBytes { get; set; }
            public string AttachName { get; set; }
            public bool HasAttachment => AttachBytes != null && AttachBytes.Length > 0;
            public string AttachLabel => HasAttachment ? "📎 Open" : "";

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPC(string n) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private List<PartyItem> _allParties = new();
        private List<ReturnListRow> _allReturns = new();
        private bool _partySearchPlaceholder = true;
        private bool _isPlaceholder = true;

        // ── Constructor ──────────────────────────────────────────────────────────
        public PurchaseReturnWindow()
        {
            InitializeComponent();
            ReturnDataGrid.ItemsSource = _items;
            DtReturnDate.SelectedDate = DateTime.Today;
            EnsureTables();
            LoadAllParties();
            GenerateReturnNo();
        }

        // ── Ensure Tables ────────────────────────────────────────────────────────
        private void EnsureTables()
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(con);

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS PurchaseReturn (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnNo TEXT, ReturnDate TEXT, SupplierId INTEGER,
                    SubTotal REAL, NetAmount REAL);";
                cmd.ExecuteNonQuery();

                // ReturnQty only — PurchasedQty column removed from new installs
                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS PurchaseReturnItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnId INTEGER, ProductId INTEGER,
                    ProductCode TEXT, ProductName TEXT, Company TEXT,
                    ReturnQty REAL,
                    Rate REAL, Amount REAL,
                    Details TEXT, AttachName TEXT, AttachData BLOB);";
                cmd.ExecuteNonQuery();

                // Safe migrations
                foreach (var col in new[]
                {
                    "ALTER TABLE PurchaseReturnItems ADD COLUMN Company TEXT",
                    "ALTER TABLE PurchaseReturnItems ADD COLUMN Details TEXT",
                    "ALTER TABLE PurchaseReturnItems ADD COLUMN AttachName TEXT",
                    "ALTER TABLE PurchaseReturnItems ADD COLUMN AttachData BLOB",
                    "ALTER TABLE PurchaseReturnItems ADD COLUMN ReturnQty REAL"
                })
                { try { new SQLiteCommand(col, con).ExecuteNonQuery(); } catch { } }

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS CustomerLedger (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartyId INTEGER, Date TEXT, Description TEXT,
                    Debit REAL, Credit REAL, Balance REAL);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { MessageBox.Show("EnsureTables: " + ex.Message); }
        }

        // ── Load ALL Parties ─────────────────────────────────────────────────────
        private void LoadAllParties()
        {
            _allParties.Clear();
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT Id, Name, Type, Contact, Address FROM Parties ORDER BY Name", con);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    _allParties.Add(new PartyItem
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        Name = r["Name"].ToString(),
                        Type = r["Type"].ToString(),
                        Contact = r["Contact"]?.ToString() ?? "",
                        Address = r["Address"]?.ToString() ?? ""
                    });
                if (_allParties.Count == 0)
                    MessageBox.Show("No suppliers found. Please add suppliers first.");
            }
            catch (Exception ex) { MessageBox.Show("LoadAllParties: " + ex.Message); }
        }

        // ── Party Popup ──────────────────────────────────────────────────────────
        private void TxtPartySearch_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_partySearchPlaceholder)
            { TxtPartySearch.Text = ""; _partySearchPlaceholder = false; }
            ShowPartyDropdown(TxtPartySearch.Text);
        }

        private void TxtPartySearch_LostFocus(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!PartyList.IsMouseOver) PartyPopup.IsOpen = false;
                if (!_partyConfirmed && string.IsNullOrWhiteSpace(TxtPartySearch.Text))
                {
                    _suppressPartyTextChanged = true;
                    TxtPartySearch.Text = "Search supplier by name or ID...";
                    _partySearchPlaceholder = true;
                    _suppressPartyTextChanged = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TxtPartySearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressPartyTextChanged) return;
            if (_partySearchPlaceholder) return;
            if (_partyConfirmed) { _partyConfirmed = false; _selectedPartyId = 0; }
            else _selectedPartyId = 0;
            ShowPartyDropdown(TxtPartySearch.Text);
        }

        private void TxtPartySearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && PartyList.Items.Count > 0)
            { PartyList.Focus(); PartyList.SelectedIndex = 0; e.Handled = true; }
            else if (e.Key == Key.Escape) PartyPopup.IsOpen = false;
        }

        private void PartyList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && PartyList.SelectedItem is PartyItem p)
            { SelectParty(p); e.Handled = true; }
            else if (e.Key == Key.Escape)
            { PartyPopup.IsOpen = false; TxtPartySearch.Focus(); }
        }

        private void PartyList_MouseUp(object sender, MouseButtonEventArgs e)
        { if (PartyList.SelectedItem is PartyItem p) SelectParty(p); }

        private void ShowPartyDropdown(string filter)
        {
            IEnumerable<PartyItem> filtered;
            if (string.IsNullOrWhiteSpace(filter))
                filtered = _allParties;
            else if (int.TryParse(filter.TrimStart('#'), out int id))
                filtered = _allParties.Where(p => p.Id == id);
            else
                filtered = _allParties.Where(p =>
                    p.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

            PartyList.ItemsSource = filtered.ToList();
            PartyPopup.IsOpen = filtered.Any();
        }

        private void SelectParty(PartyItem p)
        {
            _selectedPartyId = p.Id;
            _selectedPartyName = p.Name;
            _partyConfirmed = true;
            _partySearchPlaceholder = false;

            _suppressPartyTextChanged = true;
            TxtPartySearch.Text = $"#{p.Id}  {p.Name}  [{p.TypeLabel}]";
            _suppressPartyTextChanged = false;

            TxtSupplierPhone.Text = p.Contact;
            TxtSupplierAddress.Text = p.Address;
            PartyPopup.IsOpen = false;
        }

        // ── Generate Return No ────────────────────────────────────────────────────
        private void GenerateReturnNo()
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand("SELECT IFNULL(MAX(Id),0)+1 FROM PurchaseReturn", con);
                string no = "PRN-" + Convert.ToInt64(cmd.ExecuteScalar()).ToString().PadLeft(6, '0');
                TxtReturnNo.Text = no;
                TxtReturnNoBox.Text = no;
            }
            catch { TxtReturnNo.Text = TxtReturnNoBox.Text = "PRN-000001"; }
            DtReturnDate.SelectedDate = DateTime.Today;
        }

        // ── Product Search ───────────────────────────────────────────────────────
        private void TxtSearch_GotFocus(object sender, RoutedEventArgs e)
        { if (_isPlaceholder) { TxtSearch.Text = ""; _isPlaceholder = false; } }

        private void TxtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSearch.Text))
            { TxtSearch.Text = "Type to search..."; _isPlaceholder = true; }
            Dispatcher.BeginInvoke(new Action(() =>
            { if (!ProductList.IsMouseOver) ProductPopup.IsOpen = false; }),
            System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isPlaceholder) return;
            var txt = TxtSearch.Text?.Trim();
            if (string.IsNullOrEmpty(txt)) { ProductPopup.IsOpen = false; return; }
            PopulateProductList(txt);
        }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && ProductList.Items.Count > 0)
            { ProductList.Focus(); ProductList.SelectedIndex = 0; e.Handled = true; }
            else if (e.Key == Key.Escape) ProductPopup.IsOpen = false;
        }

        private void ProductList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ProductList.SelectedItem is ProductTag p)
            { SelectProduct(p); e.Handled = true; }
            else if (e.Key == Key.Escape)
            { ProductPopup.IsOpen = false; TxtSearch.Focus(); }
        }

        private void ProductList_MouseUp(object sender, MouseButtonEventArgs e)
        { if (ProductList.SelectedItem is ProductTag p) SelectProduct(p); }

        private void PopulateProductList(string txt)
        {
            try
            {
                ProductList.Items.Clear();
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                // Quantity column nahi chahiye — sirf product info
                using var cmd = new SQLiteCommand(
                    "SELECT Id, ProductName, Code, PurchasePrice, Category FROM Products " +
                    "WHERE ProductName LIKE @q OR Code LIKE @q LIMIT 50", con);
                cmd.Parameters.AddWithValue("@q", "%" + txt + "%");
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    ProductList.Items.Add(new ProductTag
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        Code = r["Code"]?.ToString() ?? "",
                        Name = r["ProductName"]?.ToString() ?? "",
                        Price = r["PurchasePrice"] == DBNull.Value ? 0m : Convert.ToDecimal(r["PurchasePrice"]),
                        Company = r["Category"]?.ToString() ?? ""
                    });
                ProductPopup.IsOpen = ProductList.Items.Count > 0;
            }
            catch (Exception ex) { MessageBox.Show("Search: " + ex.Message); }
        }

        private void SelectProduct(ProductTag p)
        {
            if (p == null) return;
            TxtProductCode.Text = p.Code;
            TxtProductName.Text = p.Name;
            TxtCompany.Text = p.Company;
            TxtRate.Text = p.Price.ToString("N2");
            TxtReturnQty.Text = "";          // sirf yeh field — AvailQty nahi
            ProductPopup.IsOpen = false;
            TxtSearch.Text = p.Name;
            _isPlaceholder = false;
            TxtReturnQty.Focus();
        }

        // ── Placeholder handlers ─────────────────────────────────────────────────
        private void TxtDetails_GotFocus(object sender, RoutedEventArgs e)
        { if (TxtDetails.Text == "Optional details...") TxtDetails.Text = ""; }
        private void TxtDetails_LostFocus(object sender, RoutedEventArgs e)
        { if (string.IsNullOrWhiteSpace(TxtDetails.Text)) TxtDetails.Text = "Optional details..."; }

        // ── Attachment ───────────────────────────────────────────────────────────
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
            catch (Exception ex) { MessageBox.Show("Cannot read file: " + ex.Message); }
        }

        private void BtnClearAttach_Click(object sender, RoutedEventArgs e)
        {
            _pendingAttachBytes = null;
            _pendingAttachName = null;
            BtnClearAttach.Visibility = Visibility.Collapsed;
            TxtAttachHint.Visibility = Visibility.Collapsed;
            TxtAttachHint.Text = "";
        }

        private void BtnOpenAttach_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out int idx)) return;
            var item = _items.FirstOrDefault(x => x.Index == idx);
            if (item?.AttachBytes == null) return;
            OpenBytesWithDefaultApp(item.AttachBytes, item.AttachName ?? "attachment");
        }

        private static void OpenBytesWithDefaultApp(byte[] data, string filename)
        {
            try
            {
                string ext = Path.GetExtension(filename);
                string tmp = Path.Combine(Path.GetTempPath(), $"hmi_pret_{Guid.NewGuid():N}{ext}");
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

        // ── Row-level edit ───────────────────────────────────────────────────────
        private void BtnEditRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out int idx)) return;
            var item = _items.FirstOrDefault(x => x.Index == idx);
            if (item == null) return;

            TxtProductCode.Text = item.ProductCode;
            TxtProductName.Text = item.ProductName;
            TxtCompany.Text = item.Company ?? "";
            TxtSearch.Text = item.ProductName;
            _isPlaceholder = false;
            TxtRate.Text = item.Rate.ToString("N2");
            TxtReturnQty.Text = item.ReturnQuantity.ToString();
            TxtDetails.Text = string.IsNullOrWhiteSpace(item.Details) ? "Optional details..." : item.Details;

            _pendingAttachBytes = item.AttachBytes;
            _pendingAttachName = item.AttachName;
            if (_pendingAttachBytes != null && _pendingAttachBytes.Length > 0)
            {
                BtnClearAttach.Visibility = Visibility.Visible;
                TxtAttachHint.Text = $"📎  {_pendingAttachName}  ({FormatBytes(_pendingAttachBytes.Length)})";
                TxtAttachHint.Visibility = Visibility.Visible;
            }
            else
            {
                BtnClearAttach.Visibility = Visibility.Collapsed;
                TxtAttachHint.Visibility = Visibility.Collapsed;
            }

            _editingRowIndex = idx;
            _isEditingRow = true;
            RowEditHint.Visibility = Visibility.Visible;
            TxtReturnQty.Focus();
            TxtReturnQty.SelectAll();
        }

        // ── Add / Update product ─────────────────────────────────────────────────
        private void TxtReturnQty_KeyDown(object sender, KeyEventArgs e)
        { if (e.Key == Key.Enter) BtnAddProduct_Click(null, null); }

        private void BtnAddProduct_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtProductCode.Text))
            { MessageBox.Show("Select a product first."); return; }

            if (!int.TryParse(TxtReturnQty.Text, out int rQty) || rQty <= 0)
            { MessageBox.Show("Enter a valid return quantity."); return; }

            if (!decimal.TryParse(TxtRate.Text, out decimal rate) || rate <= 0)
            { MessageBox.Show("Invalid rate."); return; }

            string details = TxtDetails.Text == "Optional details..." ? "" : TxtDetails.Text.Trim();

            if (_isEditingRow)
            {
                var existing = _items.FirstOrDefault(x => x.Index == _editingRowIndex);
                if (existing != null)
                {
                    existing.ProductCode = TxtProductCode.Text;
                    existing.ProductName = TxtProductName.Text;
                    existing.Company = TxtCompany.Text;
                    existing.Details = details;
                    existing.Rate = rate;
                    existing.ReturnQuantity = rQty;
                    existing.AttachBytes = _pendingAttachBytes;
                    existing.AttachName = _pendingAttachName;
                }
                _isEditingRow = false;
                _editingRowIndex = -1;
                RowEditHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                _items.Add(new ReturnItem
                {
                    Index = _items.Count + 1,
                    ProductCode = TxtProductCode.Text,
                    ProductName = TxtProductName.Text,
                    Company = TxtCompany.Text,
                    Details = details,
                    Rate = rate,
                    ReturnQuantity = rQty,
                    AttachBytes = _pendingAttachBytes,
                    AttachName = _pendingAttachName
                });
            }

            ReindexItems();
            UpdateTotals();
            ClearEntry();
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int idx))
            {
                var item = _items.FirstOrDefault(x => x.Index == idx);
                if (item != null) _items.Remove(item);
            }
            ReindexItems();
            UpdateTotals();
        }

        private void ReindexItems()
        { for (int i = 0; i < _items.Count; i++) _items[i].Index = i + 1; }

        private void ClearEntry()
        {
            TxtSearch.Text = "Type to search...";
            _isPlaceholder = true;
            ProductPopup.IsOpen = false;
            TxtProductCode.Text = "";
            TxtProductName.Text = "";
            TxtCompany.Text = "";
            TxtRate.Text = "";
            TxtReturnQty.Text = "";
            TxtDetails.Text = "Optional details...";
            BtnClearAttach_Click(null, null);
        }

        // ── Totals ───────────────────────────────────────────────────────────────
        private void UpdateTotals()
        {
            decimal sub = _items.Sum(i => i.Amount);
            TxtSubtotal.Text = sub.ToString("N2");
            TxtNetAmount.Text = sub.ToString("N2");
            TxtItemsCount.Text = _items.Count.ToString();
        }

        // ── Save ─────────────────────────────────────────────────────────────────
        private void BtnSaveReturn_Click(object sender, RoutedEventArgs e)
        {
            if (!_partyConfirmed || _selectedPartyId == 0)
            { MessageBox.Show("Please select a supplier from the dropdown first."); TxtPartySearch.Focus(); return; }
            if (_items.Count == 0) { MessageBox.Show("Add at least one product."); return; }

            if (_isEditMode) SaveEditedReturn();
            else SaveNewReturn();
        }

        private void SaveNewReturn()
        {
            string date = DtReturnDate.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            decimal subtotal = _items.Sum(i => i.Amount);
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var tran = con.BeginTransaction();

                long returnId = InsertReturnMaster(con, tran, TxtReturnNo.Text, date, subtotal);
                // -1 = stock se minus karo (supplier ko wapas diya)
                InsertReturnItems(con, tran, returnId, -1);
                InsertLedger(con, tran, _selectedPartyId, date,
                             $"Purchase Return - {TxtReturnNo.Text}", subtotal);
                tran.Commit();
                MessageBox.Show("Purchase return saved successfully.");
                NewReturn();
                RefreshReturnList();
            }
            catch (Exception ex) { MessageBox.Show("Save error: " + ex.Message); }
        }

        private void SaveEditedReturn()
        {
            string date = DtReturnDate.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            decimal subtotal = _items.Sum(i => i.Amount);
            string retNo = TxtReturnNo.Text.Replace("  [EDITING]", "").Trim();

            if (MessageBox.Show("Update this purchase return? Stock and ledger will be re-applied.",
                "Confirm Update", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var tran = con.BeginTransaction();

                // Purani return reverse karo (stock wapas add karo)
                ReverseOldItems(con, tran, _editingReturnId);
                DeleteLedgerByDesc(con, tran, _selectedPartyId, $"Purchase Return - {retNo}");
                new SQLiteCommand($"DELETE FROM PurchaseReturnItems WHERE ReturnId={_editingReturnId}", con, tran).ExecuteNonQuery();

                using (var cmd = new SQLiteCommand(@"
                    UPDATE PurchaseReturn SET ReturnDate=@dt, SupplierId=@pid,
                        SubTotal=@sub, NetAmount=@net WHERE Id=@id", con, tran))
                {
                    cmd.Parameters.AddWithValue("@dt", date);
                    cmd.Parameters.AddWithValue("@pid", _selectedPartyId);
                    cmd.Parameters.AddWithValue("@sub", subtotal);
                    cmd.Parameters.AddWithValue("@net", subtotal);
                    cmd.Parameters.AddWithValue("@id", _editingReturnId);
                    cmd.ExecuteNonQuery();
                }

                InsertReturnItems(con, tran, _editingReturnId, -1);
                InsertLedger(con, tran, _selectedPartyId, date,
                             $"Purchase Return - {retNo}", subtotal);
                tran.Commit();
                MessageBox.Show("Purchase return updated successfully.");
                _isEditMode = false;
                _editingReturnId = -1;
                NewReturn();
                RefreshReturnList();
            }
            catch (Exception ex) { MessageBox.Show("Update error: " + ex.Message); }
        }

        // ── Return List Panel ─────────────────────────────────────────────────────
        private void BtnShowReturnList_Click(object sender, RoutedEventArgs e)
        {
            BorderReturnList.Visibility = Visibility.Visible;
            RefreshReturnList();
        }

        private void BtnCloseReturnList_Click(object sender, RoutedEventArgs e)
        { BorderReturnList.Visibility = Visibility.Collapsed; }

        private void RefreshReturnList()
        {
            _allReturns.Clear();
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(@"
                    SELECT pr.Id, pr.ReturnNo, pr.ReturnDate, pr.NetAmount, p.Name
                    FROM PurchaseReturn pr LEFT JOIN Parties p ON p.Id = pr.SupplierId
                    ORDER BY pr.Id DESC", con);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    _allReturns.Add(new ReturnListRow
                    {
                        ReturnDbId = Convert.ToInt32(r["Id"]),
                        ReturnNo = r["ReturnNo"].ToString(),
                        Date = r["ReturnDate"].ToString(),
                        PartyName = r["Name"]?.ToString() ?? "",
                        NetTotal = r["NetAmount"] == DBNull.Value ? 0 : Convert.ToDecimal(r["NetAmount"])
                    });
            }
            catch { }
            DgReturnList.ItemsSource = null;
            DgReturnList.ItemsSource = _allReturns;
        }

        private void DgReturnList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool has = DgReturnList.SelectedItem is ReturnListRow;
            BtnEditReturn.IsEnabled = has;
            BtnDeleteReturn.IsEnabled = has;
        }

        private void BtnRetListFilter_Click(object sender, RoutedEventArgs e)
        {
            string search = TxtRetListSearch.Text.Trim().ToLower();
            bool ph = search == "search return / supplier...";
            var filtered = _allReturns.ToList();
            if (!ph && !string.IsNullOrEmpty(search))
                filtered = filtered.Where(x =>
                    x.ReturnNo.ToLower().Contains(search) ||
                    x.PartyName.ToLower().Contains(search)).ToList();
            if (DtRetListFrom.SelectedDate != null)
                filtered = filtered.Where(x =>
                    DateTime.TryParse(x.Date, out var d) &&
                    d >= DtRetListFrom.SelectedDate.Value.Date).ToList();
            if (DtRetListTo.SelectedDate != null)
                filtered = filtered.Where(x =>
                    DateTime.TryParse(x.Date, out var d) &&
                    d <= DtRetListTo.SelectedDate.Value.Date).ToList();
            DgReturnList.ItemsSource = filtered;
        }

        private void TxtRetListSearch_GotFocus(object sender, RoutedEventArgs e)
        { if (TxtRetListSearch.Text == "Search return / supplier...") TxtRetListSearch.Text = ""; }
        private void TxtRetListSearch_LostFocus(object sender, RoutedEventArgs e)
        { if (string.IsNullOrWhiteSpace(TxtRetListSearch.Text)) TxtRetListSearch.Text = "Search return / supplier..."; }
        private void TxtRetListSearch_TextChanged(object sender, TextChangedEventArgs e)
        { if (IsLoaded) BtnRetListFilter_Click(null, null); }

        // ── Load for Edit ────────────────────────────────────────────────────────
        private void BtnEditReturn_Click(object sender, RoutedEventArgs e)
        {
            if (DgReturnList.SelectedItem is ReturnListRow sel) LoadReturnForEdit(sel.ReturnDbId);
        }

        private void LoadReturnForEdit(int retId)
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();

                using (var cmd = new SQLiteCommand(@"
                    SELECT pr.*, p.Name, p.Contact, p.Address
                    FROM PurchaseReturn pr LEFT JOIN Parties p ON p.Id = pr.SupplierId
                    WHERE pr.Id = @id", con))
                {
                    cmd.Parameters.AddWithValue("@id", retId);
                    using var r = cmd.ExecuteReader();
                    if (!r.Read()) return;

                    _editingReturnId = retId;
                    _isEditMode = true;

                    _selectedPartyId = r["SupplierId"] == DBNull.Value ? 0 : Convert.ToInt32(r["SupplierId"]);
                    _selectedPartyName = r["Name"]?.ToString() ?? "";
                    _partyConfirmed = true;
                    _partySearchPlaceholder = false;

                    _suppressPartyTextChanged = true;
                    TxtPartySearch.Text = $"#{_selectedPartyId}  {_selectedPartyName}  [Supplier]";
                    _suppressPartyTextChanged = false;

                    TxtSupplierPhone.Text = r["Contact"]?.ToString() ?? "";
                    TxtSupplierAddress.Text = r["Address"]?.ToString() ?? "";
                    TxtReturnNo.Text = r["ReturnNo"].ToString() + "  [EDITING]";
                    TxtReturnNoBox.Text = r["ReturnNo"].ToString() + "  [EDITING]";

                    if (DateTime.TryParse(r["ReturnDate"].ToString(), out var d))
                        DtReturnDate.SelectedDate = d;
                }

                _items.Clear();
                using (var cmd = new SQLiteCommand(
                    "SELECT * FROM PurchaseReturnItems WHERE ReturnId=@id ORDER BY Id", con))
                {
                    cmd.Parameters.AddWithValue("@id", retId);
                    using var r = cmd.ExecuteReader();
                    int idx = 1;
                    while (r.Read())
                    {
                        byte[] ab = r["AttachData"] == DBNull.Value ? null : (byte[])r["AttachData"];
                        _items.Add(new ReturnItem
                        {
                            Index = idx++,
                            ProductCode = r["ProductCode"].ToString(),
                            ProductName = r["ProductName"].ToString(),
                            Company = r["Company"]?.ToString() ?? "",
                            Details = r["Details"]?.ToString() ?? "",
                            Rate = r["Rate"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Rate"]),
                            ReturnQuantity = r["ReturnQty"] == DBNull.Value ? 0 : Convert.ToInt32(r["ReturnQty"]),
                            AttachBytes = ab,
                            AttachName = r["AttachName"]?.ToString()
                        });
                    }
                }

                UpdateTotals();
                EditModeBadge.Visibility = Visibility.Visible;
                BorderReturnList.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { MessageBox.Show("Load error: " + ex.Message); }
        }

        // ── Delete Return ─────────────────────────────────────────────────────────
        private void BtnDeleteReturn_Click(object sender, RoutedEventArgs e)
        {
            if (DgReturnList.SelectedItem is not ReturnListRow sel) return;
            if (MessageBox.Show($"Delete return {sel.ReturnNo}?\nStock and ledger will be reversed.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var tran = con.BeginTransaction();
                ReverseOldItems(con, tran, sel.ReturnDbId);
                DeleteLedgerByReturnNo(con, tran, sel.ReturnNo);
                new SQLiteCommand($"DELETE FROM PurchaseReturnItems WHERE ReturnId={sel.ReturnDbId}", con, tran).ExecuteNonQuery();
                new SQLiteCommand($"DELETE FROM PurchaseReturn WHERE Id={sel.ReturnDbId}", con, tran).ExecuteNonQuery();
                tran.Commit();
                MessageBox.Show("Return deleted and stock restored.");
                RefreshReturnList();
            }
            catch (Exception ex) { MessageBox.Show("Delete error: " + ex.Message); }
        }

        // ── DB Helpers ───────────────────────────────────────────────────────────
        private long InsertReturnMaster(SQLiteConnection con, SQLiteTransaction tran,
            string returnNo, string date, decimal subtotal)
        {
            using var cmd = new SQLiteCommand(@"
                INSERT INTO PurchaseReturn (ReturnNo,ReturnDate,SupplierId,SubTotal,NetAmount)
                VALUES(@no,@dt,@sid,@sub,@net);
                SELECT last_insert_rowid();", con, tran);
            cmd.Parameters.AddWithValue("@no", returnNo);
            cmd.Parameters.AddWithValue("@dt", date);
            cmd.Parameters.AddWithValue("@sid", _selectedPartyId);
            cmd.Parameters.AddWithValue("@sub", subtotal);
            cmd.Parameters.AddWithValue("@net", subtotal);
            return (long)cmd.ExecuteScalar();
        }

        /// <summary>
        /// Items insert karo aur Products.Quantity direct update karo.
        /// stockDelta = -1 (purchase return → stock kam hota hai, supplier ko wapas gaya)
        /// </summary>
        private void InsertReturnItems(SQLiteConnection con, SQLiteTransaction tran,
            long returnId, int stockDelta)
        {
            foreach (var it in _items)
            {
                // ProductId resolve karo
                int pid = 0;
                using (var c = new SQLiteCommand(
                    "SELECT Id FROM Products WHERE Code=@c LIMIT 1", con, tran))
                {
                    c.Parameters.AddWithValue("@c", it.ProductCode);
                    var res = c.ExecuteScalar();
                    if (res != null && res != DBNull.Value) pid = Convert.ToInt32(res);
                }

                // Row insert — PurchasedQty column nahi
                using (var c = new SQLiteCommand(@"
                    INSERT INTO PurchaseReturnItems
                      (ReturnId, ProductId, ProductCode, ProductName, Company,
                       ReturnQty, Rate, Amount, Details, AttachName, AttachData)
                    VALUES(@rid,@pid,@code,@name,@co,
                           @rq,@rate,@amt,@det,@an,@ad)", con, tran))
                {
                    c.Parameters.AddWithValue("@rid", returnId);
                    c.Parameters.AddWithValue("@pid", pid);
                    c.Parameters.AddWithValue("@code", it.ProductCode);
                    c.Parameters.AddWithValue("@name", it.ProductName);
                    c.Parameters.AddWithValue("@co", it.Company ?? "");
                    c.Parameters.AddWithValue("@rq", it.ReturnQuantity);
                    c.Parameters.AddWithValue("@rate", it.Rate);
                    c.Parameters.AddWithValue("@amt", it.Amount);
                    c.Parameters.AddWithValue("@det", it.Details ?? "");
                    c.Parameters.AddWithValue("@an", (object)it.AttachName ?? DBNull.Value);
                    c.Parameters.AddWithValue("@ad", (object)it.AttachBytes ?? DBNull.Value);
                    c.ExecuteNonQuery();
                }

                // Products.Quantity direct update
                if (pid > 0)
                {
                    using var c = new SQLiteCommand(
                        "UPDATE Products SET Quantity = IFNULL(Quantity,0) + @q WHERE Id=@id",
                        con, tran);
                    c.Parameters.AddWithValue("@q", it.ReturnQuantity * stockDelta);
                    c.Parameters.AddWithValue("@id", pid);
                    c.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Purani return reverse karo:
        /// purchase return -stock tha, undo ke liye +stock wapas
        /// </summary>
        private void ReverseOldItems(SQLiteConnection con, SQLiteTransaction tran, int returnId)
        {
            using var cmd = new SQLiteCommand(
                "SELECT ProductCode, ReturnQty FROM PurchaseReturnItems WHERE ReturnId=@id", con, tran);
            cmd.Parameters.AddWithValue("@id", returnId);
            var old = new List<(string code, int qty)>();
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    old.Add((r["ProductCode"].ToString(),
                             r["ReturnQty"] == DBNull.Value ? 0 : Convert.ToInt32(r["ReturnQty"])));

            foreach (var (code, qty) in old)
            {
                int pid = 0;
                using (var c = new SQLiteCommand(
                    "SELECT Id FROM Products WHERE Code=@c LIMIT 1", con, tran))
                {
                    c.Parameters.AddWithValue("@c", code);
                    var res = c.ExecuteScalar();
                    if (res == null || res == DBNull.Value) continue;
                    pid = Convert.ToInt32(res);
                }
                if (pid <= 0) continue;
                // Add back to stock (reverse the -1 deduction)
                using var u = new SQLiteCommand(
                    "UPDATE Products SET Quantity = IFNULL(Quantity,0) + @q WHERE Id=@id",
                    con, tran);
                u.Parameters.AddWithValue("@q", qty);
                u.Parameters.AddWithValue("@id", pid);
                u.ExecuteNonQuery();
            }
        }

        private void InsertLedger(SQLiteConnection con, SQLiteTransaction tran,
            int partyId, string date, string desc, decimal debit)
        {
            decimal bal = 0;
            using (var c = new SQLiteCommand(
                "SELECT IFNULL(Balance,0) FROM CustomerLedger WHERE PartyId=@pid ORDER BY Id DESC LIMIT 1",
                con, tran))
            {
                c.Parameters.AddWithValue("@pid", partyId);
                var res = c.ExecuteScalar();
                if (res != null && res != DBNull.Value) bal = Convert.ToDecimal(res);
            }
            using (var c = new SQLiteCommand(@"
                INSERT INTO CustomerLedger (PartyId,Date,Description,Debit,Credit,Balance)
                VALUES(@pid,@dt,@desc,@deb,0,@bal)", con, tran))
            {
                c.Parameters.AddWithValue("@pid", partyId);
                c.Parameters.AddWithValue("@dt", date);
                c.Parameters.AddWithValue("@desc", desc);
                c.Parameters.AddWithValue("@deb", debit);
                c.Parameters.AddWithValue("@bal", bal - debit);
                c.ExecuteNonQuery();
            }
        }

        private void DeleteLedgerByDesc(SQLiteConnection con, SQLiteTransaction tran,
            int partyId, string desc)
        {
            using var c = new SQLiteCommand(
                "DELETE FROM CustomerLedger WHERE PartyId=@pid AND Description=@desc", con, tran);
            c.Parameters.AddWithValue("@pid", partyId);
            c.Parameters.AddWithValue("@desc", desc);
            c.ExecuteNonQuery();
        }

        private void DeleteLedgerByReturnNo(SQLiteConnection con, SQLiteTransaction tran, string returnNo)
        {
            using var c = new SQLiteCommand(
                "DELETE FROM CustomerLedger WHERE Description LIKE @desc", con, tran);
            c.Parameters.AddWithValue("@desc", $"%{returnNo}%");
            c.ExecuteNonQuery();
        }

        // ── Clear / New ──────────────────────────────────────────────────────────
        private void BtnClear_Click(object sender, RoutedEventArgs e) => NewReturn();

        private void NewReturn()
        {
            _isEditMode = false;
            _editingReturnId = -1;
            _isEditingRow = false;
            _editingRowIndex = -1;
            _selectedPartyId = 0;
            _selectedPartyName = "";
            _partyConfirmed = false;
            _items.Clear();

            _suppressPartyTextChanged = true;
            TxtPartySearch.Text = "Search supplier by name or ID...";
            _suppressPartyTextChanged = false;
            _partySearchPlaceholder = true;

            TxtSupplierPhone.Text = "";
            TxtSupplierAddress.Text = "";
            EditModeBadge.Visibility = Visibility.Collapsed;
            RowEditHint.Visibility = Visibility.Collapsed;
            UpdateTotals();
            ClearEntry();
            GenerateReturnNo();
        }

        // ── Print ────────────────────────────────────────────────────────────────
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) { MessageBox.Show("No items to print."); return; }
            decimal.TryParse(TxtNetAmount.Text, out decimal net);
            var doc = BuildFlowDocument(
                TxtReturnNo.Text.Replace("  [EDITING]", "").Trim(),
                DtReturnDate.SelectedDate?.ToString("dd-MM-yyyy") ?? "",
                _selectedPartyName, TxtSupplierPhone.Text, TxtSupplierAddress.Text,
                _items.ToList(), net);
            ShowPreview(doc);
        }

        private FlowDocument BuildFlowDocument(
            string returnNo, string returnDate,
            string supplierName, string supplierPhone, string supplierAddress,
            List<ReturnItem> items, decimal netTotal)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(40),
                ColumnWidth = double.PositiveInfinity,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12
            };

            var hb = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(4), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12) };
            var hp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            hp.Children.Add(new TextBlock { Text = "Hale Marketing International - Multan", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hp.Children.Add(new TextBlock { Text = "Phone: 92-306-1917073", FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            hb.Child = hp; doc.Blocks.Add(new BlockUIContainer(hb));

            var info = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
            info.Inlines.Add(new Run($"Purchase Return #: {returnNo}    Date: {returnDate}"));
            doc.Blocks.Add(info);

            var sp2 = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var sh = new Border { Background = Brushes.Black, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 4) };
            sh.Child = new TextBlock { Text = "Supplier:", Foreground = Brushes.White, FontWeight = FontWeights.Bold };
            sp2.Children.Add(sh);
            sp2.Children.Add(new TextBlock { Text = supplierName, FontSize = 13, Foreground = Brushes.Black });
            sp2.Children.Add(new TextBlock { Text = $"Phone: {(string.IsNullOrWhiteSpace(supplierPhone) ? "N/A" : supplierPhone)}", FontSize = 12, Foreground = Brushes.Black });
            sp2.Children.Add(new TextBlock { Text = $"Address: {(string.IsNullOrWhiteSpace(supplierAddress) ? "N/A" : supplierAddress)}", FontSize = 12, Foreground = Brushes.Black });
            doc.Blocks.Add(new BlockUIContainer(ClonePanel(sp2)));

            // Table: Sr#, Code, Product Name, Company, Details, Ret.Qty, Rate, Amount, 📎
            var table = new Table { CellSpacing = 0, BorderThickness = new Thickness(1), BorderBrush = Brushes.Black };
            foreach (var w in new double[] { 28, 65, 160, 105, 110, 52, 72, 88, 38 })
                table.Columns.Add(new TableColumn { Width = new GridLength(w) });

            var hg = new TableRowGroup(); var hr = new TableRow();
            foreach (var h in new[] { "Sr#", "Code", "Product Name", "Company", "Details", "Ret.Qty", "Rate", "Amount", "📎" })
                hr.Cells.Add(HCell(h));
            hg.Rows.Add(hr); table.RowGroups.Add(hg);

            var bg = new TableRowGroup(); int sr = 1;
            foreach (var it in items)
            {
                var row = new TableRow();
                foreach (var v in new[] {
                    sr.ToString(), it.ProductCode, it.ProductName,
                    it.Company ?? "", it.Details ?? "",
                    it.ReturnQuantity.ToString(), it.Rate.ToString("N2"),
                    it.Amount.ToString("N2"), it.HasAttachment ? "📎" : "" })
                    row.Cells.Add(DCell(v));
                bg.Rows.Add(row); sr++;
            }
            table.RowGroups.Add(bg); doc.Blocks.Add(table);

            var tot = new Paragraph { Margin = new Thickness(0, 12, 0, 0), TextAlignment = TextAlignment.Right };
            tot.Inlines.Add(new Run($"Net Amount: {netTotal:N2}\n") { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(tot);

            var withAttach = items.Where(i => i.HasAttachment).ToList();
            if (withAttach.Count > 0)
            {
                var note = new Paragraph { Margin = new Thickness(0, 6, 0, 0), FontSize = 10 };
                note.Inlines.Add(new Run($"📎  Attachments: {string.Join(", ", withAttach.Select(i => i.AttachName ?? $"#{i.Index}"))}"));
                doc.Blocks.Add(note);
            }

            var footer = new Paragraph { Margin = new Thickness(0, 30, 0, 0), TextAlignment = TextAlignment.Center, FontSize = 11 };
            footer.Inlines.Add(new Run("Thank you for your business!"));
            doc.Blocks.Add(footer);

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

        private static TableCell HCell(string t) => new TableCell(
            new Paragraph(new Run(t)) { FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center })
        { Background = Brushes.LightGray, Padding = new Thickness(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Black };

        private static TableCell DCell(string t) => new TableCell(
            new Paragraph(new Run(t)) { TextAlignment = TextAlignment.Center })
        { Padding = new Thickness(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Black };

        private void ShowPreview(FlowDocument doc)
        {
            var ms = new MemoryStream();
            var pkg = Package.Open(ms, FileMode.Create, FileAccess.ReadWrite);
            string uri = "pack://pret-" + Guid.NewGuid().ToString("N") + ".xps";
            PackageStore.AddPackage(new Uri(uri), pkg);
            var xps = new XpsDocument(pkg, CompressionOption.Maximum, uri);
            XpsDocument.CreateXpsDocumentWriter(xps).Write(((IDocumentPaginatorSource)doc).DocumentPaginator);
            var fd = xps.GetFixedDocumentSequence();

            var viewer = new DocumentViewer { Document = fd };
            string invTitle = TxtReturnNo.Text.Replace("  [EDITING]", "").Trim();
            var btnPrint = new Button { Content = "🖨 Print", Width = 100, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(255, 179, 0)), Foreground = new SolidColorBrush(Color.FromRgb(12, 21, 32)), FontWeight = FontWeights.Bold, BorderThickness = new Thickness(0) };
            var btnPDF = new Button { Content = "📄 Save PDF", Width = 110, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnFit = new Button { Content = "⬛ Fit", Width = 80, Margin = new Thickness(6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnZoomIn = new Button { Content = "＋", Width = 36, Margin = new Thickness(4, 6, 0, 6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };
            var btnZoomOut = new Button { Content = "－", Width = 36, Margin = new Thickness(0, 6, 6, 6), Background = new SolidColorBrush(Color.FromRgb(30, 45, 55)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(42, 63, 85)), BorderThickness = new Thickness(1) };

            btnPrint.Click += (s, ev) => { var d = new PrintDialog(); if (d.ShowDialog() == true) d.PrintDocument(fd.DocumentPaginator, $"Purchase Return - {invTitle}"); };
            btnPDF.Click += (s, ev) => { var d = new PrintDialog(); try { d.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), "Microsoft Print to PDF"); } catch { } if (d.ShowDialog() == true) d.PrintDocument(fd.DocumentPaginator, $"Purchase Return - {invTitle}"); };
            btnFit.Click += (s, ev) => viewer.FitToWidth();
            btnZoomIn.Click += (s, ev) => viewer.IncreaseZoom();
            btnZoomOut.Click += (s, ev) => viewer.DecreaseZoom();

            var tb = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(12, 21, 32)), HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var btn in new Button[] { btnZoomOut, btnZoomIn, btnFit, btnPDF, btnPrint }) tb.Children.Add(btn);

            var panel = new DockPanel();
            DockPanel.SetDock(tb, Dock.Top);
            panel.Children.Add(tb);
            panel.Children.Add(viewer);

            var wnd = new Window { Title = $"Purchase Return — {invTitle}", Width = 1100, Height = 850, Content = panel, Background = new SolidColorBrush(Color.FromRgb(17, 29, 43)), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            wnd.Closed += (s, ev) => { xps.Close(); PackageStore.RemovePackage(new Uri(uri)); ms.Close(); };
            wnd.ShowDialog();
        }

        private static UIElement ClonePanel(Panel original)
        {
            var clone = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 5, 0, 10) };
            foreach (var child in original.Children)
            {
                if (child is TextBlock tb)
                    clone.Children.Add(new TextBlock { Text = tb.Text, FontSize = tb.FontSize, FontWeight = tb.FontWeight, Foreground = tb.Foreground });
                else if (child is Border b && b.Child is TextBlock inner)
                {
                    var nb = new Border { Background = b.Background, Padding = b.Padding, Margin = b.Margin };
                    nb.Child = new TextBlock { Text = inner.Text, Foreground = inner.Foreground, FontWeight = inner.FontWeight };
                    clone.Children.Add(nb);
                }
            }
            return clone;
        }
    }
}