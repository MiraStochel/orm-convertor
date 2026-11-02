package Shop;

import java.math.BigDecimal;

public class ShopOrderLine {

    private Integer CompanyId;

    private Integer OrderId;

    private Integer LineNumber;

    private Integer ProductId;

    private String Description;

    private int Quantity;

    private BigDecimal UnitPrice;

    public Integer getCompanyId() {
        return CompanyId;
    }

    public void setCompanyId(Integer value) {
        CompanyId = value;
    }

    public Integer getOrderId() {
        return OrderId;
    }

    public void setOrderId(Integer value) {
        OrderId = value;
    }

    public Integer getLineNumber() {
        return LineNumber;
    }

    public void setLineNumber(Integer value) {
        LineNumber = value;
    }

    public Integer getProductId() {
        return ProductId;
    }

    public void setProductId(Integer value) {
        ProductId = value;
    }

    public String getDescription() {
        return Description;
    }

    public void setDescription(String value) {
        Description = value;
    }

    public int getQuantity() {
        return Quantity;
    }

    public void setQuantity(int value) {
        Quantity = value;
    }

    public BigDecimal getUnitPrice() {
        return UnitPrice;
    }

    public void setUnitPrice(BigDecimal value) {
        UnitPrice = value;
    }
}
