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

    @Column(name = "ProductName", nullable = false)
    private String ProductName;

    @Column(name = "Sku", nullable = false)
    private String Sku;

    @Column(name = "UnitPrice", nullable = false)
    private BigDecimall UnitPrice;

    @Column(name = "Weight")
    private Double Weight;

    @Column(name = "IsDiscontinued", nullable = false)
    private boolean IsDiscontinued;

    public Integer getProductId() {
        return ProductId;
    }

    public String getProductName() {
        return ProductName;
    }

    public String getSku() {
        return Sku;
    }

    public BigDecimal getUnitPrice() {
        return UnitPrice;
    }

    public Double getWeight() {
        return Weight;
    }

    public boolean isIsDiscontinued() {
        return IsDiscontinued;
    }
}
