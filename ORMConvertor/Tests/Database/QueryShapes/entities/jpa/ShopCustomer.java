// The domain of the query-shape matrices as both JPA implementations read it: one public
// class per unit, every name stated, so that one text serves Hibernate and EclipseLink
// alike (decisions 077 and 080). Fields without accessors are properties with both.
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
}
