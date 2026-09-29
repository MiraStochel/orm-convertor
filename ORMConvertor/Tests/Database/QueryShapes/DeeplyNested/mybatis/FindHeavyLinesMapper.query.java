// The mapper interface beside FindHeavyLinesMapper.xml: the one place MyBatis keeps the
// type of a parameter (decision 084), so the four of the statement are typed here. The
// extension says JavaQuery - the interface is read on the query pass as the statement's
// signature (decision 081).
package Shop;

import java.util.List;
import org.apache.ibatis.annotations.Param;

public interface FindHeavyLinesMapper {
    List<ShopOrderLine> findHeavyLines(
        @Param("minQuantity") int minQuantity,
        @Param("minTotal") int minTotal,
        @Param("skip") int skip,
        @Param("take") int take);
}
