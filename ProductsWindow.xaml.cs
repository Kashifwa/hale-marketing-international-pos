using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Hale_Marketing_International
{
    public partial class ProductsWindow : Window
    {
        private string _dbPath = AppConfig.ConnectionString;
        private int _selectedProductId = -1;
        private DataView _productsView;

        // Pending attachment for the form
        private byte[] _pendingAttachBytes = null;
        private string _pendingAttachName = null;

        // ── Constructor ──────────────────────────────────────────────────────────
        public ProductsWindow()
        {
            InitializeComponent();
            EnsureSchema();
            LoadProducts();
            SetPlaceholders();
        }

        // ── Schema ───────────────────────────────────────────────────────────────
        private void EnsureSchema()
        {
            using var con = new SQLiteConnection(_dbPath);
            con.Open();
            new SQLiteCommand(@"CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Code TEXT, ProductName TEXT, Description TEXT, Category TEXT,
                PurchasePrice REAL DEFAULT 0,
                SalePrice REAL DEFAULT 0,
                CreatedAt TEXT, UpdatedAt TEXT)", con).ExecuteNonQuery();

            // Safe migrations — add columns that may not exist yet
            foreach (var col in new[]
            {
                "ALTER TABLE Products ADD COLUMN Code TEXT",
                "ALTER TABLE Products ADD COLUMN PurchasePrice REAL DEFAULT 0",
                "ALTER TABLE Products ADD COLUMN SalePrice REAL DEFAULT 0",
                "ALTER TABLE Products ADD COLUMN CreatedAt TEXT",
                "ALTER TABLE Products ADD COLUMN UpdatedAt TEXT",
                "ALTER TABLE Products ADD COLUMN AttachName TEXT",
                "ALTER TABLE Products ADD COLUMN AttachData BLOB"
            })
            { try { new SQLiteCommand(col, con).ExecuteNonQuery(); } catch { } }
        }

        // ── Load Products ────────────────────────────────────────────────────────
        private void LoadProducts()
        {
            using var con = new SQLiteConnection(_dbPath);
            con.Open();
            var dt = new DataTable();
            new SQLiteDataAdapter(
                "SELECT *, CASE WHEN AttachData IS NOT NULL THEN 'True' ELSE 'False' END AS HasAttachment " +
                "FROM Products ORDER BY Id DESC", con).Fill(dt);
            _productsView = dt.DefaultView;
            ProductsGrid.ItemsSource = _productsView;

            int count = dt.Rows.Count;
            TxtProductCount.Text = $"  •  {count} item{(count == 1 ? "" : "s")}";
            SetStatus($"Loaded {count} products.");
        }

        // ── Grid row selected → fill form ────────────────────────────────────────
        private void ProductsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProductsGrid.SelectedItem is not DataRowView row) return;
            PopulateFormFromRow(row);
        }

        private void PopulateFormFromRow(DataRowView row)
        {
            _selectedProductId = Convert.ToInt32(row["Id"]);
            txtCode.Text = row["Code"].ToString();
            txtProductName.Text = row["ProductName"].ToString();
            txtDescription.Text = row["Description"].ToString();
            txtCategory.Text = row["Category"].ToString();
            txtPurchasePrice.Text = row["PurchasePrice"].ToString();
            txtSalePrice.Text = row["SalePrice"].ToString();

            // White foreground for real content
            var white = System.Windows.Media.Brushes.White;
            foreach (var tb in new[] { txtCode, txtProductName, txtDescription, txtCategory,
                                       txtPurchasePrice, txtSalePrice })
                tb.Foreground = white;

            // Attachment hint
            bool hasAttach = row["AttachData"] != DBNull.Value && ((byte[])row["AttachData"]).Length > 0;
            if (hasAttach)
            {
                string an = row["AttachName"] == DBNull.Value ? "attachment" : row["AttachName"].ToString();
                TxtAttachHint.Text = $"📎  Existing: {an}  (Attach to replace)";
                TxtAttachHint.Visibility = Visibility.Visible;
            }
            else
            {
                TxtAttachHint.Visibility = Visibility.Collapsed;
                TxtAttachHint.Text = "";
            }

            _pendingAttachBytes = null;
            _pendingAttachName = null;
            BtnClearAttach.Visibility = Visibility.Collapsed;

            // Show editing badge
            EditingBadge.Visibility = Visibility.Visible;
            TxtEditingLabel.Text = $"✎  Editing: {row["ProductName"]}  (ID {_selectedProductId})";
            SetStatus($"Editing: {row["ProductName"]}  (ID {_selectedProductId})");
        }

        // ── Inline Edit button in grid ────────────────────────────────────────────
        private void BtnInlineEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out int id)) return;
            // Find the row in the DataView
            foreach (DataRowView drv in _productsView)
            {
                if (Convert.ToInt32(drv["Id"]) == id)
                {
                    ProductsGrid.SelectedItem = drv;
                    PopulateFormFromRow(drv);
                    txtCode.Focus();
                    break;
                }
            }
        }

        // ── Inline Delete button in grid ─────────────────────────────────────────
        private void BtnInlineDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out int id)) return;
            string name = "";
            foreach (DataRowView drv in _productsView)
                if (Convert.ToInt32(drv["Id"]) == id) { name = drv["ProductName"].ToString(); break; }

            if (MessageBox.Show($"Delete '{name}'? This cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            using var con = new SQLiteConnection(_dbPath);
            con.Open();
            new SQLiteCommand($"DELETE FROM Products WHERE Id={id}", con).ExecuteNonQuery();
            SetStatus($"✔  '{name}' deleted.");
            ClearForm();
            LoadProducts();
        }

        // ── Add ──────────────────────────────────────────────────────────────────
        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm(out decimal pp, out decimal sp)) return;
            // ── FIX: Duplicate code check ──────────────────────────────────────────
            string code = txtCode.Text.Trim();
            if (!string.IsNullOrWhiteSpace(code) && code != "Code")
            {
                using var checkCon = new SQLiteConnection(_dbPath);
                checkCon.Open();
                using var checkCmd = new SQLiteCommand(
                    "SELECT COUNT(*) FROM Products WHERE TRIM(Code)=@c", checkCon);
                checkCmd.Parameters.AddWithValue("@c", code);
                long existing = (long)checkCmd.ExecuteScalar();
                if (existing > 0)
                {
                    MessageBox.Show($"Code '{code}' already exists. Please use a unique code.",
                        "Duplicate Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtCode.Focus();
                    return;
                }
            }
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand(@"
                    INSERT INTO Products
                        (Code, ProductName, Description, Category,
                         PurchasePrice, SalePrice,
                         AttachName, AttachData, CreatedAt)
                    VALUES
                        (@code, @name, @desc, @cat,
                         @pp, @sp,
                         @an, @ad, @now)", con);

                cmd.Parameters.AddWithValue("@code", txtCode.Text.Trim());
                cmd.Parameters.AddWithValue("@name", txtProductName.Text.Trim());
                cmd.Parameters.AddWithValue("@desc", txtDescription.Text.Trim());
                cmd.Parameters.AddWithValue("@cat", txtCategory.Text.Trim());
                cmd.Parameters.AddWithValue("@pp", pp);
                cmd.Parameters.AddWithValue("@sp", sp);
                cmd.Parameters.AddWithValue("@an", (object)_pendingAttachName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ad", (object)_pendingAttachBytes ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();

                SetStatus($"✔  Product '{txtProductName.Text.Trim()}' added.");
                ClearForm();
                LoadProducts();
            }
            catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
        }

        // ── Save / Update ────────────────────────────────────────────────────────
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProductId <= 0)
            { MessageBox.Show("Select a product row first, then click Update."); return; }

            if (!ValidateForm(out decimal pp, out decimal sp)) return;

            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                string sql = _pendingAttachBytes != null
                    ? @"UPDATE Products SET
                        Code=@code, ProductName=@name, Description=@desc, Category=@cat,
                        PurchasePrice=@pp, SalePrice=@sp,
                        AttachName=@an, AttachData=@ad, UpdatedAt=@now
                        WHERE Id=@id"
                    : @"UPDATE Products SET
                        Code=@code, ProductName=@name, Description=@desc, Category=@cat,
                        PurchasePrice=@pp, SalePrice=@sp, UpdatedAt=@now
                        WHERE Id=@id";

                using var cmd = new SQLiteCommand(sql, con);
                cmd.Parameters.AddWithValue("@code", txtCode.Text.Trim());
                cmd.Parameters.AddWithValue("@name", txtProductName.Text.Trim());
                cmd.Parameters.AddWithValue("@desc", txtDescription.Text.Trim());
                cmd.Parameters.AddWithValue("@cat", txtCategory.Text.Trim());
                cmd.Parameters.AddWithValue("@pp", pp);
                cmd.Parameters.AddWithValue("@sp", sp);
                cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@id", _selectedProductId);
                if (_pendingAttachBytes != null)
                {
                    cmd.Parameters.AddWithValue("@an", (object)_pendingAttachName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ad", (object)_pendingAttachBytes ?? DBNull.Value);
                }
                cmd.ExecuteNonQuery();

                SetStatus($"✔  Product updated (ID {_selectedProductId}).");
                ClearForm();
                LoadProducts();
            }
            catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); }
        }

        // ── Delete ───────────────────────────────────────────────────────────────
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsGrid.SelectedItem is not DataRowView row)
            { MessageBox.Show("Select a product to delete."); return; }

            string name = row["ProductName"].ToString();
            if (MessageBox.Show($"Delete '{name}'? This cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            int id = Convert.ToInt32(row["Id"]);
            using var con = new SQLiteConnection(_dbPath);
            con.Open();
            new SQLiteCommand($"DELETE FROM Products WHERE Id={id}", con).ExecuteNonQuery();
            SetStatus($"✔  '{name}' deleted.");
            ClearForm();
            LoadProducts();
        }

        // ── New ──────────────────────────────────────────────────────────────────
        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            txtCode.Focus();
            SetStatus("New product — fill in the fields above.");
        }

        // ── Search ───────────────────────────────────────────────────────────────
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_productsView == null) return;
            string q = SearchBox.Text.Trim();
            if (q.Length > 0 && q != "Search products...")
                SearchBox.Foreground = System.Windows.Media.Brushes.White;
            BtnClearSearch.Visibility = (q.Length > 0 && q != "Search products...")
                ? Visibility.Visible : Visibility.Collapsed;
            if (string.IsNullOrEmpty(q) || q == "Search products...")
                _productsView.RowFilter = "";
            else
                _productsView.RowFilter =
                    $"Convert(Code,'System.String')        LIKE '*{q}*' OR " +
                    $"Convert(ProductName,'System.String') LIKE '*{q}*' OR " +
                    $"Convert(Category,'System.String')    LIKE '*{q}*'";
            int count = _productsView.Count;
            TxtProductCount.Text = $"  •  {count} item{(count == 1 ? "" : "s")}";
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

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
            if (sender is not Button b || b.Tag == null) return;
            if (!int.TryParse(b.Tag.ToString(), out int id)) return;
            try
            {
                using var con = new SQLiteConnection(_dbPath);
                con.Open();
                using var cmd = new SQLiteCommand("SELECT AttachName, AttachData FROM Products WHERE Id=@id", con);
                cmd.Parameters.AddWithValue("@id", id);
                using var r = cmd.ExecuteReader();
                if (!r.Read() || r["AttachData"] == DBNull.Value) return;
                OpenBytesWithDefaultApp((byte[])r["AttachData"], r["AttachName"]?.ToString() ?? "attachment");
            }
            catch (Exception ex) { MessageBox.Show("Cannot open attachment: " + ex.Message); }
        }

        private static void OpenBytesWithDefaultApp(byte[] data, string filename)
        {
            try
            {
                string ext = Path.GetExtension(filename);
                string tmp = Path.Combine(Path.GetTempPath(), $"hmi_prod_{Guid.NewGuid():N}{ext}");
                File.WriteAllBytes(tmp, data);
                Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show("Cannot open: " + ex.Message); }
        }

        private static string FormatBytes(long b)
        {
            if (b < 1024) return $"{b} B";
            if (b < 1048576) return $"{b / 1024.0:N1} KB";
            return $"{b / 1048576.0:N1} MB";
        }

        // ── Validation ───────────────────────────────────────────────────────────
        private bool ValidateForm(out decimal pp, out decimal sp)
        {
            pp = 0; sp = 0;
            if (string.IsNullOrWhiteSpace(txtProductName.Text) ||
                txtProductName.Text == (string)txtProductName.Tag)
            { MessageBox.Show("Product name is required."); txtProductName.Focus(); return false; }
            if (!decimal.TryParse(txtPurchasePrice.Text, out pp) || pp < 0)
            { MessageBox.Show("Enter a valid purchase price."); txtPurchasePrice.Focus(); return false; }
            if (!decimal.TryParse(txtSalePrice.Text, out sp) || sp < 0)
            { MessageBox.Show("Enter a valid sale price."); txtSalePrice.Focus(); return false; }
            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private void ClearForm()
        {
            _selectedProductId = -1;
            EditingBadge.Visibility = Visibility.Collapsed;
            SetPlaceholders();
            ProductsGrid.SelectedItem = null;
            BtnClearAttach_Click(null, null);
        }

        private void SetPlaceholders()
        {
            SetPlaceholder(txtCode, "Code");
            SetPlaceholder(txtProductName, "Product Name");
            SetPlaceholder(txtDescription, "Optional notes");
            SetPlaceholder(txtCategory, "Company Name");
            SetPlaceholder(txtPurchasePrice, "0.00");
            SetPlaceholder(txtSalePrice, "0.00");
        }

        private void SetPlaceholder(TextBox tb, string placeholder)
        {
            tb.Text = placeholder;
            tb.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B8FA8"));
        }

        private void SetStatus(string msg) => TxtStatus.Text = msg;

        private void btnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Search placeholder focus ─────────────────────────────────────────────
        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search products...")
            { SearchBox.Text = ""; SearchBox.Foreground = System.Windows.Media.Brushes.White; }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Search products...";
                SearchBox.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B8FA8"));
            }
        }

        // ── Generic TextBox placeholder focus ───────────────────────────────────
        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (tb.Text == (string)tb.Tag) tb.Text = "";
            tb.Foreground = System.Windows.Media.Brushes.White;
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (string.IsNullOrWhiteSpace(tb.Text))
            {
                tb.Text = (string)tb.Tag ?? "";
                tb.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B8FA8"));
            }
            else
                tb.Foreground = System.Windows.Media.Brushes.White;
        }
    }
}