package cz.stochel.ormconvertor.javatests.hibernate;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.LocalDateTime;

/**
 * Two temporal columns for the first claim of decision 077: one carrying the attribute
 * the JPA builder writes today ({@code precision}), one carrying the attribute Jakarta
 * Persistence 3.2 defines for fractional seconds ({@code secondPrecision}).
 */
@Entity
@Table(name = "Stamps")
public class Stamp {

    @Id
    @Column(name = "StampId")
    private Integer stampId;

    @Column(name = "PlacedAt", precision = 3)
    private LocalDateTime placedAt;

    @Column(name = "SeenAt", secondPrecision = 3)
    private LocalDateTime seenAt;
}
