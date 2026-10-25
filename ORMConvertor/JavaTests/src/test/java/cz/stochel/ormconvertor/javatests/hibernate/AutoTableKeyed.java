package cz.stochel.ormconvertor.javatests.hibernate;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import jakarta.persistence.TableGenerator;

/** AUTO that names a table generator: whether the generator decides the mechanism (decision 187). */
@Entity
@Table(name = "AutoTableKeyeds")
public class AutoTableKeyed {

    @Id
    @GeneratedValue(generator = "auto_table")
    @TableGenerator(name = "auto_table", table = "AutoCounters", pkColumnName = "CounterName", valueColumnName = "CounterValue")
    @Column(name = "AutoTableKeyedId")
    private Integer autoTableKeyedId;
}
