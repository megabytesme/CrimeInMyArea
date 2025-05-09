using Shared_Code.Services;

namespace CrimeInMyArea
{
    public partial class LogPage : ContentPage
    {
        public LogPage()
        {
            InitializeComponent();
            LogCollectionViewLocal.ItemsSource = LogService.LogMessages;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LogService.LogAdded += HandleLogAdded;
            ScrollToLatestLog();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            LogService.LogAdded -= HandleLogAdded;
        }

        private void HandleLogAdded(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(50);
                ScrollToLatestLog();
            });
        }

        private async void ScrollToLatestLog()
        {
            if (LogService.LogMessages.Any())
            {
                if (LogScrollViewLocal.Content is VisualElement content && content.Height > LogScrollViewLocal.Height)
                {
                    await LogScrollViewLocal.ScrollToAsync(0, content.Height - LogScrollViewLocal.Height, true);
                }
            }
        }

        private void OnClearLogsClicked(object sender, EventArgs e)
        {
            LogService.ClearLogs();
        }
    }
}