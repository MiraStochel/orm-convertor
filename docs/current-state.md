# Současný stav aplikace

**Účel:** shrnutí toho, co je dnes reálně implementované a co zbývá dodělat do zamýšleného rozsahu aplikace. Slouží jako základna pro plánování další práce – než založím nový design dokument pro konkrétní rozšíření, mělo by být jasné, co přesně chybí a proč.

---

## 1. Co funguje

- Překlad entit a mapování mezi všemi třemi podporovanými ORM (Dapper, NHibernate, EF Core) v libovolném směru.
- Překlad dotazů jedním směrem: EF Core LINQ → Dapper SQL.
- Advisor (ILP optimalizátor) pro Dapper a EF Core – reálně přeloží a odbenchmarkuje sadu dotazů, doporučí framework/kombinaci podle zadaných omezení (max. počet frameworků, paměťový limit, váhy dotazů).
- REST API + Swagger dokumentace, Angular frontend s dvěma obrazovkami (Translation, Advisor).
- Integrační testy pro každou implementovanou kombinaci směru překladu, spouštěné automaticky přes CI.

## 2. Co chybí do plného rozsahu – a proč

| Mezera | Popis | Poznámka |
|---|---|---|
| Parser dotazů pro NHibernate | neexistuje – NHibernate se účastní jen entit/mapování, ne dotazů | rozhodnuto: cílíme na NHibernate LINQ (`ISession.Query<>()`) – původní srovnávací výzkum HQL explicitně nedoporučuje pro nízkou adopci, Criteria/QueryOver jsme nezvažovali |
| Builder dotazů pro EF Core a NHibernate | neexistuje – dotazy lze generovat jen do Dapper SQL | |
| Parser dotazů pro Dapper | neexistuje vůbec, a na rozdíl od EF Core/NHibernate to není jen "doplnit chybějící case" – Dapper dotazy jsou syrové SQL řetězce, takže je potřeba napsat samostatný SQL parser, ne rozšířit stávající Roslyn LINQ walker | největší nová komponenta ze všech |
| Advisor jen pro Dapper a EF Core | `SupportedFrameworks` v `AdvisorRunCoordinator` explicitně vylučuje NHibernate; `KnownFrameworks` o něm ví, ale živý pipeline ho nedokáže odbenchmarkovat (návaznost na chybějící query builder) | |
| Podmínky jen jako plochý AND | `SelectInstruction`/`HavingInstruction` neumí OR, závorky ani vnoření – potvrzeno i v původní diplomce jako známé omezení. Původní návrh navíc počítal s podmínkovým stromem i pro `JOIN` (`JOIN table1, table2, joinType, conditionTree`), ne jen pro WHERE/HAVING | řeší se v `docs/design/001-query-model-and-composite-keys.md`, `JoinInstruction` je teď navržený se stejným `ConditionNode` |
| Žádné napojení na databázi pro doplnění metadat | když zdrojová definice entity nemá dost informací (typicky u Dapperu), model se dnes nedoplňuje dotazem do DBMS – v kódu jsem nenašel žádnou logiku pro čtení schématu | zmíněno v diplomce jako budoucí rozšíření, nikdy neimplementováno |
| Kompozitní primární klíče | `AddPrimaryKey` bere jen jednu vlastnost, žádné pořadí, žádná strategie per-part | v původním návrhu se neobjevuje vůbec, ani jako budoucí práce – jde o čistě nový požadavek, ne o dodělání něčeho rozpracovaného. Řeší se v `docs/design/001-query-model-and-composite-keys.md` |
| Vícesloupcové vztahy (FK) | `Relation` visí na jednom `PropertyMap`, žádné uspořádané páry sloupců; totéž omezení mělo i `JoinInstruction` | stejně jako u composite klíčů – nový požadavek, ne rozpracovaná věc z diplomky. Řeší se v `docs/design/001-query-model-and-composite-keys.md` |
| Automatická detekce zdrojového frameworku | uživatel dnes musí framework vybrat ručně | zmíněno v diplomce jako budoucí rozšíření |

## 3. Rozpracované návrhy

- [`docs/design/001-query-model-and-composite-keys.md`](./design/001-query-model-and-composite-keys.md) – podmínkový strom, kompozitní klíče, vícesloupcové vztahy (návrh hotový, implementace zatím ne).

## 4. Nápady z původního výzkumu bez vazby na aktuální práci

Nízká priorita, jen ať se neztratí – diplomka je zmiňuje jako možné směry rozšíření Advisoru, se kterými dnešní implementace vůbec nepočítá: zahrnutí více databázových backendů do optimalizačního prostoru (ne jen výběr ORM, ale i výběr DB – PostgreSQL, MySQL, NoSQL), a modelování redundance/replikace dat (směrování dotazů na replikovaná/denormalizovaná úložiště). Žádné z toho není potřeba řešit teď, ale stojí za zapsání, kdyby se k tomu jednou vracelo.
