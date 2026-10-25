package cz.stochel.ormconvertor.javatests.eclipselink;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.SequenceGenerator;
import jakarta.persistence.Table;

/** SEQUENCE through a generator that names no sequence: which name the sequence takes (decision 187). */
@Entity
@Table(name = "NamedGeneratorKeyeds")
public class NamedGeneratorKeyed {

    @Id
    @GeneratedValue(strategy = GenerationType.SEQUENCE, generator = "keyed_gen")
    @SequenceGenerator(name = "keyed_gen")
    @Column(name = "NamedGeneratorKeyedId")
    private Integer namedGeneratorKeyedId;
}
