// The ShopDepartments table: a hierarchy over a one-part assigned key, the parent a plain
// column - the recursive rows of decision 113 walk it, and need no association to.
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
}
