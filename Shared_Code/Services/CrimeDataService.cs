using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Shared_Code.Models;

namespace Shared_Code.Services
{
    public class CrimeDataService
    {
        private readonly HttpClient _httpClient;
        private const string BaseApiUrl = "https://data.police.uk/api";

        private NotificationGranularity _currentGranularity;
        private double _forceLevelRadiusKm = 1.6;

        private string _latestAvailableCrimeMonth;
        private DateTime _lastCheckedCrimeMonthDate;

        private string _currentForceId;
        private string _currentNeighbourhoodId;
        private List<BoundaryPoint> _currentNeighbourhoodBoundaryCache;
        private string _cachedBoundaryForNeighbourhoodId;

        private double _lastFetchedLatForStreet;
        private double _lastFetchedLngForStreet;
        private string _lastFetchedNeighbourhoodIdForData;
        private string _lastFetchedForceIdForData;
        private double _lastFetchedLatForForceRadius;
        private double _lastFetchedLngForForceRadius;

        private CrimeReport _lastCrimeReport;

        public CrimeDataService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _currentGranularity = NotificationGranularity.Neighbourhood;
        }

        private void LogDebug(string message)
        {
            string logMessage = $"[CrimeDataService DEBUG] {DateTime.Now:HH:mm:ss.fff}: {message}";
            System.Diagnostics.Debug.WriteLine(logMessage);
        }

        public void Configure(
            NotificationGranularity granularity,
            double? forceLevelRadiusKm = null
        )
        {
            _currentGranularity = granularity;
            if (forceLevelRadiusKm.HasValue && granularity == NotificationGranularity.Force)
            {
                _forceLevelRadiusKm = forceLevelRadiusKm.Value;
            }
            _lastCrimeReport = null;
            _lastFetchedForceIdForData = null;
            _lastFetchedNeighbourhoodIdForData = null;
        }

        private async Task<string> GetLatestAvailableCrimeMonthAsync(bool forceRefresh = false)
        {
            LogDebug("Enter GetLatestAvailableCrimeMonthAsync");
            if (
                !forceRefresh
                && !string.IsNullOrEmpty(_latestAvailableCrimeMonth)
                && (DateTime.UtcNow - _lastCheckedCrimeMonthDate).TotalHours < 24
            )
            {
                LogDebug("Returning cached crime month.");
                return _latestAvailableCrimeMonth;
            }

            try
            {
                string url = $"{BaseApiUrl}/crimes-street-dates";
                LogDebug($"Fetching street dates from: {url}");
                var response = await _httpClient.GetAsync(url);
                LogDebug($"Street dates response status: {response.StatusCode}");
                response.EnsureSuccessStatusCode();
                LogDebug("Street dates - reading JSON...");
                var availableDates = await response.Content.ReadFromJsonAsync<
                    List<AvailableDate>
                >();
                LogDebug("Street dates - JSON read.");

                if (availableDates != null && availableDates.Any())
                {
                    _latestAvailableCrimeMonth = availableDates
                        .OrderByDescending(d =>
                            DateTime.ParseExact(d.Date, "yyyy-MM", CultureInfo.InvariantCulture)
                        )
                        .FirstOrDefault()
                        ?.Date;
                    _lastCheckedCrimeMonthDate = DateTime.UtcNow;
                    LogDebug($"Latest crime month: {_latestAvailableCrimeMonth}");
                    return _latestAvailableCrimeMonth;
                }
                LogDebug("No available dates found or list was empty.");
            }
            catch (HttpRequestException ex)
            {
                LogDebug(
                    $"HttpRequestException in GetLatestAvailableCrimeMonthAsync: {ex.Message}. Status Code: {ex.StatusCode}"
                );
            }
            catch (Exception ex)
            {
                LogDebug($"Exception in GetLatestAvailableCrimeMonthAsync: {ex.ToString()}");
            }
            LogDebug("Exit GetLatestAvailableCrimeMonthAsync (possibly with old/null month)");
            return _latestAvailableCrimeMonth;
        }

