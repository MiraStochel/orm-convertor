// The ShopProductLinks table: the edges of a graph over the products, with a cycle in it,
// under a one-part assigned key of their own.
package Shop;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

@Entity
@Table(name = "ShopProductLinks", schema = "{{schema}}")
public class ShopProductLink {
    @Id
    @Column(name = "LinkId")
    private Integer LinkId;

    @Column(name = "FromProductId", nullable = false)
    private Integer FromProductId;

    @Column(name = "ToProductId", nullable = false)
    private Integer ToProductId;
}
