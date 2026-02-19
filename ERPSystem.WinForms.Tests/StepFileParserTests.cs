using System.Text;
using ERPSystem.WinForms.Services;

namespace ERPSystem.WinForms.Tests;

public class StepFileParserTests
{
    private readonly StepFileParser _parser = new();

    [Fact]
    public void Parse_ValidSolidAndSurfaceStep_ReturnsEntitySummary()
    {
        var step = """
ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('Example'),'2;1');
ENDSEC;
DATA;
#10=MANIFOLD_SOLID_BREP('SOLID',#40);
#11=ADVANCED_FACE('',(),#50,.T.);
#12=PLANE('',#60);
ENDSEC;
END-ISO-10303-21;
""";

        var result = _parser.Parse(Encoding.UTF8.GetBytes(step));

        Assert.True(result.IsSuccess);
        Assert.True(result.HasSolids);
        Assert.True(result.HasSurfaces);
        Assert.True(result.EntityCount >= 3);
        Assert.Contains("MANIFOLD_SOLID_BREP", result.DistinctEntityTypes.Keys);
        Assert.Contains("ADVANCED_FACE", result.DistinctEntityTypes.Keys);
    }

    [Fact]
    public void Parse_ValidSurfaceOnlyStep_IsAccepted()
    {
        var step = """
ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('Surface'),'2;1');
ENDSEC;
DATA;
#20=ADVANCED_FACE('',(),#30,.T.);
#21=CYLINDRICAL_SURFACE('',#40,10.0);
ENDSEC;
END-ISO-10303-21;
""";

        var result = _parser.Parse(Encoding.UTF8.GetBytes(step));

        Assert.True(result.IsSuccess);
        Assert.False(result.HasSolids);
        Assert.True(result.HasSurfaces);
    }

    [Fact]
    public void Parse_InvalidHeader_Fails()
    {
        var malformed = "HEADER; DATA; #1=ADVANCED_FACE('',(),#2,.T.); ENDSEC;";

        var result = _parser.Parse(Encoding.UTF8.GetBytes(malformed));

        Assert.False(result.IsSuccess);
        Assert.Equal("invalid-step-header", result.ErrorCode);
    }

    [Fact]
    public void Parse_UnsupportedEntities_FailsWithExplicitError()
    {
        var noGeometry = """
ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('No Geometry'),'2;1');
ENDSEC;
DATA;
#1=PERSON('A','B',(),(),(),());
#2=ORGANIZATION('ORG','','');
ENDSEC;
END-ISO-10303-21;
""";

        var result = _parser.Parse(Encoding.UTF8.GetBytes(noGeometry));

        Assert.False(result.IsSuccess);
        Assert.Equal("unsupported-step-entities", result.ErrorCode);
    }

    [Fact]
    public void Parse_MultipleAttempts_DoNotLeakStateAcrossCalls()
    {
        var invalid = "ISO-10303-21; HEADER; ENDSEC; DATA; ENDSEC; END-ISO-10303-21;";
        var valid = """
ISO-10303-21;
HEADER;
FILE_DESCRIPTION(('Retry'),'2;1');
ENDSEC;
DATA;
#30=ADVANCED_FACE('',(),#40,.T.);
ENDSEC;
END-ISO-10303-21;
""";

        var first = _parser.Parse(Encoding.UTF8.GetBytes(invalid));
        var second = _parser.Parse(Encoding.UTF8.GetBytes(valid));

        Assert.False(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(second.HasSurfaces);
    }

}
