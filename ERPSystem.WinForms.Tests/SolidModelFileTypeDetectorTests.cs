using System.Text;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Tests;

public class SolidModelFileTypeDetectorTests
{
    private readonly SolidModelFileTypeDetector _detector = new();

    [Fact]
    public void Detect_StepContentWithoutExtension_ReturnsStepFromContent()
    {
        var payload = Encoding.UTF8.GetBytes("ISO-10303-21; HEADER; DATA; ENDSEC; END-ISO-10303-21;");

        var result = _detector.Detect(payload, "model.bin");

        Assert.Equal(SolidModelFileType.Step, result.FileType);
        Assert.Equal("content", result.DetectionSource);
        Assert.True(result.IsSupportedForRendering);
    }

    [Fact]
    public void Detect_SldPrtExtension_ReturnsKnownButNotRenderable()
    {
        var result = _detector.Detect(Array.Empty<byte>(), "part.sldprt");

        Assert.Equal(SolidModelFileType.SldPrt, result.FileType);
        Assert.False(result.IsSupportedForRendering);
    }

    [Fact]
    public void Detect_StlAsciiContent_ReturnsStl()
    {
        var payload = Encoding.UTF8.GetBytes("solid cube\nfacet normal 0 0 1\nendfacet\nendsolid cube");

        var result = _detector.Detect(payload, "unknown.dat");

        Assert.Equal(SolidModelFileType.Stl, result.FileType);
        Assert.True(result.IsSupportedForRendering);
    }
}
