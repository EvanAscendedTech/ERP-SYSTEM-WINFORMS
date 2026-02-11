namespace ERPSystem.WinForms.Controls;

public class ShippingControl : UserControl
{
    public ShippingControl()
    {
        Dock = DockStyle.Fill;

        var label = new Label
        {
            Text = "Shipping board placeholder - integrate shipment jobs and carrier docs here.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.Add(label);
    }
}
