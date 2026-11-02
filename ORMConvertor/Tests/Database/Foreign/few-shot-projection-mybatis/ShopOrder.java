package Shop;

import java.time.LocalDateTime;

public class ShopOrder {

    private Integer CompanyId;

    private Integer OrderId;

    private Integer CustomerId;

    private LocalDateTime PlacedAt;

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
}
