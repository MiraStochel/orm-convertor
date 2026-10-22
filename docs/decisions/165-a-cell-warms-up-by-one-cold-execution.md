# 165 — Buňka se zahřívá jedním studeným spuštěním: zahřátí do shody na 10 % mizí, měří se dál nejméně třikrát a nejméně 1 000 ms a vykazuje se medián; v běhu FIBEN je studeným spuštěním čtení, které dalo řádky buňky

Datum: 2026-10-22
Stav: platí; nahrazuje zahřátí do shody v odstavci *Iterační politika* rozh. 145, v odstavci *Smyčka* rozh. 146 a v mezi čekání rodiče rozh. 147
Požadavky: T2, T3, T7, F15, S6
Podklad: rozhodnutí [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md) (zafixované prostředí, iterační politika, studený start jen v prvním spuštění), [146](146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md) (javová větev touž smyčkou), [147](147-a-cell-is-a-process-and-reports-the-cost-of-the-first-result-beside-the-steady-state.md) (první spuštění se vykazuje zvlášť), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md) (běh FIBEN měří politikou 145), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md); soubory `cells.tsv` běhů FIBEN z 2026-10-20 (.NET nad `9f6082d`, Java tamtéž) a z 2026-10-22 (.NET nad `a2f498a`), záznamy v `ORMConvertor/README.md`, *The FIBEN run*

## Kontext

Politika rozh. 145 má dvě fáze. Zahřátí běží, dokud se dvě po sobě jdoucí spuštění neshodnou na 10 % pomalejšího, nejvýš pětkrát; měření běží, dokud úhrn nedosáhne 1 000 ms a nejméně třikrát, a vykazuje se medián. Rozh. 145 politiku vyslovilo dřív, než existoval běh, na kterém by se dala posoudit; dnes jsou tři celé běhy FIBEN se zaznamenaným počtem zahřívacích a měřených spuštění u každé buňky, a ukazují, kam čas jde a co která fáze rozhoduje.

Rozklad běhu .NET z 2026-10-22 (103 min, 711 změřených buněk; časy zahřátí a měření odhadnuté jako počet spuštění krát medián buňky):

| Část | Čas |
|---|---|
| první čtení (to, které dalo řádky buňky pro srovnání) | 18 min |
| zahřátí | asi 31 min |
| měřená spuštění | asi 54 min |
| z toho šest buněk nad 10 s (q205 a q233 ve třech frameworcích), všechny tři části | asi 80 min |

**Co zahřátí rozhoduje.** Přes minimum dvou spuštění se zahřátí protáhlo u 128 ze 711 buněk:

| Medián buňky | Buněk | Zahřátí delší než 2 | Zahřátí až do stropu 5 |
|---|---|---|---|
| pod 1 ms | 19 | 19 | 4 |
| 1–10 ms | 127 | 68 | 4 |
| 10–100 ms | 373 | 33 | 2 |
| 0,1–1 s | 148 | 3 | 0 |
| nad 1 s | 44 | 5 | 0 |

Běh z 2026-10-20 dává týž obraz (pod 10 ms 95 ze 130, nad 1 s 4 z 57). Shoda na 10 % tedy rozhoduje skoro jen u krátkých buněk, kde je 10 % pod úrovní šumu jednoho spuštění a kde zahřátí nic nestojí; u dlouhých buněk, kde stojí dvě celá spuštění, skončí téměř vždy na minimu. Studený je přitom opravdu jen první běh: rozh. 145 to změřilo v zafixovaném prostředí na katalogu LDBC, a q233 to ukazuje i u dotazu, kde server mezi prvním a druhým spuštěním plán sám přizpůsobí — první čtení 144, 142 a 121 s ve třech frameworcích, každé další 88–89 s, už to druhé.

**Co stojí tři měřená spuštění.** Nejméně tři platí v praxi jen dlouhé buňky; buňky nad 10 s nesou asi 70 % měřeného času. U nich se frameworky liší kolem 1 % (q205: 167,8, 169,1 a 170,3 s), méně než je rozptyl jednoho prostředí mezi průchody (pod 5 %, rozh. 145), takže tři spuštění rozdíl nerozliší o nic lépe než jedno — kupují jen ochranu před výpadkem.

Odhad celého běhu podle variant níž, z týchž souborů (pro dnešní politiku dává odhad 103, 127 a 157 min, skutečné běhy trvaly 103, 129 a 156 min):

| Varianta | .NET 2026-10-22 | .NET 2026-10-20 | Java 2026-10-20 |
|---|---|---|---|
| 1 — dnešní politika | 103 min | 127 min | 157 min |
| 2 — čtení řádků jako první zahřátí | 88 min | 108 min | 133 min |
| 3 — zahřátí do shody, shodné spuštění otevírá měřenou řadu | 72 min | 88 min | 108 min |
| 4 — jedno studené spuštění | 72 min | 88 min | 108 min |
| 5 — jako 4 a měřená řada nejvýš 60 s | 46 min | 57 min | 66 min |

## Zvažované varianty

### 1 — Ponechat politiku rozh. 145

