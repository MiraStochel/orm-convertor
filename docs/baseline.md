# Výchozí stav při převzetí projektu

**Snímek k 2026-07-15.** Dokument popisuje, co nástroj uměl a co mu chybělo ve chvíli, kdy jsme na něm začali stavět. Přítomný čas se v celém textu vztahuje k tomuto datu.

Snímek je **zmražený a nepřepisuje se** — je to referenční bod, proti kterému se poměřuje, co z dnešního stavu jsme přinesli my a co jsme převzali. Jak nástroj funguje dnes, popisuje [`architecture.md`](./architecture.md); co zbývá udělat, [`open-items.md`](./open-items.md).

---

## 1. Co funguje

- Překlad entit a mapování mezi všemi třemi podporovanými ORM (Dapper, NHibernate, EF Core) v libovolném směru.
- Překlad dotazů jedním směrem: EF Core LINQ → Dapper SQL.
- Advisor (ILP optimalizátor) pro Dapper a EF Core – reálně přeloží a odbenchmarkuje sadu dotazů, doporučí framework/kombinaci podle zadaných omezení (max. počet frameworků, paměťový limit, váhy dotazů).
- REST API + Swagger dokumentace (UI jen v Development prostředí), Angular frontend se třemi obrazovkami: úvodní stránka, Translation a Advisor.
- Testy parserů a builderů pro všechny tři ORM (převod do mezireprezentace a z ní, identity převody X→X) a kombinované end-to-end testy pro dvojice EF Core ↔ NHibernate a EF Core → Dapper (dotazy). Běží v CI při push na `main` a u pull requestů, pokud se změnilo něco v `ORMConvertor/**`.

## 2. Co chybí do plného rozsahu – a proč

| Mezera | Popis | Poznámka |
|---|---|---|
| Parser dotazů pro NHibernate | neexistuje – NHibernate se účastní jen entit/mapování, ne dotazů | přirozeným cílem je NHibernate LINQ (`ISession.Query<>()`): HQL je stringový dotazovací jazyk (viz `notes/comparison-research/NHibernate.md`), takže by vyžadoval samostatný textový parser podobně jako Dapper SQL, zatímco LINQ jde parsovat stejným Roslyn přístupem jako u EF Core; Criteria/QueryOver jsme nezvažovali |
| Builder dotazů pro EF Core a NHibernate | neexistuje – dotazy lze generovat jen do Dapper SQL | |
| Parser dotazů pro Dapper | neexistuje vůbec, a na rozdíl od EF Core/NHibernate to není jen „doplnit chybějící case" – Dapper dotazy jsou syrové SQL řetězce, takže je potřeba napsat samostatný SQL parser, ne rozšířit stávající Roslyn LINQ walker | největší nová komponenta ze všech |
| Advisor jen pro Dapper a EF Core | `SupportedFrameworks` v `AdvisorRunCoordinator` explicitně vylučuje NHibernate; `KnownFrameworks` o něm ví, ale živý pipeline ho nedokáže odbenchmarkovat (návaznost na chybějící query builder) | |
| Podmínky jen jako plochý AND | `SelectInstruction`/`HavingInstruction` neumí OR, závorky ani vnoření | potvrzeno i v původní diplomce jako známé omezení; původní návrh navíc počítal s podmínkovým stromem i pro `JOIN` (`JOIN table1, table2, joinType, conditionTree`), ne jen pro WHERE/HAVING |
| Žádné napojení na databázi pro doplnění metadat | když zdrojová definice entity nemá dost informací (typicky u Dapperu), model se nedoplňuje dotazem do DBMS – v kódu není žádná logika pro čtení schématu | zmíněno v diplomce jako budoucí rozšíření, nikdy neimplementováno |
| Kompozitní primární klíče | `AddPrimaryKey` bere jen jednu vlastnost, žádné pořadí, žádná strategie per-part | v původním návrhu se neobjevuje vůbec, ani jako budoucí práce – jde o čistě nový požadavek, ne o dodělání něčeho rozpracovaného |
| Vícesloupcové vztahy (FK) | `Relation` visí na jednom `PropertyMap`, žádné uspořádané páry sloupců; totéž omezení má i `JoinInstruction` | stejně jako u kompozitních klíčů nový požadavek, ne rozpracovaná věc z diplomky |
| Automatická detekce zdrojového frameworku | uživatel musí framework vybrat ručně | zmíněno v diplomce jako budoucí rozšíření |

