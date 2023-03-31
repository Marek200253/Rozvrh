using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

// Dokumentaci k šabloně položky Prázdná stránka najdete na adrese https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x405

namespace Rozvrh
{
    /// <summary>
    /// Prázdná stránka, která se dá použít samostatně nebo v rámci objektu Frame
    /// </summary>
    public sealed partial class MainPage : Page
    {
        List<timeT> timeTable = new List<timeT>();
        List<string> subjectTNames = new List<string>();
        List<string> subjectNames = new List<string>();
        List<Subjects> subjectList = new List<Subjects>();
        List<string> selSubjects = new List<string>();
        StorageFile path;
        DispatcherTimer colorUpd = new DispatcherTimer();
        List<Rectangle> recColor = new List<Rectangle>();
        List<Rectangle> recFColor = new List<Rectangle>();
        bool firstState = true;
        
        public MainPage()
        {
            this.InitializeComponent();

            Brush sysColor = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
            Brush sysBColor = new SolidColorBrush((Color)Application.Current.Resources["SystemBaseHighColor"]);
            colorUpd.Interval = TimeSpan.FromSeconds(5);
            recColor.Add(denR1);
            recColor.Add(denR2);
            recColor.Add(denR3);
            recColor.Add(denR4);
            recColor.Add(denR5);
            colorUpd.Tick += (arg, e) => {
                sysColor = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
                sysColor.Opacity = 0.5;
                recColor.ForEach(x => { x.Fill = sysColor; });
            };
            colorUpd.Start();
        }

        private async void pickB_Click(object sender, RoutedEventArgs e)
        {
            firstState = true;
            FileOpenPicker filePick = new FileOpenPicker();
            filePick.FileTypeFilter.Add(".txt");
            filePick.FileTypeFilter.Add(".csv");
            filePick.SuggestedStartLocation = PickerLocationId.Desktop;
            path = await filePick.PickSingleFileAsync();
            try
            {
                pathTB.Text = path.Path;
                StorageApplicationPermissions.FutureAccessList.Add(path);
            }catch (Exception) { }
        }

        private async void startB_Click(object sender, RoutedEventArgs e)
        {
            startB.IsEnabled = false;
            if (firstState)
            {
                firstState = false;
                if (path != null)
                {
                    try
                    {
                        StorageApplicationPermissions.FutureAccessList.Add(path);
                        string allText = await FileIO.ReadTextAsync(path);
                        if (allText.Length > 0)
                        {
                            subjectList.Clear();
                            var LineTxt = allText.Split('\n');
                            foreach (var line in LineTxt)
                            {
                                if (!line.StartsWith("ID") && (line.Length > 5))
                                {
                                    var items = line.Split(";");
                                    bool capacity = false;
                                    int i, j, k = 0;
                                    int.TryParse(items[4], out k);
                                    string[] temp = items[6].Split("/");
                                    int.TryParse(temp[0], out i);
                                    int.TryParse(temp[1], out j);
                                    if (j == 0) j = 1;
                                    capacity = (i / j) < 1;
                                    subjectList.Add(new Subjects(items[0], items[1], items[5], items[3], items[2], k, capacity));
                                }
                            }
                            Debug.WriteLine(subjectList.Count);

                        }

                        subjectNames.Clear();
                        foreach (var subject in subjectList)
                        {
                            bool origin = true;
                            if(subjectNames.Count > 0)
                            {
                                foreach (var subjectName in subjectNames) {
                                    if (origin)
                                    {
                                        if (subjectName.Equals(subject.predmet))
                                            origin = false;
                                    }
                                    else { break;}
                                }
                            }
                            if(origin)
                                subjectNames.Add(subject.predmet);
                        }
                        subjectsLB.Items.Clear();
                        subjectNames.ForEach(subject => subjectsLB.Items.Add(subject));
                        if(subjectsLB.Items.Count > 0)
                        {
                            subjectsLB.Visibility = Visibility.Visible;
                            searchL.Visibility = Visibility.Visible;
                            searchTB.Visibility = Visibility.Visible;
                        }

                    }
                    catch (Exception ex) { Debug.WriteLine(ex); }
                }
            }
            else
            {
                if(selSubjects.Count > 0)
                {
                    List<Subjects> subjects = new List<Subjects>();
                    foreach(var sub in selSubjects)
                    {
                        subjectList.ForEach((item) => {
                        if (sub.Equals(item.predmet))
                            subjects.Add(item);
                        });
                    }
                    addToTT(subjects[0]);
                    timeGrid.Visibility = Visibility.Visible;
                }
            }
            startB.IsEnabled = true;
        }

