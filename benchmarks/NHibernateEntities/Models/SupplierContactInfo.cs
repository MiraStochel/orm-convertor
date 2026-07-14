namespace NHibernateEntities.Models;

public class SupplierContactInfo
{
    public int SupplierID { get; set; }

    public required string SupplierName { get; set; }

    public required string PhoneNumber { get; set; }

    public required string FaxNumber { get; set; }

    public required string WebsiteURL { get; set; }

    public DateTime ValidFrom { get; set; }

    public DateTime ValidTo { get; set; }
}
