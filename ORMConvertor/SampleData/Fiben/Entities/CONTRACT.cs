namespace Fiben;

public class CONTRACT
{
    public long CONTRACTID { get; set; }

    public long? HASCONTRACTPARTY { get; set; }

    public long? ISMANAGEDBY { get; set; }

    public DateTime? HASEFFECTIVEDATE { get; set; }

    public string? HASPOSITION { get; set; }

    public DateTime? HASEXPIRATIONDATE { get; set; }

    public string? HASTITLE { get; set; }
}