        public void addToTT(Subjects subject)
        {
            string[] result = new string[3];
            string[] tResult = subject.cas.Split(" ");
            foreach (string s in tResult)
            {
                if (s.ToLower().Equals("po") || s.ToLower().Equals("út") || s.ToLower().Equals("st") || s.ToLower().Equals("čt") || s.ToLower().Equals("pá")) { result[0] = s; continue; }
                if ((result[1] != null) && (s.EndsWith("0"))) { result[2] = s; continue; }
                if (s.EndsWith("0")) { result[1] = s; continue; }
            }
            TimeSpan timeS;
            TimeSpan timeE;
            TimeSpan.TryParse(result[1], out timeS);
            TimeSpan.TryParse(result[2], out timeE);

            int i = 0; //sloupce
            int j = 0; //rádky
            int k = 0; //delka hodiny
            switch (result[0])
            {
                case "po":
                    j = 1;
                    break;
                case "út":
                    j = 2;
                    break;
                case "st":
                    j = 3;
                    break;
                case "čt":
                    j = 4;
                    break;
                case "pá":
                    j = 5;
                    break;
            }
            i = timeS.Hours - 8;
            k = (int)timeE.Subtract(timeS).TotalMinutes;
            k = (int) Math.Round((decimal) k/60);

            timeTable.Add(new timeT(i, j, timeS, timeE));
            Rectangle subR = new Rectangle();
            subR.Fill = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
            subR.SetValue(Grid.RowProperty,j + 1);
            subR.SetValue(Grid.ColumnProperty, i + 2);
            subR.SetValue(Grid.ColumnSpanProperty, k);
            timeGrid.Children.Add(subR);
        }

        private async void pathTB_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                path = await StorageFile.GetFileFromPathAsync(pathTB.Text);
            }catch (Exception) { path = null; }
        }

        int selectedItems = 0;
        private void subjectsLB_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            srolL.Visibility = Visibility.Visible;
            srolIt.Visibility = Visibility.Visible;
            selSubjects.Add(subjectsLB.SelectedItem.ToString());
            selItems.Text += "\n" + subjectsLB.SelectedItem.ToString();
            subjectsLB.Items.Remove(subjectsLB.SelectedItem);
        }

        private void searchTB_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if(searchTB.Text.Length > 0)
            {
                subjectTNames.Clear();
                foreach (var item in subjectNames)
                {
                    if (item.Contains(searchTB.Text))
                    {
                        subjectTNames.Add(item);
                    }
                }
                subjectsLB.Items.Clear();
                subjectTNames.ForEach(item => subjectsLB.Items.Add(item));
            }
        }
    }

    public class timeT
    {
        public int x = 0, y = 0;
        public TimeSpan timeS = TimeSpan.FromSeconds(0); public TimeSpan timeE = TimeSpan.FromSeconds(0);
        public timeT(int i, int j, TimeSpan timeS, TimeSpan timeE)
        {
            x = i;
            y = j;
            this.timeS = timeS;
            this.timeE = timeE;
        }
    }

    public class Subjects
    {
        public string ID, predmet, teaching, type;
        public string cas;
        public int parallel;
        public bool capacity;
        public Subjects(string iD, string predmet, string teaching, string type, string cas, int parallel, bool capacity)
        {
            ID = iD;
            this.predmet = predmet;
            this.teaching = teaching;
            this.type = type;
            this.cas = cas;
            this.parallel = parallel;
            this.capacity = capacity;
        }
    }
}