        private async Task<(string ForceId, string NeighbourhoodId)?> GetForceAndNeighbourhoodAsync(
            double latitude,
            double longitude
        )
        {
            LogDebug("Enter GetForceAndNeighbourhoodAsync");
            try
            {
                string url = $"{BaseApiUrl}/locate-neighbourhood?q={latitude:F5},{longitude:F5}";
                LogDebug($"Fetching force/neighbourhood from: {url}");
                var response = await _httpClient.GetAsync(url);
                LogDebug($"Force/neighbourhood response status: {response.StatusCode}");
                response.EnsureSuccessStatusCode();
                LogDebug("Force/neighbourhood - reading JSON...");
                var locationInfo =
                    await response.Content.ReadFromJsonAsync<NeighbourhoodLocation>();
                LogDebug("Force/neighbourhood - JSON read.");

                if (locationInfo != null)
                {
                    LogDebug(
                        $"Found Force: {locationInfo.Force}, Neighbourhood: {locationInfo.Neighbourhood}"
                    );
                    return (locationInfo.Force, locationInfo.Neighbourhood);
                }
                LogDebug("Location info was null after parsing.");
            }
            catch (HttpRequestException ex)
            {
                LogDebug(
                    $"HttpRequestException in GetForceAndNeighbourhoodAsync: {ex.Message}. Status Code: {ex.StatusCode}"
                );
            }
            catch (Exception ex)
            {
                LogDebug($"Exception in GetForceAndNeighbourhoodAsync: {ex.ToString()}");
            }
            LogDebug("Exit GetForceAndNeighbourhoodAsync (possibly null)");
            return null;
        }

        private async Task<List<BoundaryPoint>> GetNeighbourhoodBoundaryAsync(
            string forceId,
            string neighbourhoodId,
            bool forceRefresh = false
        )
        {
            LogDebug("Enter GetNeighbourhoodBoundaryAsync");
            if (
                !forceRefresh
                && _currentNeighbourhoodBoundaryCache != null
                && _cachedBoundaryForNeighbourhoodId == neighbourhoodId
            )
            {
                LogDebug("Returning cached neighbourhood boundary.");
                return _currentNeighbourhoodBoundaryCache;
            }

            try
            {
                string url = $"{BaseApiUrl}/{forceId}/{neighbourhoodId}/boundary";
                LogDebug($"Fetching neighbourhood boundary from: {url}");
                var response = await _httpClient.GetAsync(url);
                LogDebug($"Neighbourhood boundary response status: {response.StatusCode}");
                response.EnsureSuccessStatusCode();
                LogDebug("Neighbourhood boundary - reading JSON...");
                var boundaryPoints = await response.Content.ReadFromJsonAsync<
                    List<BoundaryPoint>
                >();
                LogDebug("Neighbourhood boundary - JSON read.");

                if (boundaryPoints != null)
                {
                    _currentNeighbourhoodBoundaryCache = boundaryPoints;
                    _cachedBoundaryForNeighbourhoodId = neighbourhoodId;
                    LogDebug($"Fetched {boundaryPoints.Count} boundary points.");
                    return boundaryPoints;
                }
                LogDebug("Boundary points list was null after parsing.");
            }
            catch (HttpRequestException ex)
            {
                LogDebug(
                    $"HttpRequestException in GetNeighbourhoodBoundaryAsync: {ex.Message}. Status Code: {ex.StatusCode}"
                );
            }
            catch (Exception ex)
            {
                LogDebug($"Exception in GetNeighbourhoodBoundaryAsync: {ex.ToString()}");
            }
            _currentNeighbourhoodBoundaryCache = null;
            _cachedBoundaryForNeighbourhoodId = null;
            LogDebug("Exit GetNeighbourhoodBoundaryAsync (possibly null boundary)");
            return null;
        }

