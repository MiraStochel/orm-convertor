using AbstractWrappers;
using Model;

namespace Tests;
public class DummyEntityBuilder : AbstractEntityBuilder
{
    public override List<ConversionSource> Build()
    {
        throw new NotImplementedException();
    }

    protected override void BuildForeignKey()
    {
    }

    protected override void BuildImports()
    {
    }

    protected override void BuildPrimaryKey()
    {
    }

    protected override void BuildProperties()
    {
    }

    protected override void BuildTableSchema()
    {
    }

    protected override void FinalizeBuild()
    {
    }
}
