package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

@Entity
@Table(name = "ShopDepartments", schema = "{{schema}}")
public class ShopDepartment {

    @Id
    @Column(name = "DepartmentId")
    private Integer DepartmentId;

    @Column(name = "ParentDepartmentId")
    private Integer ParentDepartmentId;

    @Column(name = "Name", nullable = false)
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
