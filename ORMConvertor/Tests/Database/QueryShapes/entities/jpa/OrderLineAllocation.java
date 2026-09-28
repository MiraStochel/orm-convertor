// The OrderLineAllocations table under a four-part key whose leading three parts are the
// foreign key to OrderLines - the join over three columns of the matrices.
package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.util.Objects;

@Entity
@Table(name = "OrderLineAllocations", schema = "Sales")
@IdClass(OrderLineAllocation.OrderLineAllocationId.class)
public class OrderLineAllocation {
    @Id
    @Column(name = "CompanyId")
    private Integer CompanyId;

    @Id
    @Column(name = "OrderId")
    private Integer OrderId;

    @Id
    @Column(name = "LineNumber")
    private Integer LineNumber;

    @Id
    @Column(name = "AllocationId")
    private Integer AllocationId;

    @Column(name = "AllocatedQuantity", nullable = false)
    private int AllocatedQuantity;

    @Column(name = "Notes")
    private String Notes;

    public static class OrderLineAllocationId implements Serializable {
        private Integer CompanyId;
        private Integer OrderId;
        private Integer LineNumber;
        private Integer AllocationId;

        public OrderLineAllocationId() {
        }

        @Override
        public boolean equals(Object obj) {
            return obj instanceof OrderLineAllocationId other
                && Objects.equals(CompanyId, other.CompanyId)
                && Objects.equals(OrderId, other.OrderId)
                && Objects.equals(LineNumber, other.LineNumber)
                && Objects.equals(AllocationId, other.AllocationId);
        }

        @Override
        public int hashCode() {
            return Objects.hash(CompanyId, OrderId, LineNumber, AllocationId);
        }
    }
}
