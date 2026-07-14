namespace NHibernateEntities;

public class CustomerTransaction
{
    public virtual int CustomerTransactionID { get; set; }

    public virtual int CustomerID { get; set; }

    public virtual DateTime TransactionDate { get; set; }

    public virtual decimal TransactionAmount { get; set; }
}
