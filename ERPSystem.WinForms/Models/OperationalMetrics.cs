namespace ERPSystem.WinForms.Models;

public class ShipmentRecord
{
    public string JobNumber { get; set; } = string.Empty;
    public DateTime PromisedShipUtc { get; set; }
    public DateTime ActualShipUtc { get; set; }

    public bool IsOnTime => ActualShipUtc <= PromisedShipUtc;
}

public class QualityRecord
{
    public string JobNumber { get; set; } = string.Empty;
    public DateTime RecordedUtc { get; set; }
    public bool IsCustomerEscape { get; set; }
    public string FailureDescription { get; set; } = string.Empty;
}

public static class OperationalMetrics
{
    public static IReadOnlyList<ShipmentRecord> GetRecentShipments()
    {
        var now = DateTime.UtcNow;
        return
        [
            new ShipmentRecord { JobNumber = "JOB-3001", PromisedShipUtc = now.AddDays(-8).Date.AddHours(16), ActualShipUtc = now.AddDays(-8).Date.AddHours(15).AddMinutes(45) },
            new ShipmentRecord { JobNumber = "JOB-3002", PromisedShipUtc = now.AddDays(-7).Date.AddHours(16), ActualShipUtc = now.AddDays(-7).Date.AddHours(16).AddMinutes(42) },
            new ShipmentRecord { JobNumber = "JOB-3003", PromisedShipUtc = now.AddDays(-6).Date.AddHours(14), ActualShipUtc = now.AddDays(-6).Date.AddHours(13).AddMinutes(32) },
            new ShipmentRecord { JobNumber = "JOB-3004", PromisedShipUtc = now.AddDays(-5).Date.AddHours(17), ActualShipUtc = now.AddDays(-5).Date.AddHours(16).AddMinutes(58) },
            new ShipmentRecord { JobNumber = "JOB-3005", PromisedShipUtc = now.AddDays(-4).Date.AddHours(12), ActualShipUtc = now.AddDays(-4).Date.AddHours(12).AddMinutes(8) },
            new ShipmentRecord { JobNumber = "JOB-3006", PromisedShipUtc = now.AddDays(-2).Date.AddHours(15), ActualShipUtc = now.AddDays(-2).Date.AddHours(14).AddMinutes(40) },
            new ShipmentRecord { JobNumber = "JOB-3007", PromisedShipUtc = now.AddDays(-1).Date.AddHours(16), ActualShipUtc = now.AddDays(-1).Date.AddHours(16).AddMinutes(19) }
        ];
    }

    public static IReadOnlyList<QualityRecord> GetRecentQualityEvents()
    {
        var now = DateTime.UtcNow;
        return
        [
            new QualityRecord { JobNumber = "JOB-3001", RecordedUtc = now.AddDays(-8).Date.AddHours(9), IsCustomerEscape = false, FailureDescription = "Burr detected at deburr station" },
            new QualityRecord { JobNumber = "JOB-3002", RecordedUtc = now.AddDays(-7).Date.AddHours(18), IsCustomerEscape = true, FailureDescription = "Customer returned part for hole size out of tolerance" },
            new QualityRecord { JobNumber = "JOB-3005", RecordedUtc = now.AddDays(-4).Date.AddHours(10), IsCustomerEscape = false, FailureDescription = "In-process finish mismatch" },
            new QualityRecord { JobNumber = "JOB-3007", RecordedUtc = now.AddDays(-1).Date.AddHours(18), IsCustomerEscape = true, FailureDescription = "Damaged threads found at receiving" }
        ];
    }

    public static IReadOnlyList<ShipmentRecord> GetShipmentsForLastDays(int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        return GetRecentShipments().Where(x => x.ActualShipUtc >= cutoff).ToList();
    }

    public static IReadOnlyList<QualityRecord> GetQualityEventsForLastDays(int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        return GetRecentQualityEvents().Where(x => x.RecordedUtc >= cutoff).ToList();
    }
}
