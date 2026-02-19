using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class CompletedQuoteDetailsForm : Form
{
    public CompletedQuoteDetailsForm(Quote quote, Control detailContent)
    {
        Text = $"Completed Quote #{quote.Id} Details";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 620);
        Size = new Size(1200, 760);
        FormBorderStyle = FormBorderStyle.Sizable;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Text = $"Quote #{quote.Id} • {quote.CustomerName} • {quote.LineItems.Count} line item(s)"
        };

        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(248, 251, 255)
        };

        detailContent.Dock = DockStyle.Top;
        detailContent.Margin = new Padding(0);
        viewport.Controls.Add(detailContent);

        var closeButton = new Button
        {
            Text = "Close",
            AutoSize = true
        };
        closeButton.Click += (_, _) => Close();

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        footer.Controls.Add(closeButton);

        Controls.Add(viewport);
        Controls.Add(footer);
        Controls.Add(header);
    }
}
