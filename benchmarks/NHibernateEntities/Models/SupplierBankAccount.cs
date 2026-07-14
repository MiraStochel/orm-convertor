namespace NHibernateEntities.Models;

public class SupplierBankAccount
{
    public int SupplierID { get; set; }

    public string? BankAccountName { get; set; }

    public string? BankAccountBranch { get; set; }

    public string? BankAccountCode { get; set; }

    public string? BankAccountNumber { get; set; }

    public string? BankInternationalCode { get; set; }
}
