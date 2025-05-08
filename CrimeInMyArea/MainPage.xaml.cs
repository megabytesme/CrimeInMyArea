using System.Collections.ObjectModel;
using Shared_Code.Models;
using Shared_Code.Services;

namespace CrimeInMyArea
{
    public partial class MainPage : ContentPage
    {
        private readonly CrimeDataService _crimeDataService;
        private readonly IGeolocation _geolocation;

        public ObservableCollection<string> LogMessages { get; } = new ObservableCollection<string>();

        public MainPage(CrimeDataService crimeDataService, IGeolocation geolocation)
        {
            InitializeComponent();
            _crimeDataService = crimeDataService;
            _geolocation = geolocation;

            LogCollectionView.ItemsSource = LogMessages;

            _crimeDataService.Configure(NotificationGranularity.Neighbourhood);
            AddLog("App initialized. Granularity: Neighbourhood.");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadCrimeDataAsync();
        }

        private async void OnRefreshButtonClicked(object sender, EventArgs e)
        {
            AddLog("Refresh button clicked.");
            await LoadCrimeDataAsync(forceRefresh: true);
        }

        private void AddLog(string message)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                string timestampedMessage = $"{DateTime.Now:HH:mm:ss}: {message}";
                LogMessages.Add(timestampedMessage);
                System.Diagnostics.Debug.WriteLine(timestampedMessage);

                if (LogMessages.Any())
                {
                    await Task.Delay(50);
                    await LogScrollView.ScrollToAsync(LogCollectionView, ScrollToPosition.End, true);

                }
            });
        }

        private async Task LoadCrimeDataAsync(bool forceRefresh = false)
        {
            CrimeSummaryLabel.Text = "Processing...";
            RefreshButton.IsEnabled = false;
            AddLog(forceRefresh ? "Starting data refresh (forced)..." : "Starting data refresh...");

            try
            {
                AddLog("Checking location permissions...");
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    AddLog("Location permission not granted. Requesting...");
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (status == PermissionStatus.Granted)
                {
                    AddLog("Location permission granted.");
                }
                else
                {
                    AddLog("Location permission denied.");
                    CrimeSummaryLabel.Text = "Location permission denied. Cannot fetch crime data.";
                    RefreshButton.IsEnabled = true;
                    return;
                }

                AddLog("Getting current location...");
                var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
                var location = await _geolocation.GetLocationAsync(request, CancellationToken.None);

                if (location != null)
                {
                    string locMsg = $"Location found: Lat={location.Latitude:F3}, Lon={location.Longitude:F3}.";
                    AddLog(locMsg);
                    CrimeSummaryLabel.Text = $"{locMsg} Fetching crimes...";

                    AddLog("Calling CrimeDataService...");
                    CrimeReport report = await _crimeDataService.GetCrimeDataAsync(location.Latitude, location.Longitude, forceRefresh);

                    if (report != null)
                    {
                        CrimeSummaryLabel.Text = report.Summary;
                        AddLog($"Service Response: {report.Summary}");
                        AddLog($"Fetched {report.CrimeCount} incidents for {report.AreaIdentifier} ({report.Granularity}) for month {report.DataMonth:yyyy-MM}");
                    }
                    else
                    {
                        string errorMsg = "Could not retrieve crime report from service.";
                        CrimeSummaryLabel.Text = errorMsg;
                        AddLog(errorMsg);
                    }
                }
                else
                {
                    string errorMsg = "Unable to get current location.";
                    CrimeSummaryLabel.Text = errorMsg;
                    AddLog(errorMsg);
                }
            }
            catch (FeatureNotSupportedException fnsEx)
            {
                string errorMsg = "Location not supported on this device.";
                CrimeSummaryLabel.Text = errorMsg;
                AddLog($"{errorMsg} Details: {fnsEx.Message}");
            }
            catch (PermissionException pEx)
            {
                string errorMsg = "Location permission error encountered.";
                CrimeSummaryLabel.Text = errorMsg;
                AddLog($"{errorMsg} Details: {pEx.Message}");
            }
            catch (Exception ex)
            {
                string errorMsg = $"An unexpected error occurred: {ex.Message.Split('.')[0]}.";
                CrimeSummaryLabel.Text = errorMsg;
                AddLog($"Error in LoadCrimeDataAsync: {ex.ToString()}");
            }
            finally
            {
                RefreshButton.IsEnabled = true;
                AddLog("Data refresh process completed.");
            }
        }
    }
}