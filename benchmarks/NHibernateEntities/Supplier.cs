namespace NHibernateEntities;

public class Supplier
{
    public virtual int SupplierID { get; set; }

    public virtual required string SupplierName { get; set; }

    public virtual int SupplierCategoryID { get; set; }

    public virtual int PrimaryContactPersonID { get; set; }

    public virtual int AlternateContactPersonID { get; set; }

    public virtual int DeliveryMethodID { get; set; }

    public virtual int DeliveryCityID { get; set; }

    public virtual int PostalCityID { get; set; }

    public virtual string? SupplierReference { get; set; }

    public virtual string? BankAccountName { get; set; }

    public virtual string? BankAccountBranch { get; set; }

    public virtual string? BankAccountCode { get; set; }

    public virtual string? BankAccountNumber { get; set; }

    public virtual string? BankInternationalCode { get; set; }

    public virtual int PaymentDays { get; set; }

    public virtual string? InternalComments { get; set; }

    public virtual required string PhoneNumber { get; set; }

    public virtual required string FaxNumber { get; set; }

    public virtual required string WebsiteURL { get; set; }

    public virtual required string DeliveryAddressLine1 { get; set; }

    public virtual string? DeliveryAddressLine2 { get; set; }

    public virtual required string DeliveryPostalCode { get; set; }

    public virtual string? DeliveryLocation { get; set; }

    public virtual required string PostalAddressLine1 { get; set; }

    public virtual string? PostalAddressLine2 { get; set; }

    public virtual required string PostalPostalCode { get; set; }

    public virtual int LastEditedBy { get; set; }

    public virtual DateTime ValidFrom { get; set; }

    public virtual DateTime ValidTo { get; set; }
}
