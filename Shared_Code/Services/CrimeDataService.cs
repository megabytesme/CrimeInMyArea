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
            LogService.AddLog("CDS: Enter GetLatestAvailableCrimeMonthAsync");
            if (
                !forceRefresh
                && !string.IsNullOrEmpty(_latestAvailableCrimeMonth)
                && (DateTime.UtcNow - _lastCheckedCrimeMonthDate).TotalHours < 24
            )
            {
                LogService.AddLog("CDS: Returning cached crime month.");
                return _latestAvailableCrimeMonth;
            }

            try
            {
                string url = $"{BaseApiUrl}/crimes-street-dates";
                LogService.AddLog($"CDS: Fetching street dates from: {url}");
                var response = await _httpClient.GetAsync(url);
                LogService.AddLog($"CDS: Street dates response status: {response.StatusCode}");
                response.EnsureSuccessStatusCode();
                var availableDates = await response.Content.ReadFromJsonAsync<
                    List<AvailableDate>
                >();

                if (availableDates != null && availableDates.Any())
                {
                    _latestAvailableCrimeMonth = availableDates
                        .OrderByDescending(d =>
                            DateTime.ParseExact(d.Date, "yyyy-MM", CultureInfo.InvariantCulture)
                        )
                        .FirstOrDefault()
                        ?.Date;
                    _lastCheckedCrimeMonthDate = DateTime.UtcNow;
                    LogService.AddLog($"CDS: Latest crime month: {_latestAvailableCrimeMonth}");
                    return _latestAvailableCrimeMonth;
                }
                LogService.AddLog("CDS: No available dates found or list was empty.");
            }
            catch (HttpRequestException ex)
            {
                LogService.AddLog(
                    $"CDS: HttpRequestException in GetLatestAvailableCrimeMonthAsync: {ex.Message}. Status Code: {ex.StatusCode}"
                );
            }
            catch (Exception ex)
            {
                LogService.AddLog(
                    $"CDS: Exception in GetLatestAvailableCrimeMonthAsync: {ex.Message}"
                );
            }
            LogService.AddLog(
                "CDS: Exit GetLatestAvailableCrimeMonthAsync (possibly with old/null month)"
            );
            return _latestAvailableCrimeMonth;
        }

        private async Task<(string ForceId, string NeighbourhoodId)?> GetForceAndNeighbourhoodAsync(
            double latitude,
            double longitude
        )
        {
            LogService.AddLog("CDS: Enter GetForceAndNeighbourhoodAsync");
            try
            {
                string url = $"{BaseApiUrl}/locate-neighbourhood?q={latitude:F5},{longitude:F5}";
                LogService.AddLog($"CDS: Fetching force/neighbourhood from: {url}");
                var response = await _httpClient.GetAsync(url);
                LogService.AddLog(
                    $"CDS: Force/neighbourhood response status: {response.StatusCode}"
                );
                response.EnsureSuccessStatusCode();
                var locationInfo =
                    await response.Content.ReadFromJsonAsync<NeighbourhoodLocation>();

                if (locationInfo != null)
                {
                    LogService.AddLog(
                        $"CDS: Found Force: {locationInfo.Force}, Neighbourhood: {locationInfo.Neighbourhood}"
                    );
                    return (locationInfo.Force, locationInfo.Neighbourhood);
                }
                LogService.AddLog("CDS: Location info was null after parsing.");
            }
            catch (HttpRequestException ex)
            {
                LogService.AddLog(
                    $"CDS: HttpRequestException in GetForceAndNeighbourhoodAsync: {ex.Message}. Status Code: {ex.StatusCode}"
                );
            }
            catch (Exception ex)
            {
                LogService.AddLog($"CDS: Exception in GetForceAndNeighbourhoodAsync: {ex.Message}");
            }
            LogService.AddLog("CDS: Exit GetForceAndNeighbourhoodAsync (possibly null)");
            return null;
        }

        private async Task<List<BoundaryPoint>> GetNeighbourhoodBoundaryAsync(
            string forceId,
            string neighbourhoodId,
            bool forceRefresh = false
        )
        {
            LogService.AddLog("CDS: Enter GetNeighbourhoodBoundaryAsync");
            if (
                !forceRefresh
                && _currentNeighbourhoodBoundaryCache != null
                && _cachedBoundaryForNeighbourhoodId == neighbourhoodId
            )
            {
                LogService.AddLog("CDS: Returning cached neighbourhood boundary.");
                return _currentNeighbourhoodBoundaryCache;
            }

            try
            {
                string url = $"{BaseApiUrl}/{forceId}/{neighbourhoodId}/boundary";
                LogService.AddLog($"CDS: Fetching neighbourhood boundary from: {url}");
                var response = await _httpClient.GetAsync(url);
                LogService.AddLog(
                    $"CDS: Neighbourhood boundary response status: {response.StatusCode}"
                );
                response.EnsureSuccessStatusCode();
                var boundaryPoints = await response.Content.ReadFromJsonAsync<
                    List<BoundaryPoint>
                >();

                if (boundaryPoints != null)
                {
                    _currentNeighbourhoodBoundaryCache = boundaryPoints;
                    _cachedBoundaryForNeighbourhoodId = neighbourhoodId;
                    LogService.AddLog($"CDS: Fetched {boundaryPoints.Count} boundary points.");
                    return boundaryPoints;
                }
                LogService.AddLog("CDS: Boundary points list was null after parsing.");
            }
            catch (HttpRequestException ex)
            {
                LogService.AddLog(
                    $"CDS: HttpRequestException in GetNeighbourhoodBoundaryAsync: {ex.Message}. Status Code: {ex.StatusCode}"
                );
            }
            catch (Exception ex)
            {
                LogService.AddLog($"CDS: Exception in GetNeighbourhoodBoundaryAsync: {ex.Message}");
            }
            _currentNeighbourhoodBoundaryCache = null;
            _cachedBoundaryForNeighbourhoodId = null;
            LogService.AddLog("CDS: Exit GetNeighbourhoodBoundaryAsync (possibly null boundary)");
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
            LogService.AddLog("CDS: Enter GetCrimeDataAsync");
            bool needsDataFetch = forceDataRefresh;
            string newLatestCrimeMonth = await GetLatestAvailableCrimeMonthAsync(forceDataRefresh);
            LogService.AddLog(
                $"CDS: Initial needsDataFetch: {needsDataFetch}, LatestCrimeMonth: {newLatestCrimeMonth}"
            );

            if (string.IsNullOrEmpty(newLatestCrimeMonth))
            {
                LogService.AddLog(
                    "CDS: newLatestCrimeMonth is null or empty. Returning potentially cached/default report."
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
                LogService.AddLog("CDS: Crime month updated. Setting needsDataFetch to true.");
            }

            var locationIds = await GetForceAndNeighbourhoodAsync(
                currentLatitude,
                currentLongitude
            );
            LogService.AddLog(
                $"CDS: LocationIDs: Force={locationIds?.ForceId}, Neighbourhood={locationIds?.NeighbourhoodId}"
            );
            if (locationIds == null)
            {
                LogService.AddLog(
                    "CDS: locationIds is null. Returning potentially cached/default report."
                );
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
                                _forceLevelRadiusKm * 500
                            )
                        )
                        {
                            needsDataFetch = true;
                        }
                        break;
                }
                LogService.AddLog(
                    $"CDS: After movement check. Granularity: {_currentGranularity}, needsDataFetch: {needsDataFetch}"
                );
            }

            if (!needsDataFetch && _lastCrimeReport != null)
            {
                LogService.AddLog(
                    "CDS: Returning cached _lastCrimeReport as no data fetch needed."
                );
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
            LogService.AddLog(
                $"CDS: Fetching new data for Granularity: {_currentGranularity}, Force: {_currentForceId}, Neighbourhood: {_currentNeighbourhoodId}, Month: {newLatestCrimeMonth}"
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
                        LogService.AddLog(
                            $"CDS: Making HTTP GET request to: {requestUrlForLogging}"
                        );
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
                            LogService.AddLog(
                                $"CDS: Built requestUrl for Neighbourhood (POST base): {requestUrlSegment}. Poly length: {polyForPost.Length}"
                            );
                            requestUrlForLogging = $"{requestUrlSegment} with poly data";
                            var parameters = new Dictionary<string, string>
                            {
                                { "date", newLatestCrimeMonth },
                                { "poly", polyForPost },
                            };
                            var content = new FormUrlEncodedContent(parameters);
                            LogService.AddLog(
                                $"CDS: Making HTTP POST request to: {requestUrlSegment}"
                            );
                            response = await _httpClient.PostAsync(requestUrlSegment, content);
                        }
                        else
                        {
                            LogService.AddLog(
                                "CDS: Neighbourhood boundary was null or empty. Cannot fetch crimes for neighbourhood."
                            );
                            report.Summary = "Could not fetch neighbourhood boundary.";
                            _lastCrimeReport = report;
                            return report;
                        }
                        break;

                    case NotificationGranularity.Force:
                        var circlePoints = new List<BoundaryPoint>();
                        for (int i = 0; i < 36; i++)
                        {
                            double angle = i * 10 * (Math.PI / 180);
                            double dx =
                                _forceLevelRadiusKm
                                * Math.Cos(angle)
                                / (111.32 * Math.Cos(currentLatitude * Math.PI / 180));
                            double dy = _forceLevelRadiusKm * Math.Sin(angle) / 111.32;
                            circlePoints.Add(
                                new BoundaryPoint
                                {
                                    Latitude = (currentLatitude + dy).ToString(
                                        "F5",
                                        CultureInfo.InvariantCulture
                                    ),
                                    Longitude = (currentLongitude + dx).ToString(
                                        "F5",
                                        CultureInfo.InvariantCulture
                                    ),
                                }
                            );
                        }
                        if (circlePoints.Any())
                        {
                            polyForPost = FormatPolygon(circlePoints);
                            requestUrlSegment = $"{BaseApiUrl}/crimes-street/all-crime";
                            report.AreaIdentifier =
                                $"Approx. {_forceLevelRadiusKm:F1}km radius around {currentLatitude:F3}, {currentLongitude:F3} (Force: {_currentForceId})";
                            _lastFetchedForceIdForData = _currentForceId;
                            _lastFetchedLatForForceRadius = currentLatitude;
                            _lastFetchedLngForForceRadius = currentLongitude;
                            LogService.AddLog(
                                $"CDS: Built requestUrl for Force (POST base): {requestUrlSegment}. Poly length: {polyForPost.Length}"
                            );
                            requestUrlForLogging = $"{requestUrlSegment} with poly data";
                            var parameters = new Dictionary<string, string>
                            {
                                { "date", newLatestCrimeMonth },
                                { "poly", polyForPost },
                            };
                            var content = new FormUrlEncodedContent(parameters);
                            LogService.AddLog(
                                $"CDS: Making HTTP POST request to: {requestUrlSegment}"
                            );
                            response = await _httpClient.PostAsync(requestUrlSegment, content);
                        }
                        else
                        {
                            LogService.AddLog(
                                "CDS: Could not generate circle polygon for Force level. This should not happen."
                            );
                            report.Summary = "Internal error generating search area for force.";
                            _lastCrimeReport = report;
                            return report;
                        }
                        break;
                }

                if (response != null)
                {
                    LogService.AddLog(
                        $"CDS: HTTP response status: {response.StatusCode} from {requestUrlForLogging}"
                    );
                    if (!response.IsSuccessStatusCode)
                    {
                        LogService.AddLog(
                            $"CDS: API Error Status: {response.StatusCode} for URL: {response.RequestMessage?.RequestUri}"
                        );
                        string errorContent = await response.Content.ReadAsStringAsync();
                        LogService.AddLog($"CDS: API Error Content: {errorContent}");
                    }
                    response.EnsureSuccessStatusCode();
                    LogService.AddLog($"CDS: Reading JSON content from {requestUrlForLogging}...");
                    incidents = await response.Content.ReadFromJsonAsync<List<CrimeIncident>>();
                    LogService.AddLog(
                        $"CDS: JSON content read from {requestUrlForLogging}. Incidents count: {incidents?.Count}"
                    );
                }
                else if (string.IsNullOrEmpty(requestUrlSegment) && polyForPost == null)
                {
                    LogService.AddLog(
                        "CDS: Request URL or POST data was not prepared, skipping HTTP call."
                    );
                }
            }
            catch (HttpRequestException ex)
            {
                LogService.AddLog(
                    $"CDS: HttpRequestException in GetCrimeDataAsync for {requestUrlForLogging}: {ex.Message}. Status Code: {ex.StatusCode}"
                );
                report.Summary = $"API Error: {ex.Message.Split('.')[0]}.";
                if (ex.StatusCode.HasValue)
                {
                    report.Summary += $" (Status: {ex.StatusCode.Value})";
                }
            }
            catch (JsonException ex)
            {
                LogService.AddLog(
                    $"CDS: JsonException in GetCrimeDataAsync for {requestUrlForLogging}: {ex.Message}"
                );
                report.Summary = "Error processing crime data.";
            }
            catch (Exception ex)
            {
                LogService.AddLog(
                    $"CDS: Generic Exception of type {ex.GetType().FullName} in GetCrimeDataAsync for {requestUrlForLogging}: {ex.ToString()}"
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
                    $"No crime data found for {report.AreaIdentifier} for {report.DataMonth:MMMM yyyy}. Check logs for API issues.";
            }
            LogService.AddLog(
                $"CDS: Finalizing report. Incidents found: {incidents != null}. Summary: {report.Summary}"
            );

            _lastCrimeReport = report;
            LogService.AddLog("CDS: Exit GetCrimeDataAsync");
            return report;
        }
    }
}
