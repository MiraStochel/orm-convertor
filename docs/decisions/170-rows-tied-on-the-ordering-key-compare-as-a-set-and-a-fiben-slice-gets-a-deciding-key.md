# 170 — Seřazený výsledek se porovnává v pořadí klíče řazení a řádky se shodným klíčem jako množina; výřez katalogu FIBEN, který dotaz neurčuje, doplní převod o rozhodující klíč

Datum: 2026-10-23
Stav: platí; nahrazuje porovnání seřazeného dotazu v pořadí v odstavci *Pořadí rozhoduje mezireprezentace* rozh. 089 a porovnání IC 14 jako množiny rozh. 117
Požadavky: F13, T2, T3
Podklad: rozhodnutí [089](089-differential-verification-as-the-fourth-level-over-a-query.md) (oddíl *Co je shodný výsledek*; v *Historii* podmínka úplného pořadí fixtury), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) (odstavec o pořadí a IC 14), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md) (převod textů a odstavec *Soudce*), [157](157-a-target-cell-that-differs-by-a-stated-limit-does-not-block-the-fiben-judge.md), [163](163-the-fiben-judge-takes-the-eight-queries-of-decisions-161-and-162-from-the-dotnet-half-of-the-run-of-2026-10-22.md), [060](060-pagination-as-a-query-instruction.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md); [`subset.md`](../subset.md) 2.7 (výřez a skalární poddotaz v cílech) a 2.9 (řazení seskupeného dotazu v LINQ); převod `ORMConvertor/FibenImport/Queries.cs`, katalog `SampleData/Fiben/queries.json` (texty i předlohy `benchmarkSql`) a schéma `database/fiben/schema.sql` s `constraints.sql`; kontrola běhu FIBEN na MIS2 nad SQL Server 2025 Express 17.0.1000.7 (q31 v EF Core a NHibernatu, i nad `f49d65f`)

## Kontext

Požadavek F13 žádá porovnat normalizované výsledky zdrojové a přeložené varianty dotazu a **pořadí zohlednit jen, je-li dané dotazem**. Všechna tři porovnání čtvrtého stupně — diferenční matice (089), soudce LDBC (117) a běh FIBEN (149) — to dnes čtou takto: nese-li dotaz řazení, porovnávají se řádky v pořadí, v jakém přišly; nenese-li, obě strany se před porovnáním seřadí podle vykresleného řádku. Obě sady to dělají stejně (`DotNetQueryRunner`, `LdbcCanonicalForm` a `FibenRun` a jejich javové protějšky).

Řazení ale určuje jen pořadí řádků s různým klíčem. Řádky se shodným klíčem smí databáze vrátit v libovolném pořadí — norma SQL ho nechává na implementaci — a které z nich přijde první, závisí na plánu, který se mezi frameworky (každý píše jiné SQL) i mezi servery liší. Dotaz q31 katalogu FIBEN (`SELECT TOP (3) COUNT(…) AS AggResult1, … GROUP BY oPerson.HASPERSONNAME ORDER BY AggResult1 DESC`) má na druhém a třetím místě dvě osoby se shodným počtem 53. Na MIS3 se běhy shodly; na SQL Server 2025 Express (MIS2) vrátí EF Core i NHibernate tyto dva řádky v opačném pořadí než zdroj, i nad `f49d65f`, a buňka je neshoda, ačkoli jde o tytéž řádky ve správném pořadí klíče. Porovnání tu tvrdí víc, než dotaz určuje.

Pod tím leží druhá, hlubší věc. **Výřez** — `TOP`, `OFFSET … FETCH`, instrukce stránkování rozhodnutí 060, i uvnitř poddotazu —, který řeže uprostřed skupiny shodného klíče nebo nemá řazení vůbec, neurčuje ani *které* řádky vrátí. U `TOP (3)` q31 by se při shodě třetího a čtvrtého místa lišila sama množina řádků; u výřezu ve skalárním poddotazu celý vnější výsledek. To už není vlastnost porovnání: dotaz má víc přípustných výsledků a porovnání jednoho z nich s jedním zmrazeným spravedlivé být nemůže.

Prošli jsme texty katalogu FIBEN a jeho schéma, bez dat. Schéma nese jen primární a cizí klíče, žádné `UNIQUE`, takže ticker, právní název ani jméno osoby klíčem nejsou. Dotazy s řazením nebo výřezem se dělí takto:

