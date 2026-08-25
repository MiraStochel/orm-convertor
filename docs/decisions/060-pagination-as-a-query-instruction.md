# 060 — Stránkování jako nesená dotazová instrukce

Datum: 2026-08-25
Stav: platí
Požadavky: F7–F10, F11, T2, T3, S1, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [022](022-native-query-syntax-in-builders.md), [023](023-query-builder-template-method.md), [028](028-assembly-name-is-not-ours-to-invent.md) a [053](053-a-query-that-would-return-other-rows-is-not-emitted.md); JSS §5.3–5.4, tabulka 2, pravidla Q1 a Q14

## Kontext

Dotazová mezireprezentace stránkování nenese. Parsery ho aspoň hlásí — `Take`/`Skip` na straně LINQ od rozhodnutí 022, `TOP` a `OFFSET/FETCH` na straně SQL od 2026-08-25 — ale záznam je `Loss` a dotaz se vydá bez něj. To je přesně tvar, který rozhodnutí [053](053-a-query-that-would-return-other-rows-is-not-emitted.md) zakázalo u podmínek: artefakt vypadá jako přeložený dotaz a vrací jinou množinu řádků, tady nadmnožinu — zdroj chtěl deset řádků a výstup vrátí všechny. Pro T3 je to tichý falešný pozitiv a kategorie *stránkování* dotazové matice T2 nemá co měřit.

Všechno okolo té mezery přitom už předpokládá, že se fakt jednou ponese. Deskriptory všech tří cílů deklarují `QueryFeature.Pagination` jako *umím vyjádřit* — u NHibernate s výslovnou poznámkou, že to není součást HQL textu, nýbrž `SetMaxResults` na obklopujícím `IQuery`. Rozhodnutí [023](023-query-builder-template-method.md) obhájilo šablonu s přihrádkami právě tím, že stránkování později nerozbije: je to „jen další přihrádka, kterou závěrečný krok umístí jinam". A článek stránkování zná — tabulka 2 mu dává vlastní řádek mezi dotazovými aspekty všech osmi frameworků —, jen ho nemá v normalizované sadě instrukcí pravidla Q1. To není zákaz: §5.4 o mezireprezentaci výslovně říká, že „the representation is designed to be extendable", a vyjmenovává, co rozšíření nese mimo záruky. Chybí tedy jediné: místo v modelu a pravidla, kdy se fakt přečte, kdy vykreslí a kdy odmítne.

Dvě vlastnosti dělají z otázky rozhodnutí, ne psaní kódu. **Stránkování se váže na řazení**: T-SQL `OFFSET/FETCH` bez `ORDER BY` nezapíše, kdežto `TOP`, `Skip`/`Take` i `SetFirstResult` řazení nevyžadují a dotaz bez něj vrací nedeterministický výřez — u zdroje stejně jako u cíle. A **ne každý cíl ho nese v textu dotazu**: v NHibernate 5.7.0 není `limit` ani `offset` součástí HQL a stránkování patří volání API na `IQuery`, tedy do generované metody, ne do holého dotazu.

## Zvažované varianty

### 1 — Nechat stránkování ztrátou se záznamem

Dnešní stav; nejmenší zásah. Zamítáme, protože záznam nedělá výstup pravdivým: dotaz bez `TOP` vrací řádky, které zdroj vyloučil, a to je táž vada, kvůli které rozhodnutí 053 zakázalo tautologie za podmínky. „Výstup je chudší než vstup" je věta o ztrátě; tady výstup není chudší, je jiný. A matice T2 by v téhle kategorii dál měřila nulu.

### 2 — Pověsit offset a limit na instrukci řazení

