using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shared_Code.Models;
using Shared_Code.Services;
using Syncfusion.Maui.Toolkit.Charts;

namespace CrimeInMyArea
{
    public partial class MainPage : ContentPage, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(
            ref T storage,
            T value,
            [CallerMemberName] string? propertyName = null
        )
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;
            storage = value;
            NotifyPropertyChanged(propertyName);
            return true;
        }

        private readonly CrimeDataService _crimeDataService;
        private readonly IGeolocation _geolocation;
        private readonly Random _random = new Random();

        private bool _isChartLoading;
        public bool IsChartLoading
        {
            get => _isChartLoading;
            set => SetProperty(ref _isChartLoading, value);
        }

        private ObservableCollection<CrimeCategoryCount> _crimeCategoryChartData;
        public ObservableCollection<CrimeCategoryCount> CrimeCategoryChartData
        {
            get => _crimeCategoryChartData;
            set => SetProperty(ref _crimeCategoryChartData, value);
        }

        private ObservableCollection<Brush> _crimeCategoryColors;
        public ObservableCollection<Brush> CrimeCategoryColors
        {
            get => _crimeCategoryColors;
            set => SetProperty(ref _crimeCategoryColors, value);
        }

        public CircularDataLabelSettings? RadialDataLabelSettings { get; }

        private string _overallSummaryText = "Fetching data...";
        public string OverallSummaryText
        {
            get => _overallSummaryText;
            set => SetProperty(ref _overallSummaryText, value);
        }
        private string _areaNameText = "Area: -";
        public string AreaNameText
        {
            get => _areaNameText;
            set => SetProperty(ref _areaNameText, value);
        }
        private string _dataMonthText = "Data for: -";
        public string DataMonthText
        {
            get => _dataMonthText;
            set => SetProperty(ref _dataMonthText, value);
        }
        private string _totalCrimesText = "Total Incidents: -";
        public string TotalCrimesText
        {
            get => _totalCrimesText;
            set => SetProperty(ref _totalCrimesText, value);
        }
        private bool _isCategoriesSectionVisible;
        public bool IsCategoriesSectionVisible
        {
            get => _isCategoriesSectionVisible;
            set => SetProperty(ref _isCategoriesSectionVisible, value);
        }
        private bool _isNoChartDataVisible;
        public bool IsNoChartDataVisible
        {
            get => _isNoChartDataVisible;
            set => SetProperty(ref _isNoChartDataVisible, value);
        }
        private bool _isNoCategoriesTextVisible;
        public bool IsNoCategoriesTextVisible
        {
            get => _isNoCategoriesTextVisible;
            set => SetProperty(ref _isNoCategoriesTextVisible, value);
        }
        private bool _isIncidentsSectionVisible;
        public bool IsIncidentsSectionVisible
        {
            get => _isIncidentsSectionVisible;
            set => SetProperty(ref _isIncidentsSectionVisible, value);
        }
        private bool _isNoIncidentsTextVisible;
        public bool IsNoIncidentsTextVisible
        {
            get => _isNoIncidentsTextVisible;
            set => SetProperty(ref _isNoIncidentsTextVisible, value);
        }

        public ObservableCollection<CrimeCategoryCount> CrimeCategories { get; } =
            new ObservableCollection<CrimeCategoryCount>();
        public ObservableCollection<CrimeIncidentDisplay> DisplayableIncidents { get; } =
            new ObservableCollection<CrimeIncidentDisplay>();

        public MainPage(CrimeDataService crimeDataService, IGeolocation geolocation)
        {
            InitializeComponent();
            _crimeDataService = crimeDataService;
            _geolocation = geolocation;

            _crimeCategoryChartData = new ObservableCollection<CrimeCategoryCount>();
            _crimeCategoryColors = new ObservableCollection<Brush>();
            RadialDataLabelSettings = new CircularDataLabelSettings()
            {
                LabelStyle = new ChartDataLabelStyle()
                {
                    TextColor =
                        Application.Current?.RequestedTheme == AppTheme.Dark
                            ? Colors.White
                            : Colors.Black,
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                },
            };

            _crimeDataService.Configure(NotificationGranularity.Neighbourhood);
            LogService.AddLog("App initialized. Granularity: Neighbourhood.");
            ResetUIDefaults("App Initialized. Waiting for data...");

            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadCrimeDataAsync();
        }

        private void ResetUIDefaults(string initialMessage = "Awaiting data...")
        {
            OverallSummaryText = initialMessage;
            AreaNameText = "Area: -";
            DataMonthText = "Data for: -";
            TotalCrimesText = "Total Incidents: -";
            CrimeCategories.Clear();
            CrimeCategoryChartData.Clear();
            CrimeCategoryColors.Clear();
            DisplayableIncidents.Clear();
            IsCategoriesSectionVisible = false;
            IsNoCategoriesTextVisible = false;
            IsNoChartDataVisible = true;
            IsIncidentsSectionVisible = false;
            IsNoIncidentsTextVisible = false;
            IsChartLoading = false;
        }

        private async void OnRefreshButtonClicked(object sender, EventArgs e)
        {
            LogService.AddLog("Refresh button clicked.");
            await LoadCrimeDataAsync(forceRefresh: true);
        }

        private async Task LoadCrimeDataAsync(bool forceRefresh = false)
        {
            IsChartLoading = true;
            RefreshButton.IsEnabled = false;
            LogService.AddLog(
                forceRefresh ? "Starting data refresh (forced)..." : "Starting data refresh..."
            );

            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }

                if (status != PermissionStatus.Granted)
                {
                    ResetUIDefaults("Location permission denied. Cannot fetch crime data.");
                    IsChartLoading = false;
                    RefreshButton.IsEnabled = true;
                    LogService.AddLog("Location permission denied.");
                    return;
                }
                LogService.AddLog("Location permission granted.");

                var request = new GeolocationRequest(
                    GeolocationAccuracy.Medium,
                    TimeSpan.FromSeconds(10)
                );
                var location = await _geolocation.GetLocationAsync(request, CancellationToken.None);

                if (location != null)
                {
                    OverallSummaryText =
                        $"Location: Lat={location.Latitude:F3}, Lon={location.Longitude:F3}. Fetching crimes...";
                    LogService.AddLog(OverallSummaryText);
                    CrimeReport report = await _crimeDataService.GetCrimeDataAsync(
                        location.Latitude,
                        location.Longitude,
                        forceRefresh
                    );

                    if (report != null)
                    {
                        LogService.AddLog($"Service Response: {report.Summary}");
                        LogService.AddLog(
                            $"Fetched {report.CrimeCount} incidents for {report.AreaIdentifier} ({report.Granularity}) for month {report.DataMonth:yyyy-MM}"
                        );
                        OverallSummaryText = report.Summary ?? "Crime data received.";
                        AreaNameText = $"Area: {report.AreaIdentifier ?? "N/A"}";
                        DataMonthText = $"Data for: {report.DataMonth:MMMM yyyy}";
                        TotalCrimesText = $"Total Incidents: {report.CrimeCount}";

                        if (report.Incidents != null && report.Incidents.Any())
                        {
                            var categoryGroups = report
                                .Incidents.GroupBy(i =>
                                    i.Category?.Replace('-', ' ')?.Trim() ?? "Unknown Category"
                                )
                                .Select(g => new CrimeCategoryCount
                                {
                                    CategoryName = g.Key,
                                    Count = g.Count(),
                                })
                                .OrderByDescending(c => c.Count)
                                .ToList();

                            CrimeCategories.Clear();
                            categoryGroups.ForEach(CrimeCategories.Add);
                            CrimeCategoryChartData.Clear();
                            CrimeCategoryColors.Clear();

                            var topCategoriesForChart = categoryGroups.Take(5).ToList();
                            if (categoryGroups.Count > 5)
                            {
                                var otherCount = categoryGroups.Skip(5).Sum(g => g.Count);
                                if (otherCount > 0)
                                    topCategoriesForChart.Add(
                                        new CrimeCategoryCount
                                        {
                                            CategoryName = "Others",
                                            Count = otherCount,
                                        }
                                    );
                            }
                            foreach (var group in topCategoriesForChart)
                            {
                                CrimeCategoryChartData.Add(group);
                                CrimeCategoryColors.Add(new SolidColorBrush(GetRandomMauiColor()));
                            }
                            IsCategoriesSectionVisible = true;
                            IsNoCategoriesTextVisible = !categoryGroups.Any();
                            IsNoChartDataVisible = !CrimeCategoryChartData.Any();

                            var incidentsToShow = report
                                .Incidents.Take(10)
                                .Select(i => new CrimeIncidentDisplay
                                {
                                    Category = i.Category?.Replace('-', ' ')?.Trim() ?? "N/A",
                                    StreetInfo = $"{i.Location?.Street?.Name ?? "Location N/A"}",
                                    OutcomeInfo =
                                        $"{i.OutcomeStatus?.Category ?? "Outcome Pending"}",
                                    Month = i.Month ?? "N/A",
                                })
                                .ToList();
                            DisplayableIncidents.Clear();
                            incidentsToShow.ForEach(DisplayableIncidents.Add);
                            IsIncidentsSectionVisible = true;
                            IsNoIncidentsTextVisible = !incidentsToShow.Any();
                        }
                        else
                        {
                            ResetUIDefaults(report.Summary ?? "No incidents reported.");
                            IsCategoriesSectionVisible = report.CrimeCount > 0;
                            IsNoCategoriesTextVisible = report.CrimeCount > 0;
                            IsNoChartDataVisible = true;
                            IsIncidentsSectionVisible = report.CrimeCount > 0;
                            IsNoIncidentsTextVisible = report.CrimeCount > 0;
                            if (report.CrimeCount == 0)
                                LogService.AddLog("No incidents reported.");
                            else
                                LogService.AddLog("Incident details missing.");
                        }
                    }
                    else
                    {
                        ResetUIDefaults("Could not retrieve crime report.");
                        LogService.AddLog("CrimeDataService returned null.");
                    }
                }
                else
                {
                    ResetUIDefaults("Unable to get current location.");
                    LogService.AddLog("GetLocationAsync returned null.");
                }
            }
            catch (Exception ex)
            {
                ResetUIDefaults($"An error occurred: {ex.Message.Split('.')[0]}.");
                LogService.AddLog($"Error in LoadCrimeDataAsync: {ex.ToString()}");
            }
            finally
            {
                IsChartLoading = false;
                RefreshButton.IsEnabled = true;
                LogService.AddLog("Data refresh process completed.");
            }
        }

        private Color GetRandomMauiColor() =>
            Color.FromRgb(
                (byte)_random.Next(50, 220),
                (byte)_random.Next(50, 220),
                (byte)_random.Next(50, 220)
            );
    }
}
