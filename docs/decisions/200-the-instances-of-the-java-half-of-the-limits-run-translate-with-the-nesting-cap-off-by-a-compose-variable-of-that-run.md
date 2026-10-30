# 200 — Instance javové půlky běhu mezí překládají se stropem zanoření vypnutým, proměnnou compose nastavenou jen pro ten běh; jinak drží výchozí strop nasazení

Datum: 2026-10-30
Stav: platí
Požadavky: S3, T3

Podklad: rozhodnutí [092](092-input-nesting-depth-capped-before-the-descent.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md), [182](182-the-limits-of-the-tool-are-searched-by-synthetic-series-growing-along-one-axis-as-a-run-on-demand-over-the-foreign-path.md), [191](191-the-depth-of-the-model-is-capped-by-the-same-number-as-the-text-and-a-reader-that-runs-out-of-stack-refuses.md), [195](195-the-java-half-of-the-limits-run-continues-where-it-was-skips-what-the-dotnet-half-could-not-translate-and-translates-by-an-instance-with-a-memory-limit.md); [záznam běhu mezí 2026-10-29](../runs/2026-10-29-limits.md), tabulka třídění, řádek *javové instance*; `Combined/QueryTextCapTest.TheConfigurationReadsBothCapsAndDefaultsEachAlone` a `TheConfigurationReadsAnEmptyVariableAsTheDefault`

## Kontext

Běh mezí (rozh. 182, 190) čte šest řad — `nesting`, `subqueries`, `expression`, `unions`, `case` a `not` — se stropem zanoření vypnutým, protože strop (rozh. 092, 191) je vyslovená mez a otázka běhu zní, co je za ní. .NET půlka to dělá v procesu: `ParseLimits.Unlimited` předává parserům přímo, ne nastavením instance, protože žádnou instanci nemá. Javová půlka tytéž vypsané body překládá nasazenými instancemi `test_app` a `test_app_dry` přes HTTP (rozh. 078, 195), a ty v compose žádný klíč `Parsing__MaxNestingDepth` nemají, drží tedy výchozích 128 a bod od 256 výš odmítnou dřív, než ho překládají.

Záznam z 2026-10-29 to ukázal na řadách `case` a `expression`: javová půlka zapsala 165 řádků `refused` s větou „deeper than the 128 levels this instance reads“, zatímco .NET půlka tytéž body přeložila do Hibernatu, EclipseLinku i MyBatisu až ke stropu 4096 a cesta se zastavila až u javacu (text přes 64 KB) a u SQL Serveru (CASE vnořený víc než desetkrát). Záznam ten řádek třídí jako „odmítnutí stropu, ne nalezená mez“ — a oddíl *Limits* souhrnu by javovým cílům dal jinou mez než cílům .NET téhož překladače, čtenou z téhož běhu.

**Je třeba rozhodnout**, zda instance javové půlky běží se stropem vypnutým, aby obě půlky měřily totéž, nebo zda řádky javové půlky nad stropem platí za řádky nasazené instance.

### Co měření ukázalo

Klíč `Parsing:MaxNestingDepth` čte `ParseLimitsConfiguration.Read` jako `int?`: chybějící klíč dává výchozí strop, `0` ho vypíná. Compose předává proměnnou hostitele vzorem `${PROMĚNNÁ-}`, který bez proměnné dosadí prázdný řetězec, takže instance pak proměnnou prostředí *má*, jen prázdnou. Jak ji konfigurace .NET čte, jsme změřili testem (`QueryTextCapTest.TheConfigurationReadsAnEmptyVariableAsTheDefault`, 2026-10-30, .NET 10): proměnná prostředí s prázdnou hodnotou projde poskytovatelem proměnných prostředí do konfigurace a `GetValue<int?>` ji vrátí jako `null`, tedy výchozí strop; `0` toutéž cestou strop vypne. Test zůstává vedle čtení, takže změna chování konfigurace při povýšení .NET padne tam.

## Zvažované varianty

### 1 — Instance drží 128 a souhrn řádky nad stropem označí (dosud)

Zamítáme. Běh mezí se ptá, kde se zastaví *překladač* (T3), a obě půlky jsou týž překladač v jiném těle: .NET půlka v procesu, javová v instanci. Kdyby javová půlka četla body se stropem a .NET půlka bez něj, oddíl *Limits* by u řady `case` dal cíli Hibernate mez 128 s důvodem „refused“ a cíli NHibernate mez 4096 s důvodem u SQL Serveru — dvě čísla o jednom překladači, z nichž jedno není mez překladače, ale konfigurace nasazení. Označení v souhrnu by ten rozdíl jen pojmenovalo; nález by stejně chyběl, protože co dělají javac, Hibernate a EclipseLink s textem za stropem, by za stropem nikdo neviděl. Záznam 2026-10-29 už ty řádky třídí jako řádky nasazené instance, ne jako nález, a položka proto vznikla.