Zamítáme. Zahřátí do shody platí u každé dlouhé buňky dvě celá spuštění a téměř nikdy nerozhodne nic jiného než minimum; tam, kde rozhoduje, reaguje na šum krátkých spuštění. Běh FIBEN navíc každou buňku jednou spustí už před měřením, aby získal její řádky, a politika toto spuštění nezná, takže dlouhá buňka běží šestkrát.

### 2 — Čtení, které dalo řádky, započítat jako první zahřátí, jinak beze změny

Zamítáme. Odstraní dvojí studený start v běhu FIBEN, ale nechá pravidlo, které podle souborů výš nerozlišuje, co rozlišovat má; Advisor, který žádné čtení řádků nemá, by z ní nic nezískal.

### 3 — Zahřátí do shody ponechat a druhé ze shodných spuštění započítat do měřené řady

Zamítáme. Stojí stejně jako varianta 4 a ponechává test ustálení. Jenže test, který podle souborů téměř nikdy nerozhodne u dlouhých buněk a u krátkých reaguje na šum, není ochrana, kterou bychom potřebovali, a spuštění vybrané do řady proto, že se shodlo s předchozím, je řadě podmíněně přidané — měřená řada by už nebyla řadou spuštění po sobě, ale řadou, jejíž první člen prošel filtrem.

### 4 — Jedno studené spuštění, pak měřená řada beze změny

### 5 — Jako 4, a měřená řada končí také po 60 s, nejméně po jednom spuštění

Zamítáme. Ušetří dalších 26 až 42 min na běh, ale zavádí druhý režim téhož pravidla — dlouhý dotaz změřený jednou —, a jedno spuštění výpadek nepozná, což byl důvod nejméně tří už v rozh. 145. Úspora navíc padá skoro celá na dva dotazy katalogu FIBEN, ne na politiku.

## Rozhodnutí

**Volíme variantu 4.**

Buňka se zahřívá **jedním studeným spuštěním**, které medián nevidí (`BenchmarkPolicy.WarmupIterations` = 1). Pak běží měřená spuštění, dokud úhrn nedosáhne 1 000 ms a nejméně třikrát, a vykazuje se medián doby jednoho spuštění — beze změny proti rozh. 145. Tolerance shody a strop zahřátí z politiky mizí; politika má tři hodnoty a mez času rozh. 146.

V **běhu FIBEN** je studeným spuštěním buňky čtení, které dalo její řádky pro srovnání: je to první spuštění téhož artefaktu v témž procesu nad týmiž daty a stalo se těsně před měřením. Měření buňky tedy začíná rovnou měřenou řadou; počet zahřívacích spuštění v `cells.tsv` je 1 a znamená právě toto čtení. **Advisor** a javová větev rozh. 146 provedou studené spuštění samy, jako první v buňce; rozh. 147 ho vykazuje jako `FirstExecutionMilliseconds`, a definice se tím zpřesňuje: první spuštění je celé zahřátí, ne jeho první část.

### Proč jedno studené spuštění stačí

Rozh. 145 změřilo, že se v zafixovaném prostředí studený start nerozkládá přes průchod, ale sedí v prvním spuštění každého dotazu: stránky dotazu z disku, překlad plánu, první práce frameworku. q233 v běhu FIBEN ukazuje totéž i u dotazu, jehož plán server po prvním spuštění sám upraví: druhé spuštění je už ustálené. Medián nejméně tří spuštění přitom unese jedno odchýlené spuštění, takže i dotaz, který by se ustálil až druhým spuštěním, dá ustálené číslo. Test shody na 10 % nic z toho nepřidává: u dlouhých buněk skoro vždy projde napoprvé a u krátkých, kde se protahuje, měří šum jednotlivého spuštění, který medián z desítek až stovek spuštění pohltí sám.

### Proč nejméně tři zůstávají

Tři je nejmenší počet, pro který existuje medián, a jediné spuštění nepozná výpadek. Že tři spuštění dlouhého dotazu nerozliší jednoprocentní rozdíl frameworků, je pravda, ale není to argument pro jedno: rozdíl pod šumem prostředí nerozliší žádný počet spuštění v jednom průchodu, a ILP ho dostane jako to, čím je — jako shodu. Nejméně tři chrání číslo před jedním chybným spuštěním, a to je vlastnost, kterou vykázané číslo mít musí.

## Důsledky

- **Kód:** `BenchmarkPolicy` (`WarmupIterations` místo `WarmupTolerance` a `MaxWarmupIterations`), `BenchmarkExecutor` (jedno studené spuštění), `FibenJudge/FibenRun` a `fiben/FibenRun` (měření bez vlastního zahřátí, konstanty v Javě, `FibenRunTest.cs` je drží rovné). Odpověď `/advisor/run` nese v `policy` pole `warmupIterations` místo `warmupTolerance` a `maxWarmupIterations`; změnu rozhraní jmenuje anotace příštího vydání (rozh. 098).
- **Rozh. 147:** rodič čeká nejdéle $(\text{WarmupIterations} + \text{MinIterations}) \cdot \text{ExecutionTimeoutSeconds}$ plus cílový úhrn.
- **Dokumentace:** `architecture.md` §8.
- **Čísla:** běhy po tomto rozhodnutí se od dřívějších liší jen počtem spuštění před měřenou řadou, ne řadou samou; zda to pohne mediánem, řekne srovnání prvního běhu FIBEN s během z 2026-10-22.
