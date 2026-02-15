using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public static class LifecycleWorkflowService
{
    public static bool CanStartProduction(QuoteStatus quoteStatus, out string message)
    {
        if (quoteStatus == QuoteStatus.Won)
        {
            message = "Production start approved.";
            return true;
        }

        message = $"Production can only start from a Won quote. Current quote status is {quoteStatus}.";
        return false;
    }

    public static bool CanStartInspection(ProductionJobStatus productionStatus, out string message)
    {
        if (productionStatus == ProductionJobStatus.Completed)
        {
            message = "Inspection start approved.";
            return true;
        }

        message = $"Inspection can only start from completed production batches. Current production status is {productionStatus}.";
        return false;
    }

    public static bool CanArchive(QuoteStatus? quoteStatus, ProductionJobStatus? productionStatus, out string message)
    {
        if (quoteStatus.HasValue)
        {
            if (quoteStatus is QuoteStatus.Completed or QuoteStatus.Won or QuoteStatus.Lost or QuoteStatus.Expired)
            {
                message = "Archive approved for terminal quote.";
                return true;
            }

            message = $"Archive is only allowed for terminal records. Quote status {quoteStatus} is not terminal.";
            return false;
        }

        if (productionStatus.HasValue)
        {
            if (productionStatus == ProductionJobStatus.Completed)
            {
                message = "Archive approved for terminal production batch.";
                return true;
            }

            message = $"Archive is only allowed for terminal records. Production status {productionStatus} is not terminal.";
            return false;
        }

        message = "Archive requires either quote or production status context.";
        return false;
    }
}
