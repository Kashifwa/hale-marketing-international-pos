using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Windows;

namespace Hale_Marketing_International
{
    public partial class PartySearchWindow : Window
    {
        private string _dbPath = "Data Source=posdata.db;Version=3;";
        private List<Party> _parties;

        public Party SelectedParty { get; private set; }

        public PartySearchWindow()
        {
            InitializeComponent();
            LoadParties();
        }

        private void LoadParties()
        {
            _parties = new List<Party>();
            using (var con = new SQLiteConnection(_dbPath))
            {
                con.Open();
                using (var cmd = new SQLiteCommand("SELECT Id, Name, Contact, Address FROM Parties ORDER BY Name", con))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        _parties.Add(new Party
                        {
                            Id = r.GetInt32(0),
                            Name = r.GetString(1),
                            Contact = r.IsDBNull(2) ? "" : r.GetString(2),
                            Address = r.IsDBNull(3) ? "" : r.GetString(3)
                        });
                    }
                }
            }
            dgParties.ItemsSource = _parties;
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string txt = TxtSearch.Text.ToLower();
            dgParties.ItemsSource = _parties.Where(p => p.Name.ToLower().Contains(txt)).ToList();
        }

        private void BtnSelect_Click(object sender, RoutedEventArgs e)
        {
            if (dgParties.SelectedItem is Party p)
            {
                SelectedParty = p;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please select a party first.");
            }
        }
    }

    public class Party
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Contact { get; set; }
        public string Address { get; set; }
    }
}
