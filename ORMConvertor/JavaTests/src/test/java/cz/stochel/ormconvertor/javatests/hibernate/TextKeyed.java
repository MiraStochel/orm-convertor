package cz.stochel.ormconvertor.javatests.hibernate;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** A key over a String that states no nullability, beside a String column outside the key (decision 188). */
@Entity
@Table(name = "TextKeyeds")
public class TextKeyed {

    @Id
    @Column(name = "Code")
    private String code;

    @Column(name = "Note")
    private String note;
}
