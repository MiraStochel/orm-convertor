package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindLikeWithABoundPrefixMapper {
    List<ShopProduct> findLikeWithABoundPrefix(@Param("prefix") String prefix);
}
