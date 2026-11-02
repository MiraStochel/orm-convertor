package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

@Entity
@Table(name = "ShopCustomers", schema = "{{schema}}")
public class ShopCustomer {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @Column(name = "CustomerId")
    private Integer CustomerId;

    @Column(name = "Name", nullable = false)
    private String Name;

    @Column(name = "Notes")
    private String Notes;

    public Integer getCustomerId() {
        return CustomerId;
    }

    public void setCustomerId(Integer value) {
        CustomerId = value;
    }

    public String getName() {
        return Name;
    }

    public void setName(String value) {
        Name = value;
    }

    public String getNotes() {
        return Notes;
    }

    public void setNotes(String value) {
        Notes = value;
    }
}
