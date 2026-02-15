using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Services;

public static class QuoteWorkflowService
{
    public static bool IsTransitionAllowed(QuoteStatus currentStatus, QuoteStatus nextStatus)
    {
        if (currentStatus == nextStatus)
        {
            return true;
        }

        return currentStatus switch
        {
            QuoteStatus.InProgress when nextStatus is QuoteStatus.Completed or QuoteStatus.Won or QuoteStatus.Lost or QuoteStatus.Expired => true,
            QuoteStatus.Completed when nextStatus is QuoteStatus.Won or QuoteStatus.Lost => true,
            _ => false
        };
    }

    public static string BuildTransitionErrorMessage(QuoteStatus currentStatus, QuoteStatus nextStatus)
    {
        return $"Invalid quote transition: {currentStatus} -> {nextStatus}. Allowed transitions are InProgress -> Completed/Won/Lost/Expired and Completed -> Won/Lost.";
    }
}