T-SQL syntakticky váže `OFFSET` na `ORDER BY`, takže se nabízí nést obě čísla jako vlastnost `OrderByInstruction`. Zamítáme ze tří důvodů: stránkování bez řazení je ve všech třech zdrojích legální (`TOP`, samotné `Take`), takže by fakt neměl kde sedět právě v případě, který vazbu zpochybňuje; instrukcí řazení může být víc a vlastnictví faktu by bylo nejednoznačné; a u NHibernate stránkování nepatří do textu dotazu vůbec, takže vazba na klauzuli řazení by modelovala povrchovou syntaxi jednoho cíle, ne fakt — přesně to, čemu se mezireprezentace celou dobu brání.

### 3 — Vlastní instrukce v normálním tvaru offset-pak-limit, osmý krok šablony a odmítnutí toho, co se nést nedá

## Rozhodnutí

**Volíme variantu 3. Slovník instrukcí se rozšiřuje o `PaginationInstruction(offset?, limit?)` — nejvýš jednu na (pod)dotaz —, šablona dotazového builderu dostává osmý krok `BuildPagination` a stránkování, které se nést nedá, končí záznamem `Failure` bez artefaktu.**

**Normálním tvarem je offset-pak-limit.** To je relační tvar `OFFSET/FETCH` i idiom LINQ `.Skip(n).Take(m)`; obě hodnoty jsou nezáporné celočíselné literály a každá smí chybět. Co do tvaru nesedne, se nepřeloží a důvod je v záznamu: opakované `Take`, `Skip` až za `Take` (v LINQ znamená jinou aritmetiku výřezu a mlčky ji přepočítat by bylo tiché přeznačení), hodnota, která není literálem — proměnná, výraz, `TOP (@n)` —, `TOP … PERCENT`, `WITH TIES` a `TOP` vedle `OFFSET` v témž dotazu, které T-SQL sám odmítá. **Krok řetězu LINQ, který s výřezem nekomutuje** — `Where`, `Join`, `GroupBy`, `OrderBy` a příbuzní za `Skip`/`Take` —, je `Failure` také: mezireprezentace nese stránkování jako poslední operaci a přehodit filtr před výřez znamená jiné řádky. Projekce a materializace s výřezem komutují a procházejí.

**Proč `Failure`, a ne `Loss`:** zahozené stránkování vrací jiné řádky, což je věta rozhodnutí 053 — tady vyslovená pro kategorii, která do modelu teprve vstupuje, takže se pravidlo píše dřív, než vůbec vznikl artefakt, o který by šlo přijít. Otevřená položka o mezích pravidla 053 tím rozhodnutá není: u plného vnějšího joinu a zahozeného poddotazu dnes artefakt se srozumitelným záznamem existuje a jeho případné odebrání je jiná úvaha než nezavést vadu do nové kategorie. Totéž pravidlo dopadá i na stránkování nad výsledkem množinové operace — koncové `OFFSET` u T-SQL, `Skip`/`Take` za `Union` v LINQ —, které dosud bylo `Loss`: složený výsledek nemá v reprezentaci kam stránkování posadit a bez něj dotaz vrací jiné řádky.

**Vykreslení je osmý krok šablony a umístění věc závěrečného kroku,** přesně jak slíbilo rozhodnutí 023. Krok běží po projekci — výřez je poslední relační operátor — a píše do vlastní přihrádky; `FinalizeQuery` ji umístí:

- **Dapper**: samotný limit je `TOP (n)`, které závěrečný krok vloží do klauzule SELECT; offset je `OFFSET n ROWS`, s limitem `FETCH NEXT m ROWS ONLY`, za řazením. Chybí-li řazení, doplní se nosič `ORDER BY (SELECT NULL)` se záznamem `Convention`: T-SQL bez `ORDER BY` `OFFSET` nezapíše a `(SELECT NULL)` je řazení podle ničeho — netvrdí žádný fakt, který zdroj neřekl, protože zdroj bez řazení vracel nedeterministický výřez a cíl ho vrací také. Je to přepis, který pravidlo Q14 výslovně připouští, týž tvar jako `BETWEEN` rozepsané na dvě porovnání.
- **EF Core**: `.Skip(n)` a `.Take(m)` na konci řetězu. Hodnota nad `Int32`, kterou T-SQL unese a `Skip`/`Take` ne, je `Failure` v místě emise.
- **NHibernate**: holé HQL stránkování nenese, protože ho jazyk nemá; metoda vrací `session.CreateQuery(…).SetFirstResult(n).SetMaxResults(m)`. Záznam o tom, že holý HQL artefakt stránkování neobsahuje, se nevydává — platil by u každého stránkovaného dotazu, což je popis formátu, ne nález o vstupu, touž úvahou, jakou rozhodnutí [028](028-assembly-name-is-not-ours-to-invent.md) odmítlo hlásit chybějící `assembly`.

