package Shop;

import java.math.BigDecimal;

public class DifferentialProduct {

    private Integer ProductId;

    private String ProductName;

    private String Sku;

    private BigDecimal UnitPrice;

    private Double Weight;

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

    public Double getWeight() {
        return Weight;
    }

    public void setWeight(Double value) {
        Weight = value;
    }

    public boolean isIsDiscontinued() {
        return IsDiscontinued;
    }

    public void setIsDiscontinued(boolean value) {
        IsDiscontinued = value;
    }
}
