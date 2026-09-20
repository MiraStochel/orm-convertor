# 085 — Počet řádků je číslo, nebo parametr

Datum: 2026-09-20
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S1, S2
Podklad: rozhodnutí [010](010-diagnostics-as-returned-data.md), [014](014-language-type-model.md), [023](023-query-builder-template-method.md), [024](024-typed-query-operand.md), [028](028-assembly-name-is-not-ours-to-invent.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [060](060-pagination-as-a-query-instruction.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [074](074-a-list-of-values-as-the-fourth-operand-shape.md), [082](082-t-sql-read-and-written-by-a-shared-project.md), [083](083-parameter-as-the-fifth-operand-shape.md) a [084](084-mybatis-wrapper-over-the-shared-sql-reading.md); JSS §5.3–5.4, tabulka 2, pravidla Q1 a Q14; otevřená položka „Parametr ve stránkovací instrukci"

## Kontext

Rozhodnutí 083 dalo parametru místo všude, kde stojí konstanta, tedy uvnitř podmínkového stromu, a jednu jedinou mez nechalo vědomě otevřenou: stránkování. `OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY` v T-SQL, `.Take(pageSize)` v LINQ, `limit :n` v HQL i `FETCH NEXT #{take} ROWS ONLY` v MyBatisu se dál odmítají záznamem `Failure` kategorie `QueryParameter`, který od 2026-09-20 jmenuje klauzuli i parametr. Rozhodnutí 083 tu mez zapsalo jako vyslovenou, ne jako díru, a poslalo ji sem.

Důvod té meze je v rozhodnutí 060 a není to opomenutí. `PaginationInstruction` nese **dvě čísla, ne strom**: nevykresluje se přes `IQueryVisitor`, protože kam čísla v cíli padnou, není vlastnost jazyka, nýbrž cíle — do závorky za `TOP`, za řazení do `OFFSET/FETCH`, na konec řetězu LINQ, nebo úplně mimo text dotazu na `IQuery` NHibernate a na `Query` JPA. Čísla proto čte osmý krok šablony každého builderu přímo z `QueryClauses`, a ne visitor. Dát instrukci operandy je volba o instrukci, dotýká se všech pěti dotazových builderů a patří vlastnímu rozhodnutí.

**Cena té meze je přitom podstatně větší než cena druhé meze, kterou 083 nechalo stát.** U výčtu hodnot je nenesený tvar `IN (1, @p)` okrajový a tvar, který se v praxi píše — `IN (:ids)` s jedním kolekčním parametrem —, nesený je; 083 si to samo tak spočítalo. U stránkování je to obráceně: **okrajový je ten literálový tvar.** `OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY` je dotaz na jednu jedinou konkrétní stránku a nikdo takový dotaz nepíše; stránkuje se tak, že se číslo stránky dodá při volání, protože jinak by dotazů musel být jeden na každou stránku. Nástroj tedy v kategorii, kterou T2 jmenuje mezi svými sedmi — projekce, filtrace, joiny, agregace, **stránkování**, poddotazy, množinové operace —, měří překlad na jediném tvaru, který se ve zdrojových projektech nevyskytuje. To není nereprezentativní vzorek ve smyslu, v jakém o něm mluvilo 083 u ostatních buněk; to je vzorek s realitou disjunktní a buňka matice nad ním nevypovídá o ničem.

Vůči článku se tím nejde nikam dál, než kam 060 došlo už jednou. `PAGINATE(offset?, limit?)` je rozšíření nad normalizovanou sadu instrukcí pravidla Q1, opřené o §5.4, který rozšiřitelnost reprezentace výslovně deklaruje, a o tabulku 2, která stránkování mezi dotazovými aspekty všech osmi frameworků vede. Tohle rozhodnutí sadu instrukcí nerozšiřuje — dopisuje tvar hodnoty uvnitř instrukce, která už existuje, aby nesla to, co frameworky doopravdy píšou.

Čtyři věci dělají z otázky rozhodnutí, ne psaní kódu.

**Přihrádka stránkování není pozice operandu.** Operand podmínky má podle rozhodnutí 024, 061, 074 a 083 pět tvarů a stojí jako strana porovnání; počet řádků žádnou stranou porovnání není a z těch pěti tvarů pro něj dávají smysl dva. Jestli se slovník operandu do stránkování přenese celý, nebo jestli pro počet řádků vznikne vlastní, užší tvar, je první otázka a rozhoduje o tom, co půjde do modelu vůbec zapsat.

**Druhá strana porovnání tady není.** Skalár parametru odvozuje podle 083 sdílená brána v `Normalize()` z druhé strany téhož porovnání; u stránkování žádná druhá strana neexistuje. Typ se musí vzít odjinud — a otázka není jen odkud, ale i jak široký: `TOP` i `OFFSET/FETCH` berou v T-SQL `bigint`, kdežto `Skip`, `Take`, `SetFirstResult`, `SetMaxResults`, `setFirstResult` i `setMaxResults` berou 32bitové `int`. Právě proto nástroj dnes literál nad `Int32` ve čtyřech ze šesti cílů odmítá v místě emise.

**Jeden zdroj o parametru mluví.** MyBatis nese vyslovený skalár — `#{take,javaType=Integer}`, `parameterType`, podpis metody mapperu — a předává ho gramatice v `SqlParameterFacts` (rozhodnutí 084). U stránkovacího parametru se tedy poprvé může stát, že zdroj typ **řekne**, a řekne ho jinak, než jaký žádá pozice. Pravidlo 083 „vyslovený má přednost před odvozeným, rozdíl je `Conflict`" na tenhle případ nesedí, protože tady se vyslovený skalár nerozchází s odhadem.

**Tři z šesti cílů stránkování do textu dotazu nepíšou.** U NHibernate, Hibernate a EclipseLinku je vázaný počet řádků argumentem volání API, ne parametrem dotazu: v holém HQL ani JPQL po něm nezůstane stopa — stejně jako po literálu. Co se v takovém případě hlásí, odpovědělo 060 mlčením podle vzoru 028, ale je třeba to vyslovit znovu: teď jde o parametr, a u parametru by čtenář záznam čekal.

## Zvažované varianty

### 1 — Ponechat odmítnutí

Dnešní stav, zvolený rozhodnutím 083. Má jednu skutečnou přednost: odmítnutí je poctivé, protože dotaz vydaný bez svého výřezu vrací nadmnožinu řádků (rozhodnutí 053 a 060), a nástroj tedy netvrdí nic nepravdivého.

Zamítáme, protože poctivost odmítnutí není totéž co užitečnost nástroje a tady se obojí rozešlo o celou kategorii matice. Odklad navíc nemá kam pokračovat: všech šest wrapperů je hotových, sedmý producent, který by slovníku něco upřesnil, nepřijde a `PaginationInstruction` je poslední místo modelu, kde hodnota stojí a parametr stát nesmí. Argument „počkáme na víc vstupů", kterým rozhodnutí 070 odklad obhajovalo, je od téhle chvíle jen jiné slovo pro „neuděláme to" — což 083 o téže otázce napsalo doslova.

### 2 — Do obou přihrádek instrukce dát `QueryOperand`

Sjednocení slovníku: `PaginationInstruction(QueryOperand? Offset, QueryOperand? Limit)`, pátý tovární tvar rozhodnutí 083 se použije beze změny a sedmý framework se učí jediné pravidlo — „parametr stojí všude, kde stojí hodnota". Brána by pak vyslovila, že stránkovací operand smí být jen nezáporný celočíselný literál nebo skalární parametr.

Zamítáme třemi důvody.

**Zapsatelné by bylo to, co žádný cíl nevydá.** Sloupec, poddotaz, výčet hodnot ani agregační funkce nad počtem řádků nemají v téhle pozici smysl a žádný ze šesti cílů je nezapíše. Model, do kterého to zapsat jde, si žádá pravidlo, které to zakáže — a pravidlo hlídané za běhu je slabší než typ, který zápis nedovolí. Uzavřený typ s továrnami je zařízení, kterým model pracuje od rozhodnutí 024 („neplatná kombinace není zapsatelná") a které si 074 i 083 výslovně pohlídaly; opustit ho právě v přihrádce s nejužším oborem hodnot v celém dotazovém modelu by bylo obrácené pořadí. Námitka, že to rozhodnutí tu kontrolu do brány stejně napíše, na tom nic nemění: kontrola by nehlídala žádný vstup, který dnešní parsery vyrábějí, nýbrž jen budoucího sedmého — dělala by tedy práci typového systému, a hůř než on.

**Literál by přestal být číslo.** `QueryConstant` nese hodnotu jako `Text` typu `string` a skalár, který smí být `null` (`Unrecognized`). Počet řádků je dnes `long?` a tři buildery nad ním počítají mez `Int32`. Cesta přes konstantu znamená, že si každý builder číslo parsuje z řetězce zpátky, že do modelu půjde zapsat `TOP ('deset')` a že se odpověď na otázku „vejde se to do `Skip`?" přesune z typu do běhu. Je to přesně ta ztráta přesnosti, kvůli které 060 zvolilo dvě čísla, a nic z toho, co přidává parametr, ji neospravedlňuje.

**Vezla by se pole, která tu nic neznamenají.** `Function`, tedy agregace nad operandem, a `Table` nemají ve stránkování co dělat a každý čtenář modelu by se na ně musel ptát.

### 3 — Druhá dvojice polí vedle dvou čísel

`PaginationInstruction(long? Offset, long? Limit, QueryParameter? OffsetParameter, QueryParameter? LimitParameter)`. Žádný nový typ, čísla zůstanou čísly a parametr se přidá vedle nich.

Zamítáme. Čtyři pole se dvěma nepsanými invarianty — číslo a parametr se v téže přihrádce vylučují — jsou dvě místa pravdy o jednom faktu, tedy tvar, který rozhodnutí 014 odstraňovalo u jazykových typů a který 083 zamítlo u deklarace parametrů na úrovni dotazu. Každý builder by se v osmém kroku musel ptát dvakrát místo jednou a na tu druhou otázku lze zapomenout tiše: opomenutý `LimitParameter` vydá dotaz bez výřezu, tedy přesně ten tichý falešný pozitiv, kvůli kterému 060 stránkování do modelu zavádělo.

### 4 — Vlastní dvoutvarý počet řádků v obou přihrádkách instrukce

## Rozhodnutí

**Volíme variantu 4. Každá ze dvou přihrádek stránkovací instrukce nese `RowCount` — buď nezáporné číslo, nebo parametr —, a skalár stránkovacího parametru určuje klauzule, ne porovnání: je to `Int`.**

### Co instrukce nese

Do `Model` přibývá jediný typ, `RowCount`, uzavřený a s továrnami po vzoru `QueryOperand`: `RowCount.Literal(long)` a `RowCount.Bound(QueryParameter)`. Nese nanejvýš jedno z obojího a nikdy nic; `Literal` odmítne zápornou hodnotu výjimkou, protože nezápornost je invariant tvaru — táž úvaha, jakou 074 nechalo prázdný výčet odmítnout továrnu, a ne kontrolu. `PaginationInstruction(RowCount? Offset, RowCount? Limit)` zůstává ve všem ostatním beze změny: normální tvar je dál offset-pak-limit, každá přihrádka smí chybět, na (pod)dotaz připadá nejvýš jedna instrukce a `Accept` vrací prázdný řetězec, protože **`IQueryVisitor` se nemění** — což je plocha, kterou S1 slibuje držet pro sedmý framework stabilní a kterou 083 ze stejného důvodu nechalo být.

`AbstractQueryBuilder.Paginate` přijímá `RowCount?` místo `long?` a `QueryClauses.Offset`/`Limit` jsou téhož typu. Je to jednorázový přepis všech volajících podle rozhodnutí 003, bez přechodného přetížení: dvě cesty k jedné věci by znamenaly, že parser smí stránkování zapsat dvěma způsoby, a jeden z nich by parametr tiše neunesl.

Proč vlastní typ, a ne operand, je vyargumentované ve variantě 2; proč typ, a ne dvě pole, ve variantě 3. Kladně řečeno: **počet řádků má právě dva tvary a `RowCount` má právě dva tvary.** Builder se v osmém kroku ptá jedinou otázkou (`IsParameter`) a odpovědi jsou dvě; nic dalšího do té přihrádky napsat nejde.

### Odkud se bere skalár

**Skalár stránkovacího parametru je `Int` a určuje ho pozice, ne porovnání.** Není to odvození ve smyslu rozhodnutí 083, nýbrž znalost: klauzule sama říká, co ta hodnota je — počet řádků. Tabulka odvození z 083 tím dostává řádek, který se na druhou stranu porovnání neptá, protože žádná druhá strana není.

Šířka `Int`, a ne `Long`, ze tří důvodů. **Cílová API jsou 32bitová:** `Skip(int)`, `Take(int)`, `SetFirstResult(int)`, `SetMaxResults(int)`, `setFirstResult(int)` a `setMaxResults(int)` — čtyři z šesti cílů širší hodnotu nezapíšou a `long` v signatuře generované metody by se u nich ani nepřeložil. **T-SQL `int` unese všude, kde unese `bigint`,** takže u Dapperu a MyBatisu se nic neztrácí, co by šlo zachovat jinak než za cenu dvou různých signatur téhož modelu. A **nástroj tuhle mez už jednou zvolil:** literál nad `Int32` dnes ve čtyřech cílech odmítá v místě emise, takže `Int` není nová hranice, nýbrž táž hranice vyslovená o tvar dřív. Jeden skalár znamená jednu signaturu ve všech šestatřiceti směrech (S2): tentýž model dá `int take` v C# i `int take` v Javě.

Do brány vstupuje `Int` jako **odvozený** skalár, takže všechna ostatní pravidla rozhodnutí 083 platí beze změny a žádné nové se kvůli stránkování psát nemusí. Týž parametr použitý jako offset i jako limit — `OFFSET @n ROWS FETCH NEXT @n ROWS ONLY` — je jeden parametr metody, protože shodné jméno znamená jednu hodnotu. Týž parametr použitý jednou ve stránkování a jednou v podmínce se sjednotí, je-li i tam `Int`, a rozejde-li se, je to dosavadní `Failure`, který oba skaláry jmenuje — pravidlo, které nikdo pro tenhle případ nepsal a které ho přesto pokrývá správně.

Za vyslovení stojí, co z volby plyne pro kontrolu meze: **u vázaného počtu řádků kontrola `Int32` odpadá**, protože `Int` přetéct nemůže. Mez zůstává tam, kam patří — u čísla, které nástroj vidí. Jakou hodnotu volající skutečně předá, je vlastnost volání, ne dotazu (rozhodnutí 083); že `FETCH NEXT` v T-SQL žádá alespoň jedničku a `OFFSET` nezáporné číslo, je běhová povinnost volajícího úplně stejně jako v ručně psaném stránkování.

### Co s vysloveným skalárem

Vyslovit skalár umí jediný zdroj ze šesti, MyBatis. Pravidlo přednosti vysloveného před odvozeným z rozhodnutí 083 se tu **neuplatňuje** a je to jediné místo, kde se od 083 odchylujeme: tam se vyslovený skalár rozchází s odhadem z druhé strany porovnání a odhad ustupuje, tady se rozchází s pozicí, a pozice odhad není.

- **Celočíselný vyslovený skalár** (`Byte`, `Short`, `Int`, `Long`) se přijme, parametr vyjde `int` a rozdíl proti `Int` je `Loss` kategorie `QueryParameter`, který jmenuje, co zdroj řekl. Je to fakt, který zdroj nesl a generovaný artefakt nepoužil — přesně definice toho druhu záznamu (rozhodnutí 010) — a ne `Conflict`, protože se tu nepotkávají dva zdroje jednoho faktu, nýbrž tvrzení zdroje a mez cílů.
- **Neceločíselný vyslovený skalár** je `Failure` kategorie `QueryParameter`, který jmenuje klauzuli i skalár. Zdroj tvrdí, že počet řádků není číslo; to není rozpor, který by se dal rozhodnout ve prospěch jedné strany, a vydat artefakt, který váže něco jiného, než co zdroj deklaroval, by bylo tiché přeznačení.

### Kolekční parametr počtem řádků není

Kolekční parametr smí podle rozhodnutí 083 stát jedině jako pravý operand operátoru `In`; jako počet řádků je `Failure` kategorie `QueryParameter`, který parametr jmenuje. Je to dosažitelný vstup, ne teorie: `<foreach>` MyBatisu se podle rozhodnutí 084 propíše do textu jako jednoprvkový výčet hodnot a může se ocitnout v klauzuli `FETCH NEXT`. Pravidlo bydlí v bráně, ne v továrně `RowCount.Bound`, a to záměrně — továrna by vyhodila výjimku tam, kde má vzniknout záznam (rozhodnutí 010).

### Co se nemění

Všechna odmítnutí rozhodnutí 060 platí dál a parametr na nich nic nemění, protože nejsou o hodnotě, nýbrž o tvaru výřezu: opakované `Skip`/`Take`, `Skip` až za `Take`, krok řetězu LINQ, který s výřezem nekomutuje, `TOP … PERCENT`, `WITH TIES`, `TOP` vedle `OFFSET` v témž dotazu, stránkování nad výsledkem množinové operace, `OFFSET` uvnitř operandu množinové operace a stránkování uvnitř poddotazu u HQL a JPQL. Nemění se ani to, co počtem řádků není: výraz (`@a + @b`), volání funkce a `${}` MyBatisu, které je textovou substitucí, a ne hodnotou (rozhodnutí 082 a 083).

Nemění se ani druhá mez rozhodnutí 083: **parametr uvnitř výčtu hodnot** zůstává odmítnutý z důvodů, které 083 vyslovilo, a tohle rozhodnutí o něm netvrdí nic nového.

### Pořadí v signatuře

Stránkovací parametry stojí v signatuře generované metody **za parametry podmínek svého rozsahu**, offset před limitem. Není to kosmetika. `TOP` čte T-SQL uvnitř klauzule `SELECT`, tedy na začátku, kdežto `Take` čte LINQ až na konci řetězu: pořadí, ve kterém stránkovací instrukce v seznamu leží, se mezi dvěma zdroji téhož dotazu liší a pořadí signatury by se s ním rozešlo. Brána proto sbírá stránkovací parametry každého rozsahu druhým průchodem, po podmínkách téhož rozsahu. Dnes všechny tři parsery, které stránkování čtou — `SqlQueryReader`, `LinqQueryParser` a dialektový háček `HibernateJpqlQueryParser`u —, volají `Paginate` až při uzavření rozsahu, takže se viditelně nemění nic — pravidlo je tu proto, aby to nezáviselo na tom, jak se rozhodne parser sedmého frameworku (S2).

### Čtení: co je v kterém zdroji stránkovací parametr

| Zdroj | Tvar | Čte se jako |
|---|---|---|
| T-SQL (Dapper) | `TOP (@n)`, `OFFSET @skip ROWS`, `FETCH NEXT @take ROWS ONLY` | pojmenovaný parametr; `VariableReference` v místě, kde dnes `ReportUnreadableRowCount` hlásí `Failure` |
| SQL MyBatisu | totéž po záměně `#{}` za `@` (rozhodnutí 084) | totéž, včetně vyslovených faktů ze `SqlParameterFacts` |
| LINQ (EF Core, NHibernate) | holý identifikátor jako argument `Skip`/`Take` | pojmenovaný parametr téhož jména — týž tvar, jaký 083 čte v pozici operandu |
| HQL (Hibernate) | `limit :n`, `offset :n`, poziční tvar | pojmenovaný, resp. poziční parametr; dialektový háček rozhodnutí 076 |
| HQL (NHibernate), JPQL | jazyk klauzuli nemá | není co číst; stránkování zdroje žije na `IQuery`/`Query`, tedy mimo čtený artefakt |

Hibernátovský háček dnes čte `limit`/`offset` metodou `ConsumeInteger()`, takže `limit :n` je dnes chyba gramatiky. *Že gramatika HQL 7.4.5 v klauzulích `limit` a `offset` parametr připouští, je tvrzení z dokumentace Hibernate a je třeba ho při implementaci ověřit proti pinnuté verzi* — po vzoru téže poznámky, jakou si 083 nechalo u souběhu pojmenovaného a pozičního tvaru v JPQL. Neplatí-li, háček se nemění a HQL prostě parametrizované stránkování nezapisuje; na modelu ani na ostatních pěti zdrojích to nemění nic.

Poziční tvar se nikde neřeší zvlášť: pravidlo 083 o zákazu míchání tvarů i o převodu pozičního na pojmenovaný se záznamem `Convention` platí beze změny, protože brána stránkovací parametry sbírá do téhož seznamu jako všechny ostatní.

### Zápis: co vypíše který cíl

| Cíl | V textu dotazu | V metodě |
|---|---|---|
| Dapper | `TOP (@take)`, `OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY` | `int take` a `new { take }` |
| MyBatis | `FETCH NEXT #{take} ROWS ONLY` | `@Param("take") int take` |
| EF Core | — (`.Skip(skip).Take(take)` nad zachycenými parametry) | `int skip, int take` |
| NHibernate | — | `int take` a `.SetMaxResults(take)` |
| Hibernate, EclipseLink | — | `int take` a `.setMaxResults(take)` |

Dvě věci na té tabulce stojí za vyslovení.

**MyBatis wrapper se kvůli tomuhle rozhodnutí nemění ani o řádek.** Sdílený T-SQL builder napíše `@take`, závěrečný průchod zástupných symbolů v `MyBatisSqlQueryBuilder` ho promění v `#{take}` a deklarace metody mapperu se skládá z téhož seznamu `Parameters` jako dosud. Je to doklad, že vrstvení rozhodnutí 082 a 084 sedí: fakt jazyka se dopsal v jazykovém projektu a wrapper frameworku o něm nemusel vědět. `TOP` přitom zůstává v závorce, protože T-SQL s proměnnou jinou podobu nepřipouští — a builder ji tam píše už dnes.

**U tří cílů z šesti vázaný počet řádků v textu dotazu není a záznam se nevydává.** `SetMaxResults(take)` a `setMaxResults(take)` berou hodnotu přímo, takže v holém HQL a JPQL po stránkování nezůstane stopa — přesně jako u literálu. Že holý textový artefakt stránkování neobsahuje, je popis formátu, ne nález o vstupu; je to táž věta, jakou 060 vyslovilo pro literál a 028 pro chybějící `assembly`, a vydávat ji znovu jen proto, že hodnota je teď parametrem, by znamenalo hlásit u každého stránkovaného dotazu totéž. Parametrizované stránkování je u těchhle tří cílů dokonce **vyjádřitelné přesněji než u ostatních**: nejde o parametr dotazu, který by se vázal, nýbrž o argument volání, takže se neváže nic a nemá se kde rozejít ani jméno.

## Důsledky

**Kategorie *stránkování* matice T2 začíná měřit tvar, který se doopravdy píše.** Dosud se v ní překlad měřil na literálovém stránkování, tedy na jediné podobě, kterou žádný zdrojový projekt nemá; nově se měří na parametrizované. Pro T3 je to totéž zlepšení, jaké u ostatních kategorií přineslo rozhodnutí 083 — podíl spustitelných a funkčně ekvivalentních výstupů se přestává počítat na dotazech, které by v cílovém projektu nikdo nepoužil.

**Model tím dopočítal parametr do konce.** Po tomhle rozhodnutí nezbývá v mezireprezentaci pozice, kde stojí hodnota a parametr stát nesmí, s jedinou vyslovenou výjimkou uvnitř výčtu hodnot. `PaginationInstruction` byla poslední: ostatní instrukce hodnotu nenesou — `DistinctInstruction` je značka bez hodnot, `OrderByInstruction` jmenuje sloupec, `FromInstruction` tabulku a projekce sloupec s aliasem —, takže otázka se sem nemá kam vrátit.

**Věta rozhodnutí 060 o dvou číslech se mění a 060 samo platí dál.** Mění se jediné: co přihrádka nese. Normální tvar offset-pak-limit, nejvýš jedna instrukce na rozsah, osmý krok šablony, umístění závěrečným krokem, `Failure` místo `Loss` i všechna odmítnutí zůstávají slovo od slova. Nový soubor vzniká proto, že 060 je naimplementované a revize na místě by čtenáři vzala starší znění — týž vztah, jaký má 083 k 070 a 074 k 061.

**Pro sedmý framework se nemění skoro nic** (S1). `Model` dostává jeden malý typ, `AbstractWrappers` jedno pravidlo v bráně a jeden druhý průchod při sběru — obojí teď a jednou. `IQueryVisitor` se nemění, deskriptory se nemění (všech pět dnešních cílů uvádí `Pagination` i `QueryParameter` jako vyjádřitelné, takže mechanické hlášení podle pravidla Q14 se nespustí ani teď), orchestrace se nemění. Osmý krok nového builderu se ptá o jednu otázku víc než dosud a odpověď na ni je jeden řádek.

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR**: přibývá schopnost — vstupy, které dosud skončily `Failure`, nově vydají artefakt —, tvar odpovědi ani REST kontrakt se nemění. Vyjde s vydáním, které podle [`open-items.md`](../open-items.md) cíl 2 vydá před `2.0.0`.

**Testy.** Ze všech zdrojů, které stránkování čtou, do všech cílů dotaz s parametrizovaným limitem a dotaz s parametrizovaným offsetem i limitem, s asercí na obojí: zástupný symbol v holém textu tam, kde ho cíl píše, a `int` v signatuře metody i v jejím vázání (`new { }`, zachycení lambdou, `SetMaxResults`, `setMaxResults`, `@Param`). Samotný parametrizovaný limit do Dapperu jako `TOP (@take)` — se závorkou. Týž parametr jako offset i jako limit jako jeden parametr metody. Parametr, který stojí ve stránkování i v podmínce nad sloupcem typu `Int`, jako jeden parametr; nad sloupcem jiného skaláru jako `Failure`, který oba skaláry jmenuje. Vyslovený `Long` z MyBatisu jako `Loss` s `int` v signatuře, vyslovený `String` jako `Failure`. Kolekční parametr ve `FETCH NEXT` jako `Failure`. Poziční stránkovací parametr do JPQL jako poziční a do ostatních cílů pojmenovaný se záznamem `Convention`. Pořadí signatury: dotaz s parametrem v podmínce i ve stránkování dá tutéž signaturu, ať přišel z T-SQL, nebo z LINQ. A beze změny procházejí všechna dosavadní odmítnutí rozhodnutí 060 i literálové stránkování ve všech dnešních směrech, což je definice toho, že se přidal tvar a nic se nepřepsalo. Třetí stupeň ověření: vygenerovaný řetěz se `Skip`/`Take` nad zachyceným parametrem přeloží provider EF Core, vygenerované SQL s `OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY` projde parserem a rozresolvováním a metoda NHibernate se `SetMaxResults` se přeloží; v javové sadě mapper MyBatisu s `#{take}` a JPA metoda se `setMaxResults` vrátí proti databázi očekávaný výřez.