| Skupina | Dotazy | Co dotaz neurčuje |
|---|---|---|
| agregát bez seskupení | q11, q12, q25, q49, q58, q71, q86 (seřazené), q15, q77 | nic: výsledek má jeden řádek, výřez i řazení jsou prázdné |
| seřazený bez výřezu | q24, q36, q38, q97 | jen pořadí uvnitř skupiny shodného klíče |
| `TOP n` s řazením, které výsledek neurčuje | q19, q35, q48 (`TOP (1)` podle data vypořádání, ticker v klíči není), q22, q98 (`TOP (1)` podle agregátu), q31 (`TOP (3)`) | pořadí i množinu řádků |
| `TOP (1000)` bez řazení | q13, q21, q27, q29, q40, q44, q61, q78, q83 | množinu řádků, přesáhne-li výsledek tisíc řádků |
| `TOP (1)` ve skalárním poddotazu | q178, q179 (řadí podle částky, vybírá měsíc), q187–q197, q206 (bez řazení, korelace přes ticker) | celý vnější výsledek |

Meze řádků jsou z předlohy (`FETCH FIRST n ROWS ONLY` v Db2), převod je jen přepsal na `TOP` (149). Poslední dvě skupiny dnešní čtení F13 vůbec nevidí: dotaz je „neseřazený", porovná se jako množina, a ta je v nich neurčená také.

Katalog LDBC a matice jsou na tom jinak. Texty LDBC píšeme podle specifikace, která řazení dotahuje až po identifikátor; jedinou výjimkou je IC 14, kterou specifikace řadí jen podle váhy cesty, a rozhodnutí 117 ji proto porovnává jako množinu — a zahazuje tím i pořadí vah, které specifikace určuje. Matice si úplné pořadí zajišťuje daty: fixtura podle *Historie* rozhodnutí 089 drží `LineNumber` jedinečné napříč objednávkami a `(ProductId, Quantity)` také.

Je třeba rozhodnout, jak porovnávat přesně to, co dotaz určuje, a co s dotazem, který neurčuje, které řádky vrátí — obojí tak, aby k tomu nebylo třeba v datech hledat, kde remízy jsou.

## Zvažované varianty

1. **Soudce drží všechna přípustná pořadí**: ke každému dotazu množinu výsledků, které se liší pořadím řádků se shodným klíčem, a u výřezu na remíze i výběrem řádků. Zamítáme. Počet pořadí roste s faktoriálem velikosti skupiny, sestavit tu množinu znamená najít v datech každou remízu, a je to jen výčet třídy, kterou varianta 3 zastoupí jedním kanonickým členem.

2. **Rozhodující klíč do každého seřazeného dotazu** a porovnání v pořadí jako dnes. Zamítáme. Volné pořadí uvnitř remízy je otázka porovnání, ne dotazu: q24 svůj výsledek určuje celý a volné nechává jen pořadí řádků se shodným maximem. Čtvrtý stupeň nesmí hlásit rozdíl, který dotaz nedefinuje, u žádného dotazu, ne jen u těch, které autor katalogu dotáhl; a dotaz, jehož výsledek je určený, by se kvůli porovnání měnil v textu, jehož překlad se zkouší. U IC 14 by to ani nešlo: očekávané pořadí cest téže váhy dává validační sada LDBC, ne náš text.

3. **Porovnání v pořadí klíče a uvnitř skupiny shodného klíče jako množina.** Pro pořadí tuhle volíme.

4. **Soudce připustí každou přípustnou odpověď výřezu**: zdroj se pro soudce spustí s `TOP (n) WITH TIES` a cíl projde, shoduje-li se se soudcem až po poslední skupinu klíče a je-li jeho poslední skupina podmnožinou soudcovy. Zamítáme. Je to varianta 1 v úsporné podobě, ale jen pro výřez s řazením na vrcholu dotazu, šest dotazů FIBEN. Pro výřez bez řazení a pro výřez v poddotazu úsporná podoba neexistuje — výběr řádku ve skalárním poddotazu mění vnější výsledek celý —, a zdroj by potřeboval druhou formu běhu, kterou matice u zdrojů mimo SQL nemá.

5. **Zjistit z dat, kde výřez řeže v remíze**, a jednat jen tam. Zamítáme. Je to vlastnost dat, ne dotazu: jiná instance dat, jiné pořadí načtení nebo další verze dat ji změní, a co jde rozhodnout z textu, by potřebovalo databázi.

6. **Výřez katalogu FIBEN, který výsledek neurčuje, doplnit jedním pravidlem převodu o rozhodující klíč.** Pro výřez tuhle volíme.

## Rozhodnutí

**Volíme varianty 3 a 6. Pořadí řeší porovnání: seřazený výsledek se porovnává v pořadí klíče řazení a řádky se shodným klíčem jako množina, ve všech třech porovnáních čtvrtého stupně a v obou sadách. Výběr řádků řeší dotaz: každý výřez katalogu FIBEN svůj výsledek určuje, a ten, který ho neurčuje, doplní převod o rozhodující klíč.**

### Kanonická forma seřazeného výsledku

