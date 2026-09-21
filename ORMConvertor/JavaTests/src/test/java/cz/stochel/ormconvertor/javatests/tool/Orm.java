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

    /**
     * Every framework the tool translates between, in the order of the enum. The
     * differential matrix walks it to make the pairs of a query (decision 089), so a
     * seventh framework enters that matrix the moment its value stands here.
     */
    public static final int[] ALL = {DAPPER, NHIBERNATE, EF_CORE, HIBERNATE, ECLIPSELINK, MYBATIS};

    private Orm() {
    }

    /**
     * The framework a name stands for, spelled as {@code ORMEnum} spells it. The
     * differential matrix names its source framework in text, and a name nobody can
     * resolve has to stop the run rather than pick a framework by accident.
     */
    public static int forName(String name) {
        for (int orm : ALL) {
            if (nameOf(orm).equals(name)) {
                return orm;
            }
        }

        throw new IllegalArgumentException("No framework is named \"" + name + "\".");
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
