package cz.stochel.ormconvertor.javatests.hibernate;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** An assigned key over a wrapper type that states no nullability, beside a column of the same type outside the key (decision 188). */
@Entity
@Table(name = "AssignedKeyeds")
public class AssignedKeyed {

    @Id
    @Column(name = "AssignedKeyedId")
    private Integer assignedKeyedId;

    @Column(name = "Note")
    private Integer note;
}
