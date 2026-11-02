package Shop;

public class ShopProductLink {

    private Integer LinkId;

    private Integer FromProductId;

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
