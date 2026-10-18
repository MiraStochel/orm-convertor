namespace Fiben;

public class SECURITIESTRANSACTION
{
    public long SECURITIESTRANSACTIONID { get; set; }

    public long? ISFACILITATEDBY { get; set; }

    public long? HASPRICE { get; set; }

    public long? REFERSTO { get; set; }

    public string? HASTYPE { get; set; }

    public DateTime? HASSETTLEMENTDATE { get; set; }

    public double? HASCOUNT { get; set; }
}
