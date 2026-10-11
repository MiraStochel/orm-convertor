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
///
/// Since decision 125 the document is read with line information, and <see cref="RangeOf(XElement)"/>
/// and <see cref="RangeOf(XAttribute)"/> turn it into character ranges of the unit as the client
/// sent it - the positions a parser hands to <see cref="AbstractEntityBuilder.At"/>.
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
            var text = trimmed.TrimEnd();
            var document = Document(text, ignoreDoctype);

            // The positions of decision 125 are a function of the text the document was read
            // from, so the text travels with the document - as an annotation, which is what
            // LINQ to XML has for exactly this - and the offset of its first character in the
            // unit as the client sent it goes along, so that every range names the client's text.
            document.AddAnnotation(new Positions(text, cut));

            return new XmlReadResult(document.Root, null);
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

    /// <summary>
    /// The range of an element's start tag - from its opening angle bracket to the closing one
    /// - in the unit as the client sent it, or null for an element of a document that was not
    /// read through <see cref="Read"/>. The start tag rather than the whole element, because
    /// that is where the attributes stating the facts stand, and a whole element would span
    /// every nested statement as well.
    /// </summary>
    public static TextRange? RangeOf(XElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element.Document?.Annotation<Positions>() is not { } positions
            || !((IXmlLineInfo)element).HasLineInfo())
        {
            return null;
        }

        var info = (IXmlLineInfo)element;

        // The line position points at the name, one past the angle bracket.
        var start = positions.Offset(info.LineNumber, info.LinePosition) - 1;
        if (start < 0 || start >= positions.Text.Length || positions.Text[start] != '<')
        {
            return null;
        }

        var end = EndOfStartTag(positions.Text, start);

        return end < 0 ? null : new TextRange(positions.Cut + start, end + 1 - start);
    }

    /// <summary>
    /// The range of an attribute - its name, the equals sign and the quoted value - in the
    /// unit as the client sent it, or null where the document carries no positions.
    /// </summary>
    public static TextRange? RangeOf(XAttribute attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        if (attribute.Document?.Annotation<Positions>() is not { } positions
            || !((IXmlLineInfo)attribute).HasLineInfo())
        {
            return null;
        }

        var info = (IXmlLineInfo)attribute;
        var text = positions.Text;
        var start = positions.Offset(info.LineNumber, info.LinePosition);

        if (start < 0 || start >= text.Length)
        {
            return null;
        }

        // name, optional spaces, '=', optional spaces, a quote, the value, the same quote.
        var index = start;
        while (index < text.Length && text[index] != '=' && !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length || text[index] != '=')
        {
            return null;
        }

        index++;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length || (text[index] != '"' && text[index] != '\''))
        {
            return null;
        }

        var close = text.IndexOf(text[index], index + 1);

        return close < 0 ? null : new TextRange(positions.Cut + start, close + 1 - start);
    }

    /// <summary>The offset of the closing bracket of the start tag opened at <paramref name="start"/>, respecting quotes.</summary>
    private static int EndOfStartTag(string text, int start)
    {
        var quote = '\0';

        for (var index = start + 1; index < text.Length; index++)
        {
            var c = text[index];

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The text a document was read from and where it begins in the client's unit, kept on the
    /// document so that a line and column can be turned into an offset of the client's text.
    /// </summary>
    private sealed class Positions
    {
        private readonly int[] lineStarts;

        public Positions(string text, int cut)
        {
            Text = text;
            Cut = cut;

            var starts = new List<int> { 0 };
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n')
                {
                    starts.Add(index + 1);
                }
            }

            lineStarts = [.. starts];
        }

        public string Text { get; }

        /// <summary>How many characters the client's unit has before the parsed text.</summary>
        public int Cut { get; }

        /// <summary>The offset in <see cref="Text"/> of a one-based line and column, or -1 off the text.</summary>
        public int Offset(int line, int column)
        {
            if (line < 1 || line > lineStarts.Length || column < 1)
            {
                return -1;
            }

            return lineStarts[line - 1] + column - 1;
        }
    }

    private static XDocument Document(string text, bool ignoreDoctype)
    {
        if (!ignoreDoctype)
        {
            return XDocument.Parse(text, LoadOptions.SetLineInfo);
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

        using var reader = XmlReader.Create(new StringReader(text), settings);

        return XDocument.Load(reader, LoadOptions.SetLineInfo);
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
