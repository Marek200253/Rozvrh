using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Xml.Linq;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.WindowManagement;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
using System.Linq;
using System.Text.RegularExpressions;
using System.Transactions;
using System.IO;

// Dokumentaci k šabloně položky Prázdná stránka najdete na adrese https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x405

namespace Rozvrh
{
    public sealed partial class MainPage : Page
    {
        public static MainPage current;
        Options OP;
        AppWindow OPW;

        HashSet<string> subjectNames = new HashSet<string>();  //Pro TXT enditaci
        List<timeT> timeTable = new List<timeT>();
        List<Subjects> subjectList = new List<Subjects>();
        List<Subjects> addedS = new List<Subjects>();
        List<(Subjects sub1, Subjects sub2, int pokus)> kolizeList = new List<(Subjects sub1, Subjects sub2, int pokus)>();

        //Pro UI
        List<Rectangle> inTimeGridR = new List<Rectangle>();
        List<TextBlock> inTimeGridTB = new List<TextBlock>();
        List<string> selSubjects = new List<string>();
        DispatcherTimer colorUpd = new DispatcherTimer();
        List<Rectangle> recColor = new List<Rectangle>();

        StorageFile path;   //Cestak souboru
        bool firstState = true;

        //Options
        public bool[] days = { true, true, true, true, true};
        public bool pauzy = true;
        public int maxPause = 2; //maximální (pokud možná) prodleva mezi předměty
        public int method = 0;
        
        public MainPage()
        {
            this.InitializeComponent();

            (App.Current as App).UnhandledExceptionOccurred += App_UnhandledExceptionOccurred;
            Windows.UI.Core.Preview.SystemNavigationManagerPreview.GetForCurrentView().CloseRequested += MainPage_CloseRequested;
            current = this;
            OP = new Options();
            Brush sysColor = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
            colorUpd.Interval = TimeSpan.FromSeconds(2);
            recColor.Add(denR1);
            recColor.Add(denR2);
            recColor.Add(denR3);
            recColor.Add(denR4);
            recColor.Add(denR5);
            colorUpd.Tick += (arg, e) => {
                sysColor = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
                sysColor.Opacity = 0.5;
                recColor.ForEach(x => { x.Fill = sysColor; });
                refleshTT();
            };
            colorUpd.Start();
        }

        private async void pickB_Click(object sender, RoutedEventArgs e)
        {
            pickB.IsEnabled = false;
            startB.IsEnabled = false;
            firstState = true;
            FileOpenPicker filePick = new FileOpenPicker();
            filePick.FileTypeFilter.Add(".txt");
            filePick.FileTypeFilter.Add(".csv");
            filePick.SuggestedStartLocation = PickerLocationId.Desktop;
            path = await filePick.PickSingleFileAsync();
            if (path != null)
            {
                pathTB.Text = path.Path;
                StorageApplicationPermissions.FutureAccessList.Add(path);
            }
            pickB.IsEnabled = true;
            startB.IsEnabled = true;
        }

