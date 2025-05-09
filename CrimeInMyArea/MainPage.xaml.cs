using System.Collections.ObjectModel;
using Shared_Code.Models;
using Shared_Code.Services;

namespace CrimeInMyArea
{
    public class CrimeCategoryCount
    {
        public string CategoryName { get; set; }
        public int Count { get; set; }
        public string DisplayText => $"{CategoryName}: {Count}";
    }

    public class CrimeIncidentDisplay
    {
        public string Category { get; set; }
        public string StreetInfo { get; set; }
        public string OutcomeInfo { get; set; }
        public string Month { get; set; }
        public string FullDescription => $"{Category}\nStreet: {StreetInfo}\nOutcome: {OutcomeInfo} (Month: {Month})";
    }

    public partial class MainPage : ContentPage
    {
        private readonly CrimeDataService _crimeDataService;
        private readonly IGeolocation _geolocation;

        public ObservableCollection<CrimeCategoryCount> CrimeCategories { get; } = new ObservableCollection<CrimeCategoryCount>();
        public ObservableCollection<CrimeIncidentDisplay> DisplayableIncidents { get; } = new ObservableCollection<CrimeIncidentDisplay>();

        public MainPage(CrimeDataService crimeDataService, IGeolocation geolocation)
        {
            InitializeComponent();
            _crimeDataService = crimeDataService;
            _geolocation = geolocation;

            CrimeCategoriesCollectionView.ItemsSource = CrimeCategories;
            RecentIncidentsCollectionView.ItemsSource = DisplayableIncidents;

            _crimeDataService.Configure(NotificationGranularity.Neighbourhood);
            LogService.AddLog("App initialized. Granularity: Neighbourhood.");
            ResetUIDefaults("App Initialized. Waiting for data...");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadCrimeDataAsync();
        }
        
        private void ResetUIDefaults(string initialMessage = "Awaiting data...")
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                OverallSummaryLabel.Text = initialMessage;
                AreaNameLabel.Text = "Area: -";
                DataMonthLabel.Text = "Data for: -";
                TotalCrimesLabel.Text = "Total Incidents: -";

                CrimeCategories.Clear();
                DisplayableIncidents.Clear();

                CategoriesSectionFrame.IsVisible = false;
                CrimeCategoriesCollectionView.IsVisible = false;
                NoCategoriesLabel.IsVisible = false;

                IncidentsSectionFrame.IsVisible = false;
                RecentIncidentsCollectionView.IsVisible = false;
                NoIncidentsLabel.IsVisible = false;

