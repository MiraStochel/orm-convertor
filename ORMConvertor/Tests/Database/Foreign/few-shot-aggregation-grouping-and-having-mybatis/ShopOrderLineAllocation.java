package Shop;

public class ShopOrderLineAllocation {

    private Integer CompanyId;

    private Integer OrderId;

    private Integer LineNumber;

    private Integer AllocationId;

    private int AllocatedQuantity;

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
}
