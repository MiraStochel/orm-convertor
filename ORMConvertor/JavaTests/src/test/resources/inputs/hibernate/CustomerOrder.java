// The second Hibernate source unit: the Orders table of the fixture schema, whose key has
// two parts. It is the composite-key shape of decisions 006 and 077 - flat key attributes
// on the entity plus a nested key class named by @IdClass - and therefore what the
// negative half of verification level 3 takes an enforced member away from.
package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.time.LocalDate;
import java.util.Objects;

@Entity
@Table(name = "Orders", schema = "{{schema}}")
@IdClass(CustomerOrder.CustomerOrderId.class)
public class CustomerOrder {

    @Id
    @Column(name = "CompanyId")
    private Integer CompanyId;

    @Id
    @Column(name = "OrderId")
    private Integer OrderId;

    @Column(name = "CustomerId", nullable = false)
    private Integer CustomerId;

    @Column(name = "OrderDate", nullable = false)
    private LocalDate OrderDate;

    @Column(name = "IsCancelled", nullable = false)
    private boolean IsCancelled;

    public static class CustomerOrderId implements Serializable {
        private Integer CompanyId;
        private Integer OrderId;

        public CustomerOrderId() {
        }

        @Override
        public boolean equals(Object obj) {
            return obj instanceof CustomerOrderId other
                && Objects.equals(CompanyId, other.CompanyId)
                && Objects.equals(OrderId, other.OrderId);
        }

        @Override
        public int hashCode() {
            return Objects.hash(CompanyId, OrderId);
        }
    }

    public Integer getCompanyId() {
        return CompanyId;
    }

    public void setCompanyId(Integer value) {
        this.CompanyId = value;
    }

    public Integer getOrderId() {
        return OrderId;
    }

    public void setOrderId(Integer value) {
        this.OrderId = value;
    }

    public Integer getCustomerId() {
        return CustomerId;
    }

    public void setCustomerId(Integer value) {
        this.CustomerId = value;
    }

    public LocalDate getOrderDate() {
        return OrderDate;
    }

    public void setOrderDate(LocalDate value) {
        this.OrderDate = value;
    }

    public boolean isIsCancelled() {
        return IsCancelled;
    }

    public void setIsCancelled(boolean value) {
        this.IsCancelled = value;
    }
}