        private string FormatPolygon(List<BoundaryPoint> boundary)
        {
            if (boundary == null || !boundary.Any())
                return string.Empty;
            return string.Join(":", boundary.Select(p => $"{p.Latitude},{p.Longitude}"));
        }

        private bool HasMovedSignificantly(
            double previousLatitude,
            double previousLongitude,
            double currentLatitude,
            double currentLongitude,
            double thresholdMeters = 200
        )
        {
            const double earthRadiusMeters = 6371e3;

            double lat1Radians = previousLatitude * Math.PI / 180.0;
            double lat2Radians = currentLatitude * Math.PI / 180.0;
            double deltaLatitudeRadians = (currentLatitude - previousLatitude) * Math.PI / 180.0;
            double deltaLongitudeRadians = (currentLongitude - previousLongitude) * Math.PI / 180.0;

            double haversine_a =
                Math.Sin(deltaLatitudeRadians / 2.0) * Math.Sin(deltaLatitudeRadians / 2.0)
                + Math.Cos(lat1Radians)
                    * Math.Cos(lat2Radians)
                    * Math.Sin(deltaLongitudeRadians / 2.0)
                    * Math.Sin(deltaLongitudeRadians / 2.0);

            double haversine_c =
                2.0 * Math.Atan2(Math.Sqrt(haversine_a), Math.Sqrt(1.0 - haversine_a));

            double distanceInMeters = earthRadiusMeters * haversine_c;

            return distanceInMeters > thresholdMeters;
        }

