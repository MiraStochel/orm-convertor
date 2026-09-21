using Tests.Database;

namespace Tests.Differential;

/// <summary>
/// The files of the differential verification, read from where both suites read them
/// (decision 089): beside the schema script in <c>Tests/Database/Differential</c>, which
/// the .NET suite embeds and the Java suite picks up as a test resource - the mechanism
/// decision 076 already set up for the schema itself, so nothing new is shared here.
///
/// The <c>{{schema}}</c> placeholder is substituted exactly as the DDL script's is, which
/// is also how each suite points its inputs at its own schema when both stand in the same
/// database at once.
/// </summary>
internal static class DifferentialData
{
    private const string ResourcePrefix = "Tests.Database.Differential.";
    private const string SchemaPlaceholder = "{{schema}}";

    /// <summary>Text of one file, with the schema placeholder substituted.</summary>
    public static string Read(string path)
        => ReadRaw(path).Replace(SchemaPlaceholder, TestDatabase.SchemaName);

    /// <summary>
    /// Text of one file as it stands. Canonical results and the conformance text are read
    /// this way: they state values, not schema names, and a substitution in them would be a
    /// way for a run to move its own target.
    /// </summary>
    public static string ReadRaw(string path)
    {
        var resource = ResourcePrefix + path.Replace('/', '.');
        using var stream = typeof(DifferentialData).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"The differential resource \"{resource}\" is missing. Files under "
                + "Tests/Database/Differential are embedded by Tests.csproj; a new one needs no "
                + "entry there, but it does need to be in the working copy.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("﻿", string.Empty);
    }

    /// <summary>
    /// Lines of one file, with the line ending of the checkout taken out of the verdict.
    /// The canonical results are text files under the repository's CRLF rule, so comparing
    /// whole files byte for byte would compare the checkout, not the query.
    /// </summary>
    public static List<string> ReadLines(string path)
    {
        var lines = ReadRaw(path).Replace("\r\n", "\n").Split('\n').ToList();

        // A text file ends with a line break, so the split leaves one empty entry behind.
        // Only the last one goes: an empty line in the middle of a file is a row of the
        // result and dropping it would be a comparison of something else.
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    /// <summary>Every differential resource the assembly carries, by its path under the folder.</summary>
    public static IEnumerable<string> Paths()
        => typeof(DifferentialData).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..]);
}
