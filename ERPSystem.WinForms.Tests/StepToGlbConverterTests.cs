using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Tests;

public class StepToGlbConverterTests
{
    [Fact]
    public void ResolveConverterPath_UsesEnvironmentVariableWhenProvided()
    {
        const string expected = "D:/tools/step2glb.exe";
        Environment.SetEnvironmentVariable("ERP_STEP2GLB_PATH", expected);

        try
        {
            var actual = StepToGlbConverter.ResolveConverterPath();
            Assert.Equal(expected, actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ERP_STEP2GLB_PATH", null);
        }
    }

    [Fact]
    public void GetConverterPathCandidates_IncludesRepoToolLocationWhenBaseDirIsBinFolder()
    {
        var baseDir = Path.Combine("C:", "ERP", "ERP-SYSTEM-WINFORMS", "ERPSystem.WinForms", "bin", "Debug", "net8.0-windows");

        var candidates = StepToGlbConverter.GetConverterPathCandidates(baseDir);

        Assert.Contains(Path.Combine("C:", "ERP", "ERP-SYSTEM-WINFORMS", "ERPSystem.WinForms", "Tools", "step2glb", "step2glb.exe"), candidates, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("C:", "ERP", "ERP-SYSTEM-WINFORMS", "ERPSystem.WinForms", "Tools", "x64", "step-to-glb-converter.exe"), candidates, StringComparer.OrdinalIgnoreCase);
    }
}
