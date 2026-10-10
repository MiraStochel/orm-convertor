# 121 — Text dotazu katalogu LDBC smí mít tvar, který plánovač SQL Serveru potřebuje, zůstane-li T-SQL, jaké by stálo vedle referenční implementace; fyzický návrh `LdbcSnb` nese indexy, které čtení katalogu potřebují, nad rámec indexů cizích klíčů

Datum: 2026-10-10
Stav: platí
Požadavky: T2, T3, F13, S5
Podklad: rozhodnutí [110](110-ldbc-snb-as-a-second-reference-domain.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) a [120](120-a-set-operation-as-the-body-of-a-subquery-operand.md); specifikace *The LDBC Social Network Benchmark*, verze 2.2.4, příloha A (IC 5 *New groups*) a kapitola 7 (pravidla auditovaného běhu: fyzický návrh volí systém pod testem); referenční implementace `ldbc_snb_interactive_v1_impls` (`postgres/ddl/schema_constraints.sql`, `postgres/queries/interactive-complex-5.sql`); měření 2026-10-10 nad `mssql_db` (SF 1) na MIS3, popsané níž

## Kontext

Verdikt soudce LDBC nad celou sadou SF 1 (rozh. 117) poprvé běžel 2026-10-10 nad commitem `b61f38c`: .NET sada přehrála za 25 minut 769 řádků ze 138 747, tedy něco přes 30 řádků za minutu, což by pro .NET sadu znamenalo zhruba 72 hodin a pro javovou podobně. Statistiky dotazů SQL Serveru za ten běh ukázaly, že čas nese jedno čtení: **IC 5** stálo 5,7 s v Dapperu, 5,5 s v NHibernatu a 11 s v EF Core, zatímco druhé nejdražší čtení (IC 14) 1,4 s a všechna ostatní pod sekundu. IC 5 má v sadě 6 818 čtení, tedy přes 40 ze 72 hodin.

Plán vysvětluje proč. Text katalogu psal okruh osob jako **dvě podmínky `IN` spojené `OR`** — přátelé v jednom poddotazu, přátelé přátel v druhém:

```sql
AND (fm.PersonId IN (SELECT k1.Person2Id FROM Person_knows_Person AS k1 WHERE k1.Person1Id = @personId)
     OR fm.PersonId IN (SELECT k2.Person2Id FROM Person_knows_Person AS k1
                        JOIN Person_knows_Person AS k2 ON k2.Person1Id = k1.Person2Id
                        WHERE k1.Person1Id = @personId))
```

Z disjunkce dvou polospojení plánovač SQL Serveru neodvodí seek: odhadne jeden řádek členství, projde **celou tabulku `Forum_hasMember_Person`** (1,6 milionu řádků nad SF 1, 7,9 milionu logických čtení) a každý řádek zvlášť zkouší proti oběma poddotazům. Skutečný okruh přitom má kolem dvou tisíc osob a 92 tisíc členství po datu — práce, která by měla trvat zlomek sekundy. Indexy to samy nespraví: s indexem členství po osobě a datu a s indexem zpráv po fóru a autorovi kleslo čtení z 5,5 s na 4,4 s, protože tvar plánu zůstal.

Referenční implementace LDBC nad PostgreSQL píše totéž místo jinak: okruh osob je **jedna množina** — odvozená tabulka s `UNION` přátel a přátel přátel —, se kterou se členství spojí. Nad SQL Serverem dá ten tvar 1,4 s, a s oběma indexy 0,4 s. Tvar s `UNION` ale pro nástroj něco stojí: množinovou operaci v těle poddotazu nese rozhodnutí 120, HQL NHibernatu 5.7 ji nemá a EclipseLink 5.0.0 ji v závorce odmítá, takže by IC 5 šlo do dvou cílů nativním SQL (113) — a `IN` nad `UNION` plánovač nevede o nic lépe než `OR` (5,4 s, s indexy 4,7 s); pomáhá jen odvozená tabulka, tedy mezivýsledek, který do HQL ani JPQL také nejde.

Otázka tedy není jen „jak zrychlit IC 5", ale dvě obecnější: **smí text katalogu dostat tvar diktovaný plánovačem jedné databáze**, když 110 říká, že text je přirozené T-SQL, jaké jde položit vedle referenční implementace, a otevřená položka *Přepis textu katalogu kvůli vadě jednoho cíle* odmítá tvar diktovaný jedním cílem; a **smí fyzický návrh `LdbcSnb` nést indexy nad rámec cizích klíčů**, když LDBC je měřítko, které si nástroj nevybral a které nemá přizpůsobovat sobě.

## Zvažované varianty

### 1 — Nechat text i indexy a běh vydržet

