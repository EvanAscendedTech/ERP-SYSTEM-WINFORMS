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
            QuoteStatus.InProgress when nextStatus is QuoteStatus.Won or QuoteStatus.Lost => true,
            _ => false
        };
    }
}
