using Hale_Marketing_International.Services;
using System.Windows;
using System.Windows.Input;
using Hale_Marketing_International.Security;
using Hale_Marketing_International.Views;

namespace Hale_Marketing_International
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Ensure Users table exists
            UserService.EnsureUserTable();

            // Sync visible textbox with pwdBox
            pwdBox.PasswordChanged += (s, e) =>
            {
                if (txtPasswordVisible.Visibility == Visibility.Visible)
                    txtPasswordVisible.Text = pwdBox.Password;
            };
            txtPasswordVisible.TextChanged += (s, e) =>
            {
                if (txtPasswordVisible.Visibility == Visibility.Visible)
                    pwdBox.Password = txtPasswordVisible.Text;
            };
        }

        private void BtnEye_Click(object sender, RoutedEventArgs e)
        {
            if (txtPasswordVisible.Visibility == Visibility.Collapsed)
            {
                txtPasswordVisible.Text = pwdBox.Password;
                txtPasswordVisible.Visibility = Visibility.Visible;
                pwdBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                pwdBox.Password = txtPasswordVisible.Text;
                txtPasswordVisible.Visibility = Visibility.Collapsed;
                pwdBox.Visibility = Visibility.Visible;
            }
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUser.Text.Trim();
            string password = pwdBox.Visibility == Visibility.Visible
                              ? pwdBox.Password
                              : txtPasswordVisible.Text;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Enter username and password.", "Login",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (UserService.ValidateCredentials(username, password))
            {
                var (fullName, role) = UserService.GetUserInfo(username);
                var mainApp = new MainAppWindow();
                mainApp.Show();
                this.Close();
            }
            else
            {
                MessageBox.Show("Invalid username or password.", "Login Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                pwdBox.Clear();
                txtPasswordVisible.Text = "";
                pwdBox.Focus();
            }
        }

        // ── Ctrl+Shift+A → Admin creation window ─────────────────────────────
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.A &&
                Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                // Only allow if no admin exists yet
                // ── Fixed code (checks actual Users table) ────────────────
                if (UserService.AdminExists())
                {
                    MessageBox.Show("Admin account already exists.",
                        "Setup", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var adminWindow = new AdminCreationWindow();
                adminWindow.Owner = this;
                adminWindow.ShowDialog();
            }

            base.OnKeyDown(e);
        }

        // ── Forgot password — lets user change password if they know old one ──
        // private void LnkForgot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        // {
        // var forgot = new ForgotPasswordWindow();
        //   forgot.Owner = this;
        // forgot.ShowDialog();
        // }

        // ── Register button — disable for clients, only shown to you ─────────
        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            // Hidden — not shown to client
            // Only accessible via Ctrl+Shift+A admin panel
        }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}