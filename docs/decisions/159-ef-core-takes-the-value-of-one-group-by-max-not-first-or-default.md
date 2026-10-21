# 159 — Hodnotu jediné skupiny korelovaného poddotazu bere cíl EF Core voláním `Max()`, ne `FirstOrDefault()`: přes `TOP(1)` SQL Server poddotaz nerozplete a jeho joiny opakuje pro každý vnější řádek

Datum: 2026-10-21
Stav: platí; nahrazuje zakončení `FirstOrDefault()` první odrážky *Rozhodnutí* a `TOP(1)` důsledku *Cena* rozh. 155
Požadavky: F15, T7, T3, F13, S1
Podklad: rozhodnutí [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) (seznam nad korelovaným poddotazem, úniková cesta), [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), [146](146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md), [155](155-sql-sum-in-ef-core-linq-is-the-projection-of-one-group-and-ef-core-sum-is-coalesce.md); článek JSS *A Unified Framework for Object-Relational Mapping Translation and Performance-Aware Selection*, oddíly 3.2 a 4.5; běh katalogu FIBEN z 2026-10-19 (záznam *The FIBEN run* v `ORMConvertor/README.md`); měření 2026-10-21 na MIS2: EF Core 10.0.10 přes `ToQueryString()` (`EFCore/EFCoreSumProviderTest`, `Combined/ExpressionVocabularyTest`) a SQL Server 2025 Express 17.0.1000.7 nad `Fiben` z `data.zip` commitu `96eaffc`, `MAXDOP 1`

## Kontext

Rozhodnutí 155 píše `SUM` ve skalárním poddotazu v cíli EF Core jako projekci nad jedinou skupinou: řetěz seskupený konstantou, součet té skupiny zvednutý do nullovatelného typu a `FirstOrDefault()`. Tutéž formu dostal od implementace rozhodnutí 113 seznam (`STRING_AGG`) nad korelovaným poddotazem; text 113 zakončení nejmenuje. Výsledek je správný: nad žádným řádkem skupina není a poddotaz dá `NULL` jako SQL.

Běh katalogu FIBEN z 2026-10-19 ukázal cenu. Šest dotazů typu JA (q207, q211, q213, q215, q216, q225) porovnává v `HAVING` součet skupiny s korelovaným součtem jiného druhu řádků. V EF Core běží 7,7–35 s, text a všechny ostatní cíle 0,07–0,1 s, a součet medianů EF Core nad katalogem stoupl z 329 s (běh z 2026-10-18, před 155) na 476 s, z nichž 122 s připadá na těchto šest buněk.

EF Core 10.0.10 píše `FirstOrDefault()` nad formou jediné skupiny jako `TOP(1)`:

```sql
HAVING CASE WHEN COUNT(CASE WHEN [h].[HASCOUNT] IS NOT NULL THEN 1 END) > 0 THEN COALESCE(SUM([h].[HASCOUNT]), 0.0E0) END > (
    SELECT TOP(1) CASE WHEN COUNT(…) > 0 THEN COALESCE(SUM([s].[HASCOUNT]), 0.0E0) END
    FROM (SELECT [h0].[HASCOUNT], 1 AS [Key] FROM [HOLDING] AS [h0] INNER JOIN … WHERE … AND <korelace>) AS [s]
    GROUP BY [s].[Key])
```

Přes `TOP(1)` SQL Server korelovaný poddotaz nerozplete do joinu a spojení čtyř vnitřních tabulek spouští znovu pro každou vnější skupinu. Změřeno nad q207 (`SET STATISTICS IO`): HOLDING se čte 214krát a 917 846 logickými čteními, v textu dvakrát a 8 578 čteními.

Pro rozhodnutí je podstatné, které tvary SQL Server rozplete. Změřeno nad q207 přes `sqlcmd` (časy včetně startu klienta, druhé ze dvou spuštění):

| Tvar poddotazu v `HAVING` | Čas | Řádky |
|---|---|---|
| text katalogu, holé `SUM` | 0,24 s | 1 |
| forma 155: `TOP(1)` nad skupinou, korelace se stráží `NULL` od EF Core | 16,8 s | 1 |
| totéž s korelací jen rovností | 12,5 s | 1 |
| `MAX` nad odvozenou tabulkou skupiny, s korelací od EF Core i bez ní | 0,15 s | 1 |
| skalární `CASE WHEN COUNT(x) > 0 THEN SUM(x) END` bez seskupení | 13,3 s | 1 |
| stráž `Any()` rozhodnutí 155: `CASE WHEN EXISTS (…) THEN (SELECT COALESCE(SUM(x), 0) …) END` | 13,0 s | 1 |
| holé `Sum` EF Core, `COALESCE(SUM(x), 0)` | 0,15 s | 211, jiné |

