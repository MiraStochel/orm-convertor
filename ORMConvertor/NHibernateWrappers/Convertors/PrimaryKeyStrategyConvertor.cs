using Model.AbstractRepresentation.Enums;

namespace NHibernateWrappers.Convertors;
public class PrimaryKeyStrategyConvertor
{
    public static PrimaryKeyStrategy FromNHibernate(string? strategy)
    {
        return strategy switch
        {
            "increment" => PrimaryKeyStrategy.Increment,
            "identity" => PrimaryKeyStrategy.Identity,
            "sequence" => PrimaryKeyStrategy.Sequence,
            "hilo" => PrimaryKeyStrategy.HiLo,
            "uuid" => PrimaryKeyStrategy.Uuid,
            "guid" => PrimaryKeyStrategy.Guid,
            "assigned" => PrimaryKeyStrategy.None,
            _ => PrimaryKeyStrategy.None
        };
    }

    public static string ToNHibernate(PrimaryKeyStrategy strategy)
    {
        return strategy switch
        {
            PrimaryKeyStrategy.None => "assigned",
            PrimaryKeyStrategy.Increment => "increment",
            PrimaryKeyStrategy.Identity => "identity",
            PrimaryKeyStrategy.Sequence => "sequence",
            PrimaryKeyStrategy.HiLo => "hilo",
            PrimaryKeyStrategy.Uuid => "uuid",
            PrimaryKeyStrategy.Guid => "guid",
            _ => throw new NotImplementedException()
        };
    }
}
