package cz.stochel.ormconvertor.javatests.eclipselink;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/**
 * The three claims of decision 080 in one entity: a key whose generation is left to the
 * implementation - which is a counter table here and a sequence under the other one - a
 * national column, which EclipseLink can only be told about through the literal type, and
 * a plain String beside it, which is not national in either implementation.
 */
@Entity
@Table(name = "Widgets")
public class Widget {

    @Id
    @GeneratedValue
    @Column(name = "WidgetId")
    private Integer widgetId;

    @Column(name = "Label", length = 200, columnDefinition = "nvarchar(200)")
    private String label;

    @Column(name = "Note", length = 200)
    private String note;
}
