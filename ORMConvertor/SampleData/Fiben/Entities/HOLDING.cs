namespace Fiben;

public class HOLDING
{
    public long HOLDINGID { get; set; }

    public long? ISHELDBY { get; set; }

    public long? REFERSTO { get; set; }

    public string? HASDESCRIPTION { get; set; }

    public double? HASCOUNT { get; set; }
}
