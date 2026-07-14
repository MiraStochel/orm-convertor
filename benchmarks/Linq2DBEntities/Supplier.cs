namespace linq2dbEntities;

public class Supplier
{
    public int SupplierID { get; set; }

    public required string SupplierName { get; set; }

    public int SupplierCategoryID { get; set; }

    public int PrimaryContactPersonID { get; set; }

    public int AlternateContactPersonID { get; set; }

    public int DeliveryMethodID { get; set; }

    public int DeliveryCityID { get; set; }

    public int PostalCityID { get; set; }

    public string? SupplierReference { get; set; }

    public string? BankAccountName { get; set; }

    public string? BankAccountBranch { get; set; }

    public string? BankAccountCode { get; set; }

    public string? BankAccountNumber { get; set; }

    public string? BankInternationalCode { get; set; }

    public int PaymentDays { get; set; }

    public string? InternalComments { get; set; }

    public required string PhoneNumber { get; set; }

    public required string FaxNumber { get; set; }

    public required string WebsiteURL { get; set; }

    public required string DeliveryAddressLine1 { get; set; }

    public string? DeliveryAddressLine2 { get; set; }

    public required string DeliveryPostalCode { get; set; }

    public string? DeliveryLocation { get; set; }

    public required string PostalAddressLine1 { get; set; }

    public string? PostalAddressLine2 { get; set; }

    public required string PostalPostalCode { get; set; }

    public int LastEditedBy { get; set; }

    public DateTime ValidFrom { get; set; }

    public DateTime ValidTo { get; set; }
}
