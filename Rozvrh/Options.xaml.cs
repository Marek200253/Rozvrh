using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// Dokumentaci k šabloně Prázdná aplikace najdete na adrese https://go.microsoft.com/fwlink/?LinkId=234238

namespace Rozvrh
{
    /// <summary>
    /// Prázdná stránka, která se dá použít samostatně nebo se na ni dá přejít v rámci
    /// </summary>
    public sealed partial class Options : Page
    {
        MainPage rootPage = MainPage.current;
        public AppWindow options { get; set; }
        public Options()
        {
            this.InitializeComponent();
            pauzyCB.IsChecked = rootPage.pauzy;
            day1CB.IsChecked = rootPage.days[0];
            day2CB.IsChecked = rootPage.days[1];
            day3CB.IsChecked = rootPage.days[2];
            day4CB.IsChecked = rootPage.days[3];
            day5CB.IsChecked = rootPage.days[4];
            pauzyTB.Text = rootPage.maxPause.ToString();
        }

        private void saveB_Click(object sender, RoutedEventArgs e)
        {
            rootPage.pauzy = (bool)pauzyCB.IsChecked;
            bool[] dny = { (bool)day1CB.IsChecked, (bool)day2CB.IsChecked, (bool)day3CB.IsChecked, (bool)day4CB.IsChecked, (bool)day5CB.IsChecked};
            rootPage.days = dny;
            int.TryParse(pauzyTB.Text, out rootPage.maxPause);
            rootPage.closeOptions();
        }
    }
}