                LoadingIndicator.IsRunning = false;
                LoadingIndicator.IsVisible = false;
            });
        }

        private async void OnRefreshButtonClicked(object sender, EventArgs e)
        {
            LogService.AddLog("Refresh button clicked.");
            await LoadCrimeDataAsync(forceRefresh: true);
        }

        private async Task LoadCrimeDataAsync(bool forceRefresh = false)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RefreshButton.IsEnabled = false;
                LoadingIndicator.IsRunning = true;
                LoadingIndicator.IsVisible = true;
                OverallSummaryLabel.Text = "Processing...";
                CrimeCategories.Clear();
                DisplayableIncidents.Clear();
                CategoriesSectionFrame.IsVisible = false;
                IncidentsSectionFrame.IsVisible = false;
            });

            LogService.AddLog(forceRefresh ? "Starting data refresh (forced)..." : "Starting data refresh...");

            try
            {
                LogService.AddLog("Checking location permissions...");
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    LogService.AddLog("Location permission not granted. Requesting...");
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (status == PermissionStatus.Granted) LogService.AddLog("Location permission granted.");
                else
                {
                    LogService.AddLog("Location permission denied.");
                    ResetUIDefaults("Location permission denied. Cannot fetch crime data.");
                    return;
                }

                LogService.AddLog("Getting current location...");
                var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
                var location = await _geolocation.GetLocationAsync(request, CancellationToken.None);

                if (location != null)
                {
                    string locMsg = $"Location: Lat={location.Latitude:F3}, Lon={location.Longitude:F3}.";
                    LogService.AddLog(locMsg);
                    MainThread.BeginInvokeOnMainThread(() => OverallSummaryLabel.Text = $"{locMsg} Fetching crimes...");

                    LogService.AddLog("Calling CrimeDataService...");
                    CrimeReport report = await _crimeDataService.GetCrimeDataAsync(location.Latitude, location.Longitude, forceRefresh);

                    if (report != null)
                    {
                        LogService.AddLog($"Service Response: {report.Summary}");
                        LogService.AddLog($"Fetched {report.CrimeCount} incidents for {report.AreaIdentifier} ({report.Granularity}) for month {report.DataMonth:yyyy-MM}");
                        
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            OverallSummaryLabel.Text = report.Summary ?? "Crime data received.";
                            AreaNameLabel.Text = $"Area: {report.AreaIdentifier ?? "N/A"}";
                            DataMonthLabel.Text = $"Data for: {report.DataMonth:MMMM yyyy}";
                            TotalCrimesLabel.Text = $"Total Incidents: {report.CrimeCount}";

                            if (report.Incidents != null && report.Incidents.Any())
                            {
                                var categoryGroups = report.Incidents
                                    .GroupBy(i => i.Category?.Replace('-', ' ')?.Trim() ?? "Unknown Category")
                                    .Select(g => new CrimeCategoryCount { CategoryName = g.Key, Count = g.Count() })
                                    .OrderByDescending(c => c.Count)
                                    .ToList();
                                
                                CrimeCategories.Clear();
                                foreach (var cat in categoryGroups) CrimeCategories.Add(cat);
                                
                                CategoriesSectionFrame.IsVisible = true;
                                CrimeCategoriesCollectionView.IsVisible = categoryGroups.Any();
                                NoCategoriesLabel.IsVisible = !categoryGroups.Any();

                                var incidentsToShow = report.Incidents.Take(10).Select(i => new CrimeIncidentDisplay
                                {
                                    Category = i.Category?.Replace('-', ' ')?.Trim() ?? "N/A",
                                    StreetInfo = $"{i.Location?.Street?.Name ?? "Location N/A"}",
                                    OutcomeInfo = $"{i.OutcomeStatus?.Category ?? "Outcome Pending"}",
                                    Month = i.Month ?? "N/A"
                                }).ToList();

                                DisplayableIncidents.Clear();
                                foreach (var inc in incidentsToShow) DisplayableIncidents.Add(inc);

                                IncidentsSectionFrame.IsVisible = true;
                                RecentIncidentsCollectionView.IsVisible = incidentsToShow.Any();
                                NoIncidentsLabel.IsVisible = !incidentsToShow.Any();
                            }
                            else
                            {
                                CategoriesSectionFrame.IsVisible = report.CrimeCount > 0;
                                NoCategoriesLabel.IsVisible = report.CrimeCount > 0;
                                CrimeCategoriesCollectionView.IsVisible = false;

                                IncidentsSectionFrame.IsVisible = report.CrimeCount > 0;
                                NoIncidentsLabel.IsVisible = report.CrimeCount > 0;
                                RecentIncidentsCollectionView.IsVisible = false;

                                if(report.CrimeCount == 0) LogService.AddLog("No incidents reported for this period/area.");
                                else LogService.AddLog("Crime count reported, but incident details are missing.");
                            }
                        });
                    }
                    else
                    {
                        ResetUIDefaults("Could not retrieve crime report from service.");
                        LogService.AddLog("CrimeDataService returned null report.");
                    }
                }
                else
                {
                    ResetUIDefaults("Unable to get current location.");
                    LogService.AddLog("Geolocation.GetLocationAsync returned null.");
                }
            }
            catch (FeatureNotSupportedException fnsEx)
            {
                ResetUIDefaults("Location not supported on this device.");
                LogService.AddLog($"FeatureNotSupportedException: {fnsEx.Message}");
            }
            catch (PermissionException pEx)
            {
                ResetUIDefaults("Location permission error.");
                LogService.AddLog($"PermissionException: {pEx.Message}");
            }
            catch (Exception ex)
            {
                ResetUIDefaults($"An unexpected error occurred: {ex.Message.Split('.')[0]}.");
                LogService.AddLog($"Error in LoadCrimeDataAsync: {ex.ToString()}");
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RefreshButton.IsEnabled = true;
                    LoadingIndicator.IsRunning = false;
                    LoadingIndicator.IsVisible = false;
                });
                LogService.AddLog("Data refresh process completed.");
            }
        }
    }
}