package cz.stochel.ormconvertor.javatests.eclipselink;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.util.Objects;

/**
 * A composite key of two parts in the shape the JPA builder writes - {@code @IdClass} with the
 * key class nested in the entity -, neither part stating a nullability, beside a column
 * outside the key (decision 188).
 */
@Entity
@Table(name = "PairKeyeds")
@IdClass(PairKeyed.Key.class)
public class PairKeyed {

    @Id
    @Column(name = "A")
    private String a;

    @Id
    @Column(name = "B")
    private Integer b;

    @Column(name = "Note")
    private String note;

    public static class Key implements Serializable {

        private String a;
        private Integer b;

        public Key() {
        }

        @Override
        public boolean equals(Object other) {
            return other instanceof Key key && Objects.equals(a, key.a) && Objects.equals(b, key.b);
        }

        @Override
        public int hashCode() {
            return Objects.hash(a, b);
        }
    }
}
