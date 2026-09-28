// The Orders table under a two-part key, in the shape of decisions 006 and 077: flat key
// attributes plus a nested key class named by @IdClass. Called CustomerOrder because
// `order` is a keyword of JPQL and HQL.
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
@Table(name = "Orders", schema = "Sales")
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

    @Column(name = "PlacedAt", nullable = false)
    private LocalDateTime PlacedAt;

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
}
