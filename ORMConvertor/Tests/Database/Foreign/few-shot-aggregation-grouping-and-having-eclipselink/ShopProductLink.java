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

    public Integer getLinkId() {
        return LinkId;
    }

    public void setLinkId(Integer value) {
        LinkId = value;
    }

    public Integer getFromProductId() {
        return FromProductId;
    }

    public void setFromProductId(Integer value) {
        FromProductId = value;
    }

    public Integer getToProductId() {
        return ToProductId;
    }

    public void setToProductId(Integer value) {
        ToProductId = value;
    }
}
