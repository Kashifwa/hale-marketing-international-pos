using System;
using System.Windows;
using Hale_Marketing_International.Services;

namespace Hale_Marketing_International
{
    public partial class AdminCreationWindow : Window
    {
        public AdminCreationWindow()
        {
            InitializeComponent();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string username = TxtUsername.Text.Trim();
            string password = PbPassword.Password;

            if (string.IsNullOrEmpty(username))
            {
                MessageBox.Show("Please enter a username.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (password.Length < 6)
            {
                MessageBox.Show("Password must be at least 6 characters.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Make sure Users table exists
            UserService.EnsureUserTable();

            // Insert into DB using UserService (same hash, same DB)
            bool created = UserService.AddUser(
                username: username,
                password: password,
                fullName: username,
                role: "Admin"
            );

            if (!created)
            {
                MessageBox.Show(
                    "Could not create user '" + username + "'.\n" +
                    "Username may already exist. Try a different one.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(
                "✅ Account '" + username + "' created!\n\n" +
                "You can now log in.\n\n" +
                "After logging in, go to ☁ Backup & Sync in the sidebar\n" +
                "to connect your Google Drive for automatic backups.",
                "Account Created", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
    }
}