// A Hibernate source unit of the Java suite (decision 078): the Products table of the
// shared fixture schema, in the explicit JPA shape the builder writes. The key is
// assigned - Products carries no IDENTITY - so verification level 4 sets it itself, and
// the schema is stated so that the catalog completion phase looks the table up in this
// suite's schema and not in the .NET suite's.
package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;

@Entity
@Table(name = "Products", schema = "{{schema}}")
public class Product {

    @Id
    @Column(name = "ProductId")
    private Integer ProductId;

    @Column(name = "ProductName", length = 100, nullable = false)
    private String ProductName;

    @Column(name = "Sku", length = 32, nullable = false)
    private String Sku;

    @Column(name = "UnitPrice", precision = 18, scale = 4, nullable = false)
    private BigDecimal UnitPrice;

    @Column(name = "IsDiscontinued", nullable = false)
    private boolean IsDiscontinued;

    public Integer getProductId() {
        return ProductId;
    }

    public void setProductId(Integer value) {
        this.ProductId = value;
    }

    public String getProductName() {
        return ProductName;
    }

    public void setProductName(String value) {
        this.ProductName = value;
    }

    public String getSku() {
        return Sku;
    }

    public void setSku(String value) {
        this.Sku = value;
    }

    public BigDecimal getUnitPrice() {
        return UnitPrice;
    }

    public void setUnitPrice(BigDecimal value) {
        this.UnitPrice = value;
    }

    public boolean isIsDiscontinued() {
        return IsDiscontinued;
    }

    public void setIsDiscontinued(boolean value) {
        this.IsDiscontinued = value;
    }
}
