using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public class ArchiveService
{
    private readonly List<string> _archivedItems = new();

    public IReadOnlyList<string> ArchivedItems => _archivedItems;

    public bool TryArchiveQuote(string quoteIdentifier, QuoteStatus quoteStatus, string actorUserId, out string message)
    {
        if (!LifecycleWorkflowService.CanArchive(quoteStatus, null, out message))
        {
            return false;
        }

        _archivedItems.Add($"{DateTime.UtcNow:O}|{actorUserId}|Quote|{quoteIdentifier}|{quoteStatus}");
        message = $"Quote {quoteIdentifier} archived.";
        return true;
    }

    public bool TryArchiveProduction(string batchIdentifier, ProductionJobStatus productionStatus, string actorUserId, out string message)
    {
        if (!LifecycleWorkflowService.CanArchive(null, productionStatus, out message))
        {
            return false;
        }

        _archivedItems.Add($"{DateTime.UtcNow:O}|{actorUserId}|Production|{batchIdentifier}|{productionStatus}");
        message = $"Production batch {batchIdentifier} archived.";
        return true;
    }

    public void Archive(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        _archivedItems.Add($"{DateTime.UtcNow:O}:{item.Trim()}");
    }
}
