using System;
using System.Linq;
using Monochrome.AppServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Core;

namespace Monochrome.Pages
{
    public sealed partial class StartPage : Page
    {
        private enum SheetMode { History, Bookmarks, Settings }

        private SheetMode _sheet;

        public sealed class ShortcutChip
        {
            public string Letter { get; set; }
            public string Title { get; set; }
            public string Url { get; set; }
        }

        public sealed class SheetRow
        {
            public string Title { get; set; }
            public string Sub { get; set; }
            public int Index { get; set; }
        }

        public StartPage()
        {
            InitializeComponent();
            Store.Load();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshShortcuts();
        }

        private void RefreshShortcuts()
        {
            Shortcuts.ItemsSource = Store.Shortcuts
                .Take(8)
                .Select(s => new ShortcutChip { Letter = FirstLetter(s.Title), Title = TrimTitle(s.Title), Url = s.Url })
                .ToList();
        }

        private static string FirstLetter(string s) =>
            string.IsNullOrEmpty(s) ? "?" : s.Substring(0, 1).ToUpperInvariant();

        private static string TrimTitle(string s) =>
            s == null ? "" : (s.Length > 10 ? s.Substring(0, 10) : s);

        // --- Поиск / навигация ---

        private void OnQueryKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                SubmitQuery();
            }
        }

        private void OnSearchGo(object sender, RoutedEventArgs e) => SubmitQuery();

        private void OnShortcutClick(object sender, RoutedEventArgs e)
        {
            var chip = ((FrameworkElement)sender).DataContext as ShortcutChip;
            if (chip?.Url != null)
                Go(BrowsePage.Mode.Direct, chip.Url);
        }

        public void SubmitQuery()
        {
            var text = HomeQuery?.Text?.Trim();
            HomeQuery?.Focus(FocusState.Programmatic);
            if (string.IsNullOrEmpty(text)) return;
            HomeQuery.Text = string.Empty;
            Frame.Navigate(typeof(BrowsePage), BrowsePage.RequestFromQuery(text));
        }

        private void Go(BrowsePage.Mode mode, string address)
            => Frame.Navigate(typeof(BrowsePage), new BrowsePage.Request { Target = mode, Address = address });

        // --- Боттом-лист ---

        private void OpenSheet(SheetMode mode)
        {
            _sheet = mode;
            SheetTitle.Text = mode == SheetMode.History ? "ИСТОРИЯ" :
                              mode == SheetMode.Bookmarks ? "ЗАКЛАДКИ" : "НАСТРОЙКИ";
            SheetClear.Visibility = mode == SheetMode.Settings ? Visibility.Collapsed : Visibility.Visible;

            if (mode == SheetMode.History)
                SheetList.ItemsSource = Store.History
                    .Select((u, i) => new SheetRow { Title = Store.DomainOf(u), Sub = u, Index = i })
                    .Take(50)
                    .ToList();
            else if (mode == SheetMode.Bookmarks)
                SheetList.ItemsSource = Store.Bookmarks
                    .Select((b, i) => new SheetRow { Title = b.Title, Sub = b.Url, Index = i })
                    .ToList();
            else
                SheetList.ItemsSource = EnumerateSettingsRows().ToList();

            SheetRoot.Visibility = Visibility.Visible;
        }

        private System.Collections.Generic.IEnumerable<SheetRow> EnumerateSettingsRows()
        {
            yield return new SheetRow { Title = "Поисковая система", Sub = EngineReadable(), Index = 0 };
            yield return new SheetRow { Title = "Шлюз Gateway", Sub = Store.Gateway ? "вкл — через прокси" : "выкл", Index = 1 };
            yield return new SheetRow { Title = "Сохранять историю", Sub = Store.SaveHistory ? "вкл" : "выкл", Index = 2 };
        }

        private static string EngineReadable()
        {
            switch (Store.Engine)
            {
                case "ddg": return "DuckDuckGo";
                case "bing": return "Bing";
                case "google": return "Google";
                case "wiki": return "Википедия";
                default: return "DuckDuckGo";
            }
        }

        private void CloseSheet() => SheetRoot.Visibility = Visibility.Collapsed;

        private void OnHistoryClick(object sender, RoutedEventArgs e) => OpenSheet(SheetMode.History);
        private void OnBookmarksClick(object sender, RoutedEventArgs e) => OpenSheet(SheetMode.Bookmarks);
        private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSheet(SheetMode.Settings);

        private void OnSheetBackdrop(object sender, RoutedEventArgs e) => CloseSheet();

        private void OnClearList(object sender, RoutedEventArgs e)
        {
            if (_sheet == SheetMode.History) { Store.History.Clear(); }
            else if (_sheet == SheetMode.Bookmarks) { Store.Bookmarks.Clear(); }
            Save();
            OpenSheet(_sheet);
        }

        private void OnRowClick(object sender, RoutedEventArgs e)
        {
            var row = ((FrameworkElement)sender).DataContext as SheetRow;
            if (row == null) return;

            if (_sheet == SheetMode.History)
                Go(BrowsePage.Mode.Direct, row.Sub);
            else if (_sheet == SheetMode.Bookmarks)
                Go(BrowsePage.Mode.Direct, row.Sub);
            else
                NextSetting(row.Index);
        }

        private void OnRowDelete(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as SheetRow
                      ?? (sender as FrameworkElement)?.Tag as SheetRow;
            if (row == null) return;

            if (_sheet == SheetMode.History && row.Index < Store.History.Count)
                Store.History.RemoveAt(row.Index);
            else if (_sheet == SheetMode.Bookmarks && row.Index < Store.Bookmarks.Count)
                Store.Bookmarks.RemoveAt(row.Index);
            Save();
            OpenSheet(_sheet);
        }

        private void NextSetting(int index)
        {
            switch (index)
            {
                case 0:
                    Store.Engine = Store.Engine == "ddg" ? "bing" :
                                   Store.Engine == "bing" ? "google" :
                                   Store.Engine == "google" ? "wiki" : "ddg";
                    break;
                case 1:
                    Store.Gateway = !Store.Gateway;
                    break;
                case 2:
                    Store.SaveHistory = !Store.SaveHistory;
                    break;
            }
            Save();
            OpenSheet(_sheet);
        }

        private void Save()
        {
            Store.Save();
        }
    }
}
