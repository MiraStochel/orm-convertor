# 182 — Meze nástroje se hledají řadami syntetických vstupů nad doménou matic, které rostou po jedné ose geometricky: běh na vyžádání cestou cizího artefaktu s časem a pamětí překladu v řádku, procesem na řadu, mez je poslední bod, který prošel, a první, který ne; body řad jsou druh `series` výpisu vstupů mimo výchozí rozsah experimentálního běhu

Datum: 2026-10-24
Stav: platí; mez dvou hodin na bod (`hung`) a konec procházky prvním bodem, který neprošel, v oddílu *Co se počítá za mez*, tabulku os v oddílu *Osy a řady* a sloupce oddílu *Limits* v oddílu *Druh `series` a experimentální běh* nahrazuje 190
Požadavky: S3, T3, T4, T5, F11, S7
Podklad: [záměr](../zamer.tex) (*Využití pro výzkumnou činnost*: posoudit „praktickou použitelnost navrženého přístupu a jeho omezení"); [specifikace](../specifikace.tex) (S3: „konkrétní výkonnostní výsledky budou součástí vyhodnocení"; kap. *Vyhodnocení*); zadání ([`requirements.md`](../requirements.md), S3, T3, T4, T5); rozhodnutí [016](016-generated-artifact-verification-levels.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [092](092-input-nesting-depth-capped-before-the-descent.md), [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md), [167](167-records-of-runs-and-measurements-have-a-folder-of-their-own.md), [172](172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md), [175](175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md) a [176](176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md); [`threat-model.md`](../threat-model.md), hrozba 2; záznamy [výkonu překladu](../runs/README.md#výkon-překladu-s3); kód `Tests/Combined/NestingDepthCapTest.cs`, `Tests/Combined/TranslationPerformanceTest.cs`, `Tests/Combined/QueryShapeInputs.cs`, `Tests/Experiment/`; kontrola generátorů a cesty na MIS3 z 2026-10-24 (níž, *Co kontrola ukázala*); otevřená položka *Meze složitosti dotazu, hloubky zanoření a velikosti tříd nikdo nehledal*

## Kontext

**O mezích nástroje víme dva body a nic mezi nimi.** S3 je změřené jedním bodem: 100 entit a 100 dotazů EF Core → NHibernate pod sekundou, čtyřicetinásobný odstup od meze 30 s ([záznamy](../runs/README.md#výkon-překladu-s3)). Rozhodnutí 092 změřilo, v jaké hloubce zanoření který parser shodí proces, a postavilo před sestup strop 128 úrovní. Záměr ale žádá posoudit „praktickou použitelnost navrženého přístupu a jeho omezení", a to není otázka na dva body: kde přestane stačit překlad dotazu s rostoucím počtem joinů, podmínek, poddotazů, kde třída s rostoucím počtem vlastností, vztahů a částí klíče, kde požadavek s rostoucím počtem jednotek — a jak přestane: odmítnutím podle pravidla, výjimkou, časem mimo praktické použití, pádem procesu, nebo tiše, artefaktem, který cíl nepřijme. Nikdo to systematicky nehledal, a text práce dnes o omezeních nástroje nemá co říct, kromě stropu, který si nástroj sám nastavil.

**Rozhodnutí 092 změřilo pád parserů, ne překlad.** Jeho tabulka říká, kolik závorek přečte který lexer, než dojde zásobník; vstupy té tabulky jsou holé tvary bez entity, které se nedají zkompilovat ani přijmout, takže o cestě artefaktu od druhého stupně výš neříkají nic. Položka proto výslovně žádá hloubku „ve všech jazycích, které nástroj čte, celou cestou" a s vypnutým stropem, protože strop je mez vyslovená, ne nalezená: zajímavé je, co leží za ním.

**Infrastruktura, která to umí měřit, už existuje.** Rozhodnutí 172 dalo cestu, která soudí artefakt stupeň po stupni a píše řádek; 173 ji otočilo na artefakt nástroje a dalo běh na vyžádání nad vstupy, které sada vypisuje; 175 řeklo, že řady hledání mezí „do baseline nepatří: nejdřív musí existovat, a pak jsou další populací téže cesty". Co chybí, jsou **řady samy** — kdo je vyrobí, nad čím, po jaké ose a v jakých krocích — a **pravidlo**, co se počítá za mez a jak se čte výsledek.

**Je třeba rozhodnout**, jak položka ptá: osy a generátor řad (syntetické, nebo škálované dotazy katalogů), co se počítá za mez, zda a jak řady vstupují do experimentálního běhu jako další druh klíče, a jak se měří čas a paměť, které ani řádek cesty, ani metriky T3 nenesou.

### Co kontrola ukázala

Před rozhodnutím jsme generátory a cestu nad nimi ověřili na MIS3 (LocalDB, sestavení Debug, 2026-10-24): nejmenší bod každé ze třinácti řad níž se přeložil z každého zdroje, který řadu vyslovuje, do všech šesti cílů bez záznamu `Failure`, s fixturou jako katalogem; bod dotazové řady prošel cestou do čtvrtého stupně nad fixturou; procházka řady `key-parts` ze čtyř zdrojů do šesti cílů v procesech potomka trvala 55 s na 168 bodů, z toho sestavení a start procesu zhruba 6 s na proces, a nenašla mez do stropu 64 částí klíče; procházka řady `nesting` se stropem vypnutým trvala 199 s na 405 bodů, ve všech 36 směrech skončila pádem procesu (přetečení zásobníku, v některých směrech porušení přístupu) mezi 1024 a 4096 úrovněmi, potomek po každém pádu pokračoval dalším cílem a žádný bod nechyběl — a už tahle kontrola ukázala, že týž zdroj padá do různých cílů v různé hloubce, tedy že mez má i emisní strana, jak 092 předpovědělo. Čísla jsou kontrola generátorů a mechanismu (sestavení Debug, zásobník hostitele), ne záznam: záznam vzniká během v zafixovaném prostředí.

## Zvažované varianty

### 1 — Škálovat dotazy katalogů LDBC a FIBEN a příklady

Řady by vznikly z toho, co už máme: vzít dotaz katalogu a násobit v něm joiny, podmínky, poddotazy. Zamítáme. Dotaz katalogu nese několik os najednou — IC 5 má joiny, agregaci, poddotaz i řazení — a znásobení jedné z nich mění význam dotazu, takže by u každého bodu bylo třeba znovu rozhodnout, co dotaz dělá, a čtvrtý stupeň by neměl měřítko: soudce katalogu zná výsledek původního dotazu, ne jeho dvojnásobku. Řada, která má říct, *na které ose* nástroj přestává stačit, musí držet všechny osy kromě jedné pevné, a to umí jen vstup psaný k tomu účelu.

### 2 — Řady jako testy sestavení s tvrzenou mezí

Napsat theory, které tvrdí, že 256 joinů projde a 512 ne. Zamítáme ze tří důvodů. Test tvrdí, a mez je číslo, které se *měří*: tvrzená mez by se musela opravovat s každou změnou parseru, a to je přesně narace, kterou živé dokumenty i sady nemají nést. Procházka řad trvá desítky minut, což do sestavení nepatří (rozh. 149 o běhu FIBEN platí beze změny). A řady s vypnutým stropem končí pádem procesu, který neshodí jeden test, nýbrž celý běh (092, *Zkouší se to tam, kde se to změřilo*); sada se k takovým číslům nesmí přiblížit.

### 3 — Vlastní populace s vlastním formátem řádku

Běh mezí jako samostatný nástroj s vlastním výstupem — tabulka os × směrů. Zamítáme z důvodu, kterým 173 zamítlo svou variantu 3: vznikl by čtvrtý formát řádku vedle `cells.tsv`, `foreign-rows.tsv` a `tool-rows.tsv`, srovnání s modelem podle 175 by potřebovalo převodník a čtvrtý stupeň by se stavěl znovu. Jednotka, která existuje a je správná, je řádek rozhodnutí 172; co řádku chybí — čas, paměť, pořadí bodu v řadě —, se dá přidat *vedle*, ne místo něj.

### 4 — Syntetické řady nad doménou matic po jedné ose, běh na vyžádání cestou 172 s časem a pamětí vedle řádku, procesem na řadu, mez jako poslední prošlý bod

Volíme, níž.

## Rozhodnutí

**Volíme variantu 4. Meze nástroje hledá běh na vyžádání (`Experiment/LimitSearchTest`, theory nad osami × zdroji, bez `ORMCONVERTOR_EXPERIMENT_RESULTS` přeskočí) nad řadami syntetických vstupů, které generuje `Experiment/LimitSeries` nad doménou matic tvarů dotazů (`Tests/Database/QueryShapes`, rozh. 089): každá řada roste po jedné ose geometrickými kroky od nejmenšího bodu ke stropu osy, je napsaná v jazyce každého zdroje, který ji vysloví, a její nejmenší bod se přeloží v každém směru, což drží test sestavení. Bod řady je klíč druhu `series` (`series:<osa>-<bod>`) výpisu vstupů rozh. 172, zapisovaný do `inputs/` adresáře běhu, až k němu procházka dojde, a soudí se cestou cizího artefaktu jako artefakt nástroje (173); řádek bodu nese navíc čas a paměť překladu. Procházka jde od zdroje do každého cíle bod po bodu a končí prvním bodem, který neprošel, nebo stropem; mez řady ve směru je poslední bod, který prošel, a první, který neprošel, říká proč. Každá řada jednoho zdroje běží v procesu potomka, protože řady s vypnutým stropem končí pádem procesu, a pád je nález, ne konec běhu. Druh `series` není ve výchozím rozsahu experimentálního běhu ani baseline srovnání s modelem; javová půlka ho soudí na vyžádání (`ORMCONVERTOR_EXPERIMENT_KINDS=series`) nad týmiž vypsanými body a souhrn běhu z bodů skládá oddíl *Limits*.**

### Osy a řady

Řady jsou tři skupiny, jak je položka vyjmenovala; body jsou mocniny dvou od prvního bodu ke stropu, protože mez je řádová veličina a běh musí skončit v hodinách, ne ve dnech. Dotazové řady stojí nad doménou matic — sedm entit `Shop*` ve všech šesti jazycích, data fixtury —, projektují `ProductName` z tabulky produktů a filtrují tak, aby každý bod vracel řádky; entitní řady vyslovují klíč samy.

| Osa | Co roste | Body | Zdroje | 4. stupeň |
|---|---|---|---|---|
| `joins` | vnitřní joiny tabulky produktů, každý na klíč předchozího | 1 … 512 | všech šest | nad fixturou |
| `conditions` | predikáty spojené `AND` v jednom plochém filtru | 1 … 4096 | všech šest | nad fixturou |
| `nesting` | úrovně závorek kolem jediného predikátu; **strop 092 vypnutý** | 2 … 4096 | všech šest | nad fixturou |
| `subqueries` | úrovně poddotazů vnořených v pravé straně `IN`; **strop 092 vypnutý** | 1 … 2048 | všech šest | nad fixturou |
| `exists` | korelované `EXISTS` vedle sebe v jednom filtru | 1 … 512 | všech šest | nad fixturou |
| `ctes` | mezivýsledky řetězené jeden na druhý, dotaz čte poslední | 1 … 256 | Dapper, EF Core, Hibernate, MyBatis (jako kategorie `IntermediateResultReadTwice`) | nad fixturou |
| `unions` | množinové operace řetězící výběry z produktů | 1 … 256 | bez NHibernatu (HQL `union` nemá, jako kategorie `SetOperation`) | nad fixturou |
| `expression` | členy jednoho aritmetického výrazu v projekci | 1 … 4096 | všech šest | nad fixturou |
| `properties` | skalární vlastnosti jedné entity vedle klíče | 8 … 4096 | EF Core, NHibernate, Hibernate, EclipseLink | není |
| `relations` | vazby many-to-one z jedné entity na zákazníka domény | 1 … 256 | tytéž čtyři | není |
| `key-parts` | části složeného klíče jedné entity | 1 … 64 | tytéž čtyři | není |
| `unit-size` | kilobajty komentáře v souboru entit domény | 64 KB … 64 MB | všech šest | není |
| `entities` | entity s jedním dotazem každá v jednom požadavku — projekt S3 škálovaný | 100 … 800 | EF Core, Hibernate | není |

**Proč tyto zdroje.** Dotazové řady čtou pět gramatik přes šest zdrojů: T-SQL (Dapper, MyBatis), LINQ (EF Core), HQL (NHibernate, holý `.hql`), JPQL (Hibernate, EclipseLink) — tedy i obě cizí gramatiky, které podle 092 padají nejdřív. Entitní řady vyslovují zdroje, které vyslovují klíč: Dapper a MyBatis by klíč syntetické třídy musely vzít z katalogu, a žádná databáze nástroje tabulku syntetické třídy nemá, takže by byly odmítnuty za mlčení katalogu, ne za velikost — totéž, proč příklady běží bez katalogu (rozh. 099). Řada `unit-size` zvětšuje soubor entit domény, které v katalogu jsou, a proto ji vyslovuje všech šest. Řada `entities` je projekt S3 (`TranslationPerformanceTest`) škálovaný, v EF Core doslova týmiž jednotkami a v JPA jejich protějškem; jiné zdroje by měřily totéž podruhé.

**Hloubka tříd se neměří řadou.** 092 změřilo vnořené generické typy a vnitřní třídy Javy a závorky v inicializátoru C#; vnořený typ není mapovaná vlastnost a vnitřní třída není entita, takže cesta od druhého stupně nemá co nést a řada by měřila jen parser, což tabulka 092 a `NestingDepthCapTest` už drží. Hloubka v řadách je hloubka dotazu, kde má celá cesta smysl.

### Co se počítá za mez

Procházka řady jednoho zdroje jde do každého cíle bod po bodu a u každého bodu překládá a soudí. Bod **neprošel**, když:

- nástroj ho **odmítl** (záznam `Failure`; řádek `refused`, 173) nebo **vyhodil výjimku** (`threw`);
- překlad trval **déle než mez S3**, 30 s (`too slow`) — mez je mez požadavku, ne vlastnost řady, a platí pro každý bod, ať artefakt vyšel jakýkoli;
- **proces zemřel** během bodu (`crashed`, s kódem ukončení a stupněm, na kterém zemřel), nebo bod nedokončil do dvou hodin (`hung`);
- cíl .NET artefakt **nepřijal** — nedosáhl třetího stupně (`unreadable`, `not compiled`, `not accepted`): artefakt, který framework nepřijme, je výstup, který uživateli nepomůže, ať nástroj hlásil cokoli.

Čtvrtý stupeň procházku **nezastaví**: dotaz, který SQL Server odmítne pro svou mez — 256 tabulek v dotazu, 32 úrovní poddotazů —, by neběžel ani ve zdroji, a je to mez platformy, ne překladu. Souhrn ho čte zvlášť: vedle *posledního přeloženého* a *posledního přijatého* bodu stojí *poslední, který běžel*, takže záznam říká obojí — kam dojde nástroj a kam databáze. U javového cíle .NET půlka zná jen překlad (čas, paměť, odmítnutí, pád); stupně dodá javová půlka nad týmž vypsaným bodem a souhrn je k bodu přiřadí.

**Mez řady ve směru je poslední bod, který prošel**; první, který neprošel, nese důvod. Řada, která dojde ke stropu bez selhání, mez nenašla, a souhrn řekne `ceiling`: strop je volba běhu, ne vlastnost nástroje, a záznam ho jmenuje.

**Čas a paměť** se měří u překladu, protože o něm mluví S3: čas volání `ConversionHandler.Convert` a bajty, které proces během něj alokoval (`GC.GetTotalAllocatedBytes`), vedle času čtení katalogu z výsledku (`CatalogReadTime`) a velikosti vstupu v bajtech. Paměť mez nemá — hrozba 2 nemá číslo, které by ji dala —, zapisuje se, aby záznam ukázal její růst po ose; vyčerpání paměti je pád nebo výjimka a končí řadu jako každé jiné. Časy stupňů cesty nese řádek 172 sám. Jedno měření na bod, ne medián podle rozh. 145: řada hledá řád meze, ne rozdíl mezi kandidáty, a opakování by čas běhu násobilo bez zisku; kde záznam potřebuje přesné číslo (bod S3), má ho měření času.

**Katalog** je rozměr běhu, ne řady: `ORMCONVERTOR_SERIES_CATALOG=fixture` (výchozí) překládá s fixturou jako katalogem — bez ní by Dapper a MyBatis neměly klíč a dotazové řady by nedošly ke čtvrtému stupni —, `none` překládá nasucho, což je druhá polovina měření, které S3 odděluje. Sloupec `catalog` v řádku to říká a souhrn řady obou hodnot rozlišuje.

### Proces na řadu

Řady `nesting` a `subqueries` běží se stropem 092 vypnutým (`ParseLimits.Unlimited`, předaný parserům přímo, ne nastavením instance), protože strop je vyslovená mez a otázka položky zní, co je za ní. Za ní je podle 092 pád procesu, který .NET nezachytí. Proto **každá řada jednoho zdroje běží v procesu potomka**: rodič (`LimitSearchTest`) spustí testovací sestavení jako program (`dotnet Tests.dll -class Tests.Experiment.LimitSeriesChildTest`, proměnné `ORMCONVERTOR_SERIES_WALK`, `_FILE`, `_INPUTS`, `_CATALOG`), potomek jde po cílech a bodech a soubor `series-points.tsv` přepisuje po každé změně — bod dostane řádek `in progress` dřív, než se přeloží, a stupeň, jak ho dosáhne. Zemře-li potomek, rodič přečte, co po něm zbylo, z řádku `in progress` udělá `crashed` s kódem ukončení a stupněm, a spustí potomka znovu; ten přeskočí každý bod, který v souboru je, a za řádkem, který neprošel, jde na další cíl. Procházka zastavená zvenčí tak pokračuje tam, kde byla, jako běh FIBEN. Rodič drží schéma fixtury po dobu běhu (kolekce `TestSchemaCollection`), potomek je mimo ni, aby schéma nevytvářel a nerušil pod rodičem; katalog čte vlastní čtečkou nad touž databází. Cena je start procesu na řadu a zdroj, zhruba šest sekund.

Proč ne jeden proces s velkým zásobníkem, proč ne vlákno: 092 to zvážilo a zamítlo pro nástroj, a pro měření platí totéž — větší zásobník mez posune, nenajde, a vlákno pád procesu nepřežije.

### Druh `series` a experimentální běh

Bod řady je klíč `series:<osa>-<bod>` a jeho vstup se vypisuje do `inputs/` adresáře běhu touž cestou jako každý jiný klíč (172), až k němu procházka dojde — vypsané body jsou tedy přesně ty, k nimž nástroj došel, a tvoří populaci, nad kterou navazující práce podle 175 měří model: kde model vydá artefakt za mezí nástroje a zda projde. Druh **není** ve výchozím rozsahu `ORMCONVERTOR_EXPERIMENT_KINDS` ani .NET, ani javové půlky, a souhrn ho z tabulek T2 × T3 vyjímá (175: řady do baseline nepatří). Javová půlka ho soudí na vyžádání: `experiment/ExperimentRunTest` s `ORMCONVERTOR_EXPERIMENT_KINDS=series` přeloží každý vypsaný bod instancí do tří javových cílů a projde jím stupně; měřítkem čtvrtého stupně dotazové řady je *běží* nad fixturou (jako projekt případové studie, 176), řada bez tabulky ve fixtuře končí třetím stupněm, a které řady to jsou, drží javová sada v seznamu, který .NET test drží rovný svému. Při překladu instancí přes HTTP narazí javová půlka na to, co .NET půlka v procesu ne — výchozí mez velikosti těla Kestrelu u řad `unit-size` a `entities` —, a to je nález o nasazení, ne chyba běhu: řádek `refused` s odpovědí instance.

Souhrn (`ExperimentSummaryTest`) z `series-points.tsv` a z javových řádků druhu `series` skládá oddíl *Limits*: po osách tabulku směrů s posledním přeloženým, přijatým a běžícím bodem, prvním neprošlým bodem s důvodem nebo `ceiling`, a časem a pamětí překladu u posledního přeloženého bodu. Záznam běhu v [`runs/`](../runs/README.md) ho přebírá jako tabulku T2 × T3.

### Co se netvrdí

Běh nic nenárokuje: S3 zůstává nárokované jedním bodem, hrozba 2 popisuje dnešek, oblast 6 §9 zůstává vyňatá. Co běh najde — mez, která je vadou (tichý neplatný artefakt, pád pod stropem, čas nad mezí u vstupu, který někdo napsal rukou) —, je položka nebo rozhodnutí; co je mezí platformy nebo řádem, který nikdo nenapíše, je věta v textu práce o omezeních nástroje. Rozhodnutí nevolí, co se s nalezenou mezí stane.

## Důsledky

- **Kód:** `Tests/Experiment/LimitSeries.cs` (osy a generátory), `LimitSearch.cs` (bod, procházka, rodič), `LimitSearchTest.cs` (`LimitSearchTest` — běh na vyžádání, `LimitSeriesChildTest` — potomek, `LimitSeriesTest` — testy sestavení: nejmenší bod v každém směru, počty, klíč, procházka v procesu nad dvěma body, bod cestou do čtvrtého stupně, souhrn, shoda seznamu s javovou sadou); `ForeignKey.Series` a měřítko druhu v `Foreign/ForeignArtifactRun` (čtvrtý stupeň jednoho dotazu bez měřítka), `ExperimentRun.Failures`/`ArtifactOf` sdílené s procházkou, oddíl *Limits* v `ExperimentSummary`; javová sada `Key.SERIES`, měřítko druhu a běh jednoho dotazu bez měřítka v `foreign/ForeignArtifactRun`.
- **[`open-items.md`](../open-items.md):** položka rozhodnutí *Meze složitosti dotazu, hloubky zanoření a velikosti tříd nikdo nehledal* mizí; vzniká práce *První záznam běhu mezí* (MIS3, zafixované prostředí, obě půlky, obě hodnoty katalogu).
- **Dokumentace:** `architecture.md` §6.2 oddíl *Experimentální běh* (běh mezí), `ORMConvertor/README.md` oddíl *The limits run*, [`runs/README.md`](../runs/README.md) druh běhu, [`traceability.md`](../traceability.md) řádky S3 (běh mezí jako druhý důkaz, bez změny stavu) a T3–T5 (řady jako další populace cesty).
- **Cena běhu.** Procházka platí stupni cesty za každý bod a startem procesu za každou řadu a zdroj; odhad celého běhu na MIS3 je v hodinách a záznam ho upřesní. Řada `entities` je nejdražší, protože druhý stupeň projektu kompiluje každý dotaz zvlášť s každou entitou, tedy kvadraticky; proto končí na 800, osminásobku scénáře S3, a čas překladu v řádku bodu je to, oč S3 jde.
- **Co toto rozhodnutí neurčuje:** co se stane s nalezenou mezí (položka nebo rozhodnutí po záznamu); hloubku tříd nad rámec 092; rozsah, který navazující práce z vypsaných bodů vezme do srovnání s modelem (175).