- **Forma.** Řádky zůstávají v pořadí, v jakém přišly; každý souvislý úsek řádků se shodnou hodnotou klíče se seřadí podle vykresleného řádku, tak jako se dnes řadí celý neseřazený výsledek. Z q31 `(60, A) (53, C) (53, B)` je `(60, A) (53, B) (53, C)`, ať přijdou řádky s 53 v kterémkoli pořadí. Neseřazený výsledek se řadí celý jako dnes.
- **Je to doslova F13.** Dotaz určuje posloupnost hodnot klíče a uvnitř jedné hodnoty nic; forma porovná první a druhé ne. Je správná, ať remízy v datech jsou, nebo nejsou, a proto je nikde hledat netřeba. Soudce dál drží jednu posloupnost a jeden digest a porovnání zůstává rovností.
- **Klíč** jsou pole výsledku, na která padají klíče řazení nejvnějšího dotazu: alias projekce, nebo výraz, který se rovná výrazu projekce. Nestojí-li některý klíč řazení v projekci, úseky z výsledku vidět nejsou; klíčem je pak celý řádek a porovnání je dnešní porovnání v pořadí. Shodu klíče posuzuje vykreslená hodnota, tedy v přesnosti, kterou položka nastavuje, jako všechno ostatní v rozhodnutí 089.
- **Kde klíč stojí.** Místo příznaku `ordered` nese klíč položka matice, vazba LDBC a záznam katalogu FIBEN, který ho dostane od převodu tak, jako dnes dostává příznak. Že je to řazení mezireprezentace (089: *pořadí rozhoduje mezireprezentace, ne cíl*), drží test nad IR zdroje: uvedený klíč je řazení jeho nejvnějšího dotazu. Jedinou výjimkou je vazba LDBC, jejíž soudce řadí méně než náš text: IC 14 má klíčem váhu cesty, protože validační sada jinak neřadí, a vazba to říká.
- **Obě sady stejně.** Kanonickou formu počítají obě sady; že se shodnou, drží `renderer-conformance.txt` případem s úsekem shodného klíče, tak jako drží vykreslení.

### Výřez katalogu FIBEN určuje své řádky

Výřez — instrukce stránkování v rozsahu dotazu či poddotazu — určuje, které řádky vrátí, platí-li aspoň jedno:

- (a) jeho rozsah je agregát bez seskupení, a má tedy jeden řádek;
- (b) jeho řazení obsahuje každý klíč seskupení — řádek seskupeného rozsahu je jedna skupina;
- (c) jeho řazení obsahuje každý operand projekce — řádky shodné v klíči jsou pak shodné celé a ve výsledku se neliší.

Podmínka se čte z textu a z mezireprezentace, bez databáze. Je postačující, ne nutná: funkční závislosti nezná, takže `ORDER BY …, p.Id` nad joinem k jedné straně nesplní, ač pořadí úplné je.

**Převod FIBEN dostává šesté pravidlo.** Výřez, který nesplní ani jednu podmínku, dostane na konec řazení chybějící klíče — v seskupeném rozsahu klíče seskupení, jinak výrazy projekce — vzestupně a tak, jak je seskupení či projekce zapisuje; výřez bez řazení dostane řazení celé. Pravidlo zapíše do protokolu převodu každý dotaz, kterého se dotkne, jako ostatní pravidla, a `benchmarkSql` nese předlohu beze změny.

| Dotaz | Dnes | Po pravidle |
|---|---|---|
| q31 | `ORDER BY AggResult1 DESC` | `ORDER BY AggResult1 DESC, oPerson.HASPERSONNAME` |
| q19 | `ORDER BY oSecuritiesTransaction.HASSETTLEMENTDATE DESC` | totéž, `, oListedSecurity.HASTICKERSYMBOL, oListedSecurity.HASLEGALNAME` |
| q178, poddotaz | `ORDER BY oInnerMonetaryAmount.HASAMOUNT` | totéž, `, MONTH(oInnerSecuritiesTransaction.HASSETTLEMENTDATE)` |
| q187, poddotaz | bez řazení | `ORDER BY oInnerMonetaryAmount.HASAMOUNT` |

Pravidlo se dotkne právě 29 dotazů tří posledních skupin tabulky v *Kontextu*; dotazy první skupiny splní (a), dotazy druhé a ostatní dotazy katalogu výřez nemají.

**Proč smíme text měnit.** Doplněné řazení je zjemnění původního: klíče předlohy zůstávají na začátku v témž pořadí, takže výřez zjemněného pořadí je jedním z výřezů, které předloha připouští, a výřez bez řazení předloha připouští jakýkoli. Každý výsledek zjemněného dotazu je tedy přípustným výsledkem předlohy. Katalog dál odpovídá na otázku benchmarku a soudce neříká, co měla předloha napsat (163) — vybírá jednu z odpovědí, které předloha sama dovoluje. Výsledek, který předloha určuje, se nemění: kde výřez v datech remízu neřeže, vrátí zjemněný dotaz tytéž řádky jako dnes. Totéž dělá specifikace LDBC, když každé řazení dotahuje identifikátorem.

