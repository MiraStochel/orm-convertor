package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindInListWithABoundValueMapper {
    List<OrderLine> findInListWithABoundValue(@Param("extra") int extra);
}
