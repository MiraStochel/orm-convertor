package cz.stochel.ormconvertor.javatests.hibernate;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import org.hibernate.annotations.Nationalized;

/** The shared schema's CustomerProfiles table, see {@link Customer}. */
@Entity
@Table(name = "CustomerProfiles")
public class CustomerProfile {

    @Id
    @Column(name = "CustomerId")
    private Integer customerId;

    @Nationalized
    @Column(name = "Website", length = 200)
    private String website;

    public CustomerProfile() {
    }

    public CustomerProfile(Integer customerId, String website) {
        this.customerId = customerId;
        this.website = website;
    }
}
