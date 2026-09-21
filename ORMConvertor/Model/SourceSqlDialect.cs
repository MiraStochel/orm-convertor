namespace Model;

/// <summary>
/// What the source of a conversion may declare about the dialect its literal SQL is
/// written in (decision 088). The counterpart of <see cref="DatabaseDialect"/> on the other
/// side: the target dialect is declared by whoever writes - the target framework's
/// descriptor - and the source dialect by whoever submits, in the conversion request.
///
/// It is deliberately <em>not</em> <see cref="DatabaseDialect"/>. A value of that
/// vocabulary picks a table of type spellings and every one of its values must be
/// writable, so a foreign system in it would make TargetFrameworkDescriptor.Dialect
/// expressible for a system SqlTypeSpelling.Name throws on.
///
/// It is not a list of system names either. The tool behaves identically towards every
/// system it does not read - it does not read them - so a name would select nothing, and a
/// vocabulary of names of systems we do not read is a promise we do not keep: to
/// "Oracle19c" the reasonable question is "so you read Oracle?" and the answer is no. The
/// record therefore does not say "the source was Oracle", it says "the source claims its
/// SQL is not the SQL this version reads", which is the whole of what the tool knows.
///
/// Two values are a shape for today that survives tomorrow. When more dialects can be
/// read, <see cref="AnotherSystem"/> breaks up into named systems and the same field turns
/// from a guard into a switch: the shape does not grow, the vocabulary does. The value
/// 1000 stands outside the run of tens precisely so that named systems can be placed among
/// the existing values rather than only after them.
/// </summary>
public enum SourceSqlDialect
{
    /// <summary>
    /// The source states its literal SQL is SQL Server 2022 - the one dialect this version
    /// reads, so reading proceeds exactly as it does without any declaration at all. The
    /// difference between the two is not in the reading but in the run record (S6): this
    /// value is a statement, an absent declaration is not.
    /// </summary>
    SqlServer2022 = 10,

    /// <summary>
    /// The source states its literal SQL is written for some other database system. The
    /// reading of that SQL stops: a query is not emitted (Failure) and a literal column
    /// type is not read (Loss of category DatabaseType). Nothing is translated that would
    /// first have to be guessed - `timestamp` is eight bytes of binary in T-SQL and an
    /// instant in time elsewhere, and no analysis of the text can tell which the author
    /// meant.
    /// </summary>
    AnotherSystem = 1000,
}
