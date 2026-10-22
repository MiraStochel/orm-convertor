# 161 — Agregát nad oknem je okno slovníku vedle tří pořadových funkcí; průměr modelu je `AVG` jazyka SQL a každý cíl ho tak vrací; celý řádek mezivýsledku vedle sloupců jsou jeho sloupce

Datum: 2026-10-22
Stav: platí; nahrazuje větu rozh. 113, že okenní agregát a rámec okna slovník nenese, a pro mezivýsledek řádek rozh. 070 o celém řádku vedle sloupců
Požadavky: F7–F11, F13, T2, T3, S1, S2
Podklad: rozhodnutí [028](028-assembly-name-is-not-ours-to-invent.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [112](112-a-query-as-a-row-source-is-a-named-intermediate-result.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md), [155](155-sql-sum-in-ef-core-linq-is-the-projection-of-one-group-and-ef-core-sum-is-coalesce.md); JSS §5.4 („Non-core SQL constructs") a pravidla Q3, Q8, Q14, Q15; katalog FIBEN q99 a q237; sondy proti připnutým verzím z 2026-10-22 — javová sada (`hibernate/WindowAggregateAndJoinSubqueryClaimsTest`, `hibernate/AverageClaimsTest`, `eclipselink/AverageClaimsTest`), plán dotazu NHibernatu (`NHibernate/NHibernateHqlProviderTest`, `NHibernate/NHibernateLinqProviderTest`) a `ToQueryString()` EF Core (`EFCore/EFCoreAverageProviderTest`); diferenciální běh javové sady z 2026-10-22 nad kategorií okenního agregátu; otevřená položka „Co katalog FIBEN odmítá: okenní agregát a stínovaný alias odvozené tabulky"

## Kontext

Rozhodnutí 113 vzalo do slovníku tři pořadové funkce nad oknem — `ROW_NUMBER`, `RANK`, `DENSE_RANK` s oddíly a povinným řazením — a vyslovilo, že **okenní agregát** (`SUM(…) OVER`) a rámec okna slovník nenese, „katalog LDBC je nepotřebuje". Katalog FIBEN (rozhodnutí 149) je potřebuje: q237 počítá `AVG(numOfficers) OVER (PARTITION BY industry)` uvnitř odvozené tabulky a filtruje nad ní, q99 počítá `MAX` a `MIN` nad oknem ve druhé ze tří vnořených odvozených tabulek. Čtečka T-SQL okenní agregát odmítá jmenovitě; v projekci dotazu je to ztráta, uvnitř mezivýsledku odmítnutí, protože dotaz kolem čte sloupec jménem (rozhodnutí 070). Katalog oba dotazy vede jako `NotTranslated` a běh na vyžádání je odmítá v každém cíli.

Druhá překážka q99 není v okně. Dotaz projektuje `months.*` a `runs.*` **vedle dalších sloupců uvnitř odvozené tabulky**. Brána výběrového seznamu takový řádek zahodí se záznamem `Loss` a nechá jen sloupce vedle něj — pravidlo rozhodnutí 070 o `SELECT t.*, c.x`, kde model řádek vedle sloupců nenese a řádky dotazu se nemění. Uvnitř mezivýsledku se ale mění, co dotaz kolem vidí: definice `runs` by měla jediný sloupec `groupnum` a dotaz nad ní by jmenoval `oPerson_hasPersonName`, který nemá — artefakt, který žádný cíl nespustí. Dnes to nikdo nehlásí, protože čtení zastaví dřív okenní agregát.

Měřítko slovníku stanovilo rozhodnutí 113: funkce vstupuje do slovníku, má-li zápis v T-SQL, a každý cíl ji buď vysloví ověřeným zápisem, nebo ji jeho deskriptor neuvede a cíl ustoupí nativním SQL. Okenní agregát zápis v T-SQL má. Co vysloví ostatní cíle, se změřilo 2026-10-22 proti připnutým verzím:

- **Hibernate 7.4.5** nad SQL Serverem (`WindowAggregateAndJoinSubqueryClaimsTest`): `count(x) over (partition by …)` vrací hodnotu oddílu u každého řádku; s `order by` v okně vrací běžící agregát — výchozí rámec SQL Serveru, který Hibernate propouští; `max`, `min`, `sum` a `avg` stojí nad oknem stejně, i uvnitř `with` s filtrem nad ním vně (tvar q237). Okno bez řazení píše Hibernate s `order by (select 0)`; všechny řádky oddílu jsou si v něm rovny, takže výchozí rámec obsáhne celý oddíl a hodnota je táž (`AverageClaimsTest`).
- **EF Core 10.0.10**: LINQ okenní funkci nemá (rozhodnutí 113); nic nového se neměří, cíl ustupuje pod touž kategorií jako u pořadových funkcí.
- **NHibernate 5.7.0** a **EclipseLink 5.0.0**: HQL 5.7 ani JPQL 3.2 okno nemají (rozhodnutí 113); ustupují.

**Průměr.** Šablona říká o agregátu, že kromě `COUNT` odpovídá skalárem svého argumentu — tak počítá SQL Server, nad celým číslem useknutým podílem. Diferenciální běh javové sady z 2026-10-22 v kategorii okenního agregátu ukázal, že Hibernate vrací průměr ceny jako `Double`, kde zdroj Dapper vrací `decimal`: kanonický tvar rozhodnutí 089 přesné a přibližné číslo rozlišuje záměrně, a tři páry s Hibernatem jako cílem i zdrojem selhaly. Průměr se proto změřil ve všech šesti jazycích nad celým číslem (1, 2, 5) a nad `decimal(18,4)` (1,0000; 2,5000; 10,1234):

| Jazyk (připnutá verze) | nad celým číslem | nad `decimal(18,4)` |
|---|---|---|
| T-SQL | `AVG(x)` — `int` 2, useknutý podíl | `AVG(x)` — `decimal(38,6)` 4,541133 |
| LINQ EF Core 10.0.10 | `AVG(CAST(x AS float))` — `double` 2,67 | `AVG(x)` — `decimal` |
| LINQ NHibernate 5.7.0 | `cast(avg(cast(x as FLOAT(53))) as FLOAT(53))` — `double` 2,67 | `cast(avg(x) as DECIMAL(29, 10))` — `decimal` |
| HQL NHibernate 5.7.0 | `avg(x)` — `Double` 2 | `avg(x)` — `Double` |
| HQL Hibernate 7.4.5 | `avg(cast(x as float(53)))` — `Double` 2,67 | `avg(x)` — `Double` |
| JPQL EclipseLink 5.0.0 | `AVG(x)` — `Double` 2,0 | `AVG(x)` — `Double` |

Jazyky se tedy liší dvakrát. **Hodnotou**: LINQ a HQL Hibernatu průměrují celé číslo v plovoucí čárce a podíl neusekávají, a to všude, kde průměr stojí — v podmínce, v `HAVING` i v projekci. **Typem**: HQL a JPQL vydávají každý průměr jako `Double`, ač SQL nad desetinným číslem počítá totéž `AVG(x)` v `decimal`; v podmínce se to neprojeví, protože porovnává SQL, ve vráceném sloupci ano. Každý cíl ale umí průměr převést zpět na hodnotu i typ SQL (změřeno týmiž sondami):

| Cíl | celé číslo → `int` (`bigint` → `long`) | `decimal` |
|---|---|---|
| EF Core, LINQ NHibernatu | `(int)g.Average(…)` → `CAST(AVG(CAST(x AS float)) AS int)`, useknuto jako v SQL | beze změny |
| HQL NHibernatu | `cast(avg(x) as int)` | `cast(avg(x) as decimal)` → `DECIMAL(29,10)`, `decimal` |
| HQL Hibernatu | `cast(avg(x) as Integer)` → `cast(avg(cast(x as float(53))) as int)`, useknuto | `cast(avg(x) as BigDecimal(38,6))` → `numeric(38,6)`, `BigDecimal`; bez přesnosti `numeric(38,2)`, které hodnotu zaokrouhlí |
| JPQL EclipseLinku | `cast(avg(x) as INTEGER)` (`BIGINT`) | `cast(avg(x) as DECIMAL(38,6))`, `BigDecimal` |

EclipseLink jméno typu v `cast` propouští do SQL Serveru; `INTEGER`, `BIGINT` a `DECIMAL(p,s)` SQL Server zná, `LONG`, `DOUBLE` a `String` ne — proto jeho deskriptor `Cast` slovníku dál neuvádí (rozhodnutí 113). Totéž platí nad oknem, v poddotazu i v `HAVING`.

Rozhodnout je třeba tři věci: **jaký tvar** okenní agregát v modelu má a co přesně nese, **co je průměr modelu** a jak ho který cíl píše a která čtečka čte, a **co s celým řádkem mezivýsledku** vedle sloupců.

## Zvažované varianty

### Okenní agregát

#### 1 — Ponechat mez a katalog stavem dotazu

Dnešní stav. Zamítáme: katalog by měřil náš slovník, ne cíle. Rozhodnutí 113 vyslovilo měřítko, podle kterého okenní agregát do slovníku patří — zápis v T-SQL má a cíl bez něj ustupuje —, a vynechalo ho jen proto, že ho katalog LDBC nepotřeboval. Druhý katalog ho potřebuje.

#### 2 — Sedmý tvar operandu

Okenní agregát by dostal vlastní tvar vedle sloupce, konstanty, poddotazu, výčtu, parametru a výrazu. Zamítáme: okno už ve slovníku je — čtvrtá továrna výrazu `Window` rozhodnutí 113 — a okenní agregát se od pořadové funkce liší jen tím, co nad oknem stojí: agregát nad argumentem místo číslování. Sedmý tvar by zdvojil oddíly, řazení, pravidlo místa i ústup tří cílů, které tvar okna už má, a rozšířil by `IQueryVisitor`, který S1 drží stabilní.

#### 3 — Přepis přes mezivýsledek

`AVG(x) OVER (PARTITION BY k)` je tolik co join na seskupenou definici `SELECT k, AVG(x) … GROUP BY k`. Řádky tytéž, ale cíl by dostal jiný tvar, než zdroj napsal a než by kdo napsal ručně; rozhodnutí 113 týž přepis zamítlo u seskupení podle výrazu z téhož důvodu. A okno s řazením — běžící agregát — se tak přepsat nedá vůbec.

#### 4 — Okenní agregát jako tvar okna, které už slovník má

### Průměr

#### A — Typ průměru je typ jazyka cíle

Zúžený nárok, jakým je `COUNT`, který HQL vydává jako `Long` a T-SQL jako `int`. Zamítáme: u celého čísla nejde o typ, ale o hodnotu — Dapper vrátí 2, Hibernate a EF Core 2,67 —, a to je jiný dotaz (rozhodnutí 053). A u desetinného čísla `Double` místo `decimal` hodnotu zaokrouhlí na šestnáct platných číslic; kanonický tvar rozhodnutí 089 tu ztrátu rozlišuje záměrně, šířku celého čísla ne.

#### B — Průměr mimo T-SQL ustupuje nativním SQL

Správné, ale každý dotaz s průměrem by ve čtyřech cílech opustil jejich dotazovací jazyk — katalog FIBEN má průměr v desítkách dotazů. Zamítáme: cíle průměr SQL vyslovit umějí, převodem svého průměru (změřeno); únik nativním SQL je pro to, co cíl nevysloví vůbec (rozhodnutí 113).

#### C — Průměr modelu je `AVG` jazyka SQL; cíl ho píše převodem, čtečka čte svůj průměr jako SQL, které počítá

Přesně postup rozhodnutí 155 u `SUM`: význam modelu je význam SQL, cíl, jehož jazyk počítá jinak, píše tvar s hodnotou SQL, a čtečka čte tvar svého jazyka jako to, co počítá.

## Rozhodnutí

**Volíme variantu 4 a variantu C. Tvar `Window` rozhodnutí 113 nese vedle pořadové funkce i jeden z pěti agregátů nad argumentem: `AVG(x) OVER (PARTITION BY … ORDER BY …)`. Řazení je u pořadové funkce povinné, u agregátu volitelné; rámec okna, `DISTINCT` v okenním agregátu a agregát nad agregátem jako argument se odmítají jmenovitě. Okno stojí jen v projekci, jak stojí pořadová funkce. T-SQL a HQL Hibernatu 7.4 ho píší i čtou; EF Core, NHibernate a EclipseLink ustupují nativním SQL pod kategorií `WindowFunction` jako u pořadových funkcí. Průměr modelu — s oknem i bez něj — je `AVG` jazyka SQL: odpovídá skalárem svého argumentu a nad celým číslem useknutým podílem; každý cíl, jehož jazyk průměruje jinak, ho píše převodem svého průměru do toho skaláru, a každá čtečka čte průměr svého jazyka jako SQL, které počítá. A celý řádek mezivýsledku projektovaný vedle sloupců jsou sloupce té definice v pořadí jejích projekcí — rozepsané šablonou před branami, protože sloupce definice jsou její projekce, a rozpis je proto přesný z konstrukce.**

### Tvar

Továrna `QueryExpression.Window` dostává druhou podobu: agregát (`COUNT`, `SUM`, `MIN`, `MAX`, `AVG`) nad listovým operandem, s oddíly a řazením jako dosud. Argumentem je sloupec, konstanta, parametr nebo výraz; `*` jen pod `COUNT`, jak je tomu u agregátu bez okna. Řazení s agregátem se nese: text je týž v T-SQL i v HQL nad týmž dialektem a výchozí rámec SQL Serveru dává tytéž řádky (změřeno).

**Co se odmítá, a kde.** Rámec okna (`ROWS`, `RANGE`, `GROUPS`) čtečky odmítají jmenovitě jako dosud — slovník ho nenese a bez něj by okno počítalo jinou hodnotu. `DISTINCT` v okenním agregátu SQL Server nepřipouští, odmítá ho čtečka. Agregát nad agregátem jako argument (`AVG(COUNT(*)) OVER ()`) odmítá čtečka týmiž slovy, jakými rozhodnutí 107 odmítá `MAX(SUM(x))`: žádný cíl ho nenapíše jinak než přes mezivýsledek, který rozhodnutí 112 nese.

**Typ.** `COUNT` nad oknem počítá v `bigint` jako pořadové funkce, ostatní odpovídají skalárem svého argumentu — totéž, co šablona říká o agregátu bez okna, a průměr nad oknem se píše a čte týmž pravidlem jako bez okna (níže). Okenní agregát **není agregát pravidla seskupení**: Q8 ani pravidlo seskupení ho za agregát neberou a jeho argument posuzují jako prostý sloupec, protože SQL Server okno počítá až nad výsledkem seskupení a argument proto musí být klíčem nebo pod agregátem sám — a agregát pod oknem se odmítá výš. Místo: jen projekce, jako u pořadové funkce; filtr nad oknem jde přes mezivýsledek (rozhodnutí 112), což je přesně tvar q99 i q237.

### Kdo okno píše a kdo čte

| Cíl | Píše | Čte |
|---|---|---|
| Dapper, MyBatis (T-SQL) | `AVG(x) OVER (PARTITION BY … ORDER BY …)` | sdílená čtečka T-SQL (rozh. 082) |
| Hibernate (HQL 7.4) | `avg(x) over (partition by … order by …)` — ověřeno | háček dialektu parseru JPQL, jako `row_number()` |
| EF Core, NHibernate, EclipseLink | ustupují nativním SQL, `Fallback` kategorie `WindowFunction` | LINQ, HQL 5.7 a JPQL okno nemají; nečtou |

Deskriptory se nemění: kategorie `WindowFunction` už říká, kdo okno vysloví.

### Průměr

**Zápis.** Cíl obalí svůj průměr převodem do skaláru, který brána odvodila pro argument — celé číslo do `int` (`bigint` do `long`), `decimal` do desetinného čísla s přesností 38 a měřítkem `max(s, 6)`, které SQL Server průměru dává, kde `s` je měřítko argumentu z mapování a mapování bez měřítka se bere jako nejvýš šest míst. Nad číslem v plovoucí čárce jazyky shodu mají a převod se nepíše. Převod stojí všude, kde průměr stojí — v projekci, v podmínce, v `HAVING`, nad oknem —, protože hodnota celého čísla se v LINQ a v HQL Hibernatu liší všude; jedno pravidlo místo dvou.

| Cíl | celé číslo | `decimal` |
|---|---|---|
| Dapper, MyBatis, nativní SQL | `AVG(x)` | `AVG(x)` |
| EF Core a LINQ NHibernatu | `(int)…Average(…)`; nullovatelně `(int?)`, smí-li průměr být `NULL` | `Average(…)` |
| HQL NHibernatu | `cast(avg(x) as int)` | `cast(avg(x) as decimal)` |
| HQL Hibernatu | `cast(avg(x) as Integer)` | `cast(avg(x) as BigDecimal(38,s))` |
| JPQL EclipseLinku | `cast(avg(x) as INTEGER)` | `cast(avg(x) as DECIMAL(38,s))` |

Zápis EclipseLinku jmenuje typ SQL Serveru, který EclipseLink propouští; patří k profilu implementace vedle zápisu Hibernatu a deskriptor `Cast` dál neuvádí. **Nezná-li brána skalár argumentu**, převod zvolit nejde a dotaz jde v LINQ, HQL a JPQL nativním SQL (`Fallback`, `Aggregation`) — jako `SUM` bez typu součtu v rozhodnutí 155.

**Čtení.** Čtečka čte průměr svého jazyka jako SQL, které počítá, a převody, které píší cíle, zpět jako `AVG(x)`:

| Zdroj | holý průměr | převod zpět na `AVG(x)` |
|---|---|---|
| T-SQL | `AVG(x)` | — |
| LINQ (EF Core, NHibernate) | nad celým číslem `AVG(CAST(x AS Double))`, jinak `AVG(x)` | `(int)`, `(long)` a nullovatelné tvary nad průměrem celého čísla |
| HQL Hibernatu | nad celým číslem `AVG(CAST(x AS Double))`, jinak `AVG(x)` | `cast(… as Integer)`, `Long` nad celým číslem; `cast(… as BigDecimal(p,k))` nad `decimal`, je-li `k` aspoň měřítko průměru |
| HQL NHibernatu | `AVG(x)` | `cast(… as int)`, `long`; `cast(… as decimal)` nad `decimal` |
| JPQL EclipseLinku | `AVG(x)` | `cast(… as INTEGER)`, `BIGINT`; `DECIMAL(p,k)` nad `decimal` |

Převod do celého čísla nad průměrem celého čísla je `AVG(x)` v každém jazyce: tam, kde jazyk průměruje v plovoucí čárce, převod podíl usekne stejně jako SQL; jinde je identitou. LINQ a HQL Hibernatu potřebují k holému průměru typ argumentu z mapování; **neurčí-li ho čtečka**, odmítne dotaz jmenovitě, protože hodnota na typu závisí a odhad by ji mohl změnit (rozhodnutí 053). **Typ, ve kterém HQL a JPQL průměr vydávají**, se nenese: holý průměr nad desetinným číslem, který zdroj vydá jako `Double`, se čte jako `AVG(x)` a cíl vydá tutéž hodnotu přesně v `decimal`; holý průměr celého čísla v HQL NHibernatu a v JPQL EclipseLinku, který SQL usekne a zdroj vydá jako `Double`, cíl vydá jako `int`. Hodnota se nemění, typ je typ SQL — zúžený nárok, jakým je šířka `COUNT`.

### Celý řádek mezivýsledku vedle sloupců

Šablona projde definice v pořadí a potom tělo dotazu a každou projekci celého řádku, jejíž alias ukazuje na definici dříve definovanou, **nahradí sloupci té definice** — jedním sloupcem za každou projekci jejího levého `SELECT`u, pod jejím jménem, v jejím pořadí —, stojí-li vedle dalších projekcí. Děje se to před branami definic, takže popis řádku (rozhodnutí 112) i pravidla sloupců vidí už rozepsaný seznam. Celý řádek jako jediná projekce zůstává celým řádkem, jak ho rozhodnutí 112 vede. Celý řádek **tabulky** vedle sloupců zůstává ztrátou rozhodnutí 070: jeho sloupce zná jen schéma a mapování je úplné jen tehdy, když to katalog potvrdí; u definice je úplnost dána tím, že sloupce definice jsou její projekce.

### Co se nemění

`IQueryVisitor`, šest tvarů operandu, osm kroků šablony a pravidlo šablony, že průměr odpovídá skalárem svého argumentu — nově platí v každém cíli. Pravidlo místa okna, kategorie `WindowFunction` a ústup pod ní. Slovník převodů rozhodnutí 113: převod do `decimal` do něj nevstupuje, cíle ho píší jen nad průměrem a čtečky ho jen tam čtou. Pravidla definice rozhodnutí 112 — jedno jméno na dotaz včetně, takže tři vnořené odvozené tabulky jmenované `a` (q99) odmítá čtečka dál a katalog je nese přejmenované zaznamenanou záplatou importu: jméno si nevymýšlíme (rozhodnutí 028) a tvar nenapíše žádný zdroj kromě T-SQL — C# stínování parametrů lambd zakazuje, `with a …, a` je v HQL i T-SQL neplatné.

## Důsledky

**Věta rozhodnutí 113 o okenním agregátu a rámci okna přestává platit v první půlce; rámec zůstává mezí.** Řádek rozhodnutí 070 o celém řádku vedle sloupců platí dál pro tabulky a pro mezivýsledek ho nahrazuje rozpis.

**Každý průměr nad celým nebo desetinným číslem mění zápis** v EF Core, NHibernatu, Hibernatu a EclipseLinku — přibývá převod —, a průměr celého čísla ze zdroje LINQ a HQL Hibernatu se čte jinak než dosud. Překlad, který dřív vrátil 2,67 tam, kde zdroj vrátil 2, nebo `Double` tam, kde zdroj vrátil `decimal`, vrací hodnotu i typ zdroje.

**Katalog FIBEN se měří znovu**: q237 přechází v překlad — T-SQL a HQL 7.4 nativně, tři cíle únikovou cestou — a q99 s ním, protože obě jeho překážky padají tímto rozhodnutím a třetí nese záplata importu. Co přesně který dotaz nese, říká katalog a drží `FibenCatalogTest`, ne tohle rozhodnutí.

**Matice T2 dostává tři kategorie**: okenní agregát v mezivýsledku s filtrem nad ním ze zdrojů Dapper, MyBatis a Hibernate, s EF Core, NHibernatem a EclipseLinkem jako cíli, které ustupují; průměr celého a desetinného čísla podle SQL ze všech šesti zdrojů, každý svým převodem; a průměr celého čísla v plovoucí čárce ze zdrojů, které ho vyslovují — holý průměr LINQ a HQL Hibernatu, převod argumentu v ostatních.

**Pro sedmý framework se nemění nic** (S1): deskriptorem říká, zda okno vysloví, profilem nebo svým visitorem, jak převádí průměr, a tvar dostává od šablony hotový. Determinismus (S2) drží: rozpis řádku definice je funkcí jejích projekcí a převod průměru funkcí skaláru argumentu.

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR**: vstupy, které končily `Failure`, nově vydají artefakt, a výstupy s průměrem nově vracejí hodnotu zdroje; veřejná plocha se nemění.

**Testy.** Čtení a zápis okenního agregátu v T-SQL a HQL 7.4 (sonda `WindowAggregateAndJoinSubqueryClaimsTest` drží měřený fakt), ústup tří cílů, odmítnutí rámce, `DISTINCT` a agregátu pod oknem, okno v mezivýsledku s filtrem nad ním (tvar q237); průměr v každém z pěti jazyků mimo T-SQL — co vrací holý a co převedený, nad oknem, v poddotazu a v `HAVING` — drží sondy `AverageClaimsTest` obou implementací JPA, `NHibernateHqlProviderTest`, `NHibernateLinqProviderTest` a `EFCoreAverageProviderTest`; zápis převodu každým cílem, ústup bez skaláru argumentu, čtení holého průměru i převodu zpět a odmítnutí bez typu; rozpis celého řádku definice vedle sloupců do T-SQL i HQL a jeho čtení zpět; celý řádek tabulky vedle sloupců dál ztrátou.
