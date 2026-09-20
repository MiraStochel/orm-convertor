package cz.stochel.ormconvertor.javatests.tool;

/**
 * The values of the tool's {@code ORMEnum}, as the REST contract puts them on the wire:
 * numbers, which decision 043 guards with a test of its own. The suite carries them as
 * constants because a process in another runtime has no other way to them - the same
 * reason the frontend keeps its own mirror in {@code js/api.js}.
 */
public final class Orm {

    public static final int DAPPER = 10;
    public static final int NHIBERNATE = 20;
    public static final int EF_CORE = 30;
    public static final int HIBERNATE = 40;
    public static final int ECLIPSELINK = 50;
    public static final int MYBATIS = 60;

    private Orm() {
    }

    /** For a message that names the framework rather than its number. */
    public static String nameOf(int orm) {
        return switch (orm) {
            case DAPPER -> "Dapper";
            case NHIBERNATE -> "NHibernate";
            case EF_CORE -> "EFCore";
            case HIBERNATE -> "Hibernate";
            case ECLIPSELINK -> "EclipseLink";
            case MYBATIS -> "MyBatis";
            default -> "ORM " + orm;
        };
    }
}
