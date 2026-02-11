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
            QuoteStatus.InProgress when nextStatus is QuoteStatus.Won or QuoteStatus.Lost or QuoteStatus.Expired => true,
            QuoteStatus.Expired when nextStatus is QuoteStatus.Lost or QuoteStatus.InProgress => true,
            _ => false
        };
    }
}
