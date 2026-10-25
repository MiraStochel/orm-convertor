package cz.stochel.ormconvertor.javatests.hibernate;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.Id;
import jakarta.persistence.SequenceGenerator;
import jakarta.persistence.Table;

/** AUTO that names a sequence generator: whether the generator decides the mechanism (decision 187). */
@Entity
@Table(name = "AutoNamedKeyeds")
public class AutoNamedKeyed {

    @Id
    @GeneratedValue(generator = "auto_gen")
    @SequenceGenerator(name = "auto_gen", sequenceName = "AutoNamed_Seq", allocationSize = 20)
    @Column(name = "AutoNamedKeyedId")
    private Integer autoNamedKeyedId;
}
