using System.Collections.ObjectModel;

namespace Shared_Code.Services
{
    public static class LogService
    {
        public static ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        public static event EventHandler LogAdded;

        public static void AddLog(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                string timestampedMessage = $"{DateTime.Now:HH:mm:ss}: {message}";
                LogMessages.Add(timestampedMessage);
                System.Diagnostics.Debug.WriteLine(timestampedMessage);
                LogAdded?.Invoke(null, EventArgs.Empty);
            });
        }

        public static void ClearLogs()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LogMessages.Clear();
                string timestampedMessage = $"{DateTime.Now:HH:mm:ss}: Logs cleared.";
                LogMessages.Add(timestampedMessage);
                System.Diagnostics.Debug.WriteLine(timestampedMessage);
                LogAdded?.Invoke(null, EventArgs.Empty);
            });
        }
    }
}