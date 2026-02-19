using System.Text;
using System.Text.RegularExpressions;

namespace ERPSystem.WinForms.Services;

public sealed class StepFileParser
{
    private static readonly Regex EntityRegex = new(@"^\s*#\d+\s*=\s*([A-Z0-9_]+)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SchemaRegex = new(@"FILE_SCHEMA\s*\(\s*\(\s*'([^']+)'", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SupportedSchemaFragments =
    [
        "AUTOMOTIVE_DESIGN",
        "CONFIG_CONTROL_DESIGN",
        "AP203",
        "AP214",
        "AP242",
        "MANAGED_MODEL_BASED_3D_ENGINEERING"
    ];

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
            return StepParseReport.Fail("missing-file-data", "file", "No STEP payload bytes were supplied.");
        }

        var text = DecodePayload(fileBytes);
        if (string.IsNullOrWhiteSpace(text))
        {
            return StepParseReport.Fail("parse-text-failed", "encoding", "Unable to decode STEP payload as text.");
        }

        var normalized = text.ToUpperInvariant();
        if (!normalized.Contains("ISO-10303-21") && !normalized.Contains("ISO10303-21"))
        {
            return StepParseReport.Fail("invalid-step-header", "header", "ISO-10303-21 header marker was not found.");
        }

        var headerIndex = normalized.IndexOf("HEADER;", StringComparison.Ordinal);
        var dataIndex = normalized.IndexOf("DATA;", StringComparison.Ordinal);
        var endSecIndex = normalized.LastIndexOf("ENDSEC;", StringComparison.Ordinal);
        if (headerIndex < 0 || dataIndex < 0 || endSecIndex < 0 || dataIndex <= headerIndex)
        {
            return StepParseReport.Fail("invalid-step-body", "section-order", "Required HEADER/DATA sections were not found in expected order.");
        }

        var schemaName = ExtractSchemaName(normalized);
        if (!string.IsNullOrWhiteSpace(schemaName) && !IsSupportedSchema(schemaName))
        {
            return StepParseReport.Fail(
                "unsupported-step-version",
                "version",
                $"STEP schema '{schemaName}' is not supported by the preview pipeline.",
                schemaName: schemaName,
                diagnosticDetails: $"Detected FILE_SCHEMA value '{schemaName}'. Supported schemas include AP203/AP214/AP242 and related automotive_design variants.");
        }

        var dataSection = normalized[dataIndex..];
        var entities = ParseEntities(dataSection);
        if (entities.Count == 0)
        {
            return StepParseReport.Fail("invalid-step-body", "missing-entities", "DATA section does not contain parseable STEP entities.");
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
            var topEntities = string.Join(", ", distinctTypes.OrderByDescending(x => x.Value).Take(5).Select(x => $"{x.Key}:{x.Value}"));
            return StepParseReport.Fail(
                "unsupported-step-entities",
                "geometry",
                "STEP file contains entities, but no supported surface or solid geometry entities.",
                schemaName,
                $"Top entity types: {topEntities}");
        }

        return StepParseReport.Success(
            entityCount: entities.Count,
            distinctEntityTypes: distinctTypes,
            surfaceEntityCount: surfaceCount,
            solidEntityCount: solidCount,
            schemaName: schemaName);
    }

    private static List<string> ParseEntities(string dataSection)
    {
        var entities = new List<string>();
        var lines = dataSection.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var statement = new StringBuilder();
        var inBlockComment = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine;

            if (inBlockComment)
            {
                var endComment = line.IndexOf("*/", StringComparison.Ordinal);
                if (endComment < 0)
                {
                    continue;
                }

                line = line[(endComment + 2)..];
                inBlockComment = false;
            }

            var startComment = line.IndexOf("/*", StringComparison.Ordinal);
            if (startComment >= 0)
            {
                var endComment = line.IndexOf("*/", startComment + 2, StringComparison.Ordinal);
                if (endComment >= 0)
                {
                    line = line.Remove(startComment, endComment - startComment + 2);
                }
                else
                {
                    line = line[..startComment];
                    inBlockComment = true;
                }
            }

            var inlineComment = line.IndexOf("//", StringComparison.Ordinal);
            if (inlineComment >= 0)
            {
                line = line[..inlineComment];
            }

            if (string.IsNullOrWhiteSpace(line))
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

    private static string ExtractSchemaName(string normalizedText)
    {
        var match = SchemaRegex.Match(normalizedText);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static bool IsSupportedSchema(string schemaName)
    {
        return SupportedSchemaFragments.Any(fragment => schemaName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static string DecodePayload(byte[] fileBytes)
    {
        try
        {
            return Encoding.UTF8.GetString(fileBytes);
        }
        catch
        {
            return Encoding.Latin1.GetString(fileBytes);
        }
    }
}

public sealed record StepParseReport(
    bool IsSuccess,
    string ErrorCode,
    string FailureCategory,
    string Message,
    string DiagnosticDetails,
    string SchemaName,
    int EntityCount,
    IReadOnlyDictionary<string, int> DistinctEntityTypes,
    int SurfaceEntityCount,
    int SolidEntityCount)
{
    public bool HasSurfaces => SurfaceEntityCount > 0;
    public bool HasSolids => SolidEntityCount > 0;

    public static StepParseReport Fail(string errorCode, string failureCategory, string message, string? schemaName = null, string? diagnosticDetails = null)
    {
        return new StepParseReport(false, errorCode, failureCategory, message, diagnosticDetails ?? string.Empty, schemaName ?? string.Empty, 0, new Dictionary<string, int>(), 0, 0);
    }

    public static StepParseReport Success(
        int entityCount,
        IReadOnlyDictionary<string, int> distinctEntityTypes,
        int surfaceEntityCount,
        int solidEntityCount,
        string schemaName)
    {
        return new StepParseReport(
            true,
            string.Empty,
            string.Empty,
            "STEP structure validated.",
            string.Empty,
            schemaName,
            entityCount,
            distinctEntityTypes,
            surfaceEntityCount,
            solidEntityCount);
    }
}
