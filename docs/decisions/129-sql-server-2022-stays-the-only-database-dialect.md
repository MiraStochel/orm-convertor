# 129 — Jediným databázovým dialektem zůstává SQL Server 2022; druhý se nepřidává

Datum: 2026-10-11
Stav: platí
Požadavky: F4, F5, F7–F10, F11, F13, S2, S5, S6, S7, T2, T3
Podklad: rozhodnutí [013](013-target-framework-versions.md), [026](026-home-of-shared-query-reading.md), [082](082-t-sql-read-and-written-by-a-shared-project.md), [086](086-target-database-dialect-declared-by-the-descriptor.md), [088](088-a-declared-foreign-source-dialect-is-not-read.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [091](091-the-catalog-completes-a-foreign-source-and-says-so.md), [107](107-an-expression-is-the-sixth-operand-shape-and-stands-wherever-an-operand-stands.md), [110](110-ldbc-snb-as-a-second-reference-domain.md) a [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md); schválený záměr ([`zamer.tex`](../zamer.tex), kap. *Technická realizace*); [`architecture.md`](../architecture.md) §9, vyňatá oblast 5; otevřená položka „Druhý databázový dialekt"; JSS §3.2 (rozsah a co cílem není), §11.2 (překlad a přepis dotazů) a §12 (budoucí práce)

## Kontext

Rozhodnutí 086 deklarovalo cílový databázový dialekt v deskriptoru a 088 dalo zdroji možnost vyslovit dialekt svého doslovného SQL: deklarace jiného systému čtení toho SQL zastaví. Obě rozhodnutí přitom vědomě nechala otevřenou jednu otázku, **zda má nástroj dialektů znát víc**, a otevřená položka „Druhý databázový dialekt" z ní udělala tři otázky, které by musel zodpovědět ten, kdo do slovníku `DatabaseDialect` přidá druhou hodnotu:

1. **Čím se dialekt volí.** Deskriptor dnes nese jednu hodnotu a rozhraní žádnou volbu nenabízí.
2. **Zda platí zamítnutí multidialektové knihovny z rozhodnutí 082.** To zamítnutí stálo na tom, že knihovna by jiný dialekt přeložila tiše. Deklarace z 086 a 088 tichost odstranila, takže tenhle důvod už neplatí. Cena knihovny ale trvá: gramatika navíc a tabulka jmen navíc.
3. **Co dělat, když se dialekt zdroje a cíle rozejdou.** Dnes ta situace nastat nemůže, protože čitelný i zapisovatelný dialekt je jeden. Vznikla by teprve se dvěma čitelnými dialekty, kdy by se zábrana z 088 proměnila v přepínač.

Položka stojí v sekci „Užitečné, ne nutné": žádná věta záruk na ní nestojí a nic jiného neblokuje. Její trvání ale není neutrální. Hranice záruk ve vyňaté oblasti 5 dnes končí větou „Druhý dialekt je otevřená položka", takže jediný dialekt čte jako **dočasný stav**, ne jako vymezení. Tohle rozhodnutí na otázku odpovídá. Ty tři otázky jsou její důsledky a s ní se zavírají.

Za vyslovení stojí, **co jediný dialekt doopravdy omezuje**, protože je toho méně, než by se z názvu zdálo. Zábrana z 088 se týká jen **doslovného SQL**: dotazů Dapperu a MyBatisu, nativního SQL v kódu a doslovného typu sloupce (`[Column(TypeName=…)]`, `columnDefinition`, `sql-type`). Entity, mapování a dotazy v jazycích frameworků (LINQ, HQL, JPQL) na dialektu nezávisí a nástroj je přeloží i z projektu psaného pro jiný databázový systém. Na SQL Server se váže výstup: doslovné typy, kterými 086 doplňuje, co slovník cíle neunese, a úniková cesta z 113, která píše celý dotaz nativním SQL. Katalog čte SQL Server z principu (029, 091).

## Zvažované varianty

### 1 — Druhý cílový dialekt s volbou v rozhraní

Přidat do `DatabaseDialect` druhý systém, třeba PostgreSQL. Deskriptory by vyjmenovaly, které dialekty jejich framework unese (NHibernate má konečný seznam dialektů, EclipseLink konečný seznam platforem), a obrazovka převodu by dostala volbu cílového dialektu vedle volby frameworku.

Zamítáme. Co by to stálo, vypočítávají rozhodnutí, která na jediném dialektu stojí, každé za sebe: druhou tabulku jmen typů pro doslovné typy (086), druhý zapisovač únikové cesty (113), nové ověření každé funkce výrazu nad druhým systémem (107 to říká výslovně o `Length`, protože `LEN` v T-SQL nepočítá koncové mezery a jinde se chová jinak), druhou čtečku katalogu (`SqlServerCatalogReader` je jediná), druhý databázový obraz s WideWorldImporters i LDBC SNB v kontejnerové konfiguraci (S5) a ovládací prvek navíc v základním scénáři, který S7 drží na pěti krocích. Každé z těch míst by navíc muselo projít všemi čtyřmi stupni ověření nad **běžícím** druhým systémem, jinak by tvrzení o něm bylo neověřené. Přesně takové tvrzení §9 nepřipouští. Výsledkem by byl druhý rozměr matice překladů, ne lepší odpověď na otázku, kterou práce klade.

### 2 — Multidialektová knihovna na zdrojové straně

Ponechat jediný cílový dialekt, ale číst doslovné SQL psané pro jiné systémy knihovnou typu `SqlParserCS` nebo gramatikou `grammars-v4` a deklaraci ze 088 použít jako přepínač čtení. Projekt nad PostgreSQL by tak mohl přejít na libovolný framework nad SQL Serverem.

Zamítáme. Tahle varianta je **překlad mezi SQL dialekty**: `LIMIT` na `OFFSET/FETCH`, `timestamp` jako okamžik na `datetime2` (v T-SQL je `timestamp` osm bajtů binární verze řádku), `||` na `+`, a s tím každá funkce, jejíž sémantika se mezi systémy liší. To je jiný problém než ten, který práce řeší. JSS (§11.2) staví nástroj výslovně **vedle** přepisu SQL napříč databázovými systémy: ten pracuje s řetězci SQL a relační algebrou, kdežto vstupem nástroje jsou dotazy na úrovni ORM, ne text SQL. Převzít ho jako vedlejší funkci by znamenalo obhajovat sémantickou ekvivalenci dvou dialektů, kterou ověřit neumíme, protože druhý systém bychom stejně neměli (varianta 1). Rozhodnutí 082 tuhle knihovnu zamítlo a my zamítnutí potvrzujeme. Jeho původní důvod, tichý překlad, odstranila deklarace. Platí ale druhý, který 082 uvedlo vedle něj: pro jediný dialekt v řešení dává komunitní gramatika slabší záruku než gramatika od výrobce (`TSql160Parser`). K tomu přidáváme důvod třetí: bez druhého systému k ověření je užitek nulový.

### 3 — Jediný dialekt, ale bez deklarace

Když druhý dialekt nepřijde, je `DatabaseDialect` s jedinou hodnotou výčtem, který nic nevybírá, a `SourceSqlDialect` zábranou proti něčemu, co nástroj stejně nečte. Obojí by šlo odstranit a vrátit se k mlčky předpokládanému SQL Serveru.

Zamítáme. Deklarace nebyla nikdy přípravou na druhý dialekt. Je to **vyslovení meze**: 086 ji zavedlo proto, aby si čtenář nemusel předpoklad dohledávat v kódu a aby ho vydával záznam každého běhu (S6). A 088 zábranu zavedlo proto, že projekt psaný pro jiný systém by se jinak přečetl jako T-SQL a vypsal jako T-SQL. Artefakt by vypadal správně a tvrdil by něco, co verze nenárokuje. Jediný dialekt je poctivý jen tehdy, když ho nástroj říká a když vstup, který ho porušuje, odmítne nahlas. Bez deklarace by se omezení stalo tichým, tedy tím, čemu se F11 brání.

### 4 — Nechat položku otevřenou

Nic nerozhodovat. Položka nic neblokuje a druhý dialekt může přijít, až bude čas.

Zamítáme. Otevřená položka tvrdí, že mez je dočasná, a promítá se do toho, jak hranici záruk čte zadavatel i oponent: oblast 5 by dál končila slibem místo důvodu. Projekt má pevný konec a v milníku 4 chybějí věci, které záměr jmenuje (zobrazení mezireprezentace, pipeline T2 × T3, příprava experimentů T4–T6). Druhý dialekt mezi nimi není a do konce projektu na něj nedojde. Položka, o které víme, že se nesplní, je v seznamu jen šum.

### 5 — Jediný dialekt jako vymezení, deklarace a zábrana zůstávají

SQL Server 2022 je jediným databázovým systémem, pro který nástroj píše a jehož SQL čte. Vymezení je trvalé, ne dočasné. Deklarace z 086, zábrana z 088 a záznam o rozporu z 091 zůstávají beze změny.

## Rozhodnutí

**Varianta 5. Rozšíření o druhý databázový dialekt zamítáme. SQL Server 2022 je jediným dialektem nástroje jako vymezení předmětu práce, ne jako odložená práce.**

1. **Zadání druhý dialekt nežádá a záměr ho vylučuje.** Schválený záměr jmenuje referenční databázi jedinou větou: „Referenční databází bude SQL Server se vzorovou databází WideWorldImporters a prostředí bude kontejnerizováno pomocí Dockeru." Žádný z požadavků F1–F15, S1–S7 a T1–T7 nejmenuje druhý databázový systém. F4 mluví o „připojené databázi" a F12 o sadě, která spouští Java frameworky „nad stejnou databází a daty". Druhou referenční doménu (LDBC SNB, rozhodnutí 110) jsme přidali nad týmž strojem, ne nad jiným. Rozmanitost vstupů, kterou práce potřebuje, je v doménách a dotazech, ne v databázových systémech.

2. **Předmětem práce je překlad mezi ORM frameworky, ne mezi SQL dialekty.** Mezireprezentace popisuje entity, mapování a dotazy tak, jak je vidí framework. Relační schéma pod nimi je pro ni pevný kontext, ne proměnná. JSS svůj rozsah zužuje vědomě, „to make the problem tractable and to provide clear and enforceable guarantees“ (§3.2), staví nástroj vedle přepisu SQL napříč databázovými systémy, ne do něj (§11.2), a jeho budoucí práce (§12) jmenuje další frameworky, bohatší dotazové konstrukce, další ekosystémy a nerelační úložiště (ODM, OGM), ne druhý relační dialekt. Druhý dialekt by k tezi práce nepřidal nic, co by šlo obhájit jako její přínos. Přidal by jen druhou osu, podél které by se musel obhajovat každý dosavadní výsledek.

3. **Jediné prostředí je podmínkou opakovatelnosti a jednoznačnosti měření (S2, S5, F13, T2, T3).** Každé tvrzení v §9 je ověřené na čtyřech stupních, z nichž čtvrtý, diferenční ověření (089), porovnává řádky zdrojového a přeloženého dotazu nad **týmž** systémem a týmiž daty. Druhý dialekt by z každé buňky matice o 36 směrech udělal několik buněk a tvrzení o shodě by záviselo i na tom, který databázový systém ty řádky vydal. Rozdíl ve výsledku by pak mohl znamenat chybu překladu, rozdíl sémantiky dvou systémů, nebo obojí, a matice T2 s metrikami T3 by to musela umět rozlišit. S jediným systémem rozdíl znamená jen chybu překladu. Totéž platí pro navazující srovnání s LLM (T4–T6): pravidlový překladač a model se srovnávají nad stejným vstupem a stejným prostředím, a každý stupeň volnosti navíc rozmazává, čím se liší.

4. **Mez je vyslovená, ne mlčky předpokládaná.** Proto deklarace z 086 a zábrana z 088 zůstávají (proti variantě 3). Uživatel, jehož projekt míří na jiný databázový systém, se to dozví záznamem: dotaz v doslovném SQL se nevydá, doslovný typ sloupce se nepřečte a katalog připojený k takovému zdroji vydá jediný záznam o rozporu (091). Část projektu nezávislou na dialektu, tedy entity, mapování a dotazy v jazycích frameworků, nástroj přeloží tak jako dnes. Vymezení tím omezuje to, co by bez ověření tvrdilo víc, než unese. To, co ověřit lze, neomezuje.

5. **Tři otázky položky dostávají konečnou odpověď.**
   - *Čím se dialekt volí:* nevolí se. Hodnota v deskriptoru je jediná a rozhraní volbu nedostane. Volba verze frameworku, kterou zmiňovalo rozhodnutí 013, je samostatná otázka a tohle rozhodnutí o ní nic neříká.
   - *Zda platí zamítnutí multidialektové knihovny:* platí, s důvody uvedenými u varianty 2.
   - *Co dělat při rozchodu dialektu zdroje a cíle:* zdroj, který deklaruje jiný systém, zastaví zábrana z 088. To je konečná odpověď, ne mezistav před přepínačem.

## Důsledky

**Kód se nemění.** `DatabaseDialect` dál nese jedinou hodnotu `SqlServer2022`, `SourceSqlDialect` dál nese dvě a test `EveryDescriptorDeclaresTheOnlyDialectThisVersionTargets` dál drží, že všech šest deskriptorů deklaruje tutéž hodnotu a že slovník má jediného člena. Číslování po desítkách a hodnota `AnotherSystem = 1000` zůstávají. Říkají, kam by se vešel další systém, ale nic neslibují a stojí nula. Kdo by druhý dialekt přece jen chtěl, musí tohle rozhodnutí nahradit novým. Test mu nedovolí přidat hodnotu mlčky.

**Dokumentace.** Otevřená položka „Druhý databázový dialekt" zmizí z [`open-items.md`](../open-items.md). Vyňatá oblast 5 v §9 [`architecture.md`](../architecture.md) i v sekci *Guarantees* kořenového `README.md` přestane druhý dialekt uvádět jako otevřenou věc a odkáže sem. Řádek dialektu v [`subset.md`](../subset.md) a řádek cíle 2 v [`traceability.md`](../traceability.md) dostanou tentýž odkaz.

**Hranice záruk se nemění, mění se jen její povaha.** Co je vyňaté, zůstává vyňaté. Nově to je vymezení s důvodem, ne slib na později. Pro text práce to znamená, že oblast 5 patří do vymezení rozsahu, ne do budoucí práce.

**Rozhodnutí 082, 086, 088, 107 a 113** zůstávají v platnosti beze změny. Jejich věty o druhém dialektu („otázka se vrátí", „druhý dialekt by řádek musel ověřit znovu", „položka o něm tím dostává další důvod") jsou zmrazený text své doby a odpověď na ně je tady.
