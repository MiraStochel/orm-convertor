// The OrderLines table under a three-part key whose leading two parts are the foreign key
// to Orders - the join over two columns of the matrices.
package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.IdClass;
import jakarta.persistence.Table;
import java.io.Serializable;
import java.math.BigDecimal;
import java.util.Objects;

@Entity
@Table(name = "OrderLines", schema = "Sales")
@IdClass(OrderLine.OrderLineId.class)
public class OrderLine {
    @Id
    @Column(name = "CompanyId")
    private Integer CompanyId;

    @Id
    @Column(name = "OrderId")
    private Integer OrderId;

    @Id
    @Column(name = "LineNumber")
    private Integer LineNumber;

    @Column(name = "ProductId", nullable = false)
    private Integer ProductId;

    @Column(name = "Description", nullable = false)
    private String Description;

    @Column(name = "Quantity", nullable = false)
    private int Quantity;

    @Column(name = "UnitPrice", nullable = false)
    private BigDecimal UnitPrice;

    public static class OrderLineId implements Serializable {
        private Integer CompanyId;
        private Integer OrderId;
        private Integer LineNumber;

        public OrderLineId() {
        }

        @Override
        public boolean equals(Object obj) {
            return obj instanceof OrderLineId other
                && Objects.equals(CompanyId, other.CompanyId)
                && Objects.equals(OrderId, other.OrderId)
                && Objects.equals(LineNumber, other.LineNumber);
        }

        @Override
        public int hashCode() {
            return Objects.hash(CompanyId, OrderId, LineNumber);
        }
    }
}
