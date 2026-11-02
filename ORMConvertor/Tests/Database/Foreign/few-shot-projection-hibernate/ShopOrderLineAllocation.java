package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.util.Objects;

@Entity
@Table(name = "ShopOrderLineAllocations", schema = "{{schema}}")
@IdClass(ShopOrderLineAllocation.ShopOrderLineAllocationId.class)
public class ShopOrderLineAllocation {

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

    public Integer getAllocationId() {
        return AllocationId;
    }

    public void setAllocationId(Integer value) {
        AllocationId = value;
    }

    public int getAllocatedQuantity() {
        return AllocatedQuantity;
    }

    public void setAllocatedQuantity(int value) {
        AllocatedQuantity = value;
    }

    public String getNotes() {
        return Notes;
    }

    public void setNotes(String value) {
        Notes = value;
    }

    public static class ShopOrderLineAllocationId implements Serializable {
        private Integer CompanyId;
        private Integer OrderId;
        private Integer LineNumber;
        private Integer AllocationId;

        public ShopOrderLineAllocationId() {
        }

        @Override
        public boolean equals(Object obj) {
            return obj instanceof ShopOrderLineAllocationId other
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