**V operandech množinových operací se stránkování nese tam, kam ho gramatika cíle pustí:** LINQ obě hodnoty (`.Take(5).Union(…)`), T-SQL jen `TOP` — `OFFSET` uvnitř operandu zapsat nejde a je `Failure`. NHibernate množinové operace odmítá vcelku podle deskriptoru, takže tam otázka nevzniká.

**`IQueryVisitor` se nemění.** Instrukce nese dvě čísla, žádný strom; jak se vypíšou, je vlastnost cíle — u NHibernate dokonce jiného jazyka, než visitor vykresluje —, takže je čtou kroky builderu přímo z `QueryClauses`. Do klauzulí je třídí `Normalize()`, který zároveň hlídá „nejvýš jedna instrukce na (pod)dotaz" a mechanickou kontrolu vyjádřitelnosti z deskriptoru rozšiřuje o tuhle kategorii.

## Důsledky

**Kategorie *stránkování* matice T2 začíná měřit překlad.** Literálové stránkování se překládá ve všech devíti směrech; co se nést nedá, se počítá jako odmítnutí, ne jako úspěch — pro T3 totéž zlepšení, jaké přineslo rozhodnutí 053: neúspěch se přizná a jiná množina řádků se nevydává za ekvivalent.

**Slovník instrukcí vystupuje nad normalizovanou sadu článku.** Odchylka se vyslovuje, neschovává: text práce ji může opřít o §5.4, který rozšiřitelnost reprezentace deklaruje, a o tabulku 2, která stránkování mezi dotazovými aspekty vede; `PAGINATE(offset?, limit?)` s normálním tvarem a pravidly odmítnutí je pak příspěvek, ne tichá záplata.

**Záznamy, po kterých dosud vyšel artefakt, artefakt ruší.** Vstup se stránkováním, které se nést nedá, dřív dostal dotaz bez něj a `Loss`; nově nedostane nic a `Failure`. Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR: mění se chování, ne veřejná plocha.

**Pro čtvrtý framework se nemění nic** — táž věta, jakou má rozhodnutí 023, a tady se poprvé proplácí: JPA nese stránkování jako `setFirstResult`/`setMaxResults` na `Query`, tedy přesně tvar, který přihrádka a její umístění závěrečným krokem už umí od NHibernate. Osmý krok je změna `AbstractWrappers` teď, jednou — nový wrapper ji nezopakuje (S1).

**Testy.** Překlad `OrderBy + Skip + Take` do `OFFSET/FETCH` a zpět; samotné `Take` jako `TOP`; `Skip` bez řazení s nosičem `ORDER BY (SELECT NULL)` a záznamem `Convention`; HQL metoda se `SetFirstResult`/`SetMaxResults` a holé HQL bez nich; odmítnutí `TOP … PERCENT`, neliterálové hodnoty, `Skip` za `Take` a filtru za výřezem — pokaždé prázdný výstup a `Failure`; stránkování za množinovou operací jako `Failure`; `TOP` v operandu množinové operace nesené, `OFFSET` v operandu odmítnutý. Třetí stupeň ověření: provider EF Core přeloží vygenerovaný řetěz se `Skip`/`Take`, vygenerované SQL s `OFFSET/FETCH` projde parserem a rozresolvováním, metoda NHibernate se `SetFirstResult` se přeloží.
