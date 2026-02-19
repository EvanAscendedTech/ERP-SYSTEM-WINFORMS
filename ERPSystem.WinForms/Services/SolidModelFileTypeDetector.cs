using System.Text;

namespace ERPSystem.WinForms.Services;

public enum SolidModelFileType
{
    Unknown,
    Step,
    Iges,
    Brep,
    Stl,
    Obj,
    SldPrt,
    Parasolid
}

public sealed record SolidModelFileTypeDetectionResult(
    SolidModelFileType FileType,
    string NormalizedExtension,
    bool IsSupportedForRendering,
    string DetectionSource)
{
    public bool IsKnownType => FileType != SolidModelFileType.Unknown;
}

public sealed class SolidModelFileTypeDetector
{
    public SolidModelFileTypeDetectionResult Detect(byte[]? fileBytes, string? fileNameOrPath)
    {
        var extension = NormalizeExtension(Path.GetExtension(fileNameOrPath ?? string.Empty));
        var headerSample = DecodeHeader(fileBytes);
        var upperSample = headerSample.ToUpperInvariant();

        if (LooksLikeStep(upperSample) || extension is ".step" or ".stp")
        {
            return new(SolidModelFileType.Step, extension, true, LooksLikeStep(upperSample) ? "content" : "extension");
        }

        if (LooksLikeStl(headerSample, upperSample) || extension == ".stl")
        {
            return new(SolidModelFileType.Stl, extension, true, LooksLikeStl(headerSample, upperSample) ? "content" : "extension");
        }

        if (LooksLikeObj(headerSample) || extension == ".obj")
        {
            return new(SolidModelFileType.Obj, extension, true, LooksLikeObj(headerSample) ? "content" : "extension");
        }

        if (extension is ".iges" or ".igs")
        {
            return new(SolidModelFileType.Iges, extension, true, "extension");
        }

        if (extension is ".brep" or ".brp")
        {
            return new(SolidModelFileType.Brep, extension, true, "extension");
        }

        if (extension == ".sldprt")
        {
            return new(SolidModelFileType.SldPrt, extension, false, "extension");
        }

        if (extension is ".x_t" or ".x_b")
        {
            return new(SolidModelFileType.Parasolid, extension, false, "extension");
        }

        return new(SolidModelFileType.Unknown, extension, false, "unknown");
    }

    public static bool IsKnownSolidExtension(string extension)
    {
        var normalized = NormalizeExtension(extension);
        return normalized is ".step" or ".stp" or ".sldprt" or ".iges" or ".igs" or ".brep" or ".brp" or ".stl" or ".obj" or ".x_t" or ".x_b";
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.StartsWith(".", StringComparison.Ordinal)
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";
    }

    private static string DecodeHeader(byte[]? fileBytes)
    {
        if (fileBytes is null || fileBytes.Length == 0)
        {
            return string.Empty;
        }

        var count = Math.Min(fileBytes.Length, 4096);
        try
        {
            return Encoding.UTF8.GetString(fileBytes, 0, count);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeStep(string upperSample)
    {
        return !string.IsNullOrWhiteSpace(upperSample)
               && (upperSample.Contains("ISO-10303-21", StringComparison.Ordinal)
                   || upperSample.Contains("ISO10303-21", StringComparison.Ordinal))
               && upperSample.Contains("HEADER;", StringComparison.Ordinal)
               && upperSample.Contains("DATA;", StringComparison.Ordinal);
    }

    private static bool LooksLikeStl(string sample, string upperSample)
    {
        if (string.IsNullOrWhiteSpace(sample))
        {
            return false;
        }

        return sample.TrimStart().StartsWith("solid ", StringComparison.OrdinalIgnoreCase)
               || upperSample.Contains("FACET NORMAL", StringComparison.Ordinal);
    }

    private static bool LooksLikeObj(string sample)
    {
        if (string.IsNullOrWhiteSpace(sample))
        {
            return false;
        }

        var lines = sample.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Any(static line => line.StartsWith("v ", StringComparison.Ordinal)
                                     || line.StartsWith("vn ", StringComparison.Ordinal)
                                     || line.StartsWith("f ", StringComparison.Ordinal));
    }
}
