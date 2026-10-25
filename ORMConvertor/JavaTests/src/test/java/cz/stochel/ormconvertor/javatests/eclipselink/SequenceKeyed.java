package cz.stochel.ormconvertor.javatests.eclipselink;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** SEQUENCE with no generator named: the sequence is the implementation's default (decision 187). */
@Entity
@Table(name = "SequenceKeyeds")
public class SequenceKeyed {

    @Id
    @GeneratedValue(strategy = GenerationType.SEQUENCE)
    @Column(name = "SequenceKeyedId")
    private Integer sequenceKeyedId;
}
