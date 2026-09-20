using System.Xml;
using System.Xml.Linq;

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
    /// <summary>The &lt;mapper&gt; root of the document, or null when the unit is not one.</summary>
    public static XElement? Root(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

        using var reader = XmlReader.Create(new StringReader(source.Trim()), settings);
        var root = XDocument.Load(reader).Root;

        return root?.Name.LocalName == "mapper" ? root : null;
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
