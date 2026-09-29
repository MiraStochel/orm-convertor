package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindInListWithABoundValueMapper {
    List<ShopOrderLine> findInListWithABoundValue(@Param("extra") int extra);
}
