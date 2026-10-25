package cz.stochel.ormconvertor.javatests.hibernate;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/**
 * A key whose generation is left to the implementation - the bare annotation, which means
 * {@code AUTO} - over a table named otherwise than the entity, so that the measurement
 * tells which of the two names the implementation's sequence takes (decision 187).
 */
@Entity
@Table(name = "AutoKeyeds")
public class AutoKeyed {

    @Id
    @GeneratedValue
    @Column(name = "AutoKeyedId")
    private Integer autoKeyedId;
}