## 3. Mapování požadavků na výchozí stav

Jak si nástroj k témuž datu stál vůči zadání vedoucího ([`requirements.md`](./requirements.md)).

| Požadavek | Stav | Poznámka |
|---|---|---|
| F1 | chybí | `AddPrimaryKey` bere jen jednu vlastnost, bez pořadí a bez strategie per-part |
| F2 | chybí | .NET část navazuje na F1; Java část závisí na F7–F9 |
| F3 | chybí | `Relation` visí na jednom `PropertyMap`, žádné uspořádané páry sloupců |
| F4 | chybí úplně | žádné čtení databázového katalogu |
| F5 | částečně | agregace více parserů do jedné IR funguje (NHibernate C# + XML); chybí DB katalog jako zdroj, priority zdrojů a hlášení konfliktů |
| F6 | chybí | závisí na F4 + F5 |
| F7–F9 | chybí | nový Java ekosystém, zatím nezačato |
| F10 | chybí | typový model IR je CLR-specifický (`CLRTypeModel`, `CLRTypeConvertor` v `Common`) – pro Javu bude potřeba jazykově neutrální reprezentace typů (JSS článek §5.2 „LangType") |
| F11 | nesplněno | validace úplnosti IR neexistuje; nepodporované konstrukce se potichu vynechávají (Dapper builder přeskakuje PK/FK); strukturovaná diagnostika chybí |
| F12–F13 | chybí | testovací infrastruktura pro Javu a diferenční běh dotazů neexistuje |
| F14 | částečně | frontend přijímá ručně vkládané artefakty po jednom; nahrání souborů/archivu projektu a per-file diagnostika chybí |
| F15 | částečně | přímá volba cíle i ILP advisor fungují (jen Dapper a EF Core); srovnání „vše X" a heuristik se uživateli nezobrazuje |
| S1 | drží | zajištěno parser/builder architekturou; hlídat při přidávání Java frameworků |
| S2 | pravděpodobně drží, netestováno | šablonové generování je deterministické, ale žádný test opakovatelnosti neexistuje |
| S3 | neměřeno | JSS článek uvádí škálovací čísla jen pro původní prototyp |
| S4 | nesplněno | benchmarky běží in-process bez limitů CPU/paměti/času; fallback connection string se `sa` heslem je natvrdo v `AdvisorRunCoordinator.cs` |
| S5 | částečně | docker-compose (aplikace + DB) existuje; jednopříkazová reprodukce testů a experimentů ne |
| S6 | chybí | žádný identifikátor běhu ani strojově čitelný záznam překladu |
| S7 | částečně | základní scénář v UI projde; validace před spuštěním, průběžný stav, chyby na úrovni souboru/řádku a stažení výstupního projektu chybí |
| T1–T7 | nezačato | T7 přímo navazuje na existující ILP advisor |

## 4. Nápady z původního výzkumu bez vazby na aktuální práci

Nízká priorita, jen ať se neztratí – diplomka je zmiňuje jako možné směry rozšíření Advisoru, se kterými dnešní implementace vůbec nepočítá: zahrnutí více databázových backendů do optimalizačního prostoru (ne jen výběr ORM, ale i výběr DB – PostgreSQL, MySQL, NoSQL), a modelování redundance/replikace dat (směrování dotazů na replikovaná/denormalizovaná úložiště). Žádné z toho není potřeba řešit teď, ale stojí za zapsání, kdyby se k tomu jednou vracelo.
