package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;

@Entity
@Table(name = "ShopProducts", schema = "{{schema}}")
public class ShopProduct {

    @Id
    @Column(name = "ProductId")
    private Integer ProductId;

    @Column(name = "ProductName", nullable = false)
    private String ProductName;

    @Column(name = "Sku", nullable = false)
    private String Sku;

    @Column(name = "UnitPrice", nullable = false)
    private BigDecimal UnitPrice;

    @Column(name = "IsDiscontinued", nullable = false)
    private boolean IsDiscontinued;

    public Integer getProductId() {
        return ProductId;
    }

    public void setProductId(Integer value) {
        ProductId = value;
    }

    public String getProductName() {
        return ProductName;
    }

    public void setProductName(String value) {
        ProductName = value;
    }

    public String getSku() {
        return Sku;
    }

    public void setSku(String value) {
        Sku = value;
    }

    public BigDecimal getUnitPrice() {
        return UnitPrice;
    }

    public void setUnitPrice(BigDecimal value) {
        UnitPrice = value;
    }

    public boolean isIsDiscontinued() {
        return IsDiscontinued;
    }

    public void setIsDiscontinued(boolean value) {
        IsDiscontinued = value;
    }
}
