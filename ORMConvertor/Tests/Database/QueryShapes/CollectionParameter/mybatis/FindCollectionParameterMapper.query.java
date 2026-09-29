package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindCollectionParameterMapper {
    List<OrderLine> findCollectionParameter(@Param("ids") List<Integer> ids);
}
