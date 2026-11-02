package Shop;

public class ShopDepartment {

    private Integer DepartmentId;

    private Integer ParentDepartmentId;

    private String Name;

    public Integer getDepartmentId() {
        return DepartmentId;
    }

    public void setDepartmentId(Integer value) {
        DepartmentId = value;
    }

    public Integer getParentDepartmentId() {
        return ParentDepartmentId;
    }

    public void setParentDepartmentId(Integer value) {
        ParentDepartmentId = value;
    }

    public String getName() {
        return Name;
    }

    public void setName(String value) {
        Name = value;
    }
}
