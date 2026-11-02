package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.math.BigDecimal;
import java.util.Objects;

@Entity
@Table(name = "ShopOrderLines", schema = "{{schema}}")
@IdClass(ShopOrderLine.ShopOrderLineId.class)
public class ShopOrderLine {

    @Id
    @Column(name = "CompanyId")
    private Integer CompanyId;

    @Id
    @Column(name = "OrderId")
    private Integer OrderId;

    @Id
    @Column(name = "LineNumber")
    private Integer LineNumber;

    @Column(name = "ProductId", nullable = false)
    private Integer ProductId;

    @Column(name = "Description", nullable = false)
    private String Description;

    @Column(name = "Quantity", nullable = false)
    private int Quantity;

    @Column(name = "UnitPrice", nullable = false)
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

    public static class ShopOrderLineId implements Serializable {
        private Integer CompanyId;
        private Integer OrderId;
        private Integer LineNumber;

        public ShopOrderLineId() {
        }

        @Override
        public boolean equals(Object obj) {
            return obj instanceof ShopOrderLineId other
                && Objects.equals(CompanyId, other.CompanyId)
                && Objects.equals(OrderId, other.OrderId)
                && Objects.equals(LineNumber, other.LineNumber);
        }

        @Override
        public int hashCode() {
            return Objects.hash(CompanyId, OrderId, LineNumber);
        }
    }
}
