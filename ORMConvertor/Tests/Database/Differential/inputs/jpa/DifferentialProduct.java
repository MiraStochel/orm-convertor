// The JPA source of the differential matrix (decision 089), read by both implementation
// profiles: an entity that states every name has no room for the defaults Hibernate and
// EclipseLink differ in, so one text really is both sources (decisions 077 and 080).
package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;

@Entity
@Table(name = "DifferentialProducts", schema = "{{schema}}")
public class DifferentialProduct {

    @Id
    @Column(name = "ProductId")
    private Integer ProductId;

    @Column(name = "ProductName", length = 100, nullable = false)
    private String ProductName;

    @Column(name = "Sku", length = 32, nullable = false)
    private String Sku;

    @Column(name = "UnitPrice", precision = 18, scale = 4, nullable = false)
    private BigDecimal UnitPrice;

    @Column(name = "Weight")
    private Double Weight;

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

    public Double getWeight() {
        return Weight;
    }

    public void setWeight(Double value) {
        this.Weight = value;
    }

    public boolean isIsDiscontinued() {
        return IsDiscontinued;
    }

    public void setIsDiscontinued(boolean value) {
        this.IsDiscontinued = value;
    }
}
