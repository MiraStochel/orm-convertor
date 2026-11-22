using System.Globalization;

namespace SedeImport;

/// <summary>
/// Where a query of the catalog comes from and under which licence it stands. The title, the
/// description and the text of a query are content of a user of the Stack Exchange Data
/// Explorer, licensed CC BY-SA in the version of the day it was contributed; the set
/// (hirupert/sede, Apache-2.0) is the selection of the queries, not a new licence of their
/// texts. The catalog therefore states with every query the day of its creation in
/// UTC, the version of the licence that day falls under and the address of the query in
/// the Data Explorer, where its author is named.
/// </summary>
public static class Origin
{
    /// <summary>The first day of CC BY-SA 3.0 on Stack Exchange; a contribution before it is under 2.5.</summary>
    private static readonly DateOnly Version3 = new(2011, 4, 8);

    /// <summary>The first day of CC BY-SA 4.0 on Stack Exchange.</summary>
    private static readonly DateOnly Version4 = new(2018, 5, 2);

    /// <summary>What of the catalog stands under which licence: the header of queries.json.</summary>
    public const string Licence =
        "The title, the description and the text of every query (title, description, sedeSql) are content of the users of the "
        + "Stack Exchange Data Explorer under CC BY-SA, in the version of the day the query was created: 2.5 before 2011-04-08, "
        + "3.0 before 2018-05-02, 4.0 from then on (https://creativecommons.org/licenses/by-sa/2.5/, /3.0/, /4.0/). Every query "
        + "states that day in UTC (created), its version (licence) and its address in the Data Explorer, where its author is named "
        + "(source). The text in sql is the author's text adapted - the placeholders of the Data Explorer turned into parameters, "
        + "nothing else - and stands under the licence of the text it adapts. The selection of the queries and their division "
        + "into parts are the SEDE data set (hirupert/sede, Apache-2.0). The other fields are this project's (MIT).";

    /// <summary>
    /// The time zone the moments of the set are shifted by. The set states the creation of a
    /// query as its moment in UTC read as a time of the east of the United States and turned
    /// into UTC once more: four hours later than the Data Explorer shows in summer, five in
    /// winter (s1233: 2010-05-27 20:08:16 in the set, 16:08:16Z there; s15605: 2010-12-03
    /// 0:10:46 in the set, 2010-12-02 19:10:46Z there).
    /// </summary>
    private static readonly TimeZoneInfo ShiftOfTheSet = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>
    /// The day the query was created, in UTC as the Data Explorer shows it: the moment the set
    /// states with its shift taken back.
    /// </summary>
    public static DateOnly CreatedOf(SedeEntry entry, string key)
        => DateTime.TryParseExact(entry.CreationDate, "yyyy-MM-dd H:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stated)
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(stated, DateTimeKind.Utc), ShiftOfTheSet))
            : throw new InvalidDataException($"{key}: the set states no creation date ('{entry.CreationDate}'), so the version of its licence is unknown.");

    /// <summary>The version of CC BY-SA a contribution of the day stands under.</summary>
    public static string LicenceOf(DateOnly created)
        => created < Version3 ? "CC BY-SA 2.5" : created < Version4 ? "CC BY-SA 3.0" : "CC BY-SA 4.0";

    /// <summary>The address of the query in the Data Explorer.</summary>
    public static string SourceOf(int querySetId)
        => $"https://data.stackexchange.com/stackoverflow/query/{querySetId.ToString(CultureInfo.InvariantCulture)}";
}
