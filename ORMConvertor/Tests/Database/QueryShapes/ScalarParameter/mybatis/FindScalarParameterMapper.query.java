package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindScalarParameterMapper {
    List<OrderLine> findScalarParameter(@Param("minQuantity") int minQuantity);
}