### 2 — Strop vypnutý v compose natrvalo, u `test_app` a `test_app_dry`

Zamítáme. Obě instance slouží celé javové sadě, ne jen běhu mezí: diferenciálním testům, případovým studiím a experimentálnímu běhu, který soudí „artefakt nástroje, jak je nasazený“ (rozh. 173). Instance se stropem natrvalo vypnutým by nebyla nasazení: strop je ochrana instance (hrozba 2 modelu hrozeb) a to, že ho žádný vstup experimentálního běhu nedosáhne, není důvod ho tam nemít — kdyby ho některý vstup dosáhl, je to nález o nasazení, který by zmizel. Výchozí stav compose má zůstat nasazením.

### 3 — Pátá instance jen pro běh mezí

Zamítáme. Instance `test_app_limits` by se od `test_app` lišila jedním klíčem a nasucho by potřebovala šestou. Každá nese jméno služby, alias pro JDK (`_` v názvu hostitele JDK odmítá), řádek v README a tabulce compose a 2 GB v součtu mezí rozh. 193, kdyby běžela vedle ostatních. Jeden klíč není důvod pro novou službu.

### 4 — Strop v těle požadavku

Zamítáme z důvodu, který už drží `ParseLimitsConfiguration`: vstup, proti kterému strop chrání, nesmí nést povolení ho překročit (rozh. 092, 191). Strop nemá pole v `ConvertRequest` a mít ho nebude.

### 5 — Proměnná compose, nastavená jen pro běh mezí

Volíme. Compose už tak nese nastavení, která platí jen pro jeden druh běhu: `DATABASE_MAX_SERVER_MEMORY_MB` a `DATABASE_MAXDOP` pro režim serveru (rozh. 164), `ORMCONVERTOR_LDBC_VALIDATION_ROWS` pro rozsah soudce, `ORMCONVERTOR_CATALOG_DATABASE` pro katalog systému. Proměnná `ORMCONVERTOR_TEST_APP_MAX_NESTING_DEPTH`, dosazená do klíče `Parsing__MaxNestingDepth` obou instancí, které javová půlka překládá, dělá totéž pro strop: bez ní instance drží výchozí strop nasazení (změřeno výš), běh mezí ji nastaví na `0` a obě půlky čtou se stropem vypnutým. Instance ukončená za stropem — přetečením zásobníku, které .NET nezachytí (rozh. 092) — je podle rozh. 195 řádek `crashed` a restart ji vrátí; to je přesně nález, který .NET půlka zapisuje jako pád potomka. Strop délky textu (rozh. 194) proměnná nemění: ten běh mezí nevypíná záměrně ani v .NET půlce.

## Rozhodnutí

**Volíme variantu 5.** Instance `test_app` a `test_app_dry` čtou `Parsing__MaxNestingDepth` z proměnné compose `ORMCONVERTOR_TEST_APP_MAX_NESTING_DEPTH`; bez proměnné platí výchozí strop nasazení, běh mezí ji nastavuje na `0` a startuje s ní každou instanci, kterou překládá. Řádky javové půlky z 2026-10-29, které instance odmítla stropem, se před pokračováním běhu ze souboru řádků odstraní, aby se jejich body soudily znovu; záznam to řekne.

## Důsledky

- **Kód:** `docker-compose.yml` — klíč u `test_app` a `test_app_dry`; `Combined/QueryTextCapTest.TheConfigurationReadsAnEmptyVariableAsTheDefault` — měření, na kterém volba stojí.
- **Dokumentace:** `ORMConvertor/README.md`, *The limits run* (řádek tabulky a příkaz startu instance); [`runs/README.md`](../runs/README.md), *Metodika*, řádek běhu mezí; [`architecture.md`](../architecture.md) §6.2, řádek *běh mezí*; položka stropu v `open-items.md` mizí a položka javové půlky nese proměnnou.
- **Co záznam uvidí jinak:** body řad se stropem vypnutým mají v javových cílech řádky za stropem 128 — kde se zastaví javac, Hibernate, EclipseLink a SQL Server, nebo instance pádem (`crashed`) —, ne odmítnutí stropu; oddíl *Limits* dává .NET i javovým cílům mez téhož překladače.
- **Co rozhodnutí neurčuje:** strop nasazení (zůstává 128, rozh. 191) a strop délky textu (rozh. 194).
