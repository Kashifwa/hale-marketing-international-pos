using Hale_Marketing_International.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
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
    public partial class SalesWindow : Window
    {
        private string _dbPath = "Data Source=posdata.db;Version=3;";
        private ObservableCollection<InvoiceItem> _items = new ObservableCollection<InvoiceItem>();

        private byte[] _pendingAttachBytes = null;
        private string _pendingAttachName = null;

        // Selected party state
        private int _selectedPartyId = 0;
        private string _selectedPartyName = "";
        private bool _partyConfirmed = false;
        private bool _suppressPartyTextChanged = false;

        // Edit mode (whole invoice)
        private int _editingSaleId = -1;
        private bool _isEditMode = false;

        // Row-level edit state
        private int _editingRowIndex = -1;
        private bool _isEditingRow = false;

        // ── Party model ──────────────────────────────────────────────────────────
        private class PartyItem
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public string Contact { get; set; }
            public string Address { get; set; }
            public string IdLabel => $"#{Id}";
            public string TypeLabel => Type == "Receivable" ? "Customer" : "Supplier";
            public override string ToString() => $"#{Id}  {Name}  [{TypeLabel}]";
        }

        // ── Invoice list row ─────────────────────────────────────────────────────
        private class SaleListRow
        {
            public int SaleId { get; set; }
            public string InvoiceNo { get; set; }
            public string Date { get; set; }
            public string PartyName { get; set; }
            public decimal NetTotal { get; set; }
        }

        // ── ProductTag — fetches available stock from transactions ────────────────
        private class ProductTag
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public decimal Price { get; set; }
            public string Company { get; set; }
            public decimal AvailableStock { get; set; }
            public override string ToString() =>
                $"{Name}   [{Code}]   {Company}   (Stock: {(AvailableStock <= 0 ? "OUT" : AvailableStock.ToString("N0"))})";
        }

        // ── InvoiceItem ──────────────────────────────────────────────────────────
        private class InvoiceItem : INotifyPropertyChanged
        {
            public int Index { get; set; }
            public string Code { get; set; }
            public string Name { get; set; }
            public string Company { get; set; }
            public string Details { get; set; }

            public byte[] AttachBytes { get; set; }
            public string AttachName { get; set; }

            public bool HasAttachment => AttachBytes != null && AttachBytes.Length > 0;
            public string AttachLabel => HasAttachment ? "📎 Open" : "";

            private decimal _quantity;
            private decimal _rate;

            public decimal Quantity
            {
                get => _quantity;
                set { _quantity = value; OnPC(nameof(Quantity)); OnPC(nameof(Amount)); }
            }
            public decimal Rate
            {
                get => _rate;
                set { _rate = value; OnPC(nameof(Rate)); OnPC(nameof(Amount)); }
            }
            public decimal Amount => Quantity * Rate;

            public event PropertyChangedEventHandler PropertyChanged;
            void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private List<PartyItem> _allParties = new();
        private List<SaleListRow> _allSalesList = new();
        private bool _partySearchPlaceholder = true;
        private bool _isPlaceholder = true;

        // ── Constructor ──────────────────────────────────────────────────────────
        public SalesWindow()
        {
            InitializeComponent();
            ProductsDataGrid.ItemsSource = _items;
            EnsureSalesTables();
            LoadAllParties();
            LoadNextInvoiceNo();
            CmbPaymentMethod.SelectedIndex = 0;
        }

        // ── Ensure Tables ────────────────────────────────────────────────────────
        private void EnsureSalesTables()
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(con);

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Sales (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceNo TEXT, InvoiceDate TEXT, PartyId INTEGER,
                    SubTotal REAL, TaxPercent REAL, TaxAmount REAL,
                    DiscountPercent REAL, DiscountAmount REAL,
                    Freight REAL, NetTotal REAL,
                    PaymentMethod TEXT, PaymentAmount REAL, FinalNote TEXT);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS SalesItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SaleId INTEGER, ProductId INTEGER,
                    ProductCode TEXT, ProductName TEXT, Company TEXT,
                    Quantity REAL, Rate REAL, Amount REAL,
                    Details TEXT, AttachName TEXT, AttachData BLOB);";
                cmd.ExecuteNonQuery();

                foreach (var col in new[]
                {
                    "ALTER TABLE Sales ADD COLUMN FinalNote TEXT",
                    "ALTER TABLE SalesItems ADD COLUMN Company TEXT",
                    "ALTER TABLE SalesItems ADD COLUMN Details TEXT",
                    "ALTER TABLE SalesItems ADD COLUMN AttachName TEXT",
                    "ALTER TABLE SalesItems ADD COLUMN AttachData BLOB"
                })
                { try { new SQLiteCommand(col, con).ExecuteNonQuery(); } catch { } }

                cmd.CommandText = @"CREATE TABLE IF NOT EXISTS CustomerLedger (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartyId INTEGER, Date TEXT, Description TEXT,
                    Debit REAL, Credit REAL, Balance REAL);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { MessageBox.Show("EnsureSalesTables: " + ex.Message); }
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
                    TxtPartySearch.Text = "Search party by name or ID...";
                    _partySearchPlaceholder = true;
                    _suppressPartyTextChanged = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TxtPartySearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressPartyTextChanged) return;
            if (_partySearchPlaceholder) return;
            if (!_partyConfirmed) _selectedPartyId = 0;
            else { _partyConfirmed = false; _selectedPartyId = 0; }
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
            else if (int.TryParse(filter.TrimStart('#'), out int idSearch))
                filtered = _allParties.Where(p => p.Id == idSearch);
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

            TxtCustomerPhone.Text = p.Contact;
            TxtCustomerAddress.Text = p.Address;
            PartyPopup.IsOpen = false;
        }

        // ── Invoice No ───────────────────────────────────────────────────────────
        private void LoadNextInvoiceNo()
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand("SELECT IFNULL(MAX(Id),0)+1 FROM Sales", con);
                TxtInvoiceNo.Text = "SINV-" + Convert.ToInt64(cmd.ExecuteScalar()).ToString().PadLeft(6, '0');
            }
            catch { TxtInvoiceNo.Text = "SINV-000001"; }
            DtInvoiceDate.SelectedDate = DateTime.Today;
        }

        // ── Calculate available stock for a product (transaction-based) ──────────
        /// <summary>
        /// Stock = Purchased - Sold + SalesReturned - PurchaseReturned
        /// Does NOT read Products.Quantity — purely from transaction tables.
        /// Optionally excludes a specific SaleId (used when editing, to not count
        /// the current invoice's own items as "already sold").
        /// </summary>
        private decimal GetAvailableStock(string productCode, int productId,
                                   int excludeSaleId = -1)
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();

                decimal purchased = 0, sold = 0, retIn = 0, retOut = 0;
                bool hasCode = !string.IsNullOrWhiteSpace(productCode);

                // PURCHASED — sirf valid code se
                if (hasCode)
                {
                    using var c = new SQLiteCommand(
                        "SELECT IFNULL(SUM(Quantity),0) FROM PurchaseItems " +
                        "WHERE TRIM(ProductCode)=@c AND TRIM(ProductCode)!=''", con);
                    c.Parameters.AddWithValue("@c", productCode.Trim());
                    purchased = Convert.ToDecimal(c.ExecuteScalar());
                }

                // SOLD — ProductId primary, code secondary (sirf non-empty)
                string soldSql = excludeSaleId > 0
                    ? (hasCode
                        ? "SELECT IFNULL(SUM(Quantity),0) FROM SalesItems WHERE (ProductId=@id OR (TRIM(ProductCode)=@c AND TRIM(ProductCode)!='')) AND SaleId<>@eid"
                        : "SELECT IFNULL(SUM(Quantity),0) FROM SalesItems WHERE ProductId=@id AND SaleId<>@eid")
                    : (hasCode
                        ? "SELECT IFNULL(SUM(Quantity),0) FROM SalesItems WHERE ProductId=@id OR (TRIM(ProductCode)=@c AND TRIM(ProductCode)!='')"
                        : "SELECT IFNULL(SUM(Quantity),0) FROM SalesItems WHERE ProductId=@id");

                using (var c = new SQLiteCommand(soldSql, con))
                {
                    c.Parameters.AddWithValue("@id", productId);
                    if (hasCode) c.Parameters.AddWithValue("@c", productCode.Trim());
                    if (excludeSaleId > 0) c.Parameters.AddWithValue("@eid", excludeSaleId);
                    sold = Convert.ToDecimal(c.ExecuteScalar());
                }

                // RETURN IN
                using (var c = new SQLiteCommand(
                    hasCode
                        ? "SELECT IFNULL(SUM(ReturnQty),0) FROM SalesReturnItems WHERE ProductId=@id OR (TRIM(ProductCode)=@c AND TRIM(ProductCode)!='')"
                        : "SELECT IFNULL(SUM(ReturnQty),0) FROM SalesReturnItems WHERE ProductId=@id", con))
                {
                    c.Parameters.AddWithValue("@id", productId);
                    if (hasCode) c.Parameters.AddWithValue("@c", productCode.Trim());
                    retIn = Convert.ToDecimal(c.ExecuteScalar());
                }

                // RETURN OUT
                using (var c = new SQLiteCommand(
                    hasCode
                        ? "SELECT IFNULL(SUM(ReturnQty),0) FROM PurchaseReturnItems WHERE ProductId=@id OR (TRIM(ProductCode)=@c AND TRIM(ProductCode)!='')"
                        : "SELECT IFNULL(SUM(ReturnQty),0) FROM PurchaseReturnItems WHERE ProductId=@id", con))
                {
                    c.Parameters.AddWithValue("@id", productId);
                    if (hasCode) c.Parameters.AddWithValue("@c", productCode.Trim());
                    retOut = Convert.ToDecimal(c.ExecuteScalar());
                }

                return purchased - sold + retIn - retOut;
            }
            catch { return 0; }
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
            else if (e.Key == Key.Enter) AddCurrentProduct();
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

                // Fetch from Products catalog (name, code, price, company only)
                using var cmd = new SQLiteCommand(
                    "SELECT Id, ProductName, Code, SalePrice, Category FROM Products " +
                    "WHERE ProductName LIKE @q OR Code LIKE @q LIMIT 50", con);
                cmd.Parameters.AddWithValue("@q", "%" + txt + "%");
                using var r = cmd.ExecuteReader();
                var tags = new List<ProductTag>();
                while (r.Read())
                    tags.Add(new ProductTag
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        Code = r["Code"]?.ToString() ?? "",
                        Name = r["ProductName"]?.ToString() ?? "",
                        Price = r["SalePrice"] == DBNull.Value ? 0m : Convert.ToDecimal(r["SalePrice"]),
                        Company = r["Category"]?.ToString() ?? ""
                    });

                // Enrich with live stock from transactions
                foreach (var tag in tags)
                {
                    tag.AvailableStock = GetAvailableStock(tag.Code, tag.Id,
                        _isEditMode ? _editingSaleId : -1);
                    ProductList.Items.Add(tag);
                }

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
            TxtQuantity.Text = "";
            ProductPopup.IsOpen = false;
            TxtSearch.Text = p.Name;
            _isPlaceholder = false;

            // Show available stock hint
            TxtAvailableQty.Text = $"Available stock: {(p.AvailableStock <= 0 ? "OUT OF STOCK" : p.AvailableStock.ToString("N0"))}";
            TxtAvailableQty.Foreground = p.AvailableStock <= 0
                ? new SolidColorBrush(Color.FromRgb(239, 83, 80))
                : p.AvailableStock <= 5
                    ? new SolidColorBrush(Color.FromRgb(255, 179, 0))
                    : new SolidColorBrush(Color.FromRgb(0, 191, 165));
            TxtAvailableQty.Visibility = Visibility.Visible;

            TxtRate.Focus();
            TxtRate.SelectAll();
        }

        // ── Placeholder handlers ─────────────────────────────────────────────────
        private void TxtDetails_GotFocus(object sender, RoutedEventArgs e)
        { if (TxtDetails.Text == "Optional details...") TxtDetails.Text = ""; }
        private void TxtDetails_LostFocus(object sender, RoutedEventArgs e)
        { if (string.IsNullOrWhiteSpace(TxtDetails.Text)) TxtDetails.Text = "Optional details..."; }
        private void TxtFinalNote_GotFocus(object sender, RoutedEventArgs e)
        { if (TxtFinalNote.Text == "Optional note...") TxtFinalNote.Text = ""; }
        private void TxtFinalNote_LostFocus(object sender, RoutedEventArgs e)
        { if (string.IsNullOrWhiteSpace(TxtFinalNote.Text)) TxtFinalNote.Text = "Optional note..."; }

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

        private void BtnOpenAttach_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out int idx)) return;
            var item = _items.FirstOrDefault(x => x.Index == idx);
            if (item?.AttachBytes == null || item.AttachBytes.Length == 0) return;
            OpenBytesWithDefaultApp(item.AttachBytes, item.AttachName ?? "attachment");
        }

        private static void OpenBytesWithDefaultApp(byte[] data, string filename)
        {
            try
            {
                string ext = Path.GetExtension(filename);
                string tmp = Path.Combine(Path.GetTempPath(), $"hmi_attach_{Guid.NewGuid():N}{ext}");
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

            TxtProductCode.Text = item.Code;
            TxtProductName.Text = item.Name;
            TxtCompany.Text = item.Company;
            TxtSearch.Text = item.Name;
            _isPlaceholder = false;
            TxtRate.Text = item.Rate.ToString("N2");
            TxtQuantity.Text = item.Quantity.ToString("N0");
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

            // Show available stock for this item (excluding its own row since it's being re-entered)
            int pid = GetProductIdByCode(item.Code);
            decimal avail = GetAvailableStock(item.Code, pid, _isEditMode ? _editingSaleId : -1)
                            + item.Quantity; // add back its own qty since we're re-editing it
            TxtAvailableQty.Text = $"Available stock: {(avail <= 0 ? "OUT OF STOCK" : avail.ToString("N0"))}";
            TxtAvailableQty.Foreground = avail <= 0
                ? new SolidColorBrush(Color.FromRgb(239, 83, 80))
                : avail <= 5
                    ? new SolidColorBrush(Color.FromRgb(255, 179, 0))
                    : new SolidColorBrush(Color.FromRgb(0, 191, 165));
            TxtAvailableQty.Visibility = Visibility.Visible;

            _editingRowIndex = idx;
            _isEditingRow = true;
            RowEditHint.Visibility = Visibility.Visible;
            TxtRate.Focus();
            TxtRate.SelectAll();
        }

        private int GetProductIdByCode(string code)
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand("SELECT Id FROM Products WHERE Code=@c LIMIT 1", con);
                cmd.Parameters.AddWithValue("@c", code);
                var res = cmd.ExecuteScalar();
                return res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
            }
            catch { return 0; }
        }

        // ── Quantity keydown → add ────────────────────────────────────────────────
        private void TxtQuantity_KeyDown(object sender, KeyEventArgs e)
        { if (e.Key == Key.Enter) AddCurrentProduct(); }

        private void BtnAddProduct_Click(object sender, RoutedEventArgs e) => AddCurrentProduct();

        private void AddCurrentProduct()
        {
            if (string.IsNullOrWhiteSpace(TxtProductCode.Text))
            { MessageBox.Show("Select a product first."); return; }

            if (!decimal.TryParse(TxtQuantity.Text, out var qty) || qty <= 0)
            { MessageBox.Show("Enter valid quantity."); return; }

            if (!decimal.TryParse(TxtRate.Text, out var rate) || rate <= 0)
            { MessageBox.Show("Enter valid rate."); return; }

            // ── Stock check against live transactions ────────────────────────────
            int pid = GetProductIdByCode(TxtProductCode.Text);
            decimal alreadyInCart = _items
                .Where(x => x.Code == TxtProductCode.Text &&
                            (_isEditingRow ? x.Index != _editingRowIndex : true))
                .Sum(x => x.Quantity);

            decimal avail = GetAvailableStock(TxtProductCode.Text, pid,
                                _isEditMode ? _editingSaleId : -1) - alreadyInCart;

            if (qty > avail)
            {
                MessageBox.Show($"Insufficient stock.\nAvailable: {avail:N0}  |  Requested: {qty:N0}",
                    "Stock Check", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string details = TxtDetails.Text == "Optional details..." ? "" : TxtDetails.Text.Trim();

            if (_isEditingRow)
            {
                var existing = _items.FirstOrDefault(x => x.Index == _editingRowIndex);
                if (existing != null)
                {
                    existing.Code = TxtProductCode.Text;
                    existing.Name = TxtProductName.Text;
                    existing.Company = TxtCompany.Text;
                    existing.Details = details;
                    existing.Quantity = qty;
                    existing.Rate = rate;
                    existing.AttachBytes = _pendingAttachBytes;
                    existing.AttachName = _pendingAttachName;
                }
                _isEditingRow = false;
                _editingRowIndex = -1;
                RowEditHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                _items.Add(new InvoiceItem
                {
                    Index = _items.Count + 1,
                    Code = TxtProductCode.Text,
                    Name = TxtProductName.Text,
                    Company = TxtCompany.Text,
                    Details = details,
                    Quantity = qty,
                    Rate = rate,
                    AttachBytes = _pendingAttachBytes,
                    AttachName = _pendingAttachName
                });
            }

            ReindexItems();
            CalculateTotals();
            ClearProductEntry();
        }

        private void ReindexItems()
        { for (int i = 0; i < _items.Count; i++) _items[i].Index = i + 1; }

        private void ClearProductEntry()
        {
            TxtSearch.Text = "Type to search...";
            _isPlaceholder = true;
            ProductPopup.IsOpen = false;
            TxtProductCode.Text = "";
            TxtProductName.Text = "";
            TxtCompany.Text = "";
            TxtQuantity.Text = "";
            TxtRate.Text = "";
            TxtDetails.Text = "Optional details...";
            TxtAvailableQty.Visibility = Visibility.Collapsed;
            BtnClearAttach_Click(null, null);
        }

        private void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse(b.Tag?.ToString(), out int idx))
            {
                var item = _items.FirstOrDefault(x => x.Index == idx);
                if (item != null) _items.Remove(item);
            }
            else if (ProductsDataGrid.SelectedItem is InvoiceItem sel)
                _items.Remove(sel);
            ReindexItems();
            CalculateTotals();
        }

        // ── Totals ───────────────────────────────────────────────────────────────
        private void TxtTaxPercent_KeyUp(object sender, KeyEventArgs e) => CalculateTotals();
        private void TxtDiscountPercent_KeyUp(object sender, KeyEventArgs e) => CalculateTotals();
        private void TxtFreight_KeyUp(object sender, KeyEventArgs e) => CalculateTotals();
        private void TxtPaymentAmount_KeyUp(object sender, KeyEventArgs e) { }

        private void CalculateTotals()
        {
            decimal sub = _items.Sum(i => i.Amount);
            decimal.TryParse(TxtTaxPercent.Text, out decimal tp);
            decimal.TryParse(TxtDiscountPercent.Text, out decimal dp);
            decimal.TryParse(TxtFreight.Text, out decimal fr);
            decimal tax = Math.Round(sub * tp / 100m, 2);
            decimal disc = Math.Round(sub * dp / 100m, 2);
            TxtSubtotal.Text = sub.ToString("N2");
            TxtTaxAmount.Text = tax.ToString("N2");
            TxtDiscountAmount.Text = disc.ToString("N2");
            TxtNetAmount.Text = (sub + tax + fr - disc).ToString("N2");
            TxtItemsCount.Text = _items.Count.ToString();
            TxtBoxesCount.Text = "0";
        }

        // ── Save ─────────────────────────────────────────────────────────────────
        private void BtnSaveInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (!_partyConfirmed || _selectedPartyId == 0)
            { MessageBox.Show("Please select a party from the dropdown list first."); TxtPartySearch.Focus(); return; }
            if (_items.Count == 0) { MessageBox.Show("Add at least one product."); return; }

            if (_isEditMode) SaveEditedInvoice();
            else SaveNewInvoice();
        }

        private void CollectFormValues(
            out string invoiceNo, out string date,
            out decimal sub, out decimal taxPct, out decimal discPct,
            out decimal discAmt, out decimal freight, out decimal paid,
            out string pm, out decimal tax, out decimal net, out string finalNote)
        {
            invoiceNo = TxtInvoiceNo.Text.Replace("  [EDITING]", "").Trim();
            date = DtInvoiceDate.SelectedDate?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            sub = _items.Sum(i => i.Amount);
            decimal.TryParse(TxtTaxPercent.Text, out taxPct);
            decimal.TryParse(TxtDiscountPercent.Text, out discPct);
            decimal.TryParse(TxtDiscountAmount.Text, out discAmt);
            decimal.TryParse(TxtFreight.Text, out freight);
            decimal.TryParse(TxtPaymentAmount.Text, out paid);
            pm = (CmbPaymentMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
            tax = Math.Round(sub * taxPct / 100m, 2);
            if (discAmt <= 0) discAmt = Math.Round(sub * discPct / 100m, 2);
            net = sub + tax + freight - discAmt;
            finalNote = TxtFinalNote.Text == "Optional note..." ? "" : TxtFinalNote.Text.Trim();
        }

        private void SaveNewInvoice()
        {
            CollectFormValues(out string invoiceNo, out string date,
                out decimal sub, out decimal taxPct, out decimal discPct,
                out decimal discAmt, out decimal freight, out decimal paid,
                out string pm, out decimal tax, out decimal net, out string fn);
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var tran = con.BeginTransaction();
                long saleId = InsertSalesMaster(con, tran, invoiceNo, date,
                                    sub, taxPct, tax, discPct, discAmt, freight, net, pm, paid, fn);
                InsertSalesItems(con, tran, saleId);   // NO stock update — purely records the sale
                InsertLedger(con, tran, _selectedPartyId, date,
                             $"Sales Invoice - {invoiceNo}", net, paid);
                tran.Commit();
                DashboardHub.Notify();

                MessageBox.Show("Sales invoice saved successfully.");
                NewInvoice();
                RefreshInvoiceList();
            }
            catch (Exception ex) { MessageBox.Show("Save error: " + ex.Message); }
        }

        private void SaveEditedInvoice()
        {
            CollectFormValues(out string invoiceNo, out string date,
                out decimal sub, out decimal taxPct, out decimal discPct,
                out decimal discAmt, out decimal freight, out decimal paid,
                out string pm, out decimal tax, out decimal net, out string fn);

            if (MessageBox.Show("Update this invoice? Ledger will be re-applied.",
                "Confirm Update", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var tran = con.BeginTransaction();

                // Delete old items and old ledger — no stock reversal needed
                new SQLiteCommand($"DELETE FROM SalesItems WHERE SaleId={_editingSaleId}", con, tran).ExecuteNonQuery();
                DeleteLedgerByDesc(con, tran, _selectedPartyId, $"Sales Invoice - {invoiceNo}");

                using (var cmd = new SQLiteCommand(@"
                    UPDATE Sales SET InvoiceDate=@dt, PartyId=@pid,
                        SubTotal=@sub, TaxPercent=@tp, TaxAmount=@ta,
                        DiscountPercent=@dp, DiscountAmount=@da,
                        Freight=@fr, NetTotal=@net,
                        PaymentMethod=@pm, PaymentAmount=@paid, FinalNote=@fn
                    WHERE Id=@id", con, tran))
                {
                    cmd.Parameters.AddWithValue("@dt", date);
                    cmd.Parameters.AddWithValue("@pid", _selectedPartyId);
                    cmd.Parameters.AddWithValue("@sub", sub);
                    cmd.Parameters.AddWithValue("@tp", taxPct);
                    cmd.Parameters.AddWithValue("@ta", tax);
                    cmd.Parameters.AddWithValue("@dp", discPct);
                    cmd.Parameters.AddWithValue("@da", discAmt);
                    cmd.Parameters.AddWithValue("@fr", freight);
                    cmd.Parameters.AddWithValue("@net", net);
                    cmd.Parameters.AddWithValue("@pm", pm);
                    cmd.Parameters.AddWithValue("@paid", paid);
                    cmd.Parameters.AddWithValue("@fn", fn);
                    cmd.Parameters.AddWithValue("@id", _editingSaleId);
                    cmd.ExecuteNonQuery();
                }

                InsertSalesItems(con, tran, _editingSaleId);
                InsertLedger(con, tran, _selectedPartyId, date,
                             $"Sales Invoice - {invoiceNo}", net, paid);
                tran.Commit();
                DashboardHub.Notify();
                MessageBox.Show("Invoice updated successfully.");
                _isEditMode = false;
                _editingSaleId = -1;
                NewInvoice();
                RefreshInvoiceList();
            }
            catch (Exception ex) { MessageBox.Show("Update error: " + ex.Message); }
        }

        // ── Invoice list panel ────────────────────────────────────────────────────
        private void BtnShowInvoiceList_Click(object sender, RoutedEventArgs e)
        { BorderInvoiceList.Visibility = Visibility.Visible; RefreshInvoiceList(); }

        private void BtnCloseInvoiceList_Click(object sender, RoutedEventArgs e)
        { BorderInvoiceList.Visibility = Visibility.Collapsed; }

        private void RefreshInvoiceList()
        {
            _allSalesList.Clear();
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(@"
                    SELECT s.Id, s.InvoiceNo, s.InvoiceDate, s.NetTotal, p.Name
                    FROM Sales s LEFT JOIN Parties p ON p.Id = s.PartyId
                    ORDER BY s.Id DESC", con);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    _allSalesList.Add(new SaleListRow
                    {
                        SaleId = Convert.ToInt32(r["Id"]),
                        InvoiceNo = r["InvoiceNo"].ToString(),
                        Date = r["InvoiceDate"].ToString(),
                        PartyName = r["Name"]?.ToString() ?? "",
                        NetTotal = r["NetTotal"] == DBNull.Value ? 0 : Convert.ToDecimal(r["NetTotal"])
                    });
            }
            catch { }
            DgInvoiceList.ItemsSource = null;
            DgInvoiceList.ItemsSource = _allSalesList;
        }

        private void DgInvoiceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool has = DgInvoiceList.SelectedItem is SaleListRow;
            BtnEditInvoice.IsEnabled = has;
            BtnDeleteInvoice.IsEnabled = has;
        }

        private void BtnInvListFilter_Click(object sender, RoutedEventArgs e)
        {
            string search = TxtInvListSearch.Text.Trim().ToLower();
            bool ph = search == "search invoice / party...";
            var filtered = _allSalesList.ToList();
            if (!ph && !string.IsNullOrEmpty(search))
                filtered = filtered.Where(x =>
                    x.InvoiceNo.ToLower().Contains(search) ||
                    x.PartyName.ToLower().Contains(search)).ToList();
            if (DtInvListFrom.SelectedDate != null)
                filtered = filtered.Where(x =>
                    DateTime.TryParse(x.Date, out var d) &&
                    d >= DtInvListFrom.SelectedDate.Value.Date).ToList();
            if (DtInvListTo.SelectedDate != null)
                filtered = filtered.Where(x =>
                    DateTime.TryParse(x.Date, out var d) &&
                    d <= DtInvListTo.SelectedDate.Value.Date).ToList();
            DgInvoiceList.ItemsSource = filtered;
        }

        private void TxtInvListSearch_GotFocus(object sender, RoutedEventArgs e)
        { if (TxtInvListSearch.Text == "Search invoice / party...") TxtInvListSearch.Text = ""; }
        private void TxtInvListSearch_LostFocus(object sender, RoutedEventArgs e)
        { if (string.IsNullOrWhiteSpace(TxtInvListSearch.Text)) TxtInvListSearch.Text = "Search invoice / party..."; }
        private void TxtInvListSearch_TextChanged(object sender, TextChangedEventArgs e)
        { if (IsLoaded) BtnInvListFilter_Click(null, null); }

        // ── Load Invoice for Edit ─────────────────────────────────────────────────
        private void BtnEditInvoice_Click(object sender, RoutedEventArgs e)
        { if (DgInvoiceList.SelectedItem is SaleListRow sel) LoadInvoiceForEdit(sel.SaleId); }

        private void LoadInvoiceForEdit(int saleId)
        {
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();

                using (var cmd = new SQLiteCommand(@"
                    SELECT s.*, p.Name, p.Contact, p.Address, p.Type
                    FROM Sales s LEFT JOIN Parties p ON p.Id = s.PartyId
                    WHERE s.Id = @id", con))
                {
                    cmd.Parameters.AddWithValue("@id", saleId);
                    using var r = cmd.ExecuteReader();
                    if (!r.Read()) return;

                    _editingSaleId = saleId;
                    _isEditMode = true;

                    _selectedPartyId = Convert.ToInt32(r["PartyId"]);
                    _selectedPartyName = r["Name"]?.ToString() ?? "";
                    _partyConfirmed = true;
                    _partySearchPlaceholder = false;

                    string pType = r["Type"]?.ToString() ?? "";
                    string typeLabel = pType == "Receivable" ? "Customer" : "Supplier";

                    _suppressPartyTextChanged = true;
                    TxtPartySearch.Text = $"#{_selectedPartyId}  {_selectedPartyName}  [{typeLabel}]";
                    _suppressPartyTextChanged = false;

                    TxtCustomerPhone.Text = r["Contact"]?.ToString() ?? "";
                    TxtCustomerAddress.Text = r["Address"]?.ToString() ?? "";
                    TxtInvoiceNo.Text = r["InvoiceNo"].ToString() + "  [EDITING]";

                    if (DateTime.TryParse(r["InvoiceDate"].ToString(), out var d))
                        DtInvoiceDate.SelectedDate = d;

                    TxtTaxPercent.Text = r["TaxPercent"].ToString();
                    TxtDiscountPercent.Text = r["DiscountPercent"].ToString();
                    TxtFreight.Text = r["Freight"].ToString();
                    TxtPaymentAmount.Text = r["PaymentAmount"].ToString();

                    string fn = r["FinalNote"]?.ToString() ?? "";
                    TxtFinalNote.Text = string.IsNullOrWhiteSpace(fn) ? "Optional note..." : fn;

                    string pmVal = r["PaymentMethod"]?.ToString() ?? "Cash";
                    foreach (ComboBoxItem ci in CmbPaymentMethod.Items)
                        if (ci.Content.ToString() == pmVal) { CmbPaymentMethod.SelectedItem = ci; break; }
                }

                _items.Clear();
                using (var cmd = new SQLiteCommand(
                    "SELECT * FROM SalesItems WHERE SaleId=@id ORDER BY Id", con))
                {
                    cmd.Parameters.AddWithValue("@id", saleId);
                    using var r = cmd.ExecuteReader();
                    int idx = 1;
                    while (r.Read())
                    {
                        byte[] ab = r["AttachData"] == DBNull.Value ? null : (byte[])r["AttachData"];
                        _items.Add(new InvoiceItem
                        {
                            Index = idx++,
                            Code = r["ProductCode"].ToString(),
                            Name = r["ProductName"].ToString(),
                            Company = r["Company"]?.ToString() ?? "",
                            Details = r["Details"]?.ToString() ?? "",
                            Quantity = r["Quantity"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Quantity"]),
                            Rate = r["Rate"] == DBNull.Value ? 0 : Convert.ToDecimal(r["Rate"]),
                            AttachBytes = ab,
                            AttachName = r["AttachName"]?.ToString()
                        });
                    }
                }

                CalculateTotals();
                EditModeBadge.Visibility = Visibility.Visible;
                BorderInvoiceList.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex) { MessageBox.Show("Load error: " + ex.Message); }
        }

        // ── Delete Invoice ────────────────────────────────────────────────────────
        private void BtnDeleteInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (DgInvoiceList.SelectedItem is not SaleListRow sel) return;
            if (MessageBox.Show($"Delete invoice {sel.InvoiceNo}?\nLedger entry will be removed.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var tran = con.BeginTransaction();
                // No stock reversal needed — stock is calculated from transactions
                DeleteLedgerByInvoiceNo(con, tran, sel.InvoiceNo);
                new SQLiteCommand($"DELETE FROM SalesItems WHERE SaleId={sel.SaleId}", con, tran).ExecuteNonQuery();
                new SQLiteCommand($"DELETE FROM Sales WHERE Id={sel.SaleId}", con, tran).ExecuteNonQuery();
                tran.Commit();
                DashboardHub.Notify();
                MessageBox.Show("Invoice deleted.");
                RefreshInvoiceList();
            }
            catch (Exception ex) { MessageBox.Show("Delete error: " + ex.Message); }
        }

        // ── DB Helpers ───────────────────────────────────────────────────────────
        private long InsertSalesMaster(SQLiteConnection con, SQLiteTransaction tran,
            string invoiceNo, string date, decimal sub, decimal taxPct, decimal tax,
            decimal discPct, decimal discAmt, decimal freight, decimal net,
            string pm, decimal paid, string fn)
        {
            using var cmd = new SQLiteCommand(@"
                INSERT INTO Sales
                  (InvoiceNo,InvoiceDate,PartyId,SubTotal,TaxPercent,TaxAmount,
                   DiscountPercent,DiscountAmount,Freight,NetTotal,
                   PaymentMethod,PaymentAmount,FinalNote)
                VALUES(@no,@dt,@pid,@sub,@tp,@ta,@dp,@da,@fr,@net,@pm,@paid,@fn);
                SELECT last_insert_rowid();", con, tran);
            cmd.Parameters.AddWithValue("@no", invoiceNo);
            cmd.Parameters.AddWithValue("@dt", date);
            cmd.Parameters.AddWithValue("@pid", _selectedPartyId);
            cmd.Parameters.AddWithValue("@sub", sub);
            cmd.Parameters.AddWithValue("@tp", taxPct);
            cmd.Parameters.AddWithValue("@ta", tax);
            cmd.Parameters.AddWithValue("@dp", discPct);
            cmd.Parameters.AddWithValue("@da", discAmt);
            cmd.Parameters.AddWithValue("@fr", freight);
            cmd.Parameters.AddWithValue("@net", net);
            cmd.Parameters.AddWithValue("@pm", pm);
            cmd.Parameters.AddWithValue("@paid", paid);
            cmd.Parameters.AddWithValue("@fn", fn);
            return (long)cmd.ExecuteScalar();
        }

        /// <summary>
        /// Inserts SalesItems rows only — does NOT touch Products.Quantity.
        /// Stock is derived from transaction tables in StockWindow.
        /// </summary>
        private void InsertSalesItems(SQLiteConnection con, SQLiteTransaction tran, long saleId)
        {
            foreach (var it in _items)
            {
                // Resolve ProductId for reference only — no stock write
                int pid = 0;
                using (var c = new SQLiteCommand("SELECT Id FROM Products WHERE Code=@c LIMIT 1", con, tran))
                {
                    c.Parameters.AddWithValue("@c", it.Code);
                    var res = c.ExecuteScalar();
                    if (res != null && res != DBNull.Value) pid = Convert.ToInt32(res);
                }
                using (var c = new SQLiteCommand(@"
                    INSERT INTO SalesItems
                      (SaleId,ProductId,ProductCode,ProductName,Company,Quantity,Rate,Amount,
                       Details,AttachName,AttachData)
                    VALUES(@sid,@pid,@code,@name,@co,@qty,@rate,@amt,@det,@an,@ad)", con, tran))
                {
                    c.Parameters.AddWithValue("@sid", saleId);
                    c.Parameters.AddWithValue("@pid", pid);
                    c.Parameters.AddWithValue("@code", it.Code);
                    c.Parameters.AddWithValue("@name", it.Name);
                    c.Parameters.AddWithValue("@co", it.Company ?? "");
                    c.Parameters.AddWithValue("@qty", it.Quantity);
                    c.Parameters.AddWithValue("@rate", it.Rate);
                    c.Parameters.AddWithValue("@amt", it.Amount);
                    c.Parameters.AddWithValue("@det", it.Details ?? "");
                    c.Parameters.AddWithValue("@an", (object)it.AttachName ?? DBNull.Value);
                    c.Parameters.AddWithValue("@ad", (object)it.AttachBytes ?? DBNull.Value);
                    c.ExecuteNonQuery();
                }
                // ── NO UPDATE Products SET Quantity ──────────────────────────────
                // Stock is computed live from PurchaseItems / SalesItems / Return tables
            }
        }

        private void InsertLedger(SQLiteConnection con, SQLiteTransaction tran,
            int partyId, string date, string desc, decimal debit, decimal credit)
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
                VALUES(@pid,@dt,@desc,@deb,@cr,@bal)", con, tran))
            {
                c.Parameters.AddWithValue("@pid", partyId);
                c.Parameters.AddWithValue("@dt", date);
                c.Parameters.AddWithValue("@desc", desc);
                c.Parameters.AddWithValue("@deb", debit);
                c.Parameters.AddWithValue("@cr", credit);
                c.Parameters.AddWithValue("@bal", bal + debit - credit);
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

        private void DeleteLedgerByInvoiceNo(SQLiteConnection con, SQLiteTransaction tran, string invoiceNo)
        {
            using var c = new SQLiteCommand(
                "DELETE FROM CustomerLedger WHERE Description LIKE @desc", con, tran);
            c.Parameters.AddWithValue("@desc", $"%{invoiceNo}%");
            c.ExecuteNonQuery();
        }

        // ── Clear / New ──────────────────────────────────────────────────────────
        private void BtnClear_Click(object sender, RoutedEventArgs e) => NewInvoice();

        private void NewInvoice()
        {
            _isEditMode = false;
            _editingSaleId = -1;
            _isEditingRow = false;
            _editingRowIndex = -1;
            _selectedPartyId = 0;
            _selectedPartyName = "";
            _partyConfirmed = false;
            _items.Clear();

            _suppressPartyTextChanged = true;
            TxtPartySearch.Text = "Search party by name or ID...";
            _suppressPartyTextChanged = false;
            _partySearchPlaceholder = true;

            TxtCustomerPhone.Text = "";
            TxtCustomerAddress.Text = "";
            TxtSubtotal.Text = "0.00";
            TxtTaxPercent.Text = "";
            TxtTaxAmount.Text = "0.00";
            TxtDiscountPercent.Text = "";
            TxtDiscountAmount.Text = "0.00";
            TxtFreight.Text = "";
            TxtNetAmount.Text = "0.00";
            TxtPaymentAmount.Text = "0.00";
            TxtFinalNote.Text = "Optional note...";
            TxtItemsCount.Text = "0";
            TxtBoxesCount.Text = "0";
            EditModeBadge.Visibility = Visibility.Collapsed;
            RowEditHint.Visibility = Visibility.Collapsed;
            CmbPaymentMethod.SelectedIndex = 0;
            ClearProductEntry();
            LoadNextInvoiceNo();
        }

        // ── Print ────────────────────────────────────────────────────────────────
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) { MessageBox.Show("No items to print."); return; }
            decimal.TryParse(TxtSubtotal.Text, out decimal sub);
            decimal.TryParse(TxtTaxAmount.Text, out decimal tax);
            decimal.TryParse(TxtDiscountAmount.Text, out decimal disc);
            decimal.TryParse(TxtFreight.Text, out decimal fr);
            decimal.TryParse(TxtNetAmount.Text, out decimal net);
            decimal.TryParse(TxtPaymentAmount.Text, out decimal paid);
            string pm = (CmbPaymentMethod.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
            string fn = TxtFinalNote.Text == "Optional note..." ? "" : TxtFinalNote.Text;

            var doc = BuildFlowDocument(
                TxtInvoiceNo.Text.Replace("  [EDITING]", "").Trim(),
                DtInvoiceDate.SelectedDate?.ToString("dd-MM-yyyy") ?? "",
                _selectedPartyId, _selectedPartyName,
                TxtCustomerPhone.Text, TxtCustomerAddress.Text,
                _items.ToList(), sub, tax, disc, fr, net, pm, paid, fn);
            ShowPreview(doc);
        }

        private FlowDocument BuildFlowDocument(
            string invoiceNo, string invoiceDate, int partyId,
            string partyName, string partyPhone, string partyAddress,
            List<InvoiceItem> items,
            decimal sub, decimal tax, decimal disc,
            decimal freight, decimal net,
            string pm, decimal paid, string finalNote)
        {
            decimal prevBal = 0;
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT IFNULL(Balance,0) FROM CustomerLedger WHERE PartyId=@pid ORDER BY Id DESC LIMIT 1", con);
                cmd.Parameters.AddWithValue("@pid", partyId);
                var res = cmd.ExecuteScalar();
                if (res != null && res != DBNull.Value) prevBal = Convert.ToDecimal(res);
            }
            catch { }

            decimal newBal = prevBal + net - paid;

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
            info.Inlines.Add(new Run($"Sales Invoice #: {invoiceNo}    Date: {invoiceDate}"));
            doc.Blocks.Add(info);

            var pb = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var ph = new Border { Background = Brushes.Black, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 4) };
            ph.Child = new TextBlock { Text = "Sold To:", Foreground = Brushes.White, FontWeight = FontWeights.Bold };
            pb.Children.Add(ph);
            pb.Children.Add(new TextBlock { Text = partyName, FontSize = 13, Foreground = Brushes.Black });
            pb.Children.Add(new TextBlock { Text = $"Phone: {(string.IsNullOrWhiteSpace(partyPhone) ? "N/A" : partyPhone)}", FontSize = 12, Foreground = Brushes.Black });
            pb.Children.Add(new TextBlock { Text = $"Address: {(string.IsNullOrWhiteSpace(partyAddress) ? "N/A" : partyAddress)}", FontSize = 12, Foreground = Brushes.Black });
            doc.Blocks.Add(new BlockUIContainer(ClonePanel(pb)));

            var table = new Table { CellSpacing = 0, BorderThickness = new Thickness(1), BorderBrush = Brushes.Black };
            foreach (var w in new double[] { 28, 65, 160, 105, 110, 52, 72, 88, 38 })
                table.Columns.Add(new TableColumn { Width = new GridLength(w) });

            var hg = new TableRowGroup(); var hr = new TableRow();
            foreach (var h in new[] { "Sr#", "Code", "Product Name", "Company", "Details", "Qty", "Rate", "Amount", "📎" })
                hr.Cells.Add(HCell(h));
            hg.Rows.Add(hr); table.RowGroups.Add(hg);

            var bg = new TableRowGroup(); int sr = 1;
            foreach (var it in items)
            {
                var row = new TableRow();
                foreach (var v in new[] { sr.ToString(), it.Code ?? "", it.Name ?? "", it.Company ?? "", it.Details ?? "", it.Quantity.ToString("N0"), it.Rate.ToString("N2"), it.Amount.ToString("N2"), it.HasAttachment ? "📎" : "" })
                    row.Cells.Add(DCell(v));
                bg.Rows.Add(row); sr++;
            }
            table.RowGroups.Add(bg); doc.Blocks.Add(table);

            var tot = new Paragraph { Margin = new Thickness(0, 12, 0, 0), TextAlignment = TextAlignment.Right };
            tot.Inlines.Add(new Run($"Sub Total:        {sub:N2}\n"));
            tot.Inlines.Add(new Run($"Tax:              {tax:N2}\n"));
            tot.Inlines.Add(new Run($"Discount:         {disc:N2}\n"));
            tot.Inlines.Add(new Run($"Freight:          {freight:N2}\n"));
            tot.Inlines.Add(new Run($"Net Amount:       {net:N2}\n") { FontWeight = FontWeights.Bold });
            tot.Inlines.Add(new Run($"\nPayment ({pm}):  {paid:N2}\n"));
            tot.Inlines.Add(new Run($"Previous Balance: {prevBal:N2}\n"));
            tot.Inlines.Add(new Run($"New Balance:      {newBal:N2}\n") { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(tot);

            var withAttach = items.Where(i => i.HasAttachment).ToList();
            if (withAttach.Count > 0)
            {
                var note = new Paragraph { Margin = new Thickness(0, 8, 0, 0), FontSize = 10 };
                note.Inlines.Add(new Run($"📎  Attachments: {string.Join(", ", withAttach.Select(i => i.AttachName ?? $"#{i.Index}"))}"));
                doc.Blocks.Add(note);
            }

            if (!string.IsNullOrWhiteSpace(finalNote))
            {
                var nb = new Border { Background = new SolidColorBrush(Color.FromRgb(240, 248, 255)), BorderBrush = Brushes.LightSteelBlue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 12, 0, 0) };
                var np = new StackPanel();
                np.Children.Add(new TextBlock { Text = "Note:", FontWeight = FontWeights.Bold, Foreground = Brushes.DarkSlateBlue });
                np.Children.Add(new TextBlock { Text = finalNote, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap });
                nb.Child = np; doc.Blocks.Add(new BlockUIContainer(nb));
            }

            var footer = new Paragraph { Margin = new Thickness(0, 20, 0, 0), TextAlignment = TextAlignment.Center, FontSize = 11 };
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
            string uri = "pack://sinv-" + Guid.NewGuid().ToString("N") + ".xps";
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

            string invTitle = TxtInvoiceNo.Text.Replace("  [EDITING]", "").Trim();
            btnPrint.Click += (s, ev) => { var d = new PrintDialog(); if (d.ShowDialog() == true) d.PrintDocument(fd.DocumentPaginator, $"Sales Invoice - {invTitle}"); };
            btnPDF.Click += (s, ev) => { var d = new PrintDialog(); try { d.PrintQueue = new System.Printing.PrintQueue(new System.Printing.PrintServer(), "Microsoft Print to PDF"); } catch { } if (d.ShowDialog() == true) d.PrintDocument(fd.DocumentPaginator, $"Sales Invoice - {invTitle}"); };
            btnFit.Click += (s, ev) => viewer.FitToWidth();
            btnZoomIn.Click += (s, ev) => viewer.IncreaseZoom();
            btnZoomOut.Click += (s, ev) => viewer.DecreaseZoom();

            var tb = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(12, 21, 32)), HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var btn in new[] { btnZoomOut, btnZoomIn, btnFit, btnPDF, btnPrint }) tb.Children.Add(btn);

            var panel = new DockPanel();
            DockPanel.SetDock(tb, Dock.Top);
            panel.Children.Add(tb);
            panel.Children.Add(viewer);

            var wnd = new Window { Title = $"Sales Invoice — {invTitle}", Width = 1100, Height = 850, Content = panel, Background = new SolidColorBrush(Color.FromRgb(17, 29, 43)), WindowStartupLocation = WindowStartupLocation.CenterScreen };
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

        private void ProductsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    }
}