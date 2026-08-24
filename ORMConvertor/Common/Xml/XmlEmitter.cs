using System.Text;

namespace Common.Xml;

/// <summary>
/// One attribute of an emitted element: a name and the value as the model holds it,
/// undecorated. The value is escaped by <see cref="XmlEmitter"/>, never by the caller -
/// that is the whole point of passing the pair instead of a piece of markup.
/// </summary>
public readonly record struct XmlAttribute(string Name, string Value);

/// <summary>
/// Writes XML mapping documents element by element (decision 046). The emitter takes an
/// element name and attribute pairs, never a finished string of markup, so there is no call
/// site left at which a value could reach the document unescaped: a table, column or
/// generator parameter holding &amp;, &lt;, &gt; or a quote used to produce a document that
/// was not well formed at all, which F11 claims it cannot.
///
/// Correctness is therefore structural rather than remembered. The emitted text is
/// deliberately ours and not a serializer's: S2 asks for byte-wise identical artifacts and
/// the exact shape of the mapping is what the tests assert, so indentation, attribute order
/// and the empty-element form stay decided here.
///
/// Lives in Common because a mapping document is not NHibernate's alone - JPA's orm.xml and
/// a MyBatis mapper are XML too (F7-F9), and with the writer here they inherit the guarantee
/// instead of repeating it.
/// </summary>
public static class XmlEmitter
{
    private const int IndentWidth = 4;

    /// <summary>The XML declaration. No model value takes part, so nothing is escaped.</summary>
    public static void Prolog(StringBuilder xml)
    {
        ArgumentNullException.ThrowIfNull(xml);

        xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
    }

    /// <summary>An element that has children: the opening tag alone.</summary>
    public static void Open(
        StringBuilder xml,
        int indentLevels,
        string name,
        IEnumerable<XmlAttribute>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(xml);

        xml.AppendLine($"{Indent(indentLevels)}<{name}{Render(attributes)}>");
    }

    /// <summary>An element with no content.</summary>
    public static void Empty(
        StringBuilder xml,
        int indentLevels,
        string name,
        IEnumerable<XmlAttribute>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(xml);

        xml.AppendLine($"{Indent(indentLevels)}<{name}{Render(attributes)} />");
    }

    /// <summary>An element whose content is text, written on one line.</summary>
    public static void Text(
        StringBuilder xml,
        int indentLevels,
        string name,
        string text,
        IEnumerable<XmlAttribute>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(xml);

        xml.AppendLine($"{Indent(indentLevels)}<{name}{Render(attributes)}>{EscapeText(text)}</{name}>");
    }

    /// <summary>
    /// The closing tag of an element opened with <see cref="Open"/>.
    /// </summary>
    /// <param name="appendLine">
    /// False for the last line of the document, which carries no trailing newline.
    /// </param>
    public static void Close(StringBuilder xml, int indentLevels, string name, bool appendLine = true)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var line = $"{Indent(indentLevels)}</{name}>";

        if (appendLine)
        {
            xml.AppendLine(line);
        }
        else
        {
            xml.Append(line);
        }
    }

    /// <summary>
    /// Escaping of an attribute value. The minimal set: the three markup characters, the
    /// delimiter (attributes here are always double-quoted, so the apostrophe stays as it
    /// is), and the whitespace a parser would otherwise normalize away - a value holding a
    /// carriage return has to come back as it went in for the round trip to close.
    /// </summary>
    public static string EscapeAttribute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return EscapeText(value)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("\r", "&#xD;", StringComparison.Ordinal)
            .Replace("\n", "&#xA;", StringComparison.Ordinal)
            .Replace("\t", "&#x9;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Escaping of element content. The ampersand goes first, otherwise the entities the
    /// later replacements introduce would be escaped a second time.
    /// </summary>
    public static string EscapeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string Indent(int levels) => new(' ', levels * IndentWidth);

    private static string Render(IEnumerable<XmlAttribute>? attributes)
        => attributes is null
            ? string.Empty
            : string.Concat(attributes.Select(a => $" {a.Name}=\"{EscapeAttribute(a.Value)}\""));
}
