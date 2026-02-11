using ERPSystem.WinForms.Models;

namespace ERPSystem.WinForms.Forms;

public class QuotePacketForm : Form
{
    private readonly Quote _quote;
    private readonly FlowLayoutPanel _lineItemContainers = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };

    public QuotePacketForm(Quote quote)
    {
        _quote = quote;

        Text = $"Quote Packet - Quote #{quote.Id}";
        Width = 980;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        foreach (var lineItem in quote.LineItems)
        {
            _lineItemContainers.Controls.Add(BuildLineItemPanel(lineItem));
        }

        var actionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var saveButton = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        actionPanel.Controls.Add(saveButton);
        actionPanel.Controls.Add(cancelButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        root.Controls.Add(_lineItemContainers, 0, 0);
        root.Controls.Add(actionPanel, 0, 1);

        Controls.Add(root);
    }

    private static Control BuildLineItemPanel(QuoteLineItem lineItem)
    {
        var group = new GroupBox
        {
            Text = $"Line Item: {lineItem.Description}",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(10),
            Width = 900
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };

        var metadataLabel = new Label
        {
            Text = $"Qty: {lineItem.Quantity} | Unit Price: {lineItem.UnitPrice:C} | Lead Time: {lineItem.LeadTimeDays} days",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        };

        var attachmentsLabel = new Label { Text = "Attached files (.pdf, .txt, .step):", AutoSize = true };
        var fileList = new ListBox { Height = 100, Dock = DockStyle.Top };
        foreach (var file in lineItem.AssociatedFiles)
        {
            fileList.Items.Add(file);
        }

        var attachmentButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        var addFileButton = new Button { Text = "Add Files", AutoSize = true };
        var removeFileButton = new Button { Text = "Remove Selected", AutoSize = true };

        addFileButton.Click += (_, _) => AddFilesFromPicker(lineItem, fileList);
        removeFileButton.Click += (_, _) => RemoveSelectedFile(lineItem, fileList);

        attachmentButtons.Controls.Add(addFileButton);
        attachmentButtons.Controls.Add(removeFileButton);

        var dropZone = new Label
        {
            Text = "Drag and drop supported files here",
            BorderStyle = BorderStyle.FixedSingle,
            Height = 48,
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleCenter,
            AllowDrop = true,
            Margin = new Padding(0, 6, 0, 6)
        };

        dropZone.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
            }
        };

        dropZone.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] droppedFiles)
            {
                AddAllowedFiles(lineItem, fileList, droppedFiles);
            }
        };

        var notesLabel = new Label { Text = "Manual notes:", AutoSize = true };
        var notesInput = new TextBox { Multiline = true, Height = 80, Dock = DockStyle.Top, Text = lineItem.Notes };
        notesInput.TextChanged += (_, _) => lineItem.Notes = notesInput.Text;

        layout.Controls.Add(metadataLabel);
        layout.Controls.Add(attachmentsLabel);
        layout.Controls.Add(fileList);
        layout.Controls.Add(attachmentButtons);
        layout.Controls.Add(dropZone);
        layout.Controls.Add(notesLabel);
        layout.Controls.Add(notesInput);

        group.Controls.Add(layout);
        return group;
    }

    private static void AddFilesFromPicker(QuoteLineItem lineItem, ListBox fileList)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Supported Files|*.pdf;*.txt;*.step|PDF Files|*.pdf|Text Files|*.txt|STEP Files|*.step",
            Multiselect = true,
            Title = "Select files for quote packet"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            AddAllowedFiles(lineItem, fileList, dialog.FileNames);
        }
    }

    private static void AddAllowedFiles(QuoteLineItem lineItem, ListBox fileList, IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            if (!IsSupportedFile(filePath) || lineItem.AssociatedFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            lineItem.AssociatedFiles.Add(filePath);
            fileList.Items.Add(filePath);
        }
    }

    private static void RemoveSelectedFile(QuoteLineItem lineItem, ListBox fileList)
    {
        if (fileList.SelectedItem is not string selectedFile)
        {
            return;
        }

        lineItem.AssociatedFiles.RemoveAll(path => string.Equals(path, selectedFile, StringComparison.OrdinalIgnoreCase));
        fileList.Items.Remove(selectedFile);
    }

    private static bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".pdf" or ".txt" or ".step";
    }
}
