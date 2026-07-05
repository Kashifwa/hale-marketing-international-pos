using Hale_Marketing_International.ViewModels;
using Hale_Marketing_International.Views;
using System.Windows;
using System.Windows.Controls;

namespace Hale_Marketing_International
{
    public partial class MainAppWindow : Window
    {
        public MainAppWindow()
        {
            InitializeComponent();
            DataContext = new DashboardViewModel();
            this.Closing += MainAppWindow_Closing; // Window closing event
        }

        // ---------- Logout ----------
        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Do you want to logout?",
                "Confirm Logout",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                // ✅ Close all open child windows before logout
                foreach (Window window in Application.Current.Windows)
                {
                    if (window != this)
                        window.Close();
                }

                // ✅ Show login window again
                MainWindow login = new MainWindow();
                login.Show();

                // ✅ Close the main window (logout complete)
                this.Close();
            }
        }



        // ---------- Sidebar Buttons ----------
        private void Stock_Click(object sender, RoutedEventArgs e)
        {
            new StockWindow().Show();

        }

        private void CompanyLedger_Click(object sender, RoutedEventArgs e)
        {
            new CompanyLedgerWindow().Show();
        }


        private void CashReceipt_Click(object sender, RoutedEventArgs e)
        {
            CashReceiptWindow cashreceiptWindow = new CashReceiptWindow();  // ✅ correct class name
            cashreceiptWindow.ShowDialog();
        }

        private void Products_Click(object sender, RoutedEventArgs e)
        {
            ProductsWindow pw = new ProductsWindow();
            pw.Show();
        }

        private void PaymentReceipt_Click(object sender, RoutedEventArgs e)
        {
            PaymentReceiptWindow paymentreceiptwindow = new PaymentReceiptWindow();
            paymentreceiptwindow.ShowDialog();
        
        }

        private void Ledger_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1️⃣ Open Party Search Popup
                var search = new PartySearchWindow { Owner = this };
                if (search.ShowDialog() == true)
                {
                    // 2️⃣ Get selected party
                    var selectedParty = search.SelectedParty;
                    if (selectedParty != null)
                    {
                        // 3️⃣ Open Party Ledger Window for selected party
                        var ledgerWindow = new PartyLedgerWindow(selectedParty);
                        ledgerWindow.Owner = this;
                        ledgerWindow.ShowDialog();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error opening party ledger: " + ex.Message);
            }
        }




        private void ItemLedger_Click(object sender, RoutedEventArgs e)
        {
            new ItemLedgerWindow().Show();
        }

        private void PurchaseFile_Click(object sender, RoutedEventArgs e)
        {
            PurchaseWindow purchaseWindow = new PurchaseWindow();  // ✅ correct class name
            purchaseWindow.ShowDialog();
        }
        private void PurchaseReturn_Click(object sender, RoutedEventArgs e)
        {
            PurchaseReturnWindow prWindow = new PurchaseReturnWindow();
            prWindow.ShowDialog();
        }


        private void SaleFile_Click(object sender, RoutedEventArgs e)
        {
            SalesWindow saleWindow = new SalesWindow();              // ✅ correct class name
            saleWindow.ShowDialog();
        }



        private void SalesReturn_Click(object sender, RoutedEventArgs e)
        {
            SalesReturnWindow srWindow = new SalesReturnWindow();
            srWindow.ShowDialog();
        }
        
       
        // ---------- Main Content Buttons ----------
        private void BtnAddParty_Click(object sender, RoutedEventArgs e)
        {
            AddPartyWindow apw = new AddPartyWindow();
            apw.Show();
        }
        private void BackupSync_Click(object sender, RoutedEventArgs e)
        {
            var win = new Hale_Marketing_International.Views.BackupSyncWindow();
            win.Owner = this;
            win.ShowDialog();
        }
        private async void MainAppWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // ── Auto backup on close ──────────────────────────────────────────
            await App.BackupManager.BackupOnCloseAsync();

                foreach (Window window in Application.Current.Windows)
            {
                if (window != this)
                    window.Close();
            }

            // Confirmation popup
            MessageBoxResult result = MessageBox.Show(
                "Do you want to logout?",
                "Confirm Logout",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                // Show Login Window
                MainWindow login = new MainWindow();
                login.Show();
            }
            else
            {
                // Cancel closing
                e.Cancel = true;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
