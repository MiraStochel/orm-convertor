package cz.stochel.ormconvertor.javatests.hibernate;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import org.hibernate.annotations.Nationalized;

/**
 * Maps the shared schema's Customers table in the explicit shape the JPA builder writes
 * (decision 076): every name stated, no association to the profile - the join is an
 * entity join with on, which is what the second claim of decision 077 is about. A
 * top-level class on purpose: Hibernate names a nested class with the enclosing one.
 */
@Entity
@Table(name = "Customers")
public class Customer {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    @Column(name = "CustomerId")
    private Integer customerId;

    @Nationalized
    @Column(name = "Name", nullable = false, length = 100)
    private String name;

    public Customer() {
    }

    public Customer(String name) {
        this.name = name;
    }

    public Integer getCustomerId() {
        return customerId;
    }

    public String getName() {
        return name;
    }
}