Totéž platí mimo `HAVING`: `SUM` jediné skupiny se čtyřmi joiny v projekci 19,4 s ve formě `TOP(1)` proti 0,24 s ve formě `MAX`. Korelovaný součet s joinem ve `WHERE` nad tisícem vnějších řádků vychází 79,8 s proti 0,14 s. Seznam nad jedinou tabulkou v projekci SQL Server rozplete ve všech třech tvarech (0,21 s `TOP(1)`, 0,27 s `MAX`, 0,30 s text). Seznam s joinem v poddotazu nerozplete v žádném z nich, text nevyjímaje (15,2 s, 14,3 s, 15,9 s): to je vlastnost `STRING_AGG … WITHIN GROUP` v SQL Serveru, ne překladu.

EF Core 10.0.10 píše `….GroupBy(x => 1).Select(g => v).Max()` jako `(SELECT MAX([o1].[c]) FROM (SELECT v AS [c] FROM (…) AS [o0] GROUP BY [o0].[Key]) AS [o1])`, bez `TOP` a bez `COALESCE` kolem. Platí to v predikátu, v projekci, v `HAVING`, se stráží počtu hodnot uvnitř skupiny i pro seznam (`STRING_AGG` v odvozené tabulce). `Min()` dává týž tvar s `MIN`. Nad šesti dotazy FIBEN spuštěnými přes EF Core proti týmž datům vrací forma `Max()` tytéž řádky jako forma `FirstOrDefault()` (a jako text) a běží 0,11–0,13 s místo 5,1–23,6 s:

| Dotaz | `FirstOrDefault()` | `Max()` | Řádky |
|---|---|---|---|
| q207 | 13,3 s | 0,13 s | 1, shodné |
| q211 | 5,1 s | 0,13 s | 36, shodné |
| q213 | 5,1 s | 0,11 s | 0 |
| q215 | 23,6 s | 0,13 s | 1, shodné |
| q216 | 23,0 s | 0,13 s | 1, shodné |
| q225 | 10,8 s | 0,13 s | 0 |

MIS2 s SQL Serverem Express není prostředí měření požadavků T (rozhodnutí 145); řád rozdílu i jeho příčina jsou ale tytéž jako v záznamu běhu na MIS3, a o rozhodnutí rozhoduje řád a příčina, ne přesné číslo.

## Zvažované varianty

1. **Nechat formu rozhodnutí 155.** Řádky jsou správné a článek v oddíle 3.2 výslovně neslibuje, že překlad zachová plán nebo výkon. Zamítáme. Oddíl 3.2 zbavuje překladač povinnosti plán předpovídat, ne práva zvolit mezi dvěma stejně věrnými formami tu, o které změřeně víme, že je o dva řády pomalejší. Advisor (F15, oddíl 4.5 článku) plní matici `cost(q, f)` během přeložené formy dotazu ve frameworku `f`: kdyby forma zůstala, připsal by EF Core cenu, která patří volbě překladače, a srovnání ILP s jednotlivými frameworky (T7) by měřilo totéž zkreslení. Cena navíc roste se součinem počtu vnějších skupin a velikosti vnitřních joinů, takže nad větším katalogem by buňka narazila na mez jednoho spuštění rozhodnutí 146 a z běhu, který jinak projde, by se stal výstup, který neprojde (T3).

2. **Nativní SQL (rozhodnutí 113) pro každý `SUM` v korelovaném poddotazu.** Text by byl rychlý, ale úniková cesta je podle 113 pro to, co jazyk cíle nevysloví, a tady ho vysloví (varianta 5). Podíl dotazů v jazyce cíle, který T2 a T3 vykazují zvlášť od nativního SQL, by klesl u běžného tvaru výkazových dotazů. Zamítáme.

3. **Stráž `Any()`**, varianta 3 rozhodnutí 155. Rozhodnutí 155 ji zamítlo úvahou o dvou průchodech. Teď je změřená a pomalá jako forma, kterou nahrazujeme (13,0 s), protože poddotaz uvnitř větve `CASE` SQL Server také nerozplete. Zamítáme.

