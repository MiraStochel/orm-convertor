// The ShopProducts table under an assigned one-part key.
package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.math.BigDecimal;

@Entity
@Table(name = "ShopProducts", schema = "{{schema}}")
public class ShopProduct {
    @Id
    @Column(name = "ProductId")
    private Integer ProductId;

    @Column(name = "ProductName", nullable = false)
    private String ProductName;

    @Column(name = "Sku", nullable = false)
    private String Sku;

    @Column(name = "UnitPrice", nullable = false)
    private BigDecimal UnitPrice;

    @Column(name = "IsDiscontinued", nullable = false)
    private boolean IsDiscontinued;
}
