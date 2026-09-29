package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindScalarParameterMapper {
    List<ShopOrderLine> findScalarParameter(@Param("minQuantity") int minQuantity);
}
