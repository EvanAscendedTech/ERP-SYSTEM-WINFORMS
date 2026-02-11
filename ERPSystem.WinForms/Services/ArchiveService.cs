namespace ERPSystem.WinForms.Services;

public class ArchiveService
{
    private readonly List<string> _archivedItems = new();

    public IReadOnlyList<string> ArchivedItems => _archivedItems;

    public void Archive(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return;
        }

        _archivedItems.Add($"{DateTime.UtcNow:O}:{item.Trim()}");
    }
}