        private async void startB_Click(object sender, RoutedEventArgs e)
        {
            if (path == null)
                return;
            startB.IsEnabled = false;
            if (firstState) // První načtení ze souboru
            {
                if (path.ToString().Length > 7)
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
                                try
                                {
                                    if (!line.StartsWith("ID") && (line.Length > 5))
                                    {
                                        var items = line.Split(";");
                                        if (items[3].Length < 5)
                                            continue;
                                        bool capacity = false;
                                        int i = 0, j = 0, parallel = 0, credits = 0;
                                        int.TryParse(items[2], out credits);
                                        int.TryParse(items[5], out parallel);
                                        string[] temp = items[7].Split("/");
                                        int.TryParse(temp[0], out i);
                                        int.TryParse(temp[1], out j);
                                        if (j == 0) j = 1;
                                        capacity = (i / j) < 1;
                                        subjectList.Add(new Subjects(items[0], items[1], items[6], items[4], items[3], parallel, capacity, credits));
                                    }
                                }
                                catch (IndexOutOfRangeException) { }
                            }
                            Debug.WriteLine(subjectList.Count);

                        }
                        subjectNames.Clear();
                        subjectList.ForEach((item) => subjectNames.Add(item.predmet));
                        subjectsLB.Items.Clear();
                        foreach (string subT1 in subjectNames) subjectsLB.Items.Add(subT1);
                        if (subjectsLB.Items.Count > 0)
                        {
                            subjectsLB.Visibility = Visibility.Visible;
                            searchL.Visibility = Visibility.Visible;
                            searchTB.Visibility = Visibility.Visible;
                            firstState = false;
                            startB.IsEnabled = true;
                            startB.Content = "Sestavit rozvrh";
                            optionB.Visibility = Visibility.Visible;
                        }

                    }
                    catch (Exception ex) { Debug.WriteLine(ex); }
                }
            }
            else // Sekundární počítání možných cest
            {
                removeTT();
                if (selSubjects.Count > 0)
                {
                    switch (method)
                    {
                        default:
                            List<(Subjects subject, bool prednaska)>[] predmety = sortList(subjectList, selSubjects, 0);
                            int pokus = 0;
                            List<(string predmet, bool prednaska, bool obsazeno)> obsazeno = new List<(string predmet, bool prednaska, bool obsazeno)>();
                            List<(Subjects subject, int id)> pokusyList = new List<(Subjects subject, int id)>();
                            int maxTries = 30;
                            List<(string name, bool prednaska)>[] chybi = new List<(string name, bool prednaska)>[maxTries];
                            int[] allParsed = new int[predmety.Length];
                            int[] conflicts = new int[maxTries+1];
                            kolizeList.Clear();
                            addLog(predmety.Length.ToString(), "Počet předmětů");
                            while (pokus < maxTries) //První fáze
                            {
                                addLog(pokus.ToString(), "\nSpouštím pokus");
                                timeTable.Clear();
                                addedS.Clear();
                                obsazeno.Clear();
                                chybi[pokus] = new List<(string name, bool prednaska)>();
                                predmety = sortList(subjectList, selSubjects, pokus);
                                for (int i = 0; i < predmety.Length; i++)
                                {
                                    int obsazenoCount = obsazeno.Count;
                                    allParsed[i] = 0;
                                    bool haveP = false;
                                    bool haveC = false;
                                    predmety[i].Sort((s1, s2) => s2.prednaska.CompareTo(s1.prednaska));
                                    Subjects prednaskaS = null;
                                    foreach ((Subjects predmet, bool prednaska) in predmety[i])
                                    {
                                        
                                        if (prednaskaS != null)
                                        {
                                            if (prednaska)
                                                continue;
                                            if (!predmet.parallel.ToString().StartsWith(prednaskaS.parallel.ToString()))
                                                continue;
                                        }
                                        if (obsazeno.Contains((predmet.predmet, prednaska, true)))
                                            break;
                                        if (prednaska && !haveP)
                                        {
                                            allParsed[i]++;
                                            haveP = true;
                                        }
                                        if (!prednaska && !haveC)
                                        {
                                            allParsed[i]++;
                                            haveC = true;
                                        }
                                        if (tryToAdd(predmet, pokus))
                                        {
                                            if (prednaska)
                                                prednaskaS = predmet;
                                            addedS.Add(predmet);
                                            obsazeno.Add((predmet.predmet, prednaska, true));
                                            pokusyList.Add((predmet, pokus));
                                            addLog(predmet.toEString(), $"Přidán předmět (pokus:{pokus}):");
                                        }
                                    }
                                    int resum = obsazeno.Count - (obsazenoCount + allParsed[i]);
                                    addLog($"{obsazenoCount} + {allParsed[i]} ?= {obsazeno.Count} (Resum:{resum})", $"Count checker ({predmety[i][0].subject.predmet})");
                                    if (resum != 0)
                                    {
                                        addLog("Hledám chybějící část", "__Obsazeno counter");
                                        if ((haveP && prednaskaS != null) || (!haveP && prednaskaS == null))
                                        {
                                            addLog("Přidávám do chybějících cvičení", "Obsazeno counter");
                                            chybi[pokus].Add((predmety[i][0].subject.predmet, false));
                                            continue;
                                        }
                                        if(haveP && (resum == -1))
                                        {
                                            addLog("Přidávám do chybějících přednášku", "Obsazeno counter");
                                            chybi[pokus].Add((predmety[i][0].subject.predmet, true));
                                        }
                                        if(haveP && (resum == -2))
                                        {
                                            addLog("Přidávám do chybějících přednášku a cvičení", "Obsazeno counter");
                                            chybi[pokus].Add((predmety[i][0].subject.predmet, true));
                                            chybi[pokus].Add((predmety[i][0].subject.predmet, false));
                                        }
                                    }
                                }
                                int b = 0;
                                foreach (int a in allParsed)
                                    b += a;
                                if (b == obsazeno.Count)
                                {
                                    pokus = 30;
                                    addedS.ForEach(item => addToTT(convertToList(item)));
                                    timeGrid.Visibility = Visibility.Visible;
                                    continue;
                                }
                                conflicts[pokus] = b - obsazeno.Count;
                                pokus++;

                                ///Sekce pro vypočítání minimálních kolizí
                                if(pokus >= maxTries)
                                {
                                    addLog();
                                    addLog("Nepovedlo se vyčíslit rozvrh bez kolizí", "2.fáze");
                                    removeTT();
                                    timeTable.Clear();
                                    addedS.Clear();
                                    obsazeno.Clear();
                                    int pokusId = 0;
                                    int pokusMax = 100000;
                                    for(int i = 0; i < maxTries; i++)
                                    {
                                        if (conflicts[i] < pokusMax)
                                        {
                                            pokusMax = conflicts[i];
                                            pokusId = i;
                                        }
                                    }
                                    addLog($"{pokusId} (min. {conflicts[pokusId]})", "Prošel pokus č.");
                                    List<(Subjects predmet, int pokus)> pokusXList = pokusyList.FindAll(item => item.id == pokusId);
                                    List<(Subjects sub1, Subjects sub2, int pokus)> pokusKList = kolizeList.FindAll(x => x.pokus == pokusId);
                                    List<Subjects>[] kolList = { new List<Subjects>(), new List<Subjects>(), new List<Subjects>(), new List<Subjects>(), new List<Subjects>() };
                                    pokusKList.ForEach(item =>
                                    {
                                        kolList[item.sub1.time.den - 1].Add(item.sub1);
                                        kolList[item.sub2.time.den - 1].Add(item.sub2);
                                    });
                                    List<HashSet<Subjects>> finalList = new List<HashSet<Subjects>>();
                                    int a = -1;
                                    foreach (List<Subjects> subjects in kolList)
                                    {
                                        a++;
                                        finalList.Add(new HashSet<Subjects>());
                                        addLog("--------------Separátor---------------", "\nListReport");
                                        addLog("Koef A: " + a.ToString(), "ListReport");
                                        finalList.Add(new HashSet<Subjects>());
                                        subjects.Sort((s1, s2) => s1.time.timeS.CompareTo(s2.time.timeS));
                                        for (int i = 1; i < subjects.Count; i++)
                                        {
                                            addLog($"V kolListu nalezeno: {subjects[i].toEString()}", "ListReport");
                                            if (compareTimes(subjects[i].time, subjects[i - 1].time))
                                            {
                                                finalList[a].Add(subjects[i - 1]);
                                                finalList[a].Add(subjects[i]);
                                            }
                                            else
                                            {
                                                finalList.Add(new HashSet<Subjects>());
                                                a++;
                                            }
                                        }
                                    }
                                    finalList.Sort((s1, s2) => s1.Count.CompareTo(s2.Count));
                                    addLog(finalList.Count.ToString(), "Počet položek hashsetu");
                                    addLog(chybi[pokusId].Count.ToString(), "Počet chybících položek");
                                    try //SEKCE FinalBuid
                                    {
                                        HashSet<(string name, bool prednaska)> nalezeno = new HashSet<(string name, bool prednaska)>();
                                        foreach ((string name, bool prednaska) in chybi[pokusId])
                                        {
                                            addLog();
                                            addLog($" Hledám: {name}", "Chyby");
                                            bool skip = false;
                                            bool found = false;
                                            foreach ((string nameN, bool prednaskaN) in nalezeno)
                                            {
                                                if (name.Equals(nameN) && prednaskaN.Equals(prednaska))
                                                {
                                                    skip = true;
                                                    addLog($"Nalezeno v přidaných předmětech: {name}", "Chyby");
                                                }
                                            }
                                            if (skip)
                                                continue;
                                            HashSet<Subjects> founded = new HashSet<Subjects>();
                                            foreach (HashSet<Subjects> subjects in finalList)
                                            {
                                                if (subjects.Count > 0)
                                                {
                                                    foreach (Subjects sub in subjects)
                                                    {
                                                        if (sub.predmet.Equals(name) && (prednaska == sub.type.Contains("P")))
                                                        {
                                                            addLog("Nalezeno: " + sub.predmet, "FinalBuild (line 340)");
                                                            found = true;
                                                            break;
                                                        }
                                                    }
                                                    if (found)
                                                    {
                                                        addLog("Přidávám předměty do listu", "FinalBuild (line 347)");
                                                        founded = subjects;
                                                        List<Subjects> subjects1 = new List<Subjects>();
                                                        foreach (Subjects sub in subjects)
                                                        {
                                                            foreach ((string nameN, bool prednaskaN) in nalezeno)
                                                            {
                                                                if (sub.predmet.Equals(nameN) && prednaskaN.Equals(sub.type.Contains("P")))
                                                                    skip = true;
                                                            }
                                                            if (skip)
                                                                continue;
                                                            nalezeno.Add((sub.predmet, sub.type.Contains("P")));
                                                            subjects1.Add(sub);
                                                            addLog("Přidávám: " + sub.toEString(), "FinalBuild (line 360)");
                                                        }
                                                        addToTT(subjects1);
                                                    }

                                                }
                                                if (found)
                                                    break;
                                            }
                                            if (found)
                                                finalList.Remove(founded);

                                        }
                                    }catch(Exception ex) { addLog(ex.Message, "Error (FinalBuild - line 370)"); }
                                    pokusXList.ForEach(item => {
                                        if(tryToAdd(item.predmet, maxTries))
                                            addToTT(convertToList(item.predmet));
                                    });
                                    timeGrid.Visibility = Visibility.Visible;
                                }
                            }
                            break;
                    }
                }
            }
            startB.IsEnabled = true;
        }

        /// <summary>
        /// Přidává do rozvrhu předmět
        /// </summary>
        /// <param name="subject">Předmět</param>
        /// <param name="coef">Koeficient výšky (1 - plná, 0.5, poloviční atd.)</param>
        void addToTT(List<Subjects> subjects)
        {
            Subjects subject = subjects[0];
            bool conflict = false;
            if (subjects.Count > 1)
            {  
                List<Subjects> predmety = addConToTT(subjects);
                subject = predmety[0];
                conflict = true;
                tryToAdd(predmety[0], -1);
            }
            List<UIElement> list = getSubjectInUI(subject, conflict);

            Rectangle subR = (Rectangle)list[0];
            Rectangle subL = (Rectangle)list[1];
            Rectangle subC = (Rectangle)list[2];
            TextBlock popis = (TextBlock)list[3];
            TextBlock parCor = (TextBlock)list[4];

            timeT time = subject.time;
            int i = time.hodina + 2; //sloupce
            int j = time.den + 1; //řádky
            int k = time.delka; //délka hodiny
            try
            {
                subR.SetValue(Grid.RowProperty, j);
                subL.SetValue(Grid.RowProperty, j);
                subC.SetValue(Grid.RowProperty, j);
                subR.SetValue(Grid.ColumnProperty, i);
                subL.SetValue(Grid.ColumnProperty, i);
                subC.SetValue(Grid.ColumnProperty, i);
                subR.SetValue(Grid.ColumnSpanProperty, k);
                subL.SetValue(Grid.ColumnSpanProperty, k);
                subC.SetValue(Grid.ColumnSpanProperty, k);

                popis.SetValue(Grid.RowProperty, j);
                parCor.SetValue(Grid.RowProperty, j);
                popis.SetValue(Grid.ColumnProperty, i);
                parCor.SetValue(Grid.ColumnProperty, i);
                popis.SetValue(Grid.ColumnSpanProperty, k);
                parCor.SetValue(Grid.ColumnSpanProperty, k);
            }catch (ArgumentException ex) { addLog(ex.Message, "Error (AddToTT - GRID.PROPERTIES - line 428)");}
            try
            {
                parCor.Tapped += (arg, obj) => showInfo(subjects);
                subR.Tapped += (arg, obj) => showInfo(subjects);
                popis.Tapped += (arg, obj) => showInfo(subjects);
                timeGrid.Children.Add(subR);
                timeGrid.Children.Add(subL);
                timeGrid.Children.Add(popis);
                timeGrid.Children.Add(parCor);
                timeGrid.Children.Add(subC);
                inTimeGridR.Add(subR);
                inTimeGridR.Add(subL);
                inTimeGridTB.Add(popis);
                inTimeGridTB.Add(parCor);
                inTimeGridR.Add(subC);
            }catch(Exception ex) { addLog(ex.Message, "Error (AddToTT - line 443)"); }
        }

        /// <summary>
        /// Vytvátí 6 UI Elemetů, které se dají vložit do rozvrhu
        /// </summary>
        /// <param name="sub"></param>
        /// <param name="conflict"></param>
        /// <returns>List: 1.Hlavní Rectangle, 2.Lineání Rectangle, 3.Rohový Rectangle, 4.Popis, 5. Rohový popis, 6. Zvýraznění okolo rohů</returns>
        List<UIElement> getSubjectInUI(Subjects sub, bool conflict)
        {
            try
            {
                string name = "P";
                SolidColorBrush BaseColor = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
                SolidColorBrush highC = BaseColor;  //barva pro zvýraznění
                SolidColorBrush coloL = BaseColor;  //barva v pozadí
                SolidColorBrush coloC = BaseColor;  //barva v rohu
                if (conflict)
                {
                    BaseColor = new SolidColorBrush(Color.FromArgb(255, 255, 70, 70));
                    coloL = BaseColor;
                    name = "XXX";
                }
                if (((sub.type.Replace("*", "")[0].ToString() + sub.parallel).Length > 2) && !conflict)
                {
                    BaseColor = new SolidColorBrush(Color.FromArgb(255, (byte)(BaseColor.Color.R + 50), (byte)(BaseColor.Color.G + 50), (byte)(BaseColor.Color.B + 50)));
                    name = "C";
                }
                List<UIElement> list = new List<UIElement>();
                Rectangle subR = new Rectangle();
                Rectangle subL = new Rectangle();
                Rectangle subC = new Rectangle();
                Rectangle higR = new Rectangle(); // pro zvýraznění
                TextBlock popis = new TextBlock();
                TextBlock parCor = new TextBlock();
                ToolTip toolTip = new ToolTip();
                Run IDR = new Run();
                Run nameR = new Run();
                Run timeR = new Run();
                Hyperlink hyperlink = new Hyperlink();
                coloC = BaseColor;
                highC = new SolidColorBrush(Color.FromArgb((byte)highC.Color.A, (byte)(highC.Color.R + 30), (byte)(highC.Color.G + 100), (byte)(highC.Color.B + 100))); //barva zvýraznění
                coloL.Opacity = 0.6;
                coloC.Opacity = 0.8;

                //ToolTip
                toolTip.Content = sub.predmet;

                //Rectangles
                subR.Name = name + "OPACITY";
                subL.Name = name;
                subC.Name = name;
                higR.Name = name + "HIGH";

                subR.Fill = coloL;
                subL.Fill = BaseColor;
                subC.Fill = coloC;
                higR.Fill = highC;

                subR.Margin = new Thickness(5);
                subL.Margin = new Thickness(5);
                subC.Margin = new Thickness(5);

                subL.Width = 5;
                subC.Width = 27;
                subC.Height = 25;

                subL.HorizontalAlignment = HorizontalAlignment.Left;
                subC.HorizontalAlignment = HorizontalAlignment.Right;
                higR.HorizontalAlignment = HorizontalAlignment.Stretch;
                subC.VerticalAlignment = VerticalAlignment.Top;
                higR.VerticalAlignment = VerticalAlignment.Stretch;

                //TextBlocks
                parCor.Name = "PARALLEL";
                popis.TextWrapping = TextWrapping.Wrap;

                popis.VerticalAlignment = VerticalAlignment.Top;
                parCor.VerticalAlignment = VerticalAlignment.Top;
                popis.HorizontalAlignment = HorizontalAlignment.Left;
                parCor.HorizontalAlignment = HorizontalAlignment.Right;

                popis.Margin = new Thickness(15, 10, 15, 10);
                parCor.Margin = new Thickness(0, 7, 10, 0);

                popis.Foreground = new SolidColorBrush((Color)Application.Current.Resources["SystemBaseHighColor"]);
                parCor.Foreground = new SolidColorBrush(Colors.White);

                parCor.FontWeight = FontWeights.Bold;
                IDR.FontWeight = FontWeights.Bold;

                IDR.Text = sub.ID;
                if (!conflict)
                {
                    hyperlink.NavigateUri = new Uri("https://new.kos.cvut.cz/course-syllabus/" + sub.ID);
                }
                else
                {
                    IDR.FontWeight = FontWeights.Bold;
                }
                hyperlink.Inlines.Add(IDR);
                nameR.Text = "\n" + sub.predmet;
                timeR.Text = $"\n{sub.time.timeS.ToString(@"hh\:mm")}/{sub.time.timeE.ToString(@"hh\:mm")}";

                popis.Inlines.Add(hyperlink);
                popis.Inlines.Add(nameR);
                popis.Inlines.Add(timeR);
                parCor.Text = sub.type.Replace("*", "")[0].ToString() + sub.parallel;

                if (parCor.Text.Length > 2)
                    subC.Width = 45;

                ToolTipService.SetToolTip(popis, toolTip);
                ToolTipService.SetToolTip(subR, toolTip);
                ToolTipService.SetToolTip(parCor, toolTip);
                ToolTipService.SetToolTip(subC, toolTip);
                ToolTipService.SetToolTip(highC, toolTip);

                list.Add(subR);
                list.Add(subL);
                list.Add(subC);
                list.Add(popis);
                list.Add(parCor);
                list.Add(higR);

                return list;
            }catch (Exception ex) { addLog(ex.Message, "Error (getSubjectUI - line 570)"); }
            return null;
        }

        /// <summary>
        /// Přidává do rozvrhu konflikt předmětů dva na jednu buňku
        /// </summary>
        /// <param name="firstS"></param>
        /// <param name="secndS"></param>
        /// <returns>List původních předmětů, ale na prvním místě se nachází placeholder pro všechny předměty</returns>
        List<Subjects> addConToTT(List<Subjects> listT)
        {
            timeT confTime = new timeT();
            confTime.timeS = TimeSpan.FromHours(20);
            Subjects subject = new Subjects();
            List<Subjects> predmety = new List<Subjects>();
            foreach(Subjects item in listT)
            {
                subject.ID += "| " + item.ID + " |";
                timeT firstTime = item.time;
                if (firstTime.timeS.TotalMinutes < confTime.timeS.TotalMinutes)
                    confTime.timeS = firstTime.timeS;
                if(firstTime.timeE.TotalMinutes > confTime.timeE.TotalMinutes)
                    confTime.timeE = firstTime.timeE;
                confTime.den = firstTime.den;
            }
            string timeF = "";
            switch (confTime.den)
            {
                case 1:
                    timeF = "po";
                    break;
                case 2:
                    timeF = "út";
                    break;
                case 3:
                    timeF = "st";
                    break;
                case 4:
                    timeF = "čt";
                    break;
                case 5:
                    timeF = "pá";
                    break;
            }
            timeF += $" {confTime.timeS} - {confTime.timeE}";
            subject.predmet = "Konflikt (rozklikněte pro více informací)";
            subject.cas = timeF;
            subject.type = "XXXXXX";
            subject.parallel = 502;
            subject.capacity = true;
            subject.credits = 0;
            subject.teaching = "";
            subject.countTime();

            predmety.Add(subject);
            listT.ForEach(item => predmety.Add(item));

            return predmety;
        }

        /// <summary>
        /// Obnovuje barvy time tablu
        /// </summary>
        void refleshTT()
        {
            try { 
            foreach(Rectangle sub in inTimeGridR)
            {
                if (sub.Name.Contains("XXX"))
                    continue;

                timeGrid.Children.Remove(sub);
                SolidColorBrush colo = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
                if(sub.Name.Equals("C"))
                    colo = new SolidColorBrush(Color.FromArgb(255, (byte)(colo.Color.R + 50), (byte)(colo.Color.G + 50), (byte)(colo.Color.B + 50)));
                if (sub.Name.Contains("OPACITY"))
                    colo.Opacity = 0.6;
                sub.Fill = colo;
                timeGrid.Children.Add(sub);
            }
            foreach(TextBlock sub in inTimeGridTB)
            {
                if (sub.Name.Contains("XXX"))
                    continue;
                timeGrid.Children.Remove(sub);
                if (!sub.Name.Equals("PARALLEL"))
                    sub.Foreground = new SolidColorBrush((Color)Application.Current.Resources["SystemBaseHighColor"]);
                timeGrid.Children.Add(sub);
                }
            }
            catch (Exception ex) { addLog(ex.Message, "Error (refleshTT - line 661)"); }
        }

        /// <summary>
        /// Odstraňuje obsah time tablu
        /// </summary>
        public void removeTT()
        {
            try
            {
                foreach (Rectangle sub in inTimeGridR)
                    timeGrid.Children.Remove(sub);
                foreach (TextBlock sub in inTimeGridTB)
                    timeGrid.Children.Remove(sub);

                inTimeGridR.Clear();
                inTimeGridTB.Clear();
            } catch (Exception ex) { addLog(ex.Message, "Error (removeTT - -line 678)"); }
        }

        /// <summary>
        /// Metoda pro porovnávání a zjišťování konfliktů časů předmětů
        /// </summary>
        /// <param name="times"></param>
        /// <param name="time"></param>
        /// <returns>Vrací hodnotu true/false - pokud se jedná o konflikt</returns>
        public bool compareTimes(List<timeT> times, timeT time)
        {
            try
            {
                foreach (timeT tim in times)
                {
                    if (tim.den == time.den)
                    {
                        if (tim.timeS.TotalMinutes < time.timeE.TotalMinutes && time.timeS.TotalMinutes < tim.timeE.TotalMinutes)
                            return true;
                    }
                }
            }catch(NullReferenceException)
            {
                Debug.WriteLine($"{time.toString()} \n{times[0].toString()}", "CompateTimes");
            }
            return false;
        }

        /// <summary>
        /// Metoda pro porovnávání a zjišťování konfliktů časů předmětů
        /// </summary>
        /// <param name="times"></param>
        /// <param name="time"></param>
        /// <returns>Vrací hodnotu true/false - pokud se jedná o konflikt</returns>
        public bool compareTimes(timeT times, timeT time)
        {
            try
            {
                if(times.den == time.den)
                    if(times.timeS.TotalMinutes < time.timeE.TotalMinutes && time.timeS.TotalMinutes < times.timeE.TotalMinutes) 
                        return true;
            }
            catch (NullReferenceException)
            {
                Debug.WriteLine($"{time.toString()} \n{times.toString()}", "CompateTimes");
            }
            return false;
        }

        private async void pathTB_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                if (pathTB.Text.Length > 7)
                    path = await StorageFile.GetFileFromPathAsync(pathTB.Text);
            }catch (Exception) { path = null; }
        }

        private void subjectsLB_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            srolL.Visibility = Visibility.Visible;
            srolIt.Visibility = Visibility.Visible;

            selSubjects.Add(subjectsLB.SelectedItem.ToString());
            selItems.Text += "\n" + subjectsLB.SelectedItem.ToString();
            subjectsLB.Items.Remove(subjectsLB.SelectedItem);
            if (selSubjects.Count > 0)
            {
                deleteB.Visibility = Visibility.Visible;
                startB.IsEnabled = true;
            }
        }

        List<string> search = new List<string>();
        private void searchTB_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if(searchTB.Text.Length > 0)
            {
                search.Clear();
                foreach (var item in subjectNames)
                {
                    if (item.ToLower().Contains(searchTB.Text.ToLower()))
                        search.Add(item);
                }
                subjectsLB.Items.Clear();
                search.ForEach(item => subjectsLB.Items.Add(item));
            }
            else
            {
                foreach (string subT1 in subjectNames) subjectsLB.Items.Add(subT1);
            }
        }

        private void optionB_Click(object sender, RoutedEventArgs e) => openOP();

        private void deleteB_Click(object sender, RoutedEventArgs e)
        {
            subjectsLB.Items.Clear();
            selSubjects.Clear();
            selItems.Text = string.Empty;
            foreach (string subT1 in subjectNames) subjectsLB.Items.Add(subT1);
            startB.IsEnabled = false;
            deleteB.Visibility = Visibility.Collapsed;
        }

        private async void openOP()
        {
            try { 
            if(OPW is null)
                OPW = await AppWindow.TryCreateAsync();
            OPW.Title = "Nastavení";
            OPW.RequestSize(new Size(800, 300));
            Frame OPF = new Frame();
            OPF.Navigate(typeof(Options));
            Windows.UI.Xaml.Hosting.ElementCompositionPreview.SetAppWindowContent(OPW, OPF);
            OP.options = OPW;
            OPW.Closed += delegate
            {
                OPW = null;
            };
            await OPW.TryShowAsync();
            }
            catch (Exception ex) { addLog(ex.Message, "Error (openOP - line 800)"); }
        }

        public async void closeOptions() {
            await OPW.CloseAsync();
            OPW = null;
        }

        //InfoBox
        ComboBox subjCB = null;
        List<UIElement> elementyI = new List<UIElement>();
        private void showInfo(List<Subjects> subj)
        {
            int part = 1; //------------------------------------Part 1
            try
            {
                colorUpd.Stop();

                SolidColorBrush colo = new SolidColorBrush((Color)Application.Current.Resources["SystemAccentColor"]);
                SolidColorBrush coloBG = new SolidColorBrush(Colors.Black);
                if (subj[0].type.Contains("Cvi") && subj.Count == 1)
                    colo = new SolidColorBrush(Color.FromArgb(255, (byte)(colo.Color.R + 50), (byte)(colo.Color.G + 50), (byte)(colo.Color.B + 50)));
                coloBG.Opacity = 0.7;

                Grid balanceGrid = new Grid();
                Grid konflictG = new Grid();
                ScrollViewer scrollV = new ScrollViewer();
                Rectangle bgR = new Rectangle();
                Rectangle fgR = new Rectangle();
                Rectangle topR = new Rectangle();

                bgR.Fill = coloBG;
                fgR.Fill = new SolidColorBrush(Colors.White);
                topR.Fill = colo;

                if (Application.Current.RequestedTheme == ApplicationTheme.Dark)
                {
                    coloBG = new SolidColorBrush(Colors.White);
                    coloBG.Opacity = 0.5;
                    fgR.Fill = new SolidColorBrush(Colors.Black);
                    bgR.Fill = coloBG;
                }

                bgR.IsDoubleTapEnabled = false;
                bgR.Tapped += (arg, obj) => hideInfo();

                bgR.SetValue(Grid.RowProperty, 0);
                balanceGrid.SetValue(Grid.RowProperty, 0);
                bgR.SetValue(Grid.ColumnProperty, 0);
                balanceGrid.SetValue(Grid.ColumnProperty, 0);
                bgR.SetValue(Grid.RowSpanProperty, 2);
                balanceGrid.SetValue(Grid.RowSpanProperty, 2);
                bgR.SetValue(Grid.ColumnSpanProperty, 2);
                balanceGrid.SetValue(Grid.ColumnSpanProperty, 2);

                scrollV.HorizontalAlignment = HorizontalAlignment.Stretch;
                bgR.HorizontalAlignment = HorizontalAlignment.Stretch;
                balanceGrid.HorizontalAlignment = HorizontalAlignment.Center;
                fgR.HorizontalAlignment = HorizontalAlignment.Stretch;
                topR.HorizontalAlignment = HorizontalAlignment.Stretch;
                scrollV.VerticalAlignment = VerticalAlignment.Stretch;
                bgR.VerticalAlignment = VerticalAlignment.Stretch;
                balanceGrid.VerticalAlignment = VerticalAlignment.Center;
                fgR.VerticalAlignment = VerticalAlignment.Stretch;
                topR.VerticalAlignment = VerticalAlignment.Top;

                double heigth = MainGrid.ActualHeight * 0.6;
                double widht = MainGrid.ActualWidth * 0.6;
                balanceGrid.Height = heigth;
                balanceGrid.Width = widht;
                balanceGrid.RequestedTheme = ElementTheme.Default;

                scrollV.Margin = new Thickness(10, 70, 10, 10);
                fgR.IsDoubleTapEnabled = false;
                topR.Height = 50;
                scrollV.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                scrollV.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

                TextBlock title = new TextBlock();
                TextBlock popis = new TextBlock();

                title.Foreground = new SolidColorBrush(Colors.White);
                popis.Foreground = new SolidColorBrush((Color)Application.Current.Resources["SystemBaseHighColor"]);

                title.FontSize = 18;
                popis.FontSize = 14;

                title.HorizontalAlignment = HorizontalAlignment.Left;
                popis.HorizontalAlignment = HorizontalAlignment.Left;
                title.VerticalAlignment = VerticalAlignment.Top;
                popis.VerticalAlignment = VerticalAlignment.Top;

                title.FontWeight = FontWeights.Bold;
                title.Margin = new Thickness(10);

                popis.TextWrapping = TextWrapping.Wrap;
                popis.LineHeight = 30;
                popis.LineStackingStrategy = LineStackingStrategy.BaselineToBaseline;
                popis.Name = "popisTB";

                title.Text = $"{subj[0].ID} - {subj[0].predmet} - {subj[0].type.Replace("*", "")[0].ToString() + subj[0].parallel}";

                RowDefinition rowD1 = new RowDefinition();
                RowDefinition rowD2 = new RowDefinition();
                rowD1.Height = new GridLength(1, GridUnitType.Star);
                rowD2.Height = new GridLength(1, GridUnitType.Star);
                balanceGrid.RowDefinitions.Add(rowD1);
                balanceGrid.RowDefinitions.Add(rowD2);

                part++; //-------------------------------------------------PART 2

                //Konflikt viewer
                ScrollViewer konSV = new ScrollViewer();
                List<Rectangle> highRList = new List<Rectangle>();
                if (subj.Count > 1)
                {
                    title.Text = "Konflikt";
                    fgR.SetValue(Grid.RowSpanProperty, 2);

                    konSV.HorizontalAlignment = HorizontalAlignment.Stretch;
                    konflictG.HorizontalAlignment = HorizontalAlignment.Stretch;
                    konSV.VerticalAlignment = VerticalAlignment.Stretch;
                    konflictG.VerticalAlignment = VerticalAlignment.Stretch;

                    konSV.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    konSV.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

                    konSV.Margin = new Thickness(10);
                    konSV.SetValue(Grid.RowProperty, 1);

                    List<UIElement>[] predmety = new List<UIElement>[subj.Count];
                    int[] bunky = new int[13];
                    for (int i = 0; i < subj.Count; i++)
                    {
                        predmety[i] = getSubjectInUI(subj[i], false);
                        for (int j = 0; j < subj[i].time.delka; j++)
                        {
                            switch (subj[i].time.timeS.Hours + j)
                            {
                                case 8:
                                    bunky[0]++;
                                    break;
                                case 9:
                                    bunky[1]++;
                                    break;
                                case 10:
                                    bunky[2]++;
                                    break;
                                case 11:
                                    bunky[3]++;
                                    break;
                                case 12:
                                    bunky[4]++;
                                    break;
                                case 13:
                                    bunky[5]++;
                                    break;
                                case 14:
                                    bunky[6]++;
                                    break;
                                case 15:
                                    bunky[7]++;
                                    break;
                                case 16:
                                    bunky[8]++;
                                    break;
                                case 17:
                                    bunky[9]++;
                                    break;
                                case 18:
                                    bunky[10]++;
                                    break;
                                case 19:
                                    bunky[11]++;
                                    break;
                                default:
                                    bunky[13]++;
                                    break;
                            }
                        }
                    }
                    int space = 0;
                    ColumnDefinition column0 = new ColumnDefinition();
                    column0.Width = new GridLength(0.75, GridUnitType.Star);
                    konflictG.ColumnDefinitions.Add(column0);
                    for (int i = 0; i < 8; i++)
                    {
                        ColumnDefinition column = new ColumnDefinition();
                        column.Width = new GridLength(1, GridUnitType.Star);
                        konflictG.ColumnDefinitions.Add(column);
                    }
                    int firstC = 0;
                    int lastC = 0;
                    for (int i = 0; i < bunky.Length; i++)
                    {
                        if (bunky[i] != 0 && firstC == 0)
                            firstC = i;
                        if (firstC != 0 && bunky[i] == 0)
                        {
                            lastC = i;
                            break;
                        }
                    }
                    if (lastC - firstC < 8)
                    {
                        space = (int)Math.Abs(Math.Round((decimal)((8 - (lastC - firstC)) / 2), 0));
                        firstC -= space;
                    }

                    part++; //-------------------------------------------------PART 3

                    Array.Sort(bunky, (s1, s2) => s2.CompareTo(s1));
                    RowDefinition row0 = new RowDefinition();
                    row0.Height = new GridLength(0.5, GridUnitType.Star);
                    konflictG.RowDefinitions.Add(row0);
                    for (int i = 0; i < bunky[0]; i++)
                    {
                        RowDefinition row = new RowDefinition();
                        row.Height = new GridLength(1, GridUnitType.Star);
                        konflictG.RowDefinitions.Add(row);
                    }
                    for (int i = 1; i < konflictG.RowDefinitions.Count; i++)
                    {
                        Border border = new Border();
                        border.BorderThickness = new Thickness(1);
                        border.BorderBrush = new SolidColorBrush(Colors.Gray);
                        border.Opacity = 0.1;
                        border.SetValue(Grid.RowProperty, i);
                        border.SetValue(Grid.ColumnProperty, 1);
                        border.SetValue(Grid.ColumnSpanProperty, 8);
                        konflictG.Children.Add(border);
                    }
                    for (int i = 0; i < konflictG.ColumnDefinitions.Count; i++)
                    {
                        Border border = new Border();
                        border.BorderThickness = new Thickness(1);
                        border.BorderBrush = new SolidColorBrush(Colors.Gray);
                        border.Opacity = 0.1;
                        border.SetValue(Grid.ColumnProperty, i);
                        border.SetValue(Grid.RowSpanProperty, konflictG.RowDefinitions.Count);
                        konflictG.Children.Add(border);
                    }
                    for (int i = 0; i < 8; i++)
                    {
                        TimeSpan casTS1 = TimeSpan.FromHours(8 + firstC + i);
                        TimeSpan casTS2 = TimeSpan.FromHours(9 + firstC + i);
                        casTS2 -= TimeSpan.FromMinutes(10);
                        TextBlock casTB = new TextBlock();
                        casTB.Text = $"{casTS1.ToString(@"hh\:mm")} - {casTS2.ToString(@"hh\:mm")}";
                        casTB.Foreground = new SolidColorBrush((Color)Application.Current.Resources["SystemBaseHighColor"]);
                        casTB.HorizontalAlignment = HorizontalAlignment.Center;
                        casTB.VerticalAlignment = VerticalAlignment.Center;
                        casTB.SetValue(Grid.RowProperty, 0);
                        casTB.SetValue(Grid.ColumnProperty, i + 1);
                        konflictG.Children.Add(casTB);
                    }
                    TextBlock denTB = new TextBlock();
                    denTB.Text = $"{subj[0].cas[0].ToString().ToUpper()}{subj[0].cas[1]}";
                    denTB.FontSize = 14;
                    denTB.FontWeight = FontWeights.Bold;
                    denTB.Foreground = new SolidColorBrush(Colors.White);
                    denTB.HorizontalAlignment = HorizontalAlignment.Center;
                    denTB.VerticalAlignment = VerticalAlignment.Center;
                    denTB.SetValue(Grid.ColumnProperty, 0);
                    denTB.SetValue(Grid.RowProperty, 1);
                    denTB.SetValue(Grid.RowSpanProperty, konflictG.RowDefinitions.Count - 1);
                    Rectangle denR = new Rectangle();
                    SolidColorBrush BColor = colo;
                    BColor.Opacity = 0.8;
                    denR.Fill = BColor;
                    denR.Margin = new Thickness(4);
                    denR.HorizontalAlignment = HorizontalAlignment.Stretch;
                    denR.VerticalAlignment = VerticalAlignment.Stretch;
                    denR.SetValue(Grid.ColumnProperty, 0);
                    denR.SetValue(Grid.RowProperty, 1);
                    denR.SetValue(Grid.RowSpanProperty, konflictG.RowDefinitions.Count - 1);

                    Border border0 = new Border();
                    border0.BorderThickness = new Thickness(1);
                    border0.BorderBrush = new SolidColorBrush(Colors.Gray);
                    border0.Opacity = 0.4;
                    border0.SetValue(Grid.RowProperty, 0);
                    border0.SetValue(Grid.ColumnSpanProperty, 9);
                    konflictG.Children.Add(border0);

                    part++; //-------------------------------------------------PART 4

                    int a = 0;
                    int b = 0;
                    List<timeT>[] addedS = new List<timeT>[bunky[0]];
                    for (int i = 0; i < bunky[0]; i++)
                        addedS[i] = new List<timeT>();
                    foreach (List<UIElement> list in predmety)
                    {
                        Subjects sub = subj[a];
                        while (compareTimes(addedS[b], sub.time))
                        {
                            b++;
                            if (b >= bunky[0])
                                b = 0;
                        }
                        addedS[b].Add(sub.time);
                        int c = 0;
                        foreach (UIElement element in list)
                        {
                            try
                            {
                                element.Tapped += (obj, e) => selCB(sub);
                                element.SetValue(Grid.ColumnProperty, sub.time.hodina + 1 - firstC);
                                element.SetValue(Grid.ColumnSpanProperty, sub.time.delka);
                                element.SetValue(Grid.RowProperty, b + 1);
                                if (c == 5)
                                {
                                    highRList.Add((Rectangle)element);
                                    continue;
                                }
                                c++;
                                konflictG.Children.Add(element);
                            }
                            catch (Exception ex) { addLog(ex.Message, "Error (showInfo - line 1137)"); }
                        }
                        a++;
                    }

                    Border mainB = new Border();
                    mainB.BorderThickness = new Thickness(1);
                    mainB.BorderBrush = new SolidColorBrush(Colors.Black);
                    mainB.Opacity = 0.8;
                    mainB.SetValue(Grid.ColumnSpanProperty, konflictG.ColumnDefinitions.Count);
                    mainB.SetValue(Grid.RowSpanProperty, konflictG.RowDefinitions.Count);

                    konflictG.Children.Add(mainB);
                    konflictG.Children.Add(denR);
                    konflictG.Children.Add(denTB);

                    konSV.Content = konflictG;
                }

                List<Inline>[] popisy = new List<Inline>[subj.Count];
                for (int i = 0; i < subj.Count; i++)
                {
                    Run IDR = new Run();    //ID Run
                    Run IDP = new Run();    //ID Popis
                    Run ParR = new Run();   //Paralelka run
                    Run TypR = new Run();   //Typ run
                    Run jmenoR = new Run(); //Jmeno run
                    Run timeR = new Run();  //Čas run
                    Run teacherR = new Run();   //Vyučující run
                    Hyperlink hyperlink = new Hyperlink();

                    IDR.FontWeight = FontWeights.Bold;
                    IDR.Text = subj[i].ID;
                    IDP.Text = "ID: ";
                    ParR.Text = "\nParalelka: " + subj[i].type.Replace("*", "")[0].ToString() + subj[i].parallel;
                    TypR.Text = "\nTyp: " + subj[i].type.Replace("*", "");
                    jmenoR.Text = "\nPředmět: " + subj[i].predmet;
                    timeR.Text = "\nČas: " + subj[i].cas;
                    teacherR.Text = "\nVyučující: " + subj[i].teaching;
                    hyperlink.NavigateUri = new Uri("https://new.kos.cvut.cz/course-syllabus/" + subj[i].ID);
                    hyperlink.Inlines.Add(IDR);
                    popisy[i] = new List<Inline>
                {
                    IDP,
                    hyperlink,
                    ParR,
                    TypR,
                    jmenoR,
                    timeR,
                    teacherR
                };
                }

                part++; //-------------------------------------------------PART 5

                if (subj.Count > 1)
                {
                    subjCB = new ComboBox();
                    subjCB.Margin = new Thickness(10, 60, 10, 10);
                    subjCB.Width = balanceGrid.Width * 0.4;
                    subjCB.SetValue(Grid.RowProperty, 0);
                    subjCB.SetValue(Grid.ColumnProperty, 0);
                    subjCB.HorizontalAlignment = HorizontalAlignment.Left;
                    subjCB.VerticalAlignment = VerticalAlignment.Top;
                    subj.ForEach(item => subjCB.Items.Add(item.toString()));
                    int lastIndex = -1;
                    subjCB.SelectionChanged += (obj, arg) =>
                    {
                        try
                        {
                            List<Inline> popis1 = null;
                            if (subjCB.SelectedIndex >= 0)
                                popis1 = popisy[subjCB.SelectedIndex];
                            if (popis1 != null)
                            {
                                popis.Inlines.Clear();
                                foreach (Inline item in popis1)
                                    popis.Inlines.Add(item);
                            }
                            //Funkce pro zvýraznění vybraného předmětu
                            List<UIElement> konflictElements = new List<UIElement> { highRList[subjCB.SelectedIndex] };
                            foreach (UIElement el in konflictG.Children)
                            {
                                if (lastIndex != -1)
                                    if (el == highRList[lastIndex])
                                        continue;
                                konflictElements.Add(el);
                            }
                            konflictG.Children.Clear();
                            konflictElements.ForEach(item => konflictG.Children.Add(item));
                            lastIndex = subjCB.SelectedIndex;
                        }catch (Exception ex) { addLog(ex.Message, "Error (showInfo - subjCB.SelectionChanged - line 1228)"); }
                    };
                }
                else
                {
                    foreach (Inline inline in popisy[0])
                        popis.Inlines.Add(inline);
                }

                balanceGrid.Children.Add(fgR);
                balanceGrid.Children.Add(topR);
                balanceGrid.Children.Add(title);
                if (subjCB != null)
                {
                    subjCB.SelectedIndex = 0;
                    balanceGrid.Children.Add(subjCB);
                    scrollV.Margin = new Thickness(10, 100, 10, 10);
                }
                scrollV.Content = popis;
                balanceGrid.Children.Add(scrollV);
                balanceGrid.Children.Add(konSV);
                MainGrid.Children.Add(bgR);
                MainGrid.Children.Add(balanceGrid);
                elementyI.Add(bgR);
                elementyI.Add(balanceGrid);
            }catch (Exception ex) { addLog(ex.Message, $"Error (showInfo - line 1253) (parts: {part})"); }
        }

        private void hideInfo()
        {
            colorUpd.Start();
            foreach(var el in elementyI)
            {
                MainGrid.Children.Remove(el);
            }
            elementyI.Clear();
            subjCB = null;
        }

        private void selCB(Subjects s)
        {
            try
            {
                if (subjCB == null)
                    return;

                int index = subjCB.Items.IndexOf(s.toString());
                subjCB.SelectedIndex = index;
            }catch (Exception ex) { addLog(ex.Message, "Error (selCB - line 1255)"); }
        }

        List<Subjects> convertToList(Subjects subjects) => new List<Subjects>{ subjects };

        public List<(Subjects subject, bool prednaska)>[] sortList(List<Subjects> subjects, List<string> predmety, int posun) {
            try
            {
                List<string> predmetyP = predmety;
                List<string> predmetyL = new List<string>();
                List<(Subjects subject, bool predmaska)>[] list = new List<(Subjects subject, bool prednaska)>[predmety.Count];
                predmetyP.Sort();
                for (int i = 0; i < predmetyP.Count; i++)
                {
                    int a = posun + i;
                    while (a >= predmetyP.Count)
                        a -= predmetyP.Count;
                    predmetyL.Add(predmetyP[a]);
                }
                for (int i = 0; i < predmetyL.Count; i++)
                {
                    list[i] = new List<(Subjects subject, bool predmaska)>();
                    foreach (var subject in subjects)
                    {
                        if (subject.predmet.Equals(predmetyL[i]))
                            list[i].Add((subject, subject.type.ToLower().Contains("p")));
                    }
                }
                return list;
            }catch (Exception ex) { addLog(ex.Message, "Error (SortList - line 1284)"); }
            return null;
        }

        public bool tryToAdd(Subjects subject, int pokus)
        {
            try
            {
                List<Subjects> kolize = new List<Subjects>();
                bool success = true;
                foreach (timeT time in timeTable)
                {
                    if (subject.time.den == time.den)
                        if (subject.time.timeS.TotalMinutes < time.timeE.TotalMinutes && subject.time.timeE.TotalMinutes > time.timeS.TotalMinutes)
                        {
                            addedS.FindAll((x) => x.time.timeS.Equals(time.timeS) && x.time.timeE.Equals(time.timeE)).ForEach(item => kolize.Add(item));
                            success = false;
                        }
                }
                if (success)
                {
                    timeTable.Add(subject.time);
                }
                else if (pokus != -1)
                {
                    kolize.ForEach(item => kolizeList.Add((subject, item, pokus)));
                }
                return success;
            }catch(Exception ex) { addLog(ex.Message, "Error (TryAdd - line 1312)"); }
            return false;
        }

        //Záznam pro debug
        List<string> logToPrint = new List<string>();
        int errors = 0;
        public void addLog() => addLog("", "\n");
        public void addLog(string log, string category)
        {
            logToPrint.Add($"{category}: {log}");
            Debug.WriteLine(log, category);
            if (category.ToLower().Contains("error"))
                errors++;
        }

        private void App_UnhandledExceptionOccurred(object sender, Exception e)
        {
            string path = ApplicationData.Current.RoamingFolder.Path + @"\Logs";
            logToPrint.Add("Chyb v programu:" + errors.ToString());
            string[] lines = new string[logToPrint.Count];
            for (int i = 0; i < logToPrint.Count; i++)
                lines[i] = logToPrint[i];
            int j = 1;
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            while (File.Exists(path + $"\\CrashLog{j}.txt"))
                j++;
            File.WriteAllLines(path + $"\\CrashLog{j}.txt", lines);
        }

        private void MainPage_CloseRequested(object sender, Windows.UI.Core.Preview.SystemNavigationCloseRequestedPreviewEventArgs e)
        {
            saveLogs();
            App.Current.Exit();
        }

        public void saveLogs()
        {
            string path = ApplicationData.Current.RoamingFolder.Path + @"\Logs";
            logToPrint.Add("Chyb v programu:" + errors.ToString());
            string[] lines = new string[logToPrint.Count];
            for (int i = 0; i < logToPrint.Count; i++)
                lines[i] = logToPrint[i];
            int j = 1;
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            while (File.Exists(path + $"\\AppLog{j}.txt"))
                j++;
            File.WriteAllLines(path + $"\\AppLog{j}.txt", lines);
        }
    }

    public class timeT //chybí rozlišení týdnů (Lichý/Sudý)
    {
        public int hodina = 0, den = 0, delka = 0;
        public TimeSpan timeS = TimeSpan.FromSeconds(0), timeE = TimeSpan.FromSeconds(0);
        public bool lichyTyden = false;
        public timeT(int i, int j, int k, TimeSpan timeS, TimeSpan timeE, bool lichyTyden)
        {
            hodina = i;
            den = j;
            delka = k;
            this.timeS = timeS;
            this.timeE = timeE;
            this.lichyTyden = lichyTyden;
        }
        public timeT() {
            hodina = 0;
            den = 0;
            delka = 0;
            timeS = TimeSpan.Zero;
            timeE = TimeSpan.Zero;
        }

        public string toString() => $"{timeS} - {timeE}";
    }

    public class Subjects
    {
        public string ID, predmet, teaching, type;
        public string cas;
        public timeT time;
        public int parallel, credits;
        public bool capacity;
        public Subjects(string iD, string predmet, string teaching, string type, string cas, int parallel, bool capacity, int kredity)
        {
            ID = iD;
            this.predmet = predmet;
            this.teaching = teaching;
            this.type = type;
            this.cas = cas;
            this.parallel = parallel;
            this.capacity = capacity;
            credits = kredity;
            countTime();
        }

        public bool obsazeno;
        public Subjects(string NAME, string TYPE, bool Vacant)
        {
            this.predmet = NAME;
            this.type = TYPE;
            this.obsazeno = Vacant;
        }

        public Subjects()
        {
            ID = "";
            this.predmet = "";
            this.teaching = "";
            this.type = "";
            this.cas = "";
            this.parallel = 0;
            this.capacity = false;
            credits = 0;
            time = new timeT();
        }

        public int count = 0;
        public Subjects(string NAME, int count)
        {
            this.predmet = NAME;
            this.count = count;
        }

        /// <summary>
        /// Porovnává název a typ předmětu a vrací buďto schodu nebo null
        /// </summary>
        /// <param name="argument">Předmět k porovnání</param>
        /// <returns>True/False</returns>
        public bool compareSub(Subjects argument) => (this.predmet.Equals(argument.predmet) && this.type.Replace("*", "").Equals(argument.type));

        public string toString() => $"{this.predmet} - {this.type} {this.parallel}";
        public string toEString() => $"{this.predmet} - {this.type} {this.parallel} {this.cas}";

        public void countTime()
        {
            string[] result = new string[3];
            string[] tResult = this.cas.Split(" ");
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

            int i = 0;  //sloupce
            int j = 0;  //řádky
            int k = 0;  //delka
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
            if (i < 0) { i = 0; }
            k = (int)timeE.Subtract(timeS).TotalMinutes;
            k = (int)Math.Round((decimal)k / 60);
            time = new timeT(i, j, k, timeS, timeE, cas.ToLower().Contains("lich"));
        }
    }
}
