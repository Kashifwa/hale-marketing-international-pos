using System;
using System.Data.SQLite;
using System.Windows;

namespace Hale_Marketing_International

{
    public static class DatabaseInitializer
    {
        public static void EnsureAllTables()
        {
            try
            {
                using var con = new SQLiteConnection(AppConfig.ConnectionString);
                con.Open();
                using var cmd = new SQLiteCommand(con);

                void Run(string sql)
                {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }

                void SafeAlter(string sql)
                {
                    try { cmd.CommandText = sql; cmd.ExecuteNonQuery(); } catch { /* column already exists */ }
                }

                // ── Parties ─────────────────────────────
                Run(@"CREATE TABLE IF NOT EXISTS Parties (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT, Type TEXT, Contact TEXT,
                    Address TEXT, Description TEXT, Date TEXT, Time TEXT
                )");

                // ── Users ───────────────────────────────
                Run(@"CREATE TABLE IF NOT EXISTS Users (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    FullName TEXT,
                    Role TEXT DEFAULT 'User',
                    IsActive INTEGER DEFAULT 1,
                    CreatedAt TEXT
                )");

                // ── CashReceipt ─────────────────────────
                Run(@"CREATE TABLE IF NOT EXISTS CashReceipt (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    VoucherNo TEXT, Date TEXT, PartyId INTEGER,
                    PaymentMode TEXT, Remarks TEXT, Amount REAL,
                    AttachName TEXT, AttachData BLOB
                )");
                SafeAlter("ALTER TABLE CashReceipt ADD COLUMN AttachName TEXT");
                SafeAlter("ALTER TABLE CashReceipt ADD COLUMN AttachData BLOB");

                // ── PaymentReceipt ──────────────────────
                Run(@"CREATE TABLE IF NOT EXISTS PaymentReceipt (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    VoucherNo TEXT, Date TEXT, PartyId INTEGER,
                    PaymentMode TEXT, Remarks TEXT, Amount REAL,
                    AttachName TEXT, AttachData BLOB
                )");
                SafeAlter("ALTER TABLE PaymentReceipt ADD COLUMN AttachName TEXT");
                SafeAlter("ALTER TABLE PaymentReceipt ADD COLUMN AttachData BLOB");

                // ── ManualExpenses / ManualAssets / ManualLiabilities ──
                Run(@"CREATE TABLE IF NOT EXISTS ManualExpenses (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT, Category TEXT, Amount REAL,
                    Description TEXT, PaymentMode TEXT, CreatedAt TEXT)");

                Run(@"CREATE TABLE IF NOT EXISTS ManualAssets (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT, AssetType TEXT, Value REAL,
                    Description TEXT, CreatedAt TEXT)");

                Run(@"CREATE TABLE IF NOT EXISTS ManualLiabilities (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT, LiabilityType TEXT, Amount REAL,
                    Description TEXT, CreatedAt TEXT)");

                // ── Products ────────────────────────────
                Run(@"CREATE TABLE IF NOT EXISTS Products (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT, ProductName TEXT, Description TEXT, Category TEXT,
                    PurchasePrice REAL DEFAULT 0,
                    SalePrice REAL DEFAULT 0,
                    CreatedAt TEXT, UpdatedAt TEXT)");
                SafeAlter("ALTER TABLE Products ADD COLUMN Code TEXT");
                SafeAlter("ALTER TABLE Products ADD COLUMN PurchasePrice REAL DEFAULT 0");
                SafeAlter("ALTER TABLE Products ADD COLUMN SalePrice REAL DEFAULT 0");
                SafeAlter("ALTER TABLE Products ADD COLUMN CreatedAt TEXT");
                SafeAlter("ALTER TABLE Products ADD COLUMN UpdatedAt TEXT");
                SafeAlter("ALTER TABLE Products ADD COLUMN AttachName TEXT");
                SafeAlter("ALTER TABLE Products ADD COLUMN AttachData BLOB");

                // ── PurchaseReturn / PurchaseReturnItems ────
                Run(@"CREATE TABLE IF NOT EXISTS PurchaseReturn (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnNo TEXT, ReturnDate TEXT, SupplierId INTEGER,
                    SubTotal REAL, NetAmount REAL)");

                Run(@"CREATE TABLE IF NOT EXISTS PurchaseReturnItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnId INTEGER, ProductId INTEGER,
                    ProductCode TEXT, ProductName TEXT, Company TEXT,
                    ReturnQty REAL, Rate REAL, Amount REAL,
                    Details TEXT, AttachName TEXT, AttachData BLOB)");
                SafeAlter("ALTER TABLE PurchaseReturnItems ADD COLUMN Company TEXT");
                SafeAlter("ALTER TABLE PurchaseReturnItems ADD COLUMN Details TEXT");
                SafeAlter("ALTER TABLE PurchaseReturnItems ADD COLUMN AttachName TEXT");
                SafeAlter("ALTER TABLE PurchaseReturnItems ADD COLUMN AttachData BLOB");
                SafeAlter("ALTER TABLE PurchaseReturnItems ADD COLUMN ReturnQty REAL");

                // ── PurchaseInvoice / PurchaseItems ─────────
                Run(@"CREATE TABLE IF NOT EXISTS PurchaseInvoice (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceNo TEXT, InvoiceDate TEXT,
                    PartyId INTEGER, Supplier TEXT,
                    SubTotal REAL, TaxPercent REAL, TaxAmount REAL,
                    DiscountPercent REAL, DiscountAmount REAL,
                    Freight REAL, NetTotal REAL,
                    PaymentMethod TEXT, PaymentAmount REAL, FinalNote TEXT)");
                SafeAlter("ALTER TABLE PurchaseInvoice ADD COLUMN PartyId INTEGER");
                SafeAlter("ALTER TABLE PurchaseInvoice ADD COLUMN TaxPercent REAL");
                SafeAlter("ALTER TABLE PurchaseInvoice ADD COLUMN TaxAmount REAL");
                SafeAlter("ALTER TABLE PurchaseInvoice ADD COLUMN DiscountPercent REAL");
                SafeAlter("ALTER TABLE PurchaseInvoice ADD COLUMN DiscountAmount REAL");
                SafeAlter("ALTER TABLE PurchaseInvoice ADD COLUMN PaymentMethod TEXT");
                SafeAlter("ALTER TABLE PurchaseInvoice ADD COLUMN PaymentAmount REAL");
                SafeAlter("ALTER TABLE PurchaseInvoice ADD COLUMN FinalNote TEXT");

                Run(@"CREATE TABLE IF NOT EXISTS PurchaseItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceId INTEGER, InvoiceNo TEXT,
                    ProductCode TEXT, ProductName TEXT, Company TEXT,
                    Quantity REAL, Rate REAL, Amount REAL,
                    Details TEXT, AttachName TEXT, AttachData BLOB)");
                SafeAlter("ALTER TABLE PurchaseItems ADD COLUMN InvoiceId INTEGER");
                SafeAlter("ALTER TABLE PurchaseItems ADD COLUMN Company TEXT");
                SafeAlter("ALTER TABLE PurchaseItems ADD COLUMN Details TEXT");
                SafeAlter("ALTER TABLE PurchaseItems ADD COLUMN AttachName TEXT");
                SafeAlter("ALTER TABLE PurchaseItems ADD COLUMN AttachData BLOB");

                // ── CustomerLedger (shared by Sales/Purchase/Returns) ──
                Run(@"CREATE TABLE IF NOT EXISTS CustomerLedger (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartyId INTEGER, Date TEXT, Description TEXT,
                    Debit REAL, Credit REAL, Balance REAL)");

                // ── SalesReturn / SalesReturnItems ──────────
                Run(@"CREATE TABLE IF NOT EXISTS SalesReturn (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnNo TEXT, ReturnDate TEXT, PartyId INTEGER,
                    SubTotal REAL, NetAmount REAL)");

                Run(@"CREATE TABLE IF NOT EXISTS SalesReturnItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ReturnId INTEGER, ProductId INTEGER,
                    ProductCode TEXT, ProductName TEXT, Company TEXT,
                    ReturnQty INTEGER, Rate REAL, Amount REAL,
                    Details TEXT, AttachName TEXT, AttachData BLOB)");
                SafeAlter("ALTER TABLE SalesReturnItems ADD COLUMN Company TEXT");
                SafeAlter("ALTER TABLE SalesReturnItems ADD COLUMN Details TEXT");
                SafeAlter("ALTER TABLE SalesReturnItems ADD COLUMN AttachName TEXT");
                SafeAlter("ALTER TABLE SalesReturnItems ADD COLUMN AttachData BLOB");
                SafeAlter("ALTER TABLE SalesReturnItems ADD COLUMN ReturnQty INTEGER");

                // ── Sales / SalesItems ──────────────────────
                Run(@"CREATE TABLE IF NOT EXISTS Sales (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    InvoiceNo TEXT, InvoiceDate TEXT, PartyId INTEGER,
                    SubTotal REAL, TaxPercent REAL, TaxAmount REAL,
                    DiscountPercent REAL, DiscountAmount REAL,
                    Freight REAL, NetTotal REAL,
                    PaymentMethod TEXT, PaymentAmount REAL, FinalNote TEXT)");
                SafeAlter("ALTER TABLE Sales ADD COLUMN FinalNote TEXT");

                Run(@"CREATE TABLE IF NOT EXISTS SalesItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SaleId INTEGER, ProductId INTEGER,
                    ProductCode TEXT, ProductName TEXT, Company TEXT,
                    Quantity REAL, Rate REAL, Amount REAL,
                    Details TEXT, AttachName TEXT, AttachData BLOB)");
                SafeAlter("ALTER TABLE SalesItems ADD COLUMN Company TEXT");
                SafeAlter("ALTER TABLE SalesItems ADD COLUMN Details TEXT");
                SafeAlter("ALTER TABLE SalesItems ADD COLUMN AttachName TEXT");
                SafeAlter("ALTER TABLE SalesItems ADD COLUMN AttachData BLOB");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Database initialization failed: " + ex.Message,
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}