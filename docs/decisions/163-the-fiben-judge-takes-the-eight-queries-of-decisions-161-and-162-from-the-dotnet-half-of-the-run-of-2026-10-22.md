# 163 — Soudce FIBEN přijímá osm dotazů přeložených od rozhodnutí 161 a 162 z .NET půlky běhu z 2026-10-22

Datum: 2026-10-22
Stav: platí; nahrazuje první podmínku odstavce *Soudce* rozh. 149 ve znění rozh. 157 pro dotazy q99, q124–126, q200–202 a q237
Požadavky: T2, T3, F13
Podklad: rozhodnutí [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md) (odstavec *Soudce*), [157](157-a-target-cell-that-differs-by-a-stated-limit-does-not-block-the-fiben-judge.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md), [161](161-a-window-aggregate-is-a-window-of-the-vocabulary-and-the-whole-row-of-a-definition-beside-columns-is-its-columns.md), [162](162-a-subquery-stands-in-a-join-condition-as-in-any-condition.md); běh FIBEN z 2026-10-22 nad `a2f498a` (záznam *The FIBEN run* v `ORMConvertor/README.md`); texty osmi dotazů v katalogu `SampleData/Fiben/queries.json` a jejich předlohy v Db2

## Kontext

Soudce FIBEN (`database/fiben/judge.tsv`) vznikl z běhu obou sad z 2026-10-20 a drží řádky 229 dotazů. Osm dalších — q99 a q237 (agregát nad oknem uvnitř odvozené tabulky) a q124–126, q200–202 (poddotaz v podmínce `ON`) — tehdy katalog odmítal; od rozhodnutí 161 a 162 se překládají. Běh na vyžádání u nich hlásí, že soudce záznam nemá, a test zdroje proti soudci tam **selže**: osm selhání v každém běhu, které nic neříkají o verzi.

Běh z 2026-10-22 nad `a2f498a` doběhl v .NET sadě celý (Dapper jako zdroj, EF Core a NHibernate jako cíle, měřeno politikou rozh. 145): všech 237 dotazů došlo do všech tří frameworků a ve všech se cíl shodl se zdrojem, osm nových včetně. Řádky 229 dotazů, které soudce už drží, jsou v jeho `judge-candidate.tsv` **beze změny** (porovnáno řádek po řádku); nových je 342 řádků jen u osmi dotazů. Javová půlka běhu byla po sedmi minutách zastavena a záznam ji nenese.

Rozhodnutí 149 dává soudci vzniknout z běhu, který splní tři podmínky; první v znění rozhodnutí 157 zní, že zdroj a všech pět cílů se nad daty shodnou, kromě buněk vyslovené meze. Pro osm dotazů ji tento běh splní jen ze tří frameworků šesti, a u dvou z nich slaběji: q99 a q237 píší EF Core i NHibernate nativním SQL (113), takže jejich shoda se zdrojem je týž text přes dvě API, ne nezávislé čtení. U q124–126 a q200–202 přeložily oba cíle dotaz do svého jazyka (LINQ, HQL 5.7) a shodly se.

Druhá podmínka — texty přečtené proti otázkám — se pro osm dotazů přečetla teď. Pět textů odpovídá své otázce. Tři se od ní odchylují **v předloze benchmarku samé**, kterou katalog nese převedenou beze změny: q99 se ptá na rok 2019 a text filtruje rok 2016, q124 se ptá na rok 2019 a text bere roky 2016–2019, a poddotaz q125 vybírá jméno vnějšího řádku (`oPerson`) místo vnitřního, takže `NOT IN` nepropustí nikoho a dotaz vrací nula řádků. Třetí podmínka je vzorek čtyř Kimových typů přepočítaný ručně; pro osm dotazů se nepřepočítával.

Rozhodnout je třeba, **odkud** se osm dotazů do soudce dostane.

## Zvažované varianty

### 1 — Počkat na příští doložený běh obou sad

Podmínky 149 by se splnily doslova, šest frameworků by se u osmi dotazů shodlo. Zamítáme: do té doby nese každý běh osm selhání, která neříkají nic o verzi, a záznam je musí pokaždé vysvětlovat. A javová půlka do obsahu soudce nic nepřidá — soudce drží řádky **zdroje**, textu katalogu přes Dapper, které vydává .NET sada; javové cíle se se zdrojem téhož běhu porovnávají v každém běhu znovu, soudce nebo ne.

### 2 — Vzít řádky z kontroly z hosta

Kontrola z 2026-10-22 osm dotazů prošla ve třech cílech .NET. Zamítáme: kontrola z hosta není záznam verze (README, *The FIBEN run*) — běží mimo prostředí rozhodnutí 145 a nemá commit, který by soudce jmenoval.

### 3 — Vzít řádky z .NET půlky doloženého běhu z 2026-10-22

## Rozhodnutí

**Volíme variantu 3. Soudce FIBEN je od 2026-10-22 `judge-candidate.tsv` .NET půlky běhu nad `a2f498a`: 229 dotazů beze změny proti soudci z 2026-10-20 a osm nových, q99, q124–126, q200–202 a q237. Pro těch osm nahrazuje toto rozhodnutí první podmínku 149 ve znění 157 shodou zdroje se dvěma cíli .NET; hlavička soudce to jmenuje.**

Odůvodnění stojí na tom, **co soudce soudí**. Je regresní (149): tvrdí, že verze vrací, co vrátil text katalogu v jmenovaném běhu, ne že text odpovídá na otázku. Podmínky vzniku mají zajistit, že zmrazené řádky jsou řádky dotazu, a ne chyba jednoho čtení. Zdroj je text v T-SQL spuštěný Dapperem bez překladu; chybou jednoho čtení by tu byl jen chybný převod textu z Db2 nebo chyba zápisu výsledku, a proti oběma stojí to, že šest z osmi dotazů přečetly a přeložily dva nezávislé jazyky se shodným výsledkem. U q99 a q237 tuto oporu nemáme; jejich nezávislé čtení je HQL Hibernatu v javové sadě, které je od rozhodnutí 161 píše, a příští běh obou sad ho porovná se zdrojem jako každou jinou buňku. Kdyby se Hibernate neshodl, ukáže to běh jako neshodu cíle se zdrojem, ne jako chybu soudce — a co s ní, je nové rozhodnutí.

Tři texty, které se od své otázky odchylují, soudce drží tak, jak je vrací: jejich odchylka je v předloze benchmarku, katalog ji nese převedenou věrně, a soudce nemá říkat, co měla předloha napsat. Nula řádků q125 je výsledek jejího textu.

## Důsledky

- **`database/fiben/judge.tsv`** je kandidát běhu z 2026-10-22 se čtyřmi řádky jeho hlavičky a pátým, který jmenuje běh, toto rozhodnutí a osm přidaných dotazů; jen LF. Loader ho nahraje při prvním startu přestavěného obrazu `mssql_db` (kontrolní součet se změnil); do té doby drží databáze soudce z 2026-10-20 a osm dotazů selhává dál.
- **Záznam běhu** z 2026-10-22 v `ORMConvertor/README.md` nese .NET půlku a jmenuje osm selhání zdroje proti soudci jako dotazy bez záznamu; javová půlka se nezapisuje.
- **Otevřená položka** soudce pro osm dotazů se uzavírá.
- **Stav 157** nese, že pro osm dotazů jeho podmínku nahrazuje toto rozhodnutí (150); 149 se nemění, jeho první podmínku nese už 157.
- **Kód** se nemění.
