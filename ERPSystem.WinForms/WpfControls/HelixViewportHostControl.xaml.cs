using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace ERPSystem.WinForms.WpfControls;

public partial class HelixViewportHostControl : System.Windows.Controls.UserControl
{
    private readonly ModelVisual3D _modelVisual = new();

    public HelixViewportHostControl()
    {
        InitializeComponent();

        if (!Viewport.Children.Contains(_modelVisual))
        {
            Viewport.Children.Add(_modelVisual);
        }
    }

    public void LoadModelFromBytes(byte[] modelBlob, string fileName)
    {
        if (modelBlob is null || modelBlob.Length == 0)
        {
            throw new ArgumentException("Model blob is empty.", nameof(modelBlob));
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A model file name with extension is required.", nameof(fileName));
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidOperationException("The model file extension is required to parse the model.");
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
        File.WriteAllBytes(tempFile, modelBlob);

        try
        {
            var importer = new ModelImporter();
            var model = importer.Load(tempFile, Dispatcher);
            if (model is null)
            {
                throw new InvalidOperationException("HelixToolkit returned an empty model.");
            }

            var wrappedModel = new Model3DGroup();
            wrappedModel.Children.Add(model);

            EnsureFrontAndBackMaterials(wrappedModel);
            _modelVisual.Content = wrappedModel;
            Viewport.ZoomExtents();
        }
        finally
        {
            try
            {
                File.Delete(tempFile);
            }
            catch
            {
                // Ignore temp file cleanup failures.
            }
        }
    }

    private static void EnsureFrontAndBackMaterials(Model3D model)
    {
        if (model is Model3DGroup group)
        {
            foreach (var child in group.Children)
            {
                EnsureFrontAndBackMaterials(child);
            }

            return;
        }

        if (model is not GeometryModel3D geometryModel)
        {
            return;
        }

        if (geometryModel.Material is null)
        {
            geometryModel.Material = new DiffuseMaterial(new SolidColorBrush(Colors.Silver));
        }

        geometryModel.BackMaterial ??= geometryModel.Material;
    }
}
