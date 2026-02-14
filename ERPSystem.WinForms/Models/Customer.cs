namespace ERPSystem.WinForms.Models;

public class Customer
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastInteractionUtc { get; set; }
    public List<CustomerContact> Contacts { get; set; } = new();

    public string DisplayLabel => $"{Code} - {Name}";
}

public class CustomerContact
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