Verdikt by trval kolem tří dnů na sadu, přes týden pro obě. Zamítáme: 117 počítalo s hodinami, ne dny, a soudce, který se pouští jednou za vydání, protože stojí týden stroje, přestane být soudcem. Doba navíc neměří nic o překladu — všech šest cílů čeká na tentýž plán jedné databáze.

### 2 — Indexy bez změny textu

Rozšířit index členství o datum a index zpráv o autora. Změřeno: 5,5 s → 4,4 s. Zamítáme jako jedinou odpověď — tvar plánu zůstává scan celé tabulky, protože příčinou není chybějící index, nýbrž disjunkce dvou poddotazů, kterou plánovač nepřepíše.

### 3 — Nápověda plánovači nebo plan guide

`OPTION (HASH JOIN)`, `FORCESEEK` či plan guide uložený v databázi. Zamítáme: nápověda je T-SQL, které žádný jazyk cíle nemá, takže by text buď přestal být překládatelný, nebo by ji překlad ztratil a cíle by měřily jiný plán než zdroj; plan guide je stav databáze neviditelný v textu i v repozitáři a nad jiným serverem by zmizel beze slova.

### 4 — Tvar referenční implementace: odvozená tabulka s `UNION`

Nejrychlejší měřený tvar a doslova ten, který stojí v referenční implementaci. Zamítáme kvůli ceně pro nástroj: mezivýsledek jde do NHibernatu i EclipseLinku únikovou cestou (113, 120), takže by katalog ztratil dva cíle v jazyce cíle u dotazu, který dnes v jazyce cíle má pět ze šesti. Měřítko překladu by se zúžilo kvůli plánovači, což je přesně tvar závislosti, který 110 nechce.

### 5 — Jedna množina jako poddotaz v poddotazu, a indexy k tomu

## Rozhodnutí

**Volíme variantu 5. Okruh osob IC 5 je jedna množina, jak ji píše referenční implementace, vyslovená bez množinové operace: osoby, které zná dotazovaná osoba nebo některý z jejích přátel — poddotaz `IN` s `OR` uvnitř a druhým poddotazem `IN` v něm. Fyzický návrh `LdbcSnb` nese vedle indexů cizích klíčů dva indexy rozšířené pro toto čtení. Text dotazu katalogu smí dostat tvar, který plánovač databáze potřebuje, jen zůstane-li přirozené T-SQL téže věty, jaké by stálo vedle referenční implementace, a jen tam, kde to měření nad SF 1 doloží.**

```sql
AND fm.PersonId IN (SELECT k2.Person2Id FROM Person_knows_Person AS k2
                    WHERE k2.Person1Id = @personId
                       OR k2.Person1Id IN (SELECT k1.Person2Id FROM Person_knows_Person AS k1 WHERE k1.Person1Id = @personId))
```

### Proč to není přepis textu kvůli vadě jednoho cíle

Otevřená položka o přepisu textu se týká tvaru, který by text dostal kvůli tomu, že **jeden cíl** neumí napsat, co ostatní umějí; měřil by se pak dotaz, který by nikdo nenapsal, a jen proto, aby jeden cíl nešel únikovou cestou. Tady jde o jinou věc ve třech bodech. **Za prvé** tvar nediktuje cíl, nýbrž databáze, nad kterou běží všech šest cílů i zdroj: plán je týž pro Dapper, EF Core, NHibernate, Hibernate, EclipseLink i MyBatis, protože všechny posílají SQL Serveru tutéž větu. **Za druhé** nový tvar je věta, kterou referenční implementace sama píše — jedna množina přátel a přátel přátel —, jen bez `UNION`, protože SQL ji umí vyslovit i poddotazem; starý tvar s `OR` nad dvěma `IN` byl naopak naše vlastní čtení, které vedle referenční implementace stálo hůř. **Za třetí** se nemění, co dotaz vrací, a soudce 117 to drží: nad validační sadou musí nový text souhlasit na každém ze 6 818 čtení stejně jako starý.

Z toho plyne pravidlo pro příště, aby se nerozhodovalo dotaz po dotazu: text katalogu **smí** změnit tvar kvůli plánovači, když (a) jde o tutéž větu specifikace, (b) výsledný tvar je T-SQL, jaké referenční implementace píše nebo by napsala, (c) nemění, co nástroj přeloží — žádný cíl nepřibude v nativním SQL ani v odmítnutí —, a (d) měření nad SF 1 je zapsané. Tvar, který by některý z bodů porušil — `UNION` v poddotazu, mezivýsledek, nápověda — je rozhodnutí, ne práce, a dvě takové otázky (BI 15 a BI 19, čtyři dotazy EclipseLinku) zůstávají otevřené tak, jak jsou; toto rozhodnutí je nezavírá.

### Proč indexy nemění měřítko

