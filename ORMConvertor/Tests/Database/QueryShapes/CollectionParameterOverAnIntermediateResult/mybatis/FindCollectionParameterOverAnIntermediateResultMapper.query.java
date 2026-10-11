package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindCollectionParameterOverAnIntermediateResultMapper {
    List<ShopOrder> findCollectionParameterOverAnIntermediateResult(@Param("ids") List<Integer> ids);
}
