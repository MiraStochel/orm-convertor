package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindScalarSubqueryAgainstABoundValueMapper {
    List<ShopOrder> findScalarSubqueryAgainstABoundValue(@Param("minLines") long minLines);
}