        public async Task<CrimeReport> GetCrimeDataAsync(
            double currentLatitude,
            double currentLongitude,
            bool forceDataRefresh = false
        )
        {
            LogDebug("Enter GetCrimeDataAsync");
            bool needsDataFetch = forceDataRefresh;
            string newLatestCrimeMonth = await GetLatestAvailableCrimeMonthAsync(forceDataRefresh);
            LogDebug(
                $"Initial needsDataFetch: {needsDataFetch}, LatestCrimeMonth: {newLatestCrimeMonth}"
            );

            if (string.IsNullOrEmpty(newLatestCrimeMonth))
            {
                LogDebug(
                    "newLatestCrimeMonth is null or empty. Returning potentially cached/default report."
                );
                return _lastCrimeReport
                    ?? new CrimeReport
                    {
                        Summary = "Could not determine latest crime data period.",
                    };
            }

            if (
                _lastCrimeReport != null
                && newLatestCrimeMonth != _lastCrimeReport.DataMonth.ToString("yyyy-MM")
            )
            {
                needsDataFetch = true;
                LogDebug("Crime month updated. Setting needsDataFetch to true.");
            }

            var locationIds = await GetForceAndNeighbourhoodAsync(
                currentLatitude,
                currentLongitude
            );
            LogDebug(
                $"LocationIDs: Force={locationIds?.ForceId}, Neighbourhood={locationIds?.NeighbourhoodId}"
            );
            if (locationIds == null)
            {
                LogDebug("locationIds is null. Returning potentially cached/default report.");
                return _lastCrimeReport
                    ?? new CrimeReport
                    {
                        Summary = "Could not determine current police force/neighbourhood.",
                    };
            }
            string newForceId = locationIds.Value.ForceId;
            string newNeighbourhoodId = locationIds.Value.NeighbourhoodId;

            if (!needsDataFetch)
            {
                switch (_currentGranularity)
                {
                    case NotificationGranularity.Street:
                        if (
                            _lastCrimeReport == null
                            || newForceId != _lastFetchedForceIdForData
                            || HasMovedSignificantly(
                                currentLatitude,
                                currentLongitude,
                                _lastFetchedLatForStreet,
                                _lastFetchedLngForStreet
                            )
                        )
                        {
                            needsDataFetch = true;
                        }
                        break;
                    case NotificationGranularity.Neighbourhood:
                        if (
                            _lastCrimeReport == null
                            || newNeighbourhoodId != _lastFetchedNeighbourhoodIdForData
                        )
                        {
                            needsDataFetch = true;
                        }
                        break;
                    case NotificationGranularity.Force:
                        if (
                            _lastCrimeReport == null
                            || newForceId != _lastFetchedForceIdForData
                            || HasMovedSignificantly(
                                currentLatitude,
                                currentLongitude,
                                _lastFetchedLatForForceRadius,
                                _lastFetchedLngForForceRadius,
                                _forceLevelRadiusKm * 50
                            )
                        )
                        {
                            needsDataFetch = true;
                        }
                        break;
                }
                LogDebug(
                    $"After movement check. Granularity: {_currentGranularity}, needsDataFetch: {needsDataFetch}"
                );
            }

            if (!needsDataFetch && _lastCrimeReport != null)
            {
                LogDebug("Returning cached _lastCrimeReport as no data fetch needed.");
                return _lastCrimeReport;
            }

            _currentForceId = newForceId;
            _currentNeighbourhoodId = newNeighbourhoodId;
            var report = new CrimeReport
            {
                Granularity = _currentGranularity,
                DataMonth = DateTime.ParseExact(
                    newLatestCrimeMonth,
                    "yyyy-MM",
                    CultureInfo.InvariantCulture
                ),
            };
            LogDebug(
                $"Fetching new data for Granularity: {_currentGranularity}, Force: {_currentForceId}, Neighbourhood: {_currentNeighbourhoodId}, Month: {newLatestCrimeMonth}"
            );

            List<CrimeIncident> incidents = null;
            string requestUrlForLogging = "N/A";
            HttpResponseMessage response = null;

            try
            {
                string requestUrlSegment = "";
                string polyForPost = null;

                switch (_currentGranularity)
                {
                    case NotificationGranularity.Street:
                        requestUrlSegment =
                            $"{BaseApiUrl}/crimes-at-location?date={newLatestCrimeMonth}&lat={currentLatitude:F5}&lng={currentLongitude:F5}";
                        report.AreaIdentifier =
                            $"Street at {currentLatitude:F3}, {currentLongitude:F3}";
                        _lastFetchedLatForStreet = currentLatitude;
                        _lastFetchedLngForStreet = currentLongitude;
                        _lastFetchedForceIdForData = _currentForceId;
                        requestUrlForLogging = requestUrlSegment;
                        LogDebug($"Making HTTP GET request to: {requestUrlForLogging}");
                        response = await _httpClient.GetAsync(requestUrlForLogging);
                        break;

                    case NotificationGranularity.Neighbourhood:
                        var boundary = await GetNeighbourhoodBoundaryAsync(
                            _currentForceId,
                            _currentNeighbourhoodId,
                            newNeighbourhoodId != _cachedBoundaryForNeighbourhoodId
                        );
                        if (boundary != null && boundary.Any())
                        {
                            polyForPost = FormatPolygon(boundary);
                            requestUrlSegment = $"{BaseApiUrl}/crimes-street/all-crime";
                            report.AreaIdentifier =
                                $"Neighbourhood: {_currentNeighbourhoodId} (Force: {_currentForceId})";
                            _lastFetchedNeighbourhoodIdForData = _currentNeighbourhoodId;
                            _lastFetchedForceIdForData = _currentForceId;

                            LogDebug(
                                $"Built requestUrl for Neighbourhood (POST base): {requestUrlSegment}. Poly length: {polyForPost.Length}"
                            );
                            requestUrlForLogging = $"{requestUrlSegment} with poly data";

                            var parameters = new Dictionary<string, string>
                            {
                                { "date", newLatestCrimeMonth },
                                { "poly", polyForPost },
                            };
                            var content = new FormUrlEncodedContent(parameters);
                            LogDebug($"Making HTTP POST request to: {requestUrlSegment}");
                            response = await _httpClient.PostAsync(requestUrlSegment, content);
                        }
                        else
                        {
                            LogDebug(
                                "Neighbourhood boundary was null or empty. Cannot fetch crimes for neighbourhood."
                            );
                            report.Summary = "Could not fetch neighbourhood boundary.";
                            _lastCrimeReport = report;
                            return report;
                        }
                        break;

                    case NotificationGranularity.Force:
                        requestUrlSegment =
                            $"{BaseApiUrl}/crimes-street/all-crime?date={newLatestCrimeMonth}&lat={currentLatitude:F5}&lng={currentLongitude:F5}";
                        report.AreaIdentifier =
                            $"Force: {_currentForceId} (Radius around: {currentLatitude:F3}, {currentLongitude:F3})";
                        _lastFetchedForceIdForData = _currentForceId;
                        _lastFetchedLatForForceRadius = currentLatitude;
                        _lastFetchedLngForForceRadius = currentLongitude;
                        requestUrlForLogging = requestUrlSegment;
                        LogDebug($"Making HTTP GET request to: {requestUrlForLogging}");
                        response = await _httpClient.GetAsync(requestUrlForLogging);
                        break;
                }

                if (response != null)
                {
                    LogDebug(
                        $"HTTP response status: {response.StatusCode} from {requestUrlForLogging}"
                    );
                    if (!response.IsSuccessStatusCode)
                    {
                        LogDebug(
                            $"API Error Status: {response.StatusCode} for URL: {response.RequestMessage?.RequestUri}"
                        );
                    }
                    response.EnsureSuccessStatusCode();
                    LogDebug($"Reading JSON content from {requestUrlForLogging}...");
                    incidents = await response.Content.ReadFromJsonAsync<List<CrimeIncident>>();
                    LogDebug(
                        $"JSON content read from {requestUrlForLogging}. Incidents count: {incidents?.Count}"
                    );
                }
                else if (string.IsNullOrEmpty(requestUrlSegment) && polyForPost == null)
                {
                    LogDebug("Request URL or POST data was not prepared, skipping HTTP call.");
                }
            }
            catch (HttpRequestException ex)
            {
                LogDebug(
                    $"HttpRequestException in GetCrimeDataAsync for {requestUrlForLogging}: {ex.Message}. Status Code: {ex.StatusCode}"
                );
                report.Summary = $"API Error: {ex.Message.Split('.')[0]}.";
                if (ex.StatusCode.HasValue)
                {
                    report.Summary += $" (Status: {ex.StatusCode.Value})";
                }
            }
            catch (JsonException ex)
            {
                LogDebug(
                    $"JsonException in GetCrimeDataAsync for {requestUrlForLogging}: {ex.Message}"
                );
                report.Summary = "Error processing crime data.";
            }
            catch (Exception ex)
            {
                LogDebug(
                    $"Generic Exception of type {ex.GetType().FullName} in GetCrimeDataAsync for {requestUrlForLogging}: {ex.ToString()}"
                );
                report.Summary = $"An unexpected error ({ex.GetType().Name}) occurred.";
            }

            if (incidents != null)
            {
                report.Incidents = incidents;
                report.CrimeCount = incidents.Count;
                if (report.CrimeCount > 0)
                {
                    report.Summary =
                        $"{report.CrimeCount} crime(s) reported in {report.AreaIdentifier} for {report.DataMonth:MMMM yyyy}.";
                }
                else
                {
                    report.Summary =
                        $"No crimes reported in {report.AreaIdentifier} for {report.DataMonth:MMMM yyyy}.";
                }
            }
            else if (string.IsNullOrEmpty(report.Summary) || report.Summary == "No data available.")
            {
                report.Summary =
                    $"No crime data found for {report.AreaIdentifier} for {report.DataMonth:MMMM yyyy}.";
            }
            LogDebug(
                $"Finalizing report. Incidents found: {incidents != null}. Summary: {report.Summary}"
            );

            _lastCrimeReport = report;
            LogDebug("Exit GetCrimeDataAsync");
            return report;
        }
    }
}
