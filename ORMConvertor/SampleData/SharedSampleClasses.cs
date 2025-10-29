namespace SampleData;

public static class SharedSampleClasses
{
    public const string CustomerTransaction = """
        public class CustomerTransaction
        {
            public int CustomerTransactionID { get; set; }

            public int CustomerID { get; set; }

            public DateTime TransactionDate { get; set; }

            public decimal TransactionAmount { get; set; }
        }
        """;
}

