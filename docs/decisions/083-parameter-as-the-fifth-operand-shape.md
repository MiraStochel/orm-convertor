# 083 — Parametr jako pátý tvar operandu podmínky

Datum: 2026-09-18
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S1, S2
Podklad: rozhodnutí [002](002-is-null-as-comparison-operator.md), [014](014-language-type-model.md), [022](022-native-query-syntax-in-builders.md), [023](023-query-builder-template-method.md), [024](024-typed-query-operand.md), [051](051-like-pattern-translated-not-carried-over.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [060](060-pagination-as-a-query-instruction.md), [061](061-subquery-as-a-condition-operand.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [074](074-a-list-of-values-as-the-fourth-operand-shape.md) a [082](082-t-sql-read-and-written-by-a-shared-project.md); JSS §5.3 a §5.4, pravidla Q5, Q14 a Q15; [srovnání javových frameworků](../analysis/java-orm-frameworks-comparison.md), §15; otevřená položka „Operand parametru v dotazové mezireprezentaci"

## Kontext

Mezireprezentace nemá pojem parametru a je to stav vědomě odkládaný od srpna. Rozhodnutí 024 při zavedení typovaného operandu napsalo, že „konstanta je konstanta", parametr nezavedlo a jeho příchod svázalo s „prvním dotazem, který má hodnotu dodat volající". Rozhodnutí 070 z toho vyvodilo důsledek, se kterým nástroj žije dodnes: parametr ve zdroji dotaz **odmítá**, protože dotaz vydaný bez svého filtru by vrátil jiné řádky. Rozhodnutí 074 pak u výčtu hodnot vyslovilo hranici — výčet nese hodnoty, které dotaz sám napsal, kdežto hodnoty dodané volajícím jsou operand parametru — a rozhodnutí 082 hranici na druhé straně: `#{}` MyBatisu je zástupce hodnoty a kandidát na tenhle operand, `${}` je textová substituce, která může nést jméno tabulky nebo celou klauzuli, a parametrem není.

Dnešní stav je proto ve všech čtyřech dotazových parserech týž a je to jediná věta:

| Parser | Tvar parametru ve zdroji | Co udělá |
|---|---|---|
| T-SQL (Dapper) | `@id` — `VariableReference` | `Failure` kategorie `QueryParameter`, artefakt nevzniká |
| LINQ (EF Core, NHibernate) | holý identifikátor v pozici operandu; holý identifikátor jako příjemce `Contains` | totéž |
| HQL (NHibernate) | `:id`, `?` | totéž |
| JPQL (Hibernate, EclipseLink) | `:id`, `?1` | totéž |

Odmítnutí je poctivé a rozhodnutí 070 ho obhájilo třemi důvody. Dva z nich dnes už neplatí. **Druhý — že tvar operandu určují producenti, které nemáme — vypršel:** producenti jsou tu všichni. JPQL parser existuje od rozhodnutí 077 a čte `:id` i `?1`, HQL parser od rozhodnutí 062 čte `:id` i `?`, LINQ parser pojmenovává hodnotu ze scope a od rozhodnutí 074 i kolekci ze scope, a čtení SQL se rozhodnutím 082 stěhuje do sdíleného projektu, odkud ho zdědí i MyBatis. **Třetí — že cena odkladu je malá, protože matice T2 kategorii „parametr" nemá — se obrátil**, jak ukazuje odstavec níž. **A první — že operand není jen jméno — je přesně to, co má tohle rozhodnutí zodpovědět.**

**Bez operandu parametru nejde F8 splnit vůbec.** Kritérium žádá „parametry" a „dynamicky parametrizované read-only dotazy" doslova a §15 srovnání ukazuje proč: dynamický příkaz MyBatisu je rodina SQL příkazů, z níž `getBoundSql` vrací jednoho člena pro konkrétní sadu parametrů, takže i po vyhodnocení dynamických značek zůstane v textu `#{}` — zástupný symbol, který musí něco nést. Cena odkladu tím přestává být malá i mimo MyBatis: **většina reálných dotazů všech šesti frameworků je parametrizovaná**, což rozhodnutí 070 samo zapsalo mezi důsledky jako viditelnou cenu. Matice T2 se dnes plní na dotazech, které parametr nemají, tedy na vzorku, který se reálnému použití nepodobá — a to není prázdná buňka, nýbrž otázka platnosti všech buněk naráz. Pro T3 je to totéž: podíl spustitelných výstupů se měří na dotazech s vypsanými hodnotami, které by v cílovém projektu nikdo nepoužil.

**Článek pojem parametru nemá.** §5.3 vyjmenovává operandy podmínky jako „constants, attributes, or nested subqueries", pravidla Q1–Q15 parametr nezmiňují a Table 2 pro něj nemá řádek. Je to tedy rozšíření nad článek téhož druhu jako poddotaz jako operand (rozhodnutí 061), výčet hodnot (rozhodnutí 074), `PAGINATE` (rozhodnutí 060) a `DISTINCT` (rozhodnutí 073), a opírá se o §5.4, který rozšiřitelnost deklaruje. Slovník `QueryFeature` naproti tomu kategorii `QueryParameter` má od rozhodnutí 022 a **deskriptor každého z pěti dnešních cílů ji uvádí jako vyjádřitelnou** — je to pravda o cílech, kterou dosud nic nevyužilo.

Čtyři věci dělají z otázky rozhodnutí, ne psaní kódu, a rozhodnutí 070 je vyjmenovalo předem.

**Typ parametru není v dotazu.** Každý z pěti dnešních cílů vydává vedle holého textu dotazu spustitelnou metodu (rozhodnutí 022) a parametr v ní musí být **typovaný parametr metody**: `int id`, ne `object id`, protože EF Core zachycuje proměnnou do lambdy a `int == object` se v LINQ nepřeloží. Zdroj typ zpravidla neříká — `@id`, `:id` ani holý identifikátor v LINQ ho nenesou —, takže se musí odvodit z druhé strany porovnání přes **mapovací** mezireprezentaci. Tu ale dotazový parser nemá: `EntityMaps` je vlastnost `AbstractQueryBuilder`u, kterou mu orchestrace předává až před `Build()`. Místo odvození je tím dané a není volbou parseru.

**Jméno parametru má napříč oběma ekosystémy tři tvary.** Pojmenovaný (`@id`, `:id`, `#{id}`), poziční s číslem (`?1` v JPQL) a poziční bez čísla (`?` v HQL). Z pěti dnešních cílů mají poziční tvar dva, oba přes JPQL, a MyBatis ho jako šestý mít nebude; pravidlo o převodu mezi tvary je tedy pravidlo pro většinu matice, ne výjimka.

**Kolekční parametr je vlastní tvar, ne zvláštní případ.** `IN (:ids)`, `ids.Contains(c.Id)` a `<foreach>` MyBatisu nad kolekčním parametrem sem rozhodnutí 074 poslalo jmenovitě a signatura metody je rozlišuje od skalárního parametru na první pohled: `IEnumerable<int> ids` proti `int id`.

**Artefakty jsou dva a parametr je v každém jinak.** Holý text (`SqlQuery`, `HqlQuery`, `JpqlQuery`) nese zástupný symbol a nic jiného; metoda vedle něj ho pojmenovává, typuje a váže. Rozhodnutí 070 se ptalo, co se stane s parametrem v holém textovém artefaktu, který metodu nemá.

## Zvažované varianty

### 1 — Ponechat odmítnutí

Dnešní stav, zvolený rozhodnutím 070. Zamítáme, protože všechny tři jeho důvody vypršely nebo se obrátily: producenti jsou tu, F8 parametr žádá doslova, a cena odkladu už není jedna buňka matice, ale reprezentativnost celého vzorku T2 i T3. Odklad navíc nemá kam pokračovat — po F8 nepřijde další producent, který by slovník upřesnil, takže „počkáme na víc vstupů" je od téhle chvíle jen jiné slovo pro „neuděláme to".

### 2 — Nést parametr jako konstantu, jejíž text je zástupný symbol

Nejmenší zásah do modelu: `QueryConstant.Unrecognized("@id")` a hotovo, visitory ho vypíšou doslova. Zamítáme třemi důvody a každý sám o sobě stačí.

**Je to přesně vada, kterou 024 odstranilo.** Konstanta nese hodnotu **bez zdobení** právě proto, aby zdobení bylo věcí cíle; `@id` je zdobení zdroje a doteklo by do všech cílů. Dapper dotaz s `@id` by se do HQL vypsal jako `@id`, což NHibernate nesváže a co se ani nezkompiluje do metody, protože ta by parametr neměla v signatuře.

**Model by přestal umět odpovědět, co je hodnota.** Konstanta je hodnota, kterou dotaz vyslovil; parametr je hodnota, kterou dotaz **nezná**. Splynutím obojího by brána z rozhodnutí 053 přišla o možnost se ptát — a ptát se musí, protože `IN (1, 'a')` odmítá podle skaláru, který parametr nemá.

**Nešlo by vyrobit signaturu metody.** Builder by neměl z čeho poznat, že text `@id` je parametr, a ne řetězec; rozeznávat ho podle tvaru textu je hádání, které 024 zamítlo jako svou variantu 1.

### 3 — Deklarace parametrů na úrovni dotazu, operand jen odkazem

Dotaz by nesl seznam deklarací (jméno, typ, kolekčnost) a operand by se na ně odkazoval jménem. Je to lákavé, protože signatura metody seznam stejně potřebuje a parametr použitý dvakrát by byl deklarovaný jednou. Zamítáme ze tří důvodů.

**Zavádí do modelu vazbu, kterou musí někdo hlídat.** Odkaz na nedeklarovaný parametr a deklarace bez užití jsou dva nové neplatné stavy — a **mezireprezentace nevaliduje**; je to invariant, který drží od začátku a který si rozhodnutí 074 pohlídalo tím, že prázdný výčet odmítá továrna, ne kontrola. Dvě místa pravdy o jednom faktu jsou přesně ten tvar, který 014 odmítlo u jazykových typů.

**Seznam se dá spočítat, a tedy se nemá ukládat.** Builder prochází podmínkový strom v `Normalize()` beztak; posbírat z něj parametry podle jména je průchod navíc, ne nový fakt. Uložený seznam by se s obsahem stromu mohl rozejít, spočítaný nikdy.

**Odporuje to gramatice článku.** §5.3 klade konstantu i poddotaz do pozice **operandu**; deklarace na úrovni dotazu je nová úroveň, kterou článek nemá, a pro každý visitor znamená nový vstupní bod. Je to táž úvaha, kterou 061 zamítlo `ExistsCondition` a 074 `InListCondition`.

### 4 — Pátý tovární tvar operandu: vlastní typ parametru vedle konstanty, skalár doplněný sdílenou bránou

## Rozhodnutí

**Volíme variantu 4. `QueryOperand` dostává pátý tovární tvar `Parameter`, jehož obsahem je nový typ `QueryParameter` vedle `QueryConstant` — jméno bez zdobení, nebo pořadí u pozičního tvaru, nepovinný skalár a příznak kolekčnosti. Parsery čtou parametr ze svého tvaru, sdílená brána v `Normalize()` mu doplní skalár z mapovací mezireprezentace a buildery ho vypíšou zástupným symbolem svého cíle a typovaným parametrem generované metody.**

**Operand zůstává uzavřený typ s továrnami** (rozhodnutí 024, 061, 074): sloupec, konstanta, poddotaz, výčet hodnot, nebo parametr. Neplatná kombinace není zapsatelná a nový tvar nepřidává na `QueryOperand` čtyři pole, nýbrž jedno — `Parameter`, po vzoru `Constant`. Parametr je vlastní typ ze tří důvodů: je to táž souměrnost, jakou má konstanta (hodnota plus skalár), drží pohromadě fakta, která platí jen o parametru (jméno proti pořadí, kolekčnost), a **`IQueryVisitor` se nemění**, což je plocha, kterou S1 slibuje držet pro sedmý framework stabilní.

### Co parametr nese

**Jméno bez zdobení, přesně jako konstanta nese hodnotu bez zdobení.** Do modelu jde `id`, ne `@id`, ne `:id`, ne `#{id}`. Zdobení je vlastnost jazyka cíle, parser ho **odstraňuje** a builder **přidává** — táž dělba, jakou 024 vyslovilo pro uvozovky a číselné přípony a 021 pro jména generátorů.

**Poziční tvar nese pořadí místo jména.** Jedno z obojího, nikdy obojí a nikdy nic: `:id` je jméno `id`, `?1` je pořadí 1, `?` je pořadí dané výskytem v textu, počítané od jedné. Nesené je pořadí, ne vymyšlené jméno, protože **jméno pozičního parametru zdroj neřekl** a vymýšlet ho v parseru by znamenalo dát do modelu fakt, který ve zdroji není (rozhodnutí 028). Kde jméno vzniknout musí — v signatuře metody a v každém cíli bez pozičního tvaru —, vzniká v **builderu** a je to zaznamenaná konvence; viz níž.

**Míchat pojmenovaný a poziční tvar v jednom dotazu je `Failure`.** Jediný cíl, který oba tvary má, je JPQL, a ten jejich souběh v jednom dotazu nepodporuje; ostatní cíle mají jen pojmenovaný, takže by jeden z tvarů musely vymyslet. Záznam kategorie `QueryParameter` oba tvary jmenuje. *Ověřit proti specifikaci Jakarta Persistence 3.2 při implementaci — tvrzení je z chování obou implementací, ne z citovaného odstavce.*

**Skalár je nepovinný a zdroj ho zpravidla neřekne.** Nese se z téhož slovníku `ScalarType`, kterým 024 otypovalo konstantu, a ze stejného důvodu: parametr a konstanta jsou týž druh faktu — hodnota —, takže druhý slovník pro totéž by byl duplicita, kterou 014 odstraňovalo na jazykové straně. `null` znamená „zdroj typ neuvedl" a doplní ho brána. Kde ho zdroj **uvede**, nese se přečtený: `#{id,javaType=Integer}` a `parameterType` na `<select>` MyBatisu jsou jediný takový případ v celé šestici a je to fakt zdroje, který by se jinak zahodil — což by šlo proti rozhodnutí 048, podle kterého fakt bez místa v modelu je hlášená ztráta, ne mlčení. Uvedený skalár má přednost před odvozeným; rozejdou-li se, platí uvedený a **rozdíl se hlásí záznamem `Conflict`**, po vzoru rozhodnutí 015 u katalogu — dva zdroje jednoho faktu se nesrovnávají potichu.

**Kolekční parametr je týž typ s příznakem, ne šestý tvar operandu.** `IN (:ids)` je pořád parametr — jméno, pořadí i odvození skaláru fungují stejně — a liší se jedině tím, že hodnotou je seznam. Příznak proto sedí na `QueryParameter`, ne na `QueryOperand`: kolekčnost je vlastnost té hodnoty, ne pozice v podmínce.

**Hodnotu parametr nenese nikdy.** Ta je vlastností volání, ne dotazu; nést ji by znamenalo nést `getBoundSql` jednoho konkrétního běhu místo dotazu (§15 srovnání).

### Kde parametr stojí

**Skalární parametr stojí všude, kde stojí konstanta** — jako levý i pravý operand porovnání, pod agregační funkcí i jako operand porovnání s poddotazem. **Kolekční parametr stojí jedině jako pravý operand operátoru `In`**, taxativně, týmž pravidlem a v témž místě, jakým 074 vymezilo pozici výčtu hodnot; jinde je `Failure` kategorie `QueryParameter`.

Dvě místa vyslovujeme jako **vyňatá** a obě mají důvod, ne opomenutí.

**Uvnitř výčtu hodnot parametr nestojí.** `IN (1, @p)` zůstává odmítnuté, jak to zapsalo rozhodnutí 074. Výčet je podle něj seznam hodnot, které dotaz vyslovil, a jeho pravidlo o sjednotitelném skaláru pracuje se skaláry přečtenými; parametr žádný nemá, dokud mu ho brána nedoplní, a doplňovat mu ho z ostatních prvků výčtu by byla třetí cesta odvození vedle dvou níž. Cena je malá: tvar, který se v praxi píše, je `IN (:ids)` s jedním kolekčním parametrem, a ten nesený je.

**Ve stránkování parametr nestojí.** `OFFSET @skip ROWS FETCH NEXT @take` a `limit #{n}` zůstávají odmítnuté záznamem kategorie `QueryParameter`, který klauzuli jmenuje. Důvod je v rozhodnutí 060: `PaginationInstruction` vědomě nese **dvě čísla, ne strom**, nevykresluje se přes visitor a kam čísla v cíli padnou, řeší krok stránkování každého builderu zvlášť. Dát jí operandy je volba o instrukci, ne o operandu, dotýká se každého builderu a patří vlastnímu rozhodnutí; otevíráme ji jako položku v [`open-items.md`](../open-items.md), ne mimochodem tady. **Je to viditelná mez** — parametrizované stránkování je běžné — a zapisujeme ji jako vyslovenou, ne jako díru.

### Odkud se bere skalár

**Odvozuje ho sdílená brána v `Normalize()`, ne parser.** Důvod je věcný, ne vkusový: skalár se odvozuje z mapovací mezireprezentace a tu má jen builder (`EntityMaps`, předané orchestrací před `Build()`). Je to zároveň totéž místo a tatáž úvaha jako u pravidel výčtu v rozhodnutí 074: parser čte, co zdroj napsal, a otázku „unese to cíl?" klade šablona — jednou, aby všechny cíle odpovídaly stejně (rozhodnutí 023 a 053).

Skalár se bere z **druhé strany téhož porovnání**:

| Druhá strana porovnání | Skalár parametru |
|---|---|
| sloupec | skalár vlastnosti, kterou sloupec mapuje |
| sloupec pod agregací `COUNT` | `Long` |
| sloupec pod jinou agregací (`SUM`, `AVG`, `MIN`, `MAX`) | skalár toho sloupce |
| konstanta | skalár konstanty |
| vzorek operátoru `Like` (parametr na kterékoli straně) | `String` |
| poddotaz | neodvozuje se |
| jiný parametr | neodvozuje se |

U **kolekčního** parametru se týmž pravidlem odvozuje skalár **prvku** — levý operand `In` je sloupec a typuje prvky, ne seznam; seznam vlastní skalár nemá, přesně jako výčet hodnot podle rozhodnutí 074.

**Co se neodvodí, odmítá artefakt** záznamem `Failure` kategorie `QueryParameter`, který parametr jmenuje a řekne, proč se typ nezjistil. Sem patří poddotaz i parametr na druhé straně, sloupec, který se v mapovací mezireprezentaci nenajde, a vlastnost bez jazykového typu (rozhodnutí 075). Je to táž odpověď, jakou dává 053 na neúplný podmínkový strom, a platí i pro Dapper cíl, který by text opsat mohl: všechny cíle mají odpovídat stejně.

**Skalár se bere v nenulovatelném tvaru.** `= @p` neporovnává s `NULL` nikdy — test na `NULL` je podle rozhodnutí 002 vlastní operátor `IsNull`/`IsNotNull` bez pravého operandu —, takže nulovatelnost vlastnosti se do signatury metody nepropisuje a vlastnost typu `int?` dá `int id`.

**Týž parametr použitý vícekrát je jeden parametr.** Shodné jméno — u pozičních shodné pořadí — znamená jednu hodnotu a jeden parametr metody. Odvodí-li se pro dva výskyty téhož jména **různý** skalár, jako ve `WHERE Id = @x OR Name = @x`, je to `Failure` kategorie `QueryParameter` se záznamem, který oba skaláry jmenuje; sjednocovat je bychom museli hádáním a jeden z obou výskytů by pak porovnával jinou hodnotu, než zdroj napsal.

**Jméno, které není prostý identifikátor, se odmítá.** `#{user.name}` MyBatisu je cesta k vlastnosti parametrického objektu a signatura metody ji nevysloví; je to `Failure` kategorie `QueryParameter`, který jméno jmenuje. Jestli a jak se cesty čtou, je věcí rozhodnutí k F8 — tohle rozhodnutí o nich netvrdí nic než to, že dnes nesené nejsou.

### Čtení: co je v kterém zdroji parametr

- **T-SQL** (po rozhodnutí 082 ve sdíleném projektu): `VariableReference`, tedy `@id`, je pojmenovaný parametr `id`. Poziční tvar T-SQL nemá.
- **LINQ**: holý identifikátor v pozici operandu je pojmenovaný parametr téhož jména — dnes hodnota ze scope, kterou parser umí pojmenovat a odmítá. Holý identifikátor jako **příjemce** `Contains` je kolekční parametr; `Contains` nad kořenem dotazu zůstává poddotazem (rozhodnutí 061) a inline kolekce výčtem (rozhodnutí 074).
- **HQL**: `:id` je pojmenovaný, `?` poziční s pořadím podle výskytu.
- **JPQL**: `:id` je pojmenovaný, `?1` poziční s uvedeným pořadím.
- **SQL MyBatisu** (až vznikne wrapper podle F8): `#{id}` je pojmenovaný parametr, `${id}` **parametr není** — je to textová substituce, kterou rozhodnutí 082 vyloučilo, a zůstává `Failure` kategorie `QueryParameter` se záznamem, který řekne, že zdroj substituuje text, ne hodnotu. Čím se zástupné symboly nahrazují před předáním gramatice a jak se vyhodnocují dynamické značky, je věcí rozhodnutí k F8.

**Jedno pravidlo platí pro všechny:** parametr se nikdy nepřepíše na konstantu. Vypsat za `@id` hodnotu by změnilo množinu řádků (rozhodnutí 053) a zároveň by z vázaného parametru udělalo literál, tedy přesně ten rozdíl, kterým §15 srovnání odlišuje bezpečné `#{}` od nebezpečného `${}`.

### Zápis: co vypíše který cíl

Holý textový artefakt nese zástupný symbol svého jazyka a nic dalšího — deklaraci nepotřebuje, protože ji volající píše sám tam, kde dotaz váže. Generovaná metoda dostává parametry **za dosavadní první argument**, v pořadí prvního výskytu při průchodu instrukcemi (u pozičních podle pořadí), takže tentýž model dá tutéž signaturu (S2):

| Cíl | V textu dotazu | Metoda |
|---|---|---|
| Dapper | `@id`; u kolekce `IN @ids` bez závorek, jak Dapper seznam rozepisuje | `…(IDbConnection connection, int id)` a `new { id }` do `Query<T>` |
| EF Core | — (řetěz LINQ) | `…(DbContext ctx, int id)`, parametr zachycený lambdou; u kolekce `IEnumerable<int> ids` a `ids.Contains(x)` |
| NHibernate | `:id` v HQL | `…(ISession session, int id)` a `.SetParameter("id", id)`; u kolekce `.SetParameterList("ids", ids)` |
| Hibernate, EclipseLink | `:id`, u pozičního `?1` v JPQL | `…(EntityManager em, int id)` a `.setParameter("id", id)`, u pozičního `.setParameter(1, p1)` |
| MyBatis (F8) | `#{id}` | metoda rozhraní mapperu s `@Param("id")` |

**Poziční parametr přežije jedině do JPQL; do ostatních cílů se převádí na pojmenovaný a je to záznam `Convention`.** Jméno vzniká z pořadí (`p1`, `p2`) a záznam říká, že výstup pojmenoval, co zdroj nepojmenoval — což je přesně definice toho druhu záznamu (rozhodnutí 010). Není to ztráta: dotaz se váže stejně, jen jinou cestou. Opačný směr — pojmenovaný do JPQL — jde beze změny, protože JPQL pojmenovaný tvar má.

**Deskriptory se nemění a jejich dosavadní deklarace se stává pravdivou.** Všech pět dnešních cílů uvádí `QueryFeature.QueryParameter` jako vyjádřitelnou; dosud to byla pravda o cílech, kterou nic nevyužívalo, protože chyběl operand v modelu. Mechanické hlášení podle pravidla Q14 se proto ani nadále nespustí a záznamy, které v tomhle rozhodnutí zůstávají, jsou vždy o mezi **modelu** nebo o odvození typu, ne o neschopnosti cíle.

**`LIKE` s parametrem má vlastní větev a rozhodnutí 051 pro ni odpověď už má.** Rozklad vzorce na `StartsWith`/`Contains`/`EndsWith` je povolený jen tam, kde je přesný, a vzorec, který při překladu neznáme, přesný být nemůže; EF Core proto vypíše `EF.Functions.Like(left, vzorec)`, což je tatáž větev, jakou 051 vyhradilo pro pravý operand, který není konstanta. Žádný záznam to nepotřebuje, protože se nic neztrácí.

## Důsledky

**Nejběžnější tvar reálného dotazu se stává přeložitelným ve všech pětadvaceti dnešních směrech — a po MyBatisu ve všech šestatřiceti.** Pro T2 to není nová buňka — kategorie „parametr" v matici není —, ale je to změna vzorku, na kterém se všechny buňky měří: filtrace, joiny i poddotazy se nově dají vyhodnotit na dotazech, jaké se v cílových projektech opravdu píšou. Pro T3 totéž: podíl spustitelných a funkčně ekvivalentních výstupů přestává být měřený výhradně na dotazech s vypsanými hodnotami.

**Věta rozhodnutí 070 o odmítnutí parametru přestává platit; 070 samo platí dál.** Jeho pravidlo — parser odmítá to, co by změnilo množinu řádků — se nemění a je to právě ono pravidlo, ze kterého plynou zbývající odmítnutí tady. Mění se jen osud parametru, protože model ho nově nese, takže parser nemá co odmítat. Nový soubor je tu proto, že 070 je naimplementované a revize na místě by čtenáři vzala starší znění — týž vztah, jaký má 074 k 061 a 065 k 053. Totéž platí pro větu rozhodnutí 024 o parametru jako ztrátě se záznamem, kterou 070 zrušilo už jednou.

**Věta rozhodnutí 074 o kolekci ze scope se naplňuje.** `ids.Contains(c.Id)` a kolekční parametr JPQL, které 074 poslalo sem, mají tvar; věta „hodnoty, které dodá volající, jsou operand parametru" přestává být příslibem. Zůstává z něj jediná výjimka, vyslovená výš: parametr **uvnitř** výčtu hodnot nesený není.

**Hranice rozhodnutí 082 se potvrzuje a nerozšiřuje.** `#{}` je kandidát na tenhle operand a tímhle rozhodnutím jím je; `${}` parametrem není a zůstává odmítnuté. Co se čím nahrazuje před předáním gramatice T-SQL a jak se vyhodnocují dynamické značky, řekne rozhodnutí k F8, kterému tenhle operand odblokovává první ze dvou blokerů.

**Parametrizované stránkování zůstává vyňaté a má vlastní položku.** Je to jediná mez, kterou tohle rozhodnutí vědomě nechává otevřenou, a je vidět: dotaz s `OFFSET @skip` se dál nepřeloží. Důvod je v rozhodnutí 060, které stránkování navrhlo jako dvě čísla; změnit to je volba o instrukci.

**Bezpečnostní stránka se zlepšuje jako vedlejší efekt a stojí za vyslovení.** Nástroj dosud parametrizovaný dotaz odmítal a přeložit uměl jedině dotaz s vypsanými hodnotami; nově vydává do každého cíle vázaný parametr, tedy tvar, který podle §15 srovnání chrání proti SQL injection ve všech třech javových i ve všech třech .NET frameworcích. Opačný převod — parametr na literál — tohle rozhodnutí zakazuje výslovně.

**Pro sedmý framework se nemění nic** (S1). `Model` dostává jeden typ a jednu tovární metodu, `AbstractWrappers` pravidla v bráně — obojí teď a jednou. `IQueryVisitor` se nemění, deskriptory se nemění, orchestrace se nemění. Každý dotazový builder dostává krok navíc v sestavení signatury metody, což je práce uvnitř wrapperu — a u Hibernate a EclipseLinku jednou, protože ji nesou společně v `AbstractJpaQueryBuilder` (rozhodnutí 077 a 080).

**Slovník operandu vystupuje nad výčet článku podruhé.** §5.3 jmenuje tři druhy, nástroj má pět: sloupec, konstanta, poddotaz, výčet hodnot, parametr. Text práce to opře o §5.4, který rozšiřitelnost deklaruje, a o to, že parametr je jediná kategorie `QueryFeature`, kterou rozhodnutí 022 zavedlo **nad** rámec kategorií matice T2 — tedy kategorie, kterou si vyžádaly frameworky, ne měření.

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR**: přibývá schopnost — vstupy, které dosud skončily `Failure`, nově vydají artefakt —, veřejné rozhraní ani tvar odpovědi se nemění. Vyjde s vydáním, které podle [`open-items.md`](../open-items.md) cíl 2 vydá před `2.0.0`.

**Testy.** Ze čtyř zdrojů do všech cílů dotaz s jedním pojmenovaným parametrem, s asercí na obojí: zástupný symbol v holém textu a typovaný parametr v signatuře metody i v jejím vázání (`new { }`, `SetParameter`, `setParameter`, zachycení lambdou). Odvození skaláru ze sloupce, z konstanty, z `COUNT` (`Long`), z jiné agregace a z operátoru `Like` (`String`). Poziční parametr: round-trip JPQL → JPQL zachová `?1`, do ostatních cílů vyjde pojmenovaný se záznamem `Convention`. Kolekční parametr: `in (:ids)` z HQL a JPQL, `ids.Contains(c.Id)` z LINQ, `IN @ids` do Dapperu a `SetParameterList` do NHibernate. Týž parametr dvakrát v jednom dotazu jako jeden parametr metody. Uvedený skalár MyBatisu, který se rozchází s odvozeným, jako `Conflict` s předností uvedeného. A odmítnutí, pokaždé prázdný výstup a `Failure` kategorie `QueryParameter`, který parametr jmenuje: parametr proti poddotazu, parametr proti parametru, sloupec mimo mapovací mezireprezentaci, týž parametr se dvěma skaláry, míchání pojmenovaného a pozičního tvaru, kolekční parametr mimo pravou stranu `In`, parametr ve výčtu hodnot, parametr ve stránkování a jméno, které není prostý identifikátor.
