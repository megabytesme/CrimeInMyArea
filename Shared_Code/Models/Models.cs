namespace Shared_Code.Models
{
    public enum NotificationGranularity
    {
        Street,
        Neighbourhood,
        Force,
    }

    public class CrimeReport
    {
        public string Summary { get; set; }
        public int CrimeCount { get; set; }
        public DateTime DataMonth { get; set; }
        public NotificationGranularity Granularity { get; set; }
        public string AreaIdentifier { get; set; }
        public List<CrimeIncident> Incidents { get; set; }

        public CrimeReport()
        {
            Incidents = new List<CrimeIncident>();
            Summary = "No data available.";
            CrimeCount = 0;
        }
    }

    public class CrimeIncident
    {
        public string Category { get; set; }
        public string LocationType { get; set; }
        public Location Location { get; set; }
        public string Context { get; set; }
        public OutcomeStatus OutcomeStatus { get; set; }
        public string PersistentId { get; set; }
        public int Id { get; set; }
        public string LocationSubtype { get; set; }
        public string Month { get; set; }
    }

    public class Location
    {
        public string Latitude { get; set; }
        public string Longitude { get; set; }
        public Street Street { get; set; }
    }

    public class Street
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class OutcomeStatus
    {
        public string Category { get; set; }
        public string Date { get; set; }
    }

    public class AvailableDate
    {
        public string Date { get; set; }
    }

    public class NeighbourhoodLocation
    {
        public string Force { get; set; }
        public string Neighbourhood { get; set; }
    }

    public class BoundaryPoint
    {
        public string Latitude { get; set; }
        public string Longitude { get; set; }
    }

    public class CrimeCategoryCount
    {
        public string? CategoryName { get; set; }
        public int Count { get; set; }
    }

    public class CrimeIncidentDisplay
    {
        public string? Category { get; set; }
        public string? StreetInfo { get; set; }
        public string? OutcomeInfo { get; set; }
        public string? Month { get; set; }
        public double DistanceKm { get; set; }
        public string DistanceDisplay => DistanceKm < 0 ? "N/A" : (DistanceKm < 0.1 ? "< 100 m" : $"{DistanceKm:F1} km away");
    }
}
