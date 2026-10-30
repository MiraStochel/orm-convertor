# 194 — Text jednoho dotazu má strop délky: týmž číslem se měří model po čtení, operand LINQ při zápisu, rozvinutí `<include>` MyBatisu a hotový artefakt, a delší dotaz je odmítnut

Datum: 2026-10-30
Stav: platí
Požadavky: S3, S4, F11
Podklad: rozhodnutí [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [092](092-input-nesting-depth-capped-before-the-descent.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [118](118-nhibernate-writes-a-linq-form-beside-its-hql.md), [119](119-quantified-comparison-as-a-comparison-with-a-quantifier.md), [156](156-a-linq-predicate-carries-sql-three-valued-logic-in-both-directions.md), [182](182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md), [190](190-a-point-of-the-limits-run-that-does-not-finish-within-thirty-minutes-is-hung.md), [191](191-the-depth-of-the-model-is-capped-by-the-same-number-as-the-text-and-a-reader-that-runs-out-of-stack-refuses.md), [192](192-nhibernate-is-held-to-a-chain-of-thirty-two-links-and-a-deeper-query-goes-out-in-native-sql.md); [`threat-model.md`](../threat-model.md), hrozba 2; otevřená položka *Délka artefaktu roste exponenciálně s hloubkou vstupu*; [záznam běhu mezí 2026-10-29](../runs/2026-10-29-limits.md); měření 2026-10-30 na hostiteli MIS3 (Windows, sestavení Debug, níž *Co měření ukázalo*); `Combined/QueryTextCapTest`

## Kontext

Rozhodnutí 156 dalo predikátu LINQ trojhodnotovou logiku SQL. Zapisovač píše podmínku tam, kde platí: `a <> b` nad hodnotou, která smí být `NULL`, vyjde jako `a != b && a != null`, a operand `a` je tím napsaný dvakrát. Čtečka zdroje EF Core čte opačným směrem to, co z predikátu udělá provider: `a != 1` nad takovou hodnotou je `a <> 1 OR a IS NULL`, a operand `a` je v modelu dvakrát — jako jeden sdílený objekt, který každý cíl píše dvakrát. Obojí je správně: tak ty dotazy znamenají. Je-li ale operand sám CASE, jehož podmínka nese další takové porovnání, zdvojí se text na každé úrovni, a vstup o kilobajtu píše megabajty v tuctu úrovní a gigabajty ve dvou. Třetí místo nesouvisí s `NULL`: `<include>` MyBatisu vloží fragment tolikrát, kolikrát je pojmenovaný, a fragment, který vkládá nižší dvakrát, se rozvine na dvojnásobek za každou úroveň fragmentů (`MyBatisStatementText`); cyklus je `Failure`, rozvinutí bez cyklu strop nemělo.

Strop zanoření (rozh. 092, 191) tomu nebrání: každá úroveň těch tvarů je jedna závorka nebo jeden CASE, 128 úrovní projde, a 2^128 je číslo, které paměť stroje vyčerpá dřív, než se cokoli napíše. Proces nespadne — je to alokace, ne zásobník —, ale překlad vyčerpá paměť a čas (hrozba 2, S3) z požadavku o pár kilobajtech. Položka to vedla jako vadu zapisovače LINQ; měření ukázalo, že čtečka se podílí stejně.

### Co měření ukázalo

Měřeno 2026-10-30 nad fixturou matic tvarů dotazů (`ShopDepartments.ParentDepartmentId`, sloupec `int?` v každém mapování, takže smí být `NULL`), překlad bez katalogu, čas na hostiteli MIS3 v sestavení Debug. Délky jsou znaky nejdelšího artefaktu dotazu; *k* je počet úrovní.

| Tvar | Směr | Růst za úroveň | Změřeno |
|---|---|---|---|
| `CASE WHEN c THEN x ELSE 0 END <> 1`, kde `c` je totéž porovnání o úroveň níž; T-SQL o 840 znacích při *k* = 14 | Dapper → EF Core | ×2 (zapisovač: test na `NULL`) | *k* = 13: 811 105; 14: 1 622 113 (327 ms); 16: 6 488 161 (1,0 s) |
| totéž | Dapper → Dapper, Dapper → Hibernate | lineární | *k* = 16: 1 102 a 1 042 |
| `(SELECT MAX(q.x) FROM … WHERE c) <> d.x`, poddotaz vnořený v `c` | Dapper → EF Core | ×2 (poddotaz se i vykreslí dvakrát) | *k* = 12: 978 970 (349 ms) |
| `(c ? d.x : 0) != 1`, operand psaný jednou za úroveň, jak ho píše autor C# | EF Core → Dapper (T-SQL) | ×2 (čtečka: `<> OR IS NULL`, operand sdílený) | *k* = 13: 1 106 055 (0,8 s); 18: 35 389 575 (18 s) |
| totéž | EF Core → EF Core | ×3 (čtečka ×2 a nad jednou z kopií zapisovač) | *k* = 9: 1 614 120; 13: 130 734 600 (19 s) |
| totéž | EF Core → NHibernate | HQL ×2, forma LINQ ×3 | *k* = 13: HQL 1 105 875, LINQ 130 734 609 |
| `<sql id="f_k"><include refid="f_k−1"/>, <include refid="f_k−1"/></sql>` v seznamu `IN`; mapper o 1,7 KB | MyBatis → Dapper | ×2 | *k* = 19: 1 573 124 (10 s); 20: 3 145 988 (18 s; čas je čtení ScriptDomem) |

Čas je ve všech řadách lineární v délce výstupu — žádný průchod není sám exponenciální (`Combined/NestedShapeTimeTest`), roste jen to, co se píše. Dvě další věci měření doložilo:

- **SQL providera roste dál.** Z artefaktu první řady při *k* = 4 (1 681 znaků LINQ) udělá EF Core 10.0.10 přes `ToQueryString()` 14 883 znaků SQL: kompenzace `!=` zdvojí operand v SQL znovu, i když LINQ už test na `NULL` nese. Co by tedy v LINQ bylo napsané jednou, server dostane stejně vícekrát.
- **Nejdelší poctivý dotaz.** Na stropech řad běhu mezí (rozh. 182, 190), se stropem zanoření vypnutým: 512 joinů Dapper → EF Core 1 056 289 znaků (průhledné identifikátory `join … into` rostou kvadraticky s počtem joinů), 4096 úrovní CASE do MyBatisu 149 724, do Hibernatu 137 242 a do EF Core 120 822, 4096 podmínek do MyBatisu 126 240 a do Dapperu 101 529; všechny ostatní body pod 100 000. Co z toho spotřebitel zkompiluje, je méně: javac nevezme řetězcovou konstantu nad 64 KB a Roslyn vrací `CS8078` od 2048 úrovní CASE a 4096 podmínek ([záznam 2026-10-29](../runs/2026-10-29-limits.md)); SQL Server vnoří CASE nejvýš desetkrát (Msg 125).

**Je třeba rozhodnout**, zda operand hláskovat jednou, nebo dát textu dotazu strop a vstup nad ním odmítnout.

## Zvažované varianty

### 1 — Napsat operand jednou: proměnná `let`, pomocná metoda

Pomocná metoda padá hned: ani EF Core, ani NHibernate volání uživatelské metody do SQL nepřeloží. `let` v metodové syntaxi je `.Select(x => new { x, v = … })` a mění řádek řetězu — průhledný identifikátor — v každé další lambdě, takže zasahuje do celého builderu LINQ a NHibernatův provider by se nad ním musel nejdřív změřit. Hlavně ale kryje jen zapisovač LINQ: čtečku EF Core ne, protože její model je správný a sdílený operand píší všechny textové cíle dvakrát bez jakéhokoli `let`; `<include>` ne, protože SQL fragment nemá a MyBatis sám text za běhu rozvine na stejnou délku. Zamítáme. A ani tam, kde by `let` text zkrátil, by nepomohl serveru — provider operand v SQL zdvojí znovu (měřeno), a CASE nad deset úrovní SQL Server stejně nespustí.

### 2 — Strop poměru výstupu ke vstupu

Odmítnout dotaz, jehož artefakt je mnohonásobkem jeho textu. Zamítáme: model délku zdrojového textu uzlu nenese, poměr by nedal spotřebiteli žádné číslo, které zná, a zasáhl by i poctivý růst — 512 joinů do EF Core je 24násobek vstupu bez jediného zdvojení.

### 3 — Strop délky jen nad hotovým artefaktem

Nejjednodušší kontrola, ale pozdní: paměť je utracená, než se artefakt změří, a model, který čtečka EF Core nechá jako graf s *k* sdílenými operandy, se do textu rozvine na 2^*k* uzlů dřív, než kontrola přijde ke slovu. Zamítáme jako jediné místo; jako poslední z několika je nutná, protože jen ona ručí za to, co z nástroje odejde.

### 4 — Strop délky textu jednoho dotazu, jedno číslo, držené tam, kde text roste násobením

Jedno číslo, délka textu jednoho dotazu ve znacích, které drží čtyři místa: model hned po čtení, počítaný tak, jak ho cíl píše; operand LINQ při zápisu; rozvinutí `<include>`; hotový artefakt. Překročení je `Failure` s větou, která říká, co a o kolik. Volíme.

## Rozhodnutí

**Volíme variantu 4. `ParseLimits.MaxQueryTextLength`, výchozí 2^20 = 1 048 576 znaků, `0` vypíná, nastavuje provozovatel klíčem `Parsing:MaxQueryTextLength` vedle stropu zanoření; tělo požadavku ho změnit nemůže a každá odpověď `/convert` vrací účinnou hodnotu (`maxQueryTextLength`), protože stejný dotaz může jedna instance přeložit a druhá odmítnout, aniž se liší verzí (S2). Totéž číslo drží čtyři místa:**

| Kde | Co se měří | Překročení |
|---|---|---|
| model hned po čtení (`ConversionHandler`, před hloubkou rozh. 191) | `QueryTextCap.WrittenNodes`: uzly modelu — instrukce, podmínky, operandy, výrazy —, jak je cíl píše: sdílený uzel tolikrát, kolikrát je dosažen, protože tolikrát ho každý zapisovač napíše; memoizace podle reference, takže jeden krok na odlišný uzel; každý uzel se v každém jazyce píše nejméně jedním znakem, a model s více uzly než strop by tedy každý cíl napsal delší, než strop dovolí | `Discard`: `Failure` s počtem uzlů, přečtené instrukce zahozené, žádný průchod do modelu nesestoupí |
| operand LINQ (`LinqQueryVisitor.Operand`) | délka textu operandu; každé místo, které operand píše dvakrát — test na `NULL` porovnání, stráž konkatenace, lambda kvantifikátoru —, bere text odsud, takže text přes strop je chycen nejpozději jedno zdvojení po překročení | `Failure` jednou za dotaz (vnořené rozsahy hlásí přes nejvnějšnější visitor), operand se napíše jako nic |
| rozvinutí `<include>` (`MyBatisStatementText.AppendInclude`) | délka rozvinutého příkazu po každém `<include>` | `Failure` se jménem fragmentu, který strop překročil; rozvinutí dál nepokračuje |
| hotový artefakt (`AbstractQueryBuilder.Build`, i druhá forma rozh. 118 a nativní SQL rozh. 113) | délka textu každého artefaktu dotazu | `Failure` formy; u druhé formy `Omitted` a závazná forma stojí, u nativního SQL odmítnutí dotazu |

**Proč jedno číslo na čtyři místa.** Je to jedna věc — kolik textu jeden dotaz smí mít — a čtyři místa jsou jen místa, kde se ta věc dá změřit včas. Model se měří hned po čtení, protože graf se sdíleným operandem je malý v paměti a velký jako text, a každý průchod po čtení (hloubka, poptávka katalogu, brány, visitory) jde modelem tak, jak se píše; operand LINQ a `<include>` se měří při psaní, protože tam text roste násobením a hotový artefakt by přišel po utracené paměti; hotový artefakt se měří, protože jen tam platí věta, že delší dotaz z nástroje neodejde — kvadratický růst 512 joinů do EF Core neprochází žádným z prvních tří míst. Počet uzlů proti počtu znaků je konzervativní odhad a nic víc: dotaz, který projde počtem uzlů a nepřejde délkou, chytí hotový artefakt.

**Proč 2^20.** Řád nad všechno, co spotřebitel zkompiluje — javac 64 KB, Roslyn do zhruba 100 KB jednoho výrazu —, řád nad nejdelší poctivý dotaz běhu mezí kromě jediného bodu, a zároveň číslo, na němž zdvojování končí, dokud stojí milisekundy: první řada se zastaví při *k* = 14 (1,6 MB, 327 ms), poddotazy při 12, čtečka EF Core do T-SQL při 13 a do LINQ při 9, `<include>` při 19. Tím jediným bodem je 512 joinů ze zdroje bez průhledných identifikátorů do EF Core, 1 056 289 znaků, o 0,8 % nad stropem: dotaz, který Roslyn nezkompiluje a pro který SQL Server plán nesestaví ([záznam 2026-10-29](../runs/2026-10-29-limits.md)), takže jeho odmítnutí nic nestojí a běh mezí ho příště ukáže jako `Failure` stropem místo `too slow`. Mocnina dvou, protože i strop zanoření je číslo bez desetinné části a protože se vysloví jedním slovem.

**Co strop nemění.** Zápis a čtení podmínky podle rozh. 156 zůstávají, slovo od slova: test na `NULL` vedle porovnání, kompenzace čtená jako `<> OR IS NULL`; strop jen ohraničuje, kolik takového textu jeden dotaz smí mít. Entitní artefakty strop nemají: rostou lineárně se vstupem (osa `properties` 4096 vlastností do MyBatisu alokovala 9 GB, ale napsala řádově 100 KB). Běh mezí (rozh. 182) strop textu nevypíná, i když strop zanoření vypíná: zanoření je mez čteček, za kterou běh hledá zásobník, kdežto délka textu je mez, kterou nástroj klade sám, a běh ji má ukázat tak, jak platí.

## Důsledky

- **Kód.** `ParseLimits.MaxQueryTextLength`, `DefaultMaxQueryTextLength`, `CapsQueryText`; `QueryTextCap` (`WrittenNodes`, `ModelReason`, `TextReason`) v `AbstractWrappers`; `AbstractQueryBuilder.Limits` (nastavuje orchestrace jako `EntityMaps`, dědí druhá forma i builder nativního SQL), `WrittenNodes`, kontrola `ExceedsTextCap` v `Build` a `SecondForm`; `ConversionHandler` měří model před hloubkou rozh. 191 a předává strop builderům; `LinqQueryVisitor.Limits` a obal `Operand` nad `WriteOperand`; `MyBatisStatementText` bere `ParseLimits` a měří po každém `<include>`; `ConversionResult.MaxQueryTextLength`, `ConvertResponse`, `ParseLimitsConfiguration.QueryTextKey`, `openapi.json`. Model, `IQueryVisitor` ani deskriptory se nemění (S1).
- **Testy.** `Combined/QueryTextCapTest`: každé ze čtyř míst nad malým stropem na obou stranách hranice a jednou nad výchozím stropem s tvarem, který ho překračuje, včetně třiceti úrovní sdíleného operandu ze zdroje EF Core do tří cílů (text by byl tisícinásobkem paměti stroje, odmítnutí stojí milisekundy); druhá forma NHibernatu `Omitted` s HQL stojícím; vypnutí stropu; konfigurace obou klíčů. `Combined/NestedShapeTimeTest` drží dál, že průchody jsou lineární v délce výstupu.
- **Běh mezí.** Bod `joins` 512 do EF Core vyjde `Failure` stropem; žádný jiný bod na stropech řad se nemění.
- **Dokumentace.** `architecture.md` (společná pravidla čtení, `ParseLimits`, záznam běhu, šablona builderu), `subset.md` (vyslovená mez), `threat-model.md` (hrozba 2: artefakt exponenciální v hloubce přechází mezi to, co verze chrání), `traceability.md` S3 a S4, `ORMConvertor/README.md` (konfigurační klíč); položka mizí z `open-items.md`.
- **Co rozhodnutí neřeší.** Limit počtu a velikosti jednotek na `/convert` zůstává nenárokovaný (hrozba 2). SQL, které provider pošle serveru z dotazu pod stropem, je dál delší než artefakt — to je věc providera a spotřebitele, a CASE nad deset úrovní SQL Server nespustí, ať je dotaz jakkoli dlouhý.
