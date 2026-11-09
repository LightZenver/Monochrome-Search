using System;
using Monochrome.AppServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Monochrome.Pages
{
    /// <summary>
    /// Страница просмотра: один активный WebView, адресная строка,
    /// маршрут через Gateway (если включён) и обработка системного назад.
    /// </summary>
    public sealed partial class BrowsePage : Page
    {
        public enum Mode { Direct, Search, Url }

        public sealed class Request
        {
            public Mode Target { get; set; }
            public string Address { get; set; }
        }

        public static Request RequestFromQuery(string text)
        {
            return new Request { Target = Mode.Search, Address = text };
        }

        private bool _suppress;
        private string _currentUrl;
        private Request _arg;

        public BrowsePage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _arg = e.Parameter as Request;
            NavigateFromRequest();
        }

        private void NavigateFromRequest()
        {
            if (_arg == null) return;

            string url = null;
            if (_arg.Target == Mode.Search)
                url = Store.SearchUrl(_arg.Address);
            url = url ?? _arg.Address;

            Open(url, true);
        }

        private void Open(string url, bool addHistory)
        {
            _currentUrl = url;
            UpdateAddressBox(url);
            UpdateGatewayBadge();
            HideError();
            Progress.Visibility = Visibility.Visible;

            try
            {
                Web.Navigate(new Uri(Store.Route(url)));
                if (addHistory) Store.AddHistory(url);
            }
            catch (Exception ex)
            {
                ShowError("Адрес не разбран", ex.Message);
            }
        }

        private void UpdateAddressBox(string url)
        {
            var mapped = StripGateway(url);
            var display = (_arg != null && _arg.Target == Mode.Search && mapped == Store.SearchUrl(_arg.Address)) ? _arg.Address : mapped;
            AddressBox.Text = display;
        }

        private string StripGateway(string url)
        {
            if (Store.Gateway)
            {
                try
                {
                    var gw = Store.GatewayUrl.TrimEnd('/');
                    if (url != null && url.StartsWith(gw, StringComparison.OrdinalIgnoreCase))
                    {
                        var marker = "?orig=";
                        var at = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                        if (at >= 0)
                        {
                            var enc = url.Substring(at + marker.Length);
                            // до первого дополнительного параметра, если он есть
                            var amp = enc.IndexOf('&');
                            if (amp >= 0) enc = enc.Substring(0, amp);
                            return System.Net.WebUtility.UrlDecode(enc);
                        }
                    }
                }
                catch { }
            }
            return url ?? "";
        }

        private void UpdateGatewayBadge()
        {
            TxtGateway.Visibility = Store.Gateway ? Visibility.Visible : Visibility.Collapsed;
        }

        // --- WebView events ---

        private void OnNavStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            Progress.Visibility = Visibility.Visible;

            // Через Gateway все новые переходы идут тоже через прокси.
            if (Store.Gateway && ShouldRoute(args.Uri))
            {
                var original = args.Uri.ToString();
                args.Cancel = true;
                Window.Current.Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    () => Open(Store.Route(original), false));
                return;
            }
        }

        private static bool ShouldRoute(Uri uri)
        {
            if (uri == null) return false;
            if (uri.Scheme != "http" && uri.Scheme != "https") return false;
            var gw = Store.GatewayUrl?.TrimEnd('/') ?? "";
            if (gw.Length == 0) return false;
            var abs = uri.ToString();
            return !abs.StartsWith(gw + "/", StringComparison.OrdinalIgnoreCase)
                && !abs.StartsWith(gw + "/?", StringComparison.OrdinalIgnoreCase)
                && !abs.StartsWith(gw, StringComparison.OrdinalIgnoreCase);
        }

        private void OnContentLoading(WebView sender, WebViewContentLoadingEventArgs args)
        {
            var uri = args.Uri?.ToString();
            if (!string.IsNullOrEmpty(uri) && AddressBox.FocusState != FocusState.Keyboard)
                UpdateAddressBox(uri);
        }

        private void OnNavCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            Progress.Visibility = Visibility.Collapsed;
            if (args.IsSuccess) HideError();
        }

        private void OnNavFailed(WebView sender, WebViewNavigationFailedEventArgs args)
        {
            Progress.Visibility = Visibility.Collapsed;
            if (args.Uri == null) return;
            ShowError("Сайт не открывается", args.WebErrorStatus.ToString());
        }

        private void OnUnsupportedScheme(WebView sender, WebViewUnsupportedUriSchemeIdentifiedEventArgs args)
        {
            args.Handled = true;
        }

        // --- UI handlers ---

        private void OnAddressKeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                // Снятие фокуса с адресной строки без Blur(): перехват на скрытую кнопку.
                BtnHomeHost.Visibility = Visibility.Visible;
                BtnHomeHost.Focus(FocusState.Programmatic);
                BtnHomeHost.Visibility = Visibility.Collapsed;
                CommitAddress();
            }
        }

        private void OnAddressLostFocus(object sender, RoutedEventArgs e)
        {
            // Возврат текста к текущему URL, если пользователь ушёл с поля без ввода.
            if (!string.IsNullOrEmpty(_currentUrl) && string.IsNullOrWhiteSpace(AddressBox.Text))
                UpdateAddressBox(_currentUrl);
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            Progress.Visibility = Visibility.Visible;
            Web.Refresh();
        }

        private void OnWebBack(object sender, RoutedEventArgs e)
        {
            if (Web.CanGoBack) Web.GoBack();
            else Frame.GoBack();
        }

        private void OnHomeClick(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private void OnRetry(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentUrl))
                Open(_currentUrl, false);
            else
                Frame.GoBack();
        }

        private void CommitAddress()
        {
            var text = (AddressBox.Text ?? "").Trim();
            if (text.Length == 0) return;
            AddressBox.Text = string.Empty;
            Open(ResolveAddress(text), true);
        }

        private void ShowError(string title, string sub)
        {
            HostError.Visibility = Visibility.Visible;
            WebHost.Visibility = Visibility.Collapsed;
            ErrTitle.Text = title;
            ErrSub.Text = sub ?? "Сайт или соединение недоступны.";
        }

        private void HideError()
        {
            HostError.Visibility = Visibility.Collapsed;
            WebHost.Visibility = Visibility.Visible;
        }

        /// <summary>Назад: URL сайта, затем уход со страницы. Для аппаратной кнопки App.xaml - OnBackRequested.</summary>
        public bool HandleBackRequest()
        {
            if (Web.CanGoBack)
            {
                Web.GoBack();
                return true;
            }
            return false;
        }

        internal static string ResolveAddress(string text)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0) return "about:blank";
            if (t.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return t;

            var isUrl = t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                     || (t.IndexOf(' ') < 0 && t.IndexOf('.') >= 0 && t.IndexOf('/') >= 0 && Uri.TryCreate("https://" + t, UriKind.Absolute, out Uri _))
                     || (t.IndexOf(' ') < 0 && HostLike(t) && Uri.TryCreate("https://" + t, UriKind.Absolute, out Uri _));

            if (isUrl)
            {
                if (!t.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    t = "https://" + t;
                return t;
            }
            return Store.SearchUrl(t);
        }

        private static bool HostLike(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 253) return false;
            foreach (var c in s)
            {
                var isVAL = char.IsLetterOrDigit(c) || c == '-' || c == '.';
                if (!isVAL) return false;
            }
            return s.Length >= 4;
        }
    }
}
