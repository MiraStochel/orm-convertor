# 192 — NHibernate drží HQL u řetězu do 32 článků a hlubší dotaz jde nativním SQL: mez vlastního parseru cíle vyslovuje deskriptor, ne strop nástroje

Datum: 2026-10-27
Stav: platí
Požadavky: F11, T3, S4
Podklad: rozhodnutí [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [190](190-a-point-of-the-limits-run-that-does-not-finish-within-thirty-minutes-is-hung.md), [191](191-the-depth-of-the-model-is-capped-by-the-same-number-as-the-text-and-a-reader-that-runs-out-of-stack-refuses.md); otevřená položka *Parser HQL NHibernatu přeteče na řetězu hlubokém kolem sta článků*; sondy na hostiteli MIS3 (Windows) z 2026-10-27, každá hloubka ve vlastním procesu (níž, *Co měření ukázalo*); `NHibernate/NHibernateChainDepthProviderTest`

## Kontext

Procházka řad řetězů se stropem vypnutým (rozh. 190) ukázala, že artefakt NHibernatu s řetězem operací, CASE nebo NOT hlubokým 128 článků přeteče při přijetí zásobník — ne v nástroji, nýbrž ve vlastním parseru HQL NHibernatu (`NHibernate.Hql.Ast.ANTLR.HqlParser`), když NHibernate staví session factory a čte HQL. Na hostiteli MIS3 s Windows se to stalo ze všech šesti zdrojů, v linuxovém kontejneru až při 1024. Strop rozh. 191 přitom 128 článků pouští: nástroj tedy z přijatého vstupu vydá HQL, které na Windows ukončí proces uživatele, jakmile NHibernate dotaz přečte. To je vada podle definice [`open-items.md`](../open-items.md) — artefakt, který cíl nepřijme —, a horší než odmítnutí, protože se o ní uživatel dozví pádem.

### Co měření ukázalo

Sonda přeložila řetěz ze zdroje NHibernate do NHibernatu se stropem vypnutým a artefakt prohnala cestou cizího artefaktu, každou hloubku ve vlastním procesu, aby pád ukončil jen ji:

| Tvar | Poslední přijatý | První přetečení |
|---|---|---|
| součet | 96 | 104 |
| NOT | 104 | 112 |
| CASE v ELSE | 112 | 120 |
| vnořené poddotazy | 128 (strop textu) projde | — |
| závorky | 256 projde (model nadbytečné závorky nenese) | — |

Parser HQL tedy přeteče jen na řetězu, od 97 článků; poddotazy a závorky drží strop textu (rozh. 092). Sonda běžela na vlákně poolu s 1536 KB zásobníku, a stejně velký zásobník má na Windows každé vlákno .NET bez vlastního nastavení: hlavní vlákno konzolové aplikace, vlákno poolu, na kterém ASP.NET Core vyřizuje požadavek, i `new Thread()` (změřeno 2026-10-27 `GetCurrentThreadStackLimits`). Mez tedy platí pro každého spotřebitele, který zásobník nemění. Nativní SQL, které NHibernate předá serveru přes `ISession.CreateSQLQuery`, parser HQL nečte, a NHibernate ho přijal i při 128 a 256 článcích.

Mimochodem měření ukázalo mez, která s NHibernatem nesouvisí: SQL Server vnoří CASE nejvýš desetkrát (`Msg 125`, „Case expressions may only be nested to level 10"), takže CASE vnořený hlouběji neběží, ať dotaz napsal kdokoli. Je to mez platformy na čtvrtém stupni, jakou rozh. 182 čte zvlášť.

**Je třeba rozhodnout**, jak nástroj zachází s cílem, jehož vlastní parser dotazu přeteče pod stropem nástroje.

## Zvažované varianty

### 1 — Vyslovit to jako mez cíle v `subset.md`

Napsat, že NHibernate řetěz hlubší než zhruba sto článků nepřečte, a nic neměnit. Zamítáme: artefakt by dál vznikal a končil pádem procesu uživatele; vyslovená mez je pro konstrukci, kterou nástroj odmítne nebo přeloží jinak, ne pro artefakt, o kterém ví, že shodí cíl.

### 2 — Snížit strop rozh. 191 pro všechny

Strop 64 by NHibernate ochránil. Zamítáme: strop je mez vstupu, kterou si nástroj stanovil podle svých čteček a průchodů, a mez parseru jednoho cíle je fakt toho cíle. Sníženým stropem by platilo pět cílů, jejichž jazyk hlubší řetěz nese, a číslo, které provozovatel nastavuje, by přestalo znamenat jednu věc.

### 3 — Odmítnout dotaz hlubší než mez NHibernatu

`Failure` se jménem parseru. Zamítáme: NHibernate má API pro nativní SQL a rozh. 113 říká, že co jazyk cíle nevysloví, jde celé nativním SQL dialektu; odmítnutím by se ztratil překlad, který existuje a který NHibernate přijme.

### 4 — Mez řetězu v deskriptoru cíle a hlubší dotaz nativním SQL

Deskriptor — místo, kde stojí, co cíl vyžaduje a co nevysloví — dostane `ChainDepth`: hloubku modelu (počítanou jako ve strop rozh. 191), kterou vlastní parser dotazu cíle unese. Dotaz hlubší je konstrukce, kterou jazyk cíle nevysloví, a šablona ho podle rozh. 113 napíše nativním SQL, se záznamem `Fallback`, který říká proč. Volíme.

## Rozhodnutí

**Volíme variantu 4. `TargetFrameworkDescriptor.ChainDepth` vyslovuje, jak hluboký řetěz operací, CASE, negací a množinových operací vlastní parser dotazu cíle unese na zásobníku výchozího vlákna; u NHibernatu je to 32, u ostatních cílů nic, protože žádný jiný pod stropem nepřetekl. Šablona na začátku stavby změří hloubku modelu a hlubší dotaz ohlásí jako nevyslovený (`Fallback` `Expression`); cíl s `NativeSqlApi` ho pak napíše celý nativním SQL, cíl bez něj by ho odmítl. Builder nativního SQL mez nečte — jeho text parser cíle nečte.**

**Proč 32.** Třetina prvního změřeného přetečení (97 článků součtu, 96 prošlo). Rezerva je menší než osminásobek rozh. 092 a větší než to, co by bylo nutné na výchozím vlákně, a obojí má důvod. Menší, protože cena nižšího čísla je malá: řetěz o 33 až 96 článcích nevyjde v HQL, ale v nativním SQL, a ne vůbec. Větší, protože naměřené číslo platí pro sondu, ne pro aplikaci: spotřebitel volá NHibernate z vlastního zásobníku — middleware, kontrolér, vlastní kód —, který si z 1,5 MB vezme dřív, než parser začne, a jiná verze NHibernatu může mít větší rámce. 32 článků přitom ručně psaný dotaz nepřekročí; řetěz sčítání třiceti sloupců už je dotaz, který skládá stroj, a ten v nativním SQL dostane totéž.

**Proč v deskriptoru.** Je to fakt cíle, stejně jako `NativeSqlApi` a `QuerySupport` vedle něj, a nový cíl s podobnou mezí ji vysloví stejným polem, bez zásahu do šablony (S1). Hodnota je změřená a test, který ji drží, stojí u ní: `NHibernateChainDepthProviderTest` ověří, že řetěz hluboký jako `ChainDepth` NHibernate v HQL přijme, a že dvakrát hlubší jde nativním SQL a NHibernate ho přijme také. Samo přetečení se v sadě nezkouší, protože by ukončilo běh; aktualizace NHibernatu, která parser zhorší, selže na první polovině.

## Důsledky

- **Kód:** `TargetFrameworkDescriptor.ChainDepth`; `NHibernateDescriptor.ChainDepth = 32`; v `AbstractQueryBuilder.BuildArtifacts` kontrola hloubky před ostatními branami, mimo builder nativního SQL.
- **Testy:** `NHibernate/NHibernateChainDepthProviderTest` — součet, NOT a CASE o 32 článcích v HQL bez `Fallback`, o 64 článcích nativním SQL s `Fallback`; součet a NOT běží nad fixturou, CASE jen projde přijetím, protože SQL Server vnoří CASE nejvýš desetkrát.
- **Běh mezí:** body řad `expression`, `case` a `not` do NHibernatu nad 32 vyjdou nativním SQL (jazyk řádku `native SQL`) místo pádu při přijetí; bod řady `case`, jehož dotaz vnoří CASE hlouběji než desetkrát, SQL Server nespustí — čtečka LINQ čte řetěz `?:` jako jeden CASE s více větvemi, ostatní čtečky CASE v ELSE nechávají vnořený.
- **[`subset.md`](../subset.md):** řádek o řetězu hlubším než mez parseru cíle — NHibernate jde nativním SQL; CASE vnořený hlouběji než desetkrát SQL Server nespustí.
- **Co rozhodnutí neřeší:** spotřebitele, který NHibernate volá na vlákně s menším zásobníkem, než má výchozí; ten si mez posouvá sám.
