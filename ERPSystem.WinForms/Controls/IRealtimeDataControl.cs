namespace ERPSystem.WinForms.Controls;

public interface IRealtimeDataControl
{
    Task RefreshDataAsync(bool fromFailSafeCheckpoint);
}
