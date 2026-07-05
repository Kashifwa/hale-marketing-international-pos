using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Hale_Marketing_International
{
    public partial class AddPartyWindow : Window
    {
        private List<Party> parties = new();
        private string partyDbPath = "Data Source=posdata.db;Version=3;";

        public class Party
        {
            public int Id { get; set; }
            public string Date { get; set; }
            public string Time { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public string Contact { get; set; }
            public string Address { get; set; }
            public string Description { get; set; }
        }

        public AddPartyWindow()
        {
            InitializeComponent();
            CreateDatabaseIfNotExists();
            LoadPartiesFromDB();
            SetAutoFields();
        }

        // ✅ Updates the party count label in the top bar
        private void UpdatePartyCount()
        {
            int count = parties?.Count ?? 0;
            TxtPartyCount.Text = $"  •  {count} {(count == 1 ? "party" : "parties")}";
        }

        // ✅ Create table with correct syntax
        private void CreateDatabaseIfNotExists()
        {
            using var con = new SQLiteConnection(partyDbPath);
            con.Open();
            string tableQuery = @"CREATE TABLE IF NOT EXISTS Parties (
                                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                    Name TEXT,
                                    Type TEXT,
                                    Contact TEXT,
                                    Address TEXT,
                                    Description TEXT,
                                    Date TEXT,
                                    Time TEXT
                                )";
            new SQLiteCommand(tableQuery, con).ExecuteNonQuery();
        }

        private void SetAutoFields()
        {
            dpDate.SelectedDate = DateTime.Now;
            txtTime.Text = DateTime.Now.ToString("hh:mm tt");
            txtId.Text = (parties.Count + 1).ToString();
        }

        private void LoadPartiesFromDB()
        {
            parties.Clear();
            using var con = new SQLiteConnection(partyDbPath);
            con.Open();
            using var cmd = new SQLiteCommand("SELECT * FROM Parties", con);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                parties.Add(new Party
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Date = reader["Date"].ToString(),
                    Time = reader["Time"].ToString(),
                    Name = reader["Name"].ToString(),
                    Type = reader["Type"].ToString(),
                    Contact = reader["Contact"].ToString(),
                    Address = reader["Address"].ToString(),
                    Description = reader["Description"].ToString()
                });
            }
            dgParties.ItemsSource = null;
            dgParties.ItemsSource = parties;

            // ✅ Update count after loading
            UpdatePartyCount();
        }

        // ✅ Add new Party (inserts into DB immediately and retrieves ID)
        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtPartyName.Text) || txtPartyName.Text == "Party Name" || cmbType.SelectedItem == null)
            {
                MessageBox.Show("Please fill required fields.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newParty = new Party
            {
                Date = dpDate.SelectedDate?.ToString("yyyy-MM-dd"),
                Time = txtTime.Text,
                Name = txtPartyName.Text,
                Type = ((ComboBoxItem)cmbType.SelectedItem).Content.ToString(),
                Contact = txtContact.Text,
                Address = txtAddress.Text,
                Description = txtDescription.Text
            };

            using (var con = new SQLiteConnection(partyDbPath))
            {
                con.Open();
                // ✅ If table is empty, reset AUTOINCREMENT (so next ID starts from 1)
                string countQuery = "SELECT COUNT(*) FROM Parties";
                using (var cmdCheck = new SQLiteCommand(countQuery, con))
                {
                    long count = (long)cmdCheck.ExecuteScalar();
                    if (count == 0)
                    {
                        string resetQuery = "DELETE FROM sqlite_sequence WHERE name='Parties'";
                        using (var cmdReset = new SQLiteCommand(resetQuery, con))
                        {
                            cmdReset.ExecuteNonQuery();
                        }
                    }
                }
                string insert = @"INSERT INTO Parties (Date, Time, Name, Type, Contact, Address, Description)
                                  VALUES (@Date, @Time, @Name, @Type, @Contact, @Address, @Description)";
                using (var cmd = new SQLiteCommand(insert, con))
                {
                    cmd.Parameters.AddWithValue("@Date", newParty.Date);
                    cmd.Parameters.AddWithValue("@Time", newParty.Time);
                    cmd.Parameters.AddWithValue("@Name", newParty.Name);
                    cmd.Parameters.AddWithValue("@Type", newParty.Type);
                    cmd.Parameters.AddWithValue("@Contact", newParty.Contact);
                    cmd.Parameters.AddWithValue("@Address", newParty.Address);
                    cmd.Parameters.AddWithValue("@Description", newParty.Description);
                    cmd.ExecuteNonQuery();

                    // ✅ Get Auto Incremented ID
                    cmd.CommandText = "SELECT last_insert_rowid()";
                    newParty.Id = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }

            parties.Add(newParty);
            dgParties.ItemsSource = null;
            dgParties.ItemsSource = parties;

            // ✅ Update count after add
            UpdatePartyCount();

            ResetPartyFields();
            SetAutoFields();
            MessageBox.Show("Party added successfully!");
        }

        // ✅ Update Party
        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (dgParties.SelectedItem is Party selected)
            {
                selected.Date = dpDate.SelectedDate?.ToString("yyyy-MM-dd");
                selected.Time = txtTime.Text;
                selected.Name = txtPartyName.Text;
                selected.Type = ((ComboBoxItem)cmbType.SelectedItem)?.Content.ToString();
                selected.Contact = txtContact.Text;
                selected.Address = txtAddress.Text;
                selected.Description = txtDescription.Text;

                using (var con = new SQLiteConnection(partyDbPath))
                {
                    con.Open();
                    string update = @"UPDATE Parties 
                                      SET Date=@Date, Time=@Time, Name=@Name, Type=@Type, 
                                          Contact=@Contact, Address=@Address, Description=@Description
                                      WHERE Id=@Id";
                    using var cmd = new SQLiteCommand(update, con);
                    cmd.Parameters.AddWithValue("@Date", selected.Date);
                    cmd.Parameters.AddWithValue("@Time", selected.Time);
                    cmd.Parameters.AddWithValue("@Name", selected.Name);
                    cmd.Parameters.AddWithValue("@Type", selected.Type);
                    cmd.Parameters.AddWithValue("@Contact", selected.Contact);
                    cmd.Parameters.AddWithValue("@Address", selected.Address);
                    cmd.Parameters.AddWithValue("@Description", selected.Description);
                    cmd.Parameters.AddWithValue("@Id", selected.Id);
                    cmd.ExecuteNonQuery();
                }

                dgParties.ItemsSource = null;
                dgParties.ItemsSource = parties;

                // ✅ Count stays same on update, but refresh anyway
                UpdatePartyCount();

                ResetPartyFields();
                SetAutoFields();
                MessageBox.Show("Party updated successfully!");
            }
        }

        // ✅ Delete Party
        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (dgParties.SelectedItem is Party selectedParty)
            {
                if (MessageBox.Show("Are you sure you want to delete this party?",
                                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
                    return;

                using (var con = new SQLiteConnection(partyDbPath))
                {
                    con.Open();

                    string deleteQuery = "DELETE FROM Parties WHERE Id=@Id";
                    using (var cmd = new SQLiteCommand(deleteQuery, con))
                    {
                        cmd.Parameters.AddWithValue("@Id", selectedParty.Id);
                        cmd.ExecuteNonQuery();
                    }

                    string countQuery = "SELECT COUNT(*) FROM Parties";
                    using (var cmdCheck = new SQLiteCommand(countQuery, con))
                    {
                        long count = (long)cmdCheck.ExecuteScalar();
                        if (count == 0)
                        {
                            string resetQuery = "DELETE FROM sqlite_sequence WHERE name='Parties'";
                            using (var cmdReset = new SQLiteCommand(resetQuery, con))
                            {
                                cmdReset.ExecuteNonQuery();
                            }
                        }
                    }
                }

                parties.Remove(selectedParty);
                dgParties.ItemsSource = null;
                dgParties.ItemsSource = parties;

                // ✅ Update count after delete
                UpdatePartyCount();

                BtnUpdate.IsEnabled = false;
                BtnDelete.IsEnabled = false;

                ResetPartyFields();
                SetAutoFields();

                MessageBox.Show("Party deleted successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Please select a party to delete.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ✅ Load selection to edit
        private void dgParties_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgParties.SelectedItem is Party selected)
            {
                txtId.Text = selected.Id.ToString();
                dpDate.SelectedDate = DateTime.TryParse(selected.Date, out DateTime d) ? d : DateTime.Now;
                txtTime.Text = selected.Time;
                txtPartyName.Text = selected.Name;
                cmbType.Text = selected.Type;
                txtContact.Text = selected.Contact;
                txtAddress.Text = selected.Address;
                txtDescription.Text = selected.Description;

                BtnUpdate.IsEnabled = true;
                BtnDelete.IsEnabled = true;
            }
        }

        // ✅ Reset all textboxes
        private void ResetPartyFields()
        {
            txtPartyName.Text = "Party Name";
            txtContact.Text = "Contact";
            txtAddress.Text = "Address";
            txtDescription.Text = "Description";
            cmbType.SelectedIndex = -1;
            cmbType.Text = "Type";
            txtSearch.Text = "Search...";

            BtnUpdate.IsEnabled = false;
            BtnDelete.IsEnabled = false;
        }

        // ✅ Search Filter
        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (parties == null || dgParties == null)
                return;

            string filter = txtSearch.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(filter) || filter == "search...")
            {
                dgParties.ItemsSource = parties;
            }
            else
            {
                var filtered = parties.Where(p =>
                    (!string.IsNullOrEmpty(p.Name) && p.Name.ToLower().Contains(filter)) ||
                    (!string.IsNullOrEmpty(p.Type) && p.Type.ToLower().Contains(filter)) ||
                    (!string.IsNullOrEmpty(p.Contact) && p.Contact.ToLower().Contains(filter)) ||
                    (!string.IsNullOrEmpty(p.Address) && p.Address.ToLower().Contains(filter))
                ).ToList();

                dgParties.ItemsSource = filtered;
            }
        }

        private void txtSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb != null && tb.Text == "Search...")
                tb.Text = "";
        }

        private void txtSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb != null && string.IsNullOrWhiteSpace(tb.Text))
                tb.Text = "Search...";
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (tb.Text == "Search..." || tb.Text == "Party Name" || tb.Text == "PartyName" ||
                    tb.Text == "Contact" || tb.Text == "Address" || tb.Text == "Description")
                {
                    tb.Text = "";
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && string.IsNullOrWhiteSpace(tb.Text))
            {
                switch (tb.Name)
                {
                    case "txtSearch": tb.Text = "Search..."; break;
                    case "txtPartyName": tb.Text = "Party Name"; break;
                    case "txtContact": tb.Text = "Contact"; break;
                    case "txtAddress": tb.Text = "Address"; break;
                    case "txtDescription": tb.Text = "Description"; break;
                }
            }
        }

        // ✅ Save All button
        private void BtnSaveAll_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("All parties saved successfully!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ✅ ComboBox Placeholder Handling
        private void cmbType_Loaded(object sender, RoutedEventArgs e)
        {
            cmbType.SelectedIndex = -1;
            cmbType.Background = System.Windows.Media.Brushes.Black;
            cmbType.Foreground = System.Windows.Media.Brushes.White;
        }

        private void cmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            cmbType.Background = System.Windows.Media.Brushes.Black;
            cmbType.Foreground = System.Windows.Media.Brushes.White;
        }

        private void txtDescription_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}