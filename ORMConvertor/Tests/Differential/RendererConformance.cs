using SampleData;
using Tests.LdbcJudge;

namespace Tests.Differential;

/// <summary>
/// The rows both ecosystems render to prove they render alike (decision 089). The list is
/// code rather than data on purpose: every entry has to be expressible in both languages,
/// and a value written into a file would only prove that both suites can read a file.
///
/// The counterpart is <c>RendererConformance.java</c>; the expected text is
/// <c>renderer-conformance.txt</c> beside the schema script, and it is checked in rather
/// than recorded, so neither suite can move the target by re-running itself.
///
/// Each entry is one row. Most carry a single field, because what is being pinned down is
/// one rule each; the last one carries five, because the separator and a null among real
/// values are rules of their own.
/// </summary>
internal static class RendererConformance
{
    /// <summary>The settings the conformance rows are rendered with - the defaults of decision 089.</summary>
    public static ResultRow.RenderSettings Settings => new();

    public static IReadOnlyList<object?[]> Rows =>
    [
        [null],
        [true],
        [false],
        [0],
        [-42],
        [9007199254740993L],

        // Exact decimals: the stated scale is kept even when the value has fewer digits,
        // and a tie is resolved half to even in both directions - the digit before the tie
        // is even in the first case and odd in the second.
        [0m],
        [1250.5m],
        [1.0000005m],
        [1.0000015m],

        // Approximate numbers: trailing zeros go, so that one ecosystem cannot write 3.0
        // where the other writes 3. Every value here is exact in binary floating point, so
        // the case under test is the rendering and not the arithmetic.
        [3.0d],
        [2.5d],
        [0.5d],
        [-1.75d],
        [0.0d],

        ["plain"],
        ["a\"b\\c\t\n\r"],
        [string.Empty],

        [new DateTime(2026, 1, 2, 3, 4, 5, 678)],
        [new DateOnly(2024, 6, 10)],

        [1, "Zither", 1250.5m, null, true],
    ];

    /// <summary>
    /// The conversions of decision 117, which both suites make before they render: the JSON of
    /// LDBC's validation set and the rows of an artifact, each by the kind the binding of the
    /// catalog names. They follow the rows above in the conformance text, so the converters are
    /// held to one text the way the renderers are - and a converter is the part that parses
    /// JSON, which two libraries in two languages could read two ways. The bindings and the JSON
    /// are code in both suites, for the reason the rows above are.
    /// </summary>
    public static IEnumerable<string> LdbcRows()
    {
        // One object is one row; a moment and a date from milliseconds since the epoch in UTC.
        LdbcValidation single = new("IS1", [],
        [
            new("CreationDate", "creationDate", LdbcValueKind.Moment),
            new("Birthday", "birthday", LdbcValueKind.Date),
            new("CityId", "cityId"),
            new("FirstName", "firstName"),
        ]);

        // A flag as a number; a set in ordinal order, of texts and of objects; a sequence in
        // its own order; a weight written with and without a fraction; an empty list as NULL.
        LdbcValidation lists = new("IC0", [],
        [
            new("IsNew", "isNew", LdbcValueKind.Flag),
            new("Emails", "emails", LdbcValueKind.Set, ";"),
            new("Companies", "companies", LdbcValueKind.Set, ";", ["organizationName", "year", "placeName"], ","),
            new("Path", "path", LdbcValueKind.Sequence, ","),
            new("Weight", "weight"),
        ]);

        // Rows of a query the specification does not order completely are compared sorted.
        LdbcValidation unordered = new("IC0", [], [new("Name", "name")], Ordered: false);

        // What an artifact hands back: a date as a moment at midnight, a flag as a truth value,
        // a set joined in no particular order.
        LdbcValidation actual = new("IC0", [],
        [
            new("Birthday", "birthday", LdbcValueKind.Date),
            new("IsNew", "isNew", LdbcValueKind.Flag),
            new("Emails", "emails", LdbcValueKind.Set, ";"),
        ]);

        return
        [
            .. LdbcCanonicalForm.Expected(single, """{"firstName":"Jun","birthday":575424000000,"cityId":507,"creationDate":1331161432355}"""),
            .. LdbcCanonicalForm.Expected(lists, """
                [{"isNew":true,"emails":["b@x.org","a@x.org"],"companies":[{"organizationName":"Zeta","year":2005,"placeName":"Prague"},{"organizationName":"Alpha","year":2001,"placeName":"Brno"}],"path":[3,1,2],"weight":17.0},
                 {"isNew":false,"emails":[],"companies":[],"path":[],"weight":18.5}]
                """),
            .. LdbcCanonicalForm.Expected(unordered, """[{"name":"b"},{"name":"a"}]"""),
            .. LdbcCanonicalForm.Actual(actual, [[new DateTime(1988, 3, 27), true, "c;a;b"]]),
        ];
    }
}