4. **Úprava korelace nebo ruční dekorelace.** Stráž `NULL`, kterou EF Core ke korelaci `g.Key == x` přidává, stojí ve formě `TOP(1)` asi čtvrtinu času (16,8 s proti 12,5 s) a ve formě `MAX` nic; příčinou není. Přepis korelovaného poddotazu na join se seskupenou odvozenou tabulkou by měnil strukturu dotazu, ne jeho formu. Platil by jen pro korelaci rovností a v projekci by potřeboval levý join. Po variantě 5 ho není třeba. Zamítáme.

5. **`Max()` místo `FirstOrDefault()`.** Maximum přes nejvýš jednu skupinu je hodnota té skupiny a nad žádnou skupinou je `NULL`, tedy přesně sémantika, kterou forma 155 nese. Provider píše agregát nad odvozenou tabulkou, který SQL Server rozplete. Tuhle volíme.

## Rozhodnutí

**Volíme variantu 5. Forma jediné skupiny skalárního poddotazu — `SUM` pod providerem, který `Sum` koalescuje (rozhodnutí 155), i seznam nad korelovaným poddotazem (rozhodnutí 113) — končí voláním `Max()`: `….GroupBy(x => 1).Select(g => hodnota).Max()`.** Všechno ostatní z rozhodnutí 155 platí, jak je: seskupení konstantou, zvednutí součtu, stráž počtu hodnot, `COALESCE(SUM(x), 0)` jako holý `Sum` i háček `CoalescesSum`.

- **`Max()`, ne `Min()`.** Oba tvary jsou si rovné. Zapisovač píše jeden, aby forma byla jedna.
- **Zvednutí součtu zůstává.** Pod `Max()` EF Core poddotaz nekoalescuje ani bez zvednutí (změřeno), takže důvod, který pro zvednutí uvedlo rozhodnutí 155, odpadá. Hodnota poddotazu je ale nad žádnou skupinou `NULL` a v projekci ji musí nést typ, který `NULL` unese; `int` z nezvednutého `g.Sum(…)` by ji nenesl.
- **Čtečka LINQ** čte formu jediné skupiny zakončenou `Max()` i `FirstOrDefault()` jako týž poddotaz. `FirstOrDefault()` je forma, kterou nástroj psal do tohoto rozhodnutí a kterou může napsat i člověk, a vrací tytéž řádky. Formu zakončenou `Min()` nástroj nepíše a čtečka ji jako formu jediné skupiny nečte.
- **NHibernate se nemění.** Jeho provider `Sum` nekoalescuje, takže formu jediné skupiny pro `SUM` nepíše. Seznam v HQL 5.7 nevysloví, takže druhý tvar rozhodnutí 118 nad seznamem nevznikne.

## Důsledky

- **Kód.** `AbstractLinqQueryBuilder.OverOneGroup` končí `.Max()`; `LinqQueryParser.ReadOneGroupSubQuery` bere `Max` i `FirstOrDefault` bez argumentu. Model, `AbstractWrappers` ani orchestrace se nemění (S1).
- **Testy.** Fakta providera drží `EFCore/EFCoreSumProviderTest`: forma `Max()` v predikátu, v projekci, v `HAVING` a se stráží je `MAX` nad odvozenou tabulkou skupiny bez `TOP`. Pro seznam drží totéž `Combined/ExpressionVocabularyTest` nad kategorií `ListAggregationOverACorrelatedSubquery`, kterou čtvrtý stupeň spouští. Čtvrtý stupeň `SUM` drží `Combined/SumOverNoValuesTest`, nově i s projekcí poddotazu bez řádků, a zpětné čtení obou zakončení drží tytéž dva testy. To, že SQL Server rozplete formu `MAX` a formu `TOP(1)` ne, test nedrží. Plán závisí na datech a statistikách, ne na verzi frameworku, takže ho drží běh katalogu FIBEN (rozhodnutí 149), který měří každou buňku.
- **Cena.** SQL EF Core má u formy jediné skupiny o jednu odvozenou tabulku víc než s `FirstOrDefault()`. Ve změřených případech není pomalejší nikde; tam, kde `TOP(1)` rozplétání bránilo, je rychlejší o dva řády.
- **Záznam.** Příští běh katalogu FIBEN na MIS3 zaznamená čas šesti buněk v prostředí rozhodnutí 145.
- **Dokumentace.** `architecture.md` §5 (poddotaz, agregace do seznamu); položka `open-items.md` o formě `SUM` v korelovaném `HAVING` zaniká.
