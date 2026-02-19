using System.Text;
using System.Text.RegularExpressions;

namespace ERPSystem.WinForms.Services;

public sealed class StepFileParser
{
    private static readonly Regex EntityRegex = new(@"^\s*#\d+\s*=\s*([A-Z0-9_]+)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SurfaceEntities =
    [
        "ADVANCED_FACE",
        "FACE_SURFACE",
        "B_SPLINE_SURFACE",
        "B_SPLINE_SURFACE_WITH_KNOTS",
        "BOUNDED_SURFACE",
        "CURVE_BOUNDED_SURFACE",
        "RECTANGULAR_COMPOSITE_SURFACE",
        "CLOSED_SHELL",
        "OPEN_SHELL",
        "PLANE",
        "CYLINDRICAL_SURFACE",
        "CONICAL_SURFACE",
        "SPHERICAL_SURFACE",
        "TOROIDAL_SURFACE",
        "SURFACE_OF_LINEAR_EXTRUSION",
        "SURFACE_OF_REVOLUTION"
    ];

    private static readonly HashSet<string> SolidEntities =
    [
        "MANIFOLD_SOLID_BREP",
        "BREP_WITH_VOIDS",
        "SHELL_BASED_SURFACE_MODEL",
        "FACETED_BREP",
        "ADVANCED_BREP_SHAPE_REPRESENTATION",
        "CSG_SOLID",
        "BLOCK",
        "RIGHT_ANGULAR_WEDGE",
        "SPHERE",
        "TORUS"
    ];

    public StepParseReport Parse(byte[]? fileBytes)
    {
        if (fileBytes is null || fileBytes.Length == 0)
        {
            return StepParseReport.Fail("missing-file-data", "No STEP payload bytes were supplied.");
        }

        var text = DecodePayload(fileBytes);
        if (string.IsNullOrWhiteSpace(text))
        {
            return StepParseReport.Fail("parse-text-failed", "Unable to decode STEP payload as text.");
        }

        var normalized = text.ToUpperInvariant();
        if (!normalized.Contains("ISO-10303-21") && !normalized.Contains("ISO10303-21"))
        {
            return StepParseReport.Fail("invalid-step-header", "ISO-10303-21 header marker was not found.");
        }

        var headerIndex = normalized.IndexOf("HEADER;", StringComparison.Ordinal);
        var dataIndex = normalized.IndexOf("DATA;", StringComparison.Ordinal);
        var endSecIndex = normalized.LastIndexOf("ENDSEC;", StringComparison.Ordinal);
        if (headerIndex < 0 || dataIndex < 0 || endSecIndex < 0 || dataIndex <= headerIndex)
        {
            return StepParseReport.Fail("invalid-step-body", "Required HEADER/DATA sections were not found in expected order.");
        }

        var dataSection = normalized[dataIndex..];
        var entities = ParseEntities(dataSection);
        if (entities.Count == 0)
        {
            return StepParseReport.Fail("invalid-step-body", "DATA section does not contain parseable STEP entities.");
        }

        var distinctTypes = entities
            .GroupBy(static entity => entity)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        var surfaceCount = distinctTypes
            .Where(kvp => SurfaceEntities.Contains(kvp.Key))
            .Sum(static kvp => kvp.Value);

        var solidCount = distinctTypes
            .Where(kvp => SolidEntities.Contains(kvp.Key))
            .Sum(static kvp => kvp.Value);

        if (surfaceCount == 0 && solidCount == 0)
        {
            return StepParseReport.Fail("unsupported-step-entities", "STEP file contains entities, but no supported surface or solid geometry entities.");
        }

        return StepParseReport.Success(
            entityCount: entities.Count,
            distinctEntityTypes: distinctTypes,
            surfaceEntityCount: surfaceCount,
            solidEntityCount: solidCount);
    }

    private static List<string> ParseEntities(string dataSection)
    {
        var entities = new List<string>();
        var lines = dataSection.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var statement = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.StartsWith("/*", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            statement.Append(line);
            statement.Append(' ');

            if (!line.Contains(';', StringComparison.Ordinal))
            {
                continue;
            }

            var current = statement.ToString();
            statement.Clear();

            var match = EntityRegex.Match(current);
            if (!match.Success)
            {
                continue;
            }

            entities.Add(match.Groups[1].Value);
        }

        return entities;
    }

    private static string DecodePayload(byte[] fileBytes)
    {
        try
        {
            return Encoding.UTF8.GetString(fileBytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}

public sealed record StepParseReport(
    bool IsSuccess,
    string ErrorCode,
    string Message,
    int EntityCount,
    IReadOnlyDictionary<string, int> DistinctEntityTypes,
    int SurfaceEntityCount,
    int SolidEntityCount)
{
    public bool HasSurfaces => SurfaceEntityCount > 0;
    public bool HasSolids => SolidEntityCount > 0;

    public static StepParseReport Fail(string errorCode, string message)
    {
        return new StepParseReport(false, errorCode, message, 0, new Dictionary<string, int>(), 0, 0);
    }

    public static StepParseReport Success(
        int entityCount,
        IReadOnlyDictionary<string, int> distinctEntityTypes,
        int surfaceEntityCount,
        int solidEntityCount)
    {
        return new StepParseReport(
            true,
            string.Empty,
            "STEP structure validated.",
            entityCount,
            distinctEntityTypes,
            surfaceEntityCount,
            solidEntityCount);
    }
}