LDBC předepisuje data a dotazy, ne fyzický návrh: kapitola 7 specifikace nechává indexy, rozvržení a konfiguraci na systému pod testem a žádá jen, aby byly zveřejněny. Referenční implementace nad PostgreSQL zakládá indexy nad každým sloupcem cizího klíče — tutéž sadu, jakou `constraints.sql` nesl od rozhodnutí 110. Dva indexy tohoto rozhodnutí tu sadu **rozšiřují**, ne mění: index členství po osobě dostává za druhý sloupec datum vstupu (`IX_Forum_hasMember_Person_PersonId` nad `PersonId, JoinDate`), takže členství osoby po datu je jeden rozsah, a index zpráv po fóru dostává autora a nese rodiče (`IX_Message_ContainerForumId` nad `ContainerForumId, CreatorPersonId` s `ParentMessageId`), takže příspěvky člena ve fóru jsou jeden seek. Oba zůstávají indexy nad cizími klíči, jen širší; `IX_Message_CreatorPersonId` nad `CreatorPersonId, CreationDate` tak stál už od 110.

Soudce 117 porovnává řádky, ne časy, takže mu index nemůže změnit verdikt. Nárok T2 a T3 je o překladu a o shodě výsledků, ne o rychlosti běhu, a žádné číslo T-požadavku se nad `LdbcSnb` neměří jako výkon databáze — a kdyby se někdy mělo (otevřená položka o referenční databázi experimentů), platí zveřejnění: indexy stojí v repozitáři jako skript, který každá databáze dostane stejně. Rozhodnutí 110 a 117 už říkají, že nic z toho není běh LDBC Benchmarku; index na tom nic nemění.

### Kde indexy žijí a jak se dostanou do databáze, která už data má

Indexy se stěhují z `constraints.sql` do vlastního **`database/ldbc/indexes.sql`**, který je **znovu spustitelný**: každý index se zahodí, existuje-li, a založí znovu. Loader (`load-ldbc.sh`) ho spouští po `constraints.sql` a navíc **vždy, když se kontrolní součet skriptu liší od rozšířené vlastnosti `ldbc.indexes`** databáze — takže svazek načtený před touto změnou dostane indexy při příštím startu bez nového načtení dat (70 s dat a minuty validační sady by jinak padly na každou změnu indexu), a databáze načtená ručně (MIS2) dostane tentýž skript ručně. Testovací fixture staví prázdné schéma `<schéma>_ldbc` dál ze stejných skriptů, nově i z `indexes.sql`, aby zůstala pravda, že test ověřuje překlad nad týmž schématem, nad jakým ho uživatel spustí (110).

## Důsledky

- **Katalog:** text IC 5 a jeho poznámka v `SampleData/LdbcSnbSample.cs`; stav zůstává *podle specifikace* a vazba na soudce beze změny. Překlad nového tvaru do pěti cílů drží `Combined/LdbcCatalogTest` jako dosud; poddotaz v poddotazu je konstrukce, kterou katalog dosud nepsal, a matice T2 kategorii pro ni nemá — doplnit ji je práce podle rozhodnutí 089 a 099, ne součást této volby.
- **Databáze:** `constraints.sql` nese jen cizí klíče, `indexes.sql` indexy; `load-ldbc.sh` a `database.Dockerfile` ho znají; vlastnost `ldbc.indexes`. První start existujícího svazku `mssql_db` po této změně obnoví všechny indexy `LdbcSnb` (nad SF 1 desítky sekund).
- **Měření:** jedno čtení IC 5 nad SF 1 (osoba 2199023266354, nejdražší ze tří měřených): původní tvar 5 466 ms, s indexy 4 405 ms; odvozená tabulka s `UNION` 1 369 ms, s indexy 421 ms; zvolený tvar 1 383 ms, s indexy **535 ms**; `IN` nad `UNION` 5 440 ms, s indexy 4 673 ms. Všechny tvary vrátily týchž 20 řádků. Projekce .NET sady nad celou sadou klesá z ~72 h o ~40 h; zbytek nese IC 14, IC 9, IC 12 a IC 10 (1,4 s, 1 s, 0,7 s a 0,35 s na čtení) a desítky tisíc levných čtení — verdikt je dál běh na hodiny, ne na minuty, a výsledek jde do záznamu běhů podle 117.
- **Testy:** `LdbcCatalogTest` (překlad a spuštění nového textu nad prázdným schématem), `TestSchemaFixtureTest` (schéma se staví i s `indexes.sql`), soudce 117 nad IC 5 v obou sadách. Žádný nový test: rozhodnutí nemění, co nástroj umí, jen jeden text a fyzický návrh databáze.
- **Co to neotevírá:** tvar ostatních dotazů katalogu se nemění; BI 15, BI 19 a čtyři dotazy EclipseLinku zůstávají otevřenými položkami podle svých bodů (a)–(d).
