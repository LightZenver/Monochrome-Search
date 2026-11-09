using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Monochrome
{
    /// <summary>Точка входа Monochrome Search.</summary>
    public sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
                Window.Current.Content = rootFrame;

                SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;
            }

            if (rootFrame.Content == null)
            {
                rootFrame.Navigated += OnNavigated;
                rootFrame.Navigate(typeof(Pages.StartPage), e.Arguments);
            }

            UpdateBackVisibility(rootFrame);
            Window.Current.Activate();
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            UpdateBackVisibility((Frame)sender);
        }

        private static void UpdateBackVisibility(Frame frame)
        {
            var view = SystemNavigationManager.GetForCurrentView();
            view.AppViewBackButtonVisibility =
                frame.CanGoBack ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            var browse = frame?.Content as Pages.BrowsePage;

            // Страница просмотра: сначала история WebView, только затем — назад по фрейму.
            if (browse != null && browse.HandleBackRequest())
            {
                e.Handled = true;
                return;
            }

            if (frame != null && frame.CanGoBack)
            {
                frame.GoBack();
                e.Handled = true;
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            AppServices.Store.Save();
        }
    }
}
