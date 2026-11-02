package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.time.LocalDateTime;
import java.util.Objects;

@Entity
@Table(name = "ShopOrders", schema = "{{schema}}")
@IdClass(ShopOrder.ShopOrderId.class)
public class ShopOrder {

    @Id
    @Column(name = "CompanyId")
    private Integer CompanyId;

    @Id
    @Column(name = "OrderId")
    private Integer OrderId;

    @Column(name = "CustomerId", nullable = false)
    private Integer CustomerId;

    @Column(name = "PlacedAt", nullable = false)
    private LocalDateTime PlacedAt;

    @Column(name = "IsCancelled", nullable = false)
    private boolean IsCancelled;

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

    public Integer getCustomerId() {
        return CustomerId;
    }

    public void setCustomerId(Integer value) {
        CustomerId = value;
    }

    public LocalDateTime getPlacedAt() {
        return PlacedAt;
    }

    public void setPlacedAt(LocalDateTime value) {
        PlacedAt = value;
    }

    public boolean isIsCancelled() {
        return IsCancelled;
    }

    public void setIsCancelled(boolean value) {
        IsCancelled = value;
    }

    public static class ShopOrderId implements Serializable {
        private Integer CompanyId;
        private Integer OrderId;

        public ShopOrderId() {
        }

        @Override
        public boolean equals(Object obj) {
            return obj instanceof ShopOrderId other
                && Objects.equals(CompanyId, other.CompanyId)
                && Objects.equals(OrderId, other.OrderId);
        }

        @Override
        public int hashCode() {
            return Objects.hash(CompanyId, OrderId);
        }
    }
}
