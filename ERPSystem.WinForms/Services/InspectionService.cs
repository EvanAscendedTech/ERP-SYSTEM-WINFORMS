namespace ERPSystem.WinForms.Services;

public class InspectionService
{
    private readonly List<string> _openFindings = new();

    public IReadOnlyList<string> OpenFindings => _openFindings;

    public void AddFinding(string finding)
    {
        if (string.IsNullOrWhiteSpace(finding))
        {
            return;
        }

        _openFindings.Add(finding.Trim());
    }

    public bool CloseFinding(string finding)
    {
        return _openFindings.Remove(finding);
    }
}
