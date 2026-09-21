// The MyBatis source of the differential matrix (decision 089): a domain class with
// nothing on it at all - no annotation, no base class, no import from the framework
// (decision 084). What maps it stands in the mapper beside the statement.
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
