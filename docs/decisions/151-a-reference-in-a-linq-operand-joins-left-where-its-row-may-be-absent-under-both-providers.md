# 151 — Navigace přes referenci v operandu LINQ je levý join, smí-li řádek reference chybět, u obou providerů stejně; řádek za volitelným řádkem je volitelný

Datum: 2026-10-18
Stav: platí; nahrazuje odstavec *Každá jiná jednohodnotová asociace na cestě je join odvozený ze vztahu, jehož druh dá provider* a důsledek *Jiný provider, jiný join nad týmž textem* rozh. 148
Požadavky: F10, F11, S1, S2, T1, T2
Podklad: rozhodnutí [026](026-home-of-shared-query-reading.md) (sdílený parser LINQ s háčky provideru), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [065](065-row-set-as-the-boundary-of-rule-053.md), [070](070-a-parser-refuses-what-would-change-the-row-set.md), [118](118-nhibernate-writes-a-linq-form-beside-its-hql.md), [148](148-a-reference-in-a-linq-operand-is-an-implicit-join-whose-kind-the-provider-takes-from-the-nullability-of-the-foreign-key.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md); měření 2026-10-18 proti zafixovaným verzím: provider LINQ NHibernate 5.7.0 (`NHibernate/NHibernateLinqProviderTest`, *TheProviderJoinsANavigation*) a EF Core 10.0.10 (`EFCore/EFCoreNavigationJoinProviderTest`); dokumentace NHibernate 5.7.0 k `NhJoinClause` a `WhereJoinDetector` („All joins are created as outer joins. An optimization in WhereJoinDetector finds joins that may be inner joined"); revize celého repozitáře z [2026-10-18](../audits/2026-10-18-whole-repository-audit.md), kapitola 4

## Kontext

Rozhodnutí 148 dalo sdílenému parseru LINQ čtení navigace přes referenci v operandu (`o.Customer.Name`) mechanismem cesty rozhodnutí 144 a druh joinu, který z ní vznikne, svěřilo potomkovi za provider: EF Core odpovídá `LEFT JOIN` pro nulovatelný cizí klíč a `INNER JOIN` pro ostatní, potomek NHibernatu nechává základ — **vnitřní join vždy**, s odůvodněním, že provider NHibernatu navigaci překládá na implicitní join HQL, a ten je vnitřní. Na tom odůvodnění stálo i zamítnutí varianty 3 („levý join vždy"): „zdroj NHibernate, jehož provider volitelnou referenci vypouští".

Revize z 2026-10-18 to změřila proti zafixovanému provideru a **neplatí to.** NHibernate 5.7.0 navigaci v operandu do HQL nepřekládá jako implicitní join, nýbrž jako vlastní join, který vytvoří vnější a teprve optimalizace `WhereJoinDetector` zúží na vnitřní tam, kde to řádky nezmění. Nad mapováním s `<many-to-one>` bez `not-null`:

| Řetěz | SQL provideru |
|---|---|
| `Select(o => o.Customer.CustomerName)` | `left outer join Customers` |
| `Select(o => o.Customer.Region.RegionName)` | dvakrát `left outer join` |
| `OrderBy(o => o.Customer.CustomerName)` | `left outer join` |
| `GroupBy(o => o.Customer.CustomerName)` | `left outer join` |
| `Where(o => o.Customer.CustomerName == null)` | `left outer join` |
| `Where(o => o.Customer.CustomerName == "A" \|\| o.Total > 5)` | `left outer join` |
| `Where(o => o.Customer.CustomerName == "A")` | `inner join` |

Vnitřní join stojí jen ve filtru, který `NULL` splnit nemůže — tam vrátí tytéž řádky jako levý. Čtení podle 148 tedy u zdroje NHibernate s volitelnou referencí **tiše měnilo množinu řádků**: `session.Query<Order>().Select(o => o.Customer.CustomerName)` vrací objednávky bez zákazníka s `NULL`, nástroj z něj dělal vnitřní join, který je vypustí. Je to přesně třída selhání, kterou rozhodnutí 053 a 070 zakazují, a nebyla vidět: dotaz se přeložil, zkompiloval i spustil.

Druhá vada je společná oběma providerům a 148 ji nepředvídalo, protože o řetězu řeklo jen „řetěz je opakování kroku". Druh joinu se odvozoval jen z cizího klíče reference samé. Jenže reference, přes kterou se jde z řádku, který sám smí chybět, vede k řádku, který smí chybět také: v `o.Customer.Region.RegionName` nad volitelným zákazníkem a povinným regionem dával nástroj `LEFT JOIN Customers … INNER JOIN Regions`, a vnitřní join vypustil přesně ty objednávky, které levý join před ním zachoval. EF Core 10 to rozvine jako dva levé joiny (změřeno, `EFCoreNavigationJoinProviderTest`), NHibernate také. Totéž platí pro referenci z pravé strany levého joinu (`LeftJoin(…).Select(x => x.o.Customer.Name)`), z levé strany pravého a z obou stran plného.

## Zvažované varianty

1. **Ponechat 148.** Zamítáme: odpověď NHibernatu, na které stojí, je změřeně jiná, a čtení podle ní vrací jiné řádky než zdroj.

2. **Potomek NHibernatu odpovídá levým joinem vždy**, doslova jako jeho provider. Množina řádků by seděla, ale mezireprezentace téhož dotazu nad povinnou referencí by se mezi providery lišila (`LEFT` od NHibernatu, `INNER` od EF Core) bez rozdílu v řádcích — nad povinným cizím klíčem s integritou vrací vnitřní i levý join totéž. Každý z pěti cílů by od zdroje NHibernate dostal `LEFT JOIN`, který zdroj EF Core u téže reference nenapíše; to je přesně cena, kterou 148 uvedlo proti variantě 3. Zamítáme.

3. **Jedno pravidlo pro oba providery: levý join tam, kde řádek reference smí chybět, vnitřní jinde.** Řádek smí chybět, smí-li cizí klíč nést `NULL` (fakty a pořadí 148, inverzní reference vždy), nebo smí-li chybět řádek, z něhož se reference kříží — pravá strana levého joinu, levá strana pravého, obě strany plného a řádek levého implicitního joinu. Obě odpovědi providerů dávají přesně tytéž řádky: EF Core podle volitelnosti, NHibernate svým vnějším joinem, který nad povinnou referencí vrací totéž co vnitřní. Mezireprezentace téhož dotazu je od obou providerů táž.

## Rozhodnutí

**Volíme variantu 3. Druh implicitního joinu z navigace v operandu LINQ je `Left`, smí-li řádek reference chybět, jinak `Inner` — u EF Core i NHibernatu. Řádek reference smí chybět, smí-li její cizí klíč nést `NULL` podle faktů a pořadí rozhodnutí 148, nebo smí-li chybět řádek, z něhož se reference kříží; takový řádek je volitelný a volitelnost se nese po celém řetězu.** Háček `ImplicitJoinKind` zůstává, protože druh joinu je fakt provideru (026, S1): sdílené čtení mu dá jedinou otázku — smí řádek chybět? — a základní odpověď je ta, kterou dnes oba providery dávají; potomek, jehož provider odpoví jinak, ji přepíše.

Řádek entity v rozsahu nese příznak volitelnosti (`EntityRow.Optional`). Dostane ho pravá strana `LeftJoin` a korelovaného `SelectMany` s `DefaultIfEmpty()`, každý řádek v rozsahu pod `RightJoin` (a pod plným joinem obě strany) a řádek implicitního joinu, který vyšel levý; spojený řádek ho nese u svých členů, takže `x.o.Customer` ví, že `o` chybět smí.

Odstavec 148 *Každá jiná jednohodnotová asociace na cestě je join odvozený ze vztahu, jehož druh dá provider* platí dál ve všem kromě odpovědi NHibernatu a kromě toho, že druh nezávisí jen na cizím klíči reference; důsledek *Jiný provider, jiný join nad týmž textem* neplatí — týž text nad týmž mapováním dá od obou providerů týž join (150).

## Důsledky

- **Kód.** `LinqQueryParser.ImplicitJoinKind(bool optional)` má základ `optional ? Left : Inner` a přepis v `EFCoreLinqQueryParser` odpadá; `ImplicitJoin` se ptá `owner.Optional || ForeignKeyMayBeNull(…)`; `HandleJoin` a `HandleCorrelatedSelectMany` značí volitelné řádky. Model, `AbstractWrappers`, orchestrace ani builder se nemění (S1).
- **Testy.** Fakt provideru drží `NHibernate/NHibernateLinqProviderTest` (*TheProviderJoinsANavigation*: levý vnější join v projekci, řetězu, řazení, klíči seskupení, testu na `NULL` a disjunkci, vnitřní jen v porovnání s hodnotou) a `EFCore/EFCoreNavigationJoinProviderTest` (povinný řetěz dvakrát vnitřní, reference za volitelnou dvakrát levý). `Combined/LinqReferencePathTest`: nulovatelný cizí klíč dá `LEFT JOIN` a týž text pod oběma providery; reference za volitelnou referencí a z volitelné strany levého joinu joinuje levým joinem.
- **Test rozhodnutí 026** „týž dotaz v obou LINQ dá tutéž mezireprezentaci" platí i nad nulovatelným cizím klíčem.
- **Dokumentace.** `architecture.md` §5 (*Čtení LINQ*, tabulka konstrukcí), `subset.md` 2.6.
