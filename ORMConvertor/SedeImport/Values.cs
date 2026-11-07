using System.Globalization;
using System.Text.RegularExpressions;

namespace SedeImport;

/// <summary>
/// The value of a parameter the author gave no default for (decision 208): chosen once over the
/// data of dba.stackexchange.com, by the name the author gave the placeholder, as the example
/// parameters of the LDBC catalog were chosen over its data (decision 110). A name is matched
/// without regard to case, first whole, then by the word it contains; a name no rule reads gets
/// the value of its type, and the import logs it so that the rules can grow. A query whose
/// values return no rows is noted by the probe of the import and judged with an empty result.
/// </summary>
public static class Values
{
    // What the dump of dba.stackexchange.com (2024-04-06) holds: the user with the most answers
    // (Paul White, 1192), the most used tag (sql-server), a question with an accepted answer and
    // many votes (2371), a year inside the span of the dump (2011-2024), a word many posts hold.
    private const string User = "1192";
    private const string UserName = "Paul White";
    private const string TagName = "sql-server";
    private const string OtherTag = "postgresql";
    private const string Post = "2371";
    private const string Start = "2015-01-01";
    private const string End = "2016-01-01";
    private const string Word = "index";

    /// <summary>Whole names the authors use most, where the word rules below would read them wrong or not at all.</summary>
    private static readonly IReadOnlyDictionary<string, string> ByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = User,
        ["uid"] = User,
        ["name"] = UserName,
        ["username"] = UserName,
        ["displayname"] = UserName,
        ["list"] = User,
        ["userids"] = User,
        ["postids"] = Post,
        ["tag2"] = OtherTag,
        ["tagname2"] = OtherTag,
        ["othertag"] = OtherTag,
        ["secondtag"] = OtherTag,
        ["tags2"] = OtherTag,
        ["excludedtag"] = OtherTag,
        ["exclusion"] = OtherTag,
        ["date2"] = End,
        ["enddate"] = End,
        ["end"] = End,
        ["todate"] = End,
        ["tocreationdate"] = End,
        ["maxdate"] = End,
        ["date1"] = Start,
        ["n"] = "100",
        ["num"] = "100",
        ["a"] = "1",
        ["q"] = Word,
        ["type"] = "1",
        ["parameter"] = "1",
        ["site"] = "dba",
        ["site1"] = "dba",
        ["site2"] = "dba",
        ["language"] = "sql",
        ["languagetag"] = TagName,
        ["emailhash"] = "x",
        ["badge"] = "Nice Answer",
        ["badgename"] = "Nice Answer",
        ["percent"] = "10",
        ["percentdownvotes"] = "10",
        ["uvdvratio"] = "10",
        ["step"] = "100",
        ["chunksize"] = "100",
        ["ownerid"] = User,
        ["modid"] = User,
        ["myid"] = User,
    };

    /// <summary>Words of a name and the value they ask for, read in this order; the first word found decides.</summary>
    private static readonly (Regex Word, string Value)[] ByWord =
    [
        (new Regex("(?i)month", RegexOptions.Compiled), "6"),
        (new Regex("(?i)(days|hours|age)", RegexOptions.Compiled), "365"),
        (new Regex("(?i)(date|asof|since|before|after)", RegexOptions.Compiled), Start),
        (new Regex("(?i)year", RegexOptions.Compiled), "2015"),
        (new Regex("(?i)tag", RegexOptions.Compiled), TagName),
        (new Regex("(?i)(country|location)", RegexOptions.Compiled), "Germany"),
        (new Regex("(?i)city", RegexOptions.Compiled), "London"),
        (new Regex("(?i)(search|text|query|string|vocab|word|term|phrase|pattern|title|keyword)", RegexOptions.Compiled), Word),
        (new Regex("(?i)(url|domain)", RegexOptions.Compiled), "stackoverflow.com"),
        (new Regex("(?i)(user|owner|voter|asker|answerer|opponent|competitor|account|id1|id2)", RegexOptions.Compiled), User),
        (new Regex("(?i)(post|question|answer)", RegexOptions.Compiled), Post),
        (new Regex("(?i)(rep|points)", RegexOptions.Compiled), "1000"),
        (new Regex("(?i)(score|votes|acceptances)", RegexOptions.Compiled), "5"),
        (new Regex("(?i)(ratio|percent)", RegexOptions.Compiled), "10"),
        (new Regex("(?i)(limit|top|howmany|number|num|size|results|count)", RegexOptions.Compiled), "100"),
        (new Regex("(?i)(min|max|threshold|bound|occur|dupes)", RegexOptions.Compiled), "10"),
        (new Regex("(?i)(id)", RegexOptions.Compiled), User),
    ];

    /// <summary>The value for a parameter, and whether a rule chose it or the type alone did. A name the text declared itself is read without its p_ prefix.</summary>
    public static (string Value, bool Chosen) For(string name, string sqlType)
    {
        var asked = name.StartsWith("p_", StringComparison.Ordinal) ? name[2..] : name;

        if (ByName.TryGetValue(asked, out var value))
        {
            return (value, true);
        }

        foreach (var (word, chosen) in ByWord)
        {
            if (word.IsMatch(asked))
            {
                return (chosen, true);
            }
        }

        return (Column.BaseType(sqlType) switch
        {
            "INT" or "BIGINT" or "FLOAT" => "1",
            _ => TagName,
        }, false);
    }

    /// <summary>Whether a text is a whole number: the value of an untyped placeholder that makes it INT.</summary>
    public static bool IsInteger(string value) => long.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    /// <summary>Whether a text is a decimal number with a point: the value of an untyped placeholder that makes it FLOAT.</summary>
    public static bool IsDecimal(string value) => value.Contains('.') && double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
