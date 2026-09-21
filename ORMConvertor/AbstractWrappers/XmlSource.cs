using System.Xml;
using System.Xml.Linq;

namespace AbstractWrappers;

/// <summary>
/// What became of one attempt to read a unit as XML (decision 093): the document's root, or
/// the one sentence saying where the reading stopped. Both null means the unit held nothing
/// to read - a blank unit is not a claim (decision 025) - and never both at once.
/// </summary>
public readonly record struct XmlReadResult(XElement? Root, string? Reason);

/// <summary>
/// The XML reading of decision 093, shared by every parser that takes a mapping document:
/// the hbm.xml on both of its passes, the orm.xml of both JPA wrappers, and the MyBatis
/// mapper on both of its passes.
///
/// It exists because malformed XML used to leave the wrappers as an <see cref="XmlException"/>,
/// travel through the orchestration untouched and end as a 400 - so one broken unit took the
/// artifacts of every healthy unit beside it with it, which is exactly what F14 and decision
/// 045 forbid. Input the reader cannot read is a fact about one unit, like a syntax error in
/// SQL or in a Java class, and it is said in the same shape: a Failure record with the
/// position where the reading stopped.
///
/// The sentence lives here and nowhere else, next to <see cref="NestingDepthGuard"/> and for
/// the same reason: five reading sites that each wrote their own would drift apart.
/// </summary>
public static class XmlSource
{
    /// <summary>
    /// The root element of the document, or the reason it could not be read.
    /// </summary>
    /// <param name="ignoreDoctype">
    /// True for a document whose form carries a DOCTYPE - every MyBatis mapper does. The
    /// declaration is then ignored rather than resolved, and the resolver is switched off
    /// with it, so nothing in the document can make the tool fetch anything (threat model,
    /// the entry point of an uploaded unit). It is a property of the document, not of the
    /// failure, which is why it travels as a parameter instead of forking the sentence.
    /// </param>
    public static XmlReadResult Read(string? source, bool ignoreDoctype = false)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return default;
        }

        // Whitespace before the XML declaration is itself an error, so the text has always
        // been trimmed before parsing - and trimming moves every line number that follows.
        // What was cut off the head is therefore measured and added back, or the record would
        // name a line in a text the client never saw and S7 would be met only in appearance.
        var trimmed = source.TrimStart();
        var cut = source.Length - trimmed.Length;
        var head = source.AsSpan(0, cut);
        var lastBreak = head.LastIndexOf('\n');

        try
        {
            return new XmlReadResult(Document(trimmed.TrimEnd(), ignoreDoctype).Root, null);
        }
        catch (XmlException error)
        {
            return new XmlReadResult(
                null,
                Reason(
                    error,
                    lines: head.Count('\n'),
                    columns: lastBreak < 0 ? cut : cut - lastBreak - 1));
        }
    }

    private static XDocument Document(string text, bool ignoreDoctype)
    {
        if (!ignoreDoctype)
        {
            return XDocument.Parse(text);
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

        using var reader = XmlReader.Create(new StringReader(text), settings);

        return XDocument.Load(reader);
    }

    /// <summary>
    /// The one sentence all five XML readings refuse with, in the shape the four other
    /// languages already use. The platform's own message repeats the position in its text and
    /// we keep our prefix anyway: it is the half a machine can read, and one shape across five
    /// languages is worth the repetition.
    /// </summary>
    private static string Reason(XmlException error, int lines, int columns)
    {
        if (error.LineNumber <= 0)
        {
            return $"The XML could not be read: {error.Message}";
        }

        var column = error.LineNumber == 1 ? error.LinePosition + columns : error.LinePosition;

        return $"The XML could not be read at line {error.LineNumber + lines}, column {column}: {error.Message}";
    }
}
