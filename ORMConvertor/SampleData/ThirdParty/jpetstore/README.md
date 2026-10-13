# MyBatis JPetStore 6

- **Project:** [mybatis/jpetstore-6](https://github.com/mybatis/jpetstore-6), the MyBatis team's
  reference web shop: plain domain classes, mapper interfaces and XML mapper documents.
- **Commit:** `344ef63ddf05dd83cd5bfc325a9e8ef7b143f860` (2026-10-11).
- **Licence:** Apache License 2.0, [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).
- **Taken from:** `src/main/java/org/mybatis/jpetstore/domain/` (the seven persistent classes;
  `Cart` and `CartItem` live in the session and are left out), `…/mapper/*.java` and
  `src/main/resources/org/mybatis/jpetstore/mapper/*.xml` (all seven mappers).

## What was changed

1. **Domain classes and six mapper interfaces are unchanged** apart from the provenance note.
   `AccountMapper.getAccountByUsernameAndPassword` names its two parameters with `@Param`, so its
   statement can bind them by name instead of `#{param1}` and `#{param2}`.
2. **The statements that write are left out** of the mapper documents (`<insert>`, `<update>`):
   the tool translates reads.
3. **The selects are rewritten where the tool reads them only in another form**, and each mapper
   document lists its changes in its note:
   - comma-separated tables (`FROM ITEM I, PRODUCT P WHERE P.PRODUCTID = I.PRODUCTID ...`) are
     written as `INNER JOIN ... ON` with the same conditions - the tool refuses a cross join;
   - `#{value}`, which MyBatis binds to the method's only parameter whatever its name, names that
     parameter (`#{productId}`, `#{orderId}`, ...) - the tool types a parameter from the interface
     by its name;
   - every column of a joined select is qualified by its table (`P.NAME`, `ORDERSTATUS.STATUS`) -
     without the schema the tool gives a bare column to the first table of the join;
   - the dotted aliases of the item selects (`I.PRODUCTID AS "product.productId"`), with which
     MyBatis fills `item.product` by auto-mapping, become two result maps with an `<association>`
     - the tool writes such an alias without its quotes.

The SQL is the project's HSQLDB SQL, which is also T-SQL as far as these selects go.
