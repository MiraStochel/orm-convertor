// The domain of the query-shape matrices as MyBatis has it: plain classes with nothing of
// the framework on them (decision 084). What maps them stands in ShopMapper.xml.
package Shop;

public class Customer {
    private Integer CustomerId;
    private String Name;
    private String Notes;
}