**Test.** `Combined/FibenCatalogTest` drží při každém sestavení, že každý výřez katalogu splní (a), (b) nebo (c). Příští dotaz, který svůj výsledek neurčuje, tak selže v testu na kterémkoli stroji, ne až v běhu nad jiným serverem. Je to tvrzení katalogu o sobě, ne stav překladu, takže do sestavení patří i podle rozhodnutí 149.

**LDBC a matice** podmínku jako test nedostávají. Jejich výřezy výsledek určují jinou cestou — LDBC specifikací (117), matice fixturou (089) — a postačující podmínka bez funkčních závislostí by je zamítla, ač úplné jsou. Dokazovat je by žádalo odvozování závislostí přes joiny, které za dva katalogy, jež píšeme sami, nestojí.

### Soudce FIBEN

Kanonická forma i texty 29 dotazů se mění, a soudce FIBEN se proto obnoví: soudcem se stane `judge-candidate.tsv` .NET půlky prvního běhu po implementaci, za podmínek rozhodnutí 149 ve znění 157, a hlavička soudce jmenuje toto rozhodnutí. Záznam dotazu, jehož text se nezměnil, se od dosavadního smí lišit jen pořadím uvnitř úseků shodného klíče; jiný rozdíl je nález a pro ten dotaz nový soudce nevzniká.

## Důsledky

- **Kód.** Kanonická forma v `ResultRow` obou sad; `DotNetQueryRunner`, `LdbcCanonicalForm` a `FibenRun` v obou sadách ji volají místo dnešní volby mezi pořadím a seřazením. Klíč nesou `matrix.txt` s čtečkami obou sad, vazba `LdbcValidation` a záznam katalogu FIBEN; FibenImport ho odvozuje, provádí šesté pravidlo a `queries.json` se z něj přegeneruje. Překlad, model, `AbstractWrappers` ani orchestrace se nemění (S1).
- **Rozhraní.** Koncové body `/ldbc` a `/fiben` vydávají `ordered` (`RestContractTest`); zůstává jako údaj odvozený z klíče, takže se smlouva nemění.
- **Testy.** Diferenční ověření doloží případem, že pořadí uvnitř úseku shodného klíče se neporovnává — 089 žádá doklad u každého chování porovnání. Vazbu klíče na IR drží `DifferentialMatrixTest`, `LdbcCatalogTest` a `FibenCatalogTest`, podmínku výřezu `FibenCatalogTest`. Mutace vypuštěného řazení (089, 117) musí dál končit rozdílem; dnes ji odhalí jakékoli jiné pořadí, nově jen jiné pořadí klíče. Fixtura matice klíče drží jedinečné, takže se tam nic nemění; podmínky *Historie* 089 zůstávají, i když pro dotaz bez výřezu už nutné nejsou.
- **Stavy překladu a podíly T2 a T3 katalogu FIBEN.** 29 textů je nových. Výřez v poddotazu (q178–q206) píšou cíle HQL a JPQL a skalární poddotaz, který není jediným agregátem, cíl EF Core nativním SQL už dnes ([`subset.md`](../subset.md) 2.7); seskupený dotaz LINQ řadí nad skupinou i s výřezem (2.9). Změnu stavu proto nečekáme, potvrdí to ale až první běh, a změna stavu kterékoli buňky je jeho nález.
- **Měřené časy.** Doplněné řazení přidá třídění devíti dotazům s `TOP (1000)` a může změnit plán výřezů v poddotazu i na vrcholu. Časy 29 dotazů tak nejsou srovnatelné se záznamy běhů před implementací a první záznam po ní to řekne. Srovnání frameworků mezi sebou to nemění: všechny dostávají týž text.
- **Soudce FIBEN** se obnoví podle oddílu výše; dotazy, jejichž výřez v datech remízu řeže, mohou v novém soudci mít jiné řádky než ve starém, a to je zamýšlený výsledek, ne nález.
- **Soudce LDBC.** IC 14 se porovná přísněji, v pořadí vah. Kdyby validační sada vracela cesty v jiném pořadí vah, než určuje specifikace, ukáže to první běh soudce jako neshodu — nález o sadě nebo o textu, ne o porovnání. Verdikt celé sady SF 1, který proběhne před implementací, porovnává IC 14 ještě jako množinu.
- **Katalog a stránka `/fiben`** nesou zjemněný text; předloha zůstává v `benchmarkSql`.
- **Dokumentace při implementaci.** [`architecture.md`](../architecture.md) §6.2 (*Diferenční ověření*, kanonický řádek), řádek F13 v [`traceability.md`](../traceability.md); položka v [`open-items.md`](../open-items.md) zaniká.
