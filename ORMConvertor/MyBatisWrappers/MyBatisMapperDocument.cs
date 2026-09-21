using System.Xml.Linq;
using AbstractWrappers;

namespace MyBatisWrappers;

/// <summary>
/// Reading of a MyBatis mapper document. It exists for one reason: every mapper carries the
/// DOCTYPE of the MyBatis DTD, and the default reader behind XDocument.Parse refuses a
/// document that has one. The declaration is therefore ignored rather than resolved, and the
/// resolver is switched off with it, so that nothing in the document can make the tool fetch
/// anything (threat model, the entry point of an uploaded unit).
/// </summary>
internal static class MyBatisMapperDocument
{
    /// <summary>
    /// The &lt;mapper&gt; root of the document, or the reason the text could not be read at all
    /// (decision 093). A unit that reads as XML but is not a mapper is neither: it carries no
    /// root and no reason, because a document of some other form is nothing this parser has to
    /// say anything about.
    /// </summary>
    public static XmlReadResult Read(string source)
    {
        var read = XmlSource.Read(source, ignoreDoctype: true);

        if (read.Reason is not null)
        {
            return read;
        }

        return read.Root?.Name.LocalName == "mapper" ? read : default;
    }

    /// <summary>The namespace the document declares; the empty string where it declares none.</summary>
    public static string NamespaceOf(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.Attribute("namespace")?.Value ?? string.Empty;
    }

    /// <summary>The elements MyBatis counts as statements, whose ids share one collection.</summary>
    public static bool IsStatement(string elementName)
        => elementName is "select" or "insert" or "update" or "delete";
}
