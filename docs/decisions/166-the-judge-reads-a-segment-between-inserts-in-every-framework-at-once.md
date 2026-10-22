# 166 — Soudce LDBC čte úsek mezi dvěma vkládáními ve všech frameworcích sady najednou, každý framework ve vlastním vlákně a v pořadí řádků; režim soudce plní procesory souběžnými sériovými dotazy a běh FIBEN pozná měřicí režim podle MAXDOP i podle vlastních procesorů databáze

Datum: 2026-10-22
Stav: platí; nahrazuje `MAXDOP` 0 režimu soudce a poznání měřicího režimu samotným `MAXDOP` v běhu FIBEN rozh. 164
Požadavky: T2, T3, F13, S5, S6
Podklad: rozhodnutí [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) (přehrání v pořadí: čtení na pozici *k* vidí vkládání 1 až *k* − 1 a žádné pozdější), [164](164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md) (dva režimy serveru), [145](145-the-measurement-environment-and-the-iteration-policy-are-fixed.md) (měřicí prostředí), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md); záznam *Time by step* z 2026-10-18 v `ORMConvertor/README.md` (prefix 250 řádků SF 1 v měřicím režimu: .NET 7,2 min, Java 6,7 min); měření na MIS3 z 2026-10-22 nad `1bc69c3` a pracovním stromem tohoto rozhodnutí (níž)

## Kontext

Obě sady přehrávají validační sadu přísně za sebou: řádek po řádku, a v řádku se čtením framework po frameworku (`LdbcReplay.cs`, `LdbcReplay.java`). Na serveru tak v každém okamžiku běží jediný dotaz. Rozh. 164 dalo soudci všechny procesory VM a `MAXDOP` 0, takže víc než jeden procesor mohl dotaz použít jen paralelním plánem, a ten server volí jen nad prahem ceny. Rozh. 117 přitom řazení vyžaduje jen vůči vkládáním: čtení na pozici *k* vidí vkládání 1 až *k* − 1 a žádné pozdější, takže všechna čtení mezi dvěma vkládáními vidí týž stav a na pořadí mezi sebou nezávisí. Artefakty jednotlivých frameworků nesdílejí nic měnitelného: EF Core má vlastní kontext v artefaktu, Dapper, NHibernate, Hibernate, EclipseLink i MyBatis si na každé čtení otevírají vlastní spojení, sezení nebo `EntityManager`.

Měření z 2026-10-22 na MIS3: prefix 250 řádků SF 1 (233 čtení, 17 vkládání) proti `mssql_db` v režimu soudce, ze sítě, s turbem, před každým během vyprázdněná cache plánů a data v buffer poolu; „souběžně" je přehrání tohoto rozhodnutí. Všech 90 testů .NET a 82 javových prošlo v každém běhu.

| Sada | Přehrání | `MAXDOP` | Čas přehrání | Select jednoho frameworku přes 233 čtení |
|---|---|---|---|---|
| .NET | v měřicím režimu, 2026-10-18 | 1 | 7,2 min | 117–132 s |
| .NET | sekvenčně | 0 | 3,6 min | 58–60 s |
| .NET | sekvenčně | 1 | 6,8 min | 71–74 s; EF Core 225 s (IC 14 ve špatném plánu) |
| .NET | souběžně | 0 | 2,9 a 3,2 min | 130–150 s |
| .NET | souběžně | **1** | **2,8 a 2,6 min** | **118–128 s** |
| .NET | souběžně | 2 | 3,1 a 3,1 min | 131–147 s |
| Java | v měřicím režimu, 2026-10-18 | 1 | 6,7 min | 117–122 s |
| Java | sekvenčně | 0 | 3,4 min | 58–62 s |
| Java | souběžně | **1** | **2,6 min** | **120–123 s** |
| Java | souběžně | 0 | 2,8 min | 135–141 s |

Ve sloupci selectu je vidět, proč souběh nedá trojnásobek: tři souběžné frameworky čtou každý zhruba dvakrát déle, protože čtyři procesory VM jsou plné. Je vidět i to, proč `MAXDOP` 1: při souběhu čte každý framework o 10–15 % rychleji než s paralelními plány, které se s ostatními dotazy dělí o tatáž jádra. A je vidět ještě jedna věc, kterou žádné nastavení neřeší: čtení IC 14 v EF Core je v některých bězích 1,0–2,5 s, v jiných 13,7–27,6 s, podle toho, s jakými parametry server text přeloží; záznam z 2026-10-18 vidí totéž (43 s). Tři sekvenční běhy s `MAXDOP` 0 mimo tento postup v tabulce nejsou: první po přepnutí se studeným buffer poolem (4,2 min), druhý nad plány, které v cache nechal první (9,4 min, IC 14 v EF Core 27,6 s na čtení), a jeden, během něhož hostitel na pozadí stahoval (5,6 min).

## Zvažované varianty

### 1 — Přehrávat dál za sebou

Zamítáme. Sekvenční přehrání nechává server na jednom dotazu, a v režimu soudce je o čtvrtinu pomalejší než souběžné (.NET 3,6 proti 2,6–2,8 min, Java 3,4 proti 2,6 min).

### 2 — Čtení úseku rozdělit mezi libovolný počet vláken bez ohledu na framework

Zamítáme. Artefakt by běžel z více vláken, a kontext EF Core souběžné použití nesnese; každé vlákno by potřebovalo vlastní instanci každého artefaktu, tedy kompilaci a továrnu navíc. Zisk by na dnešní VM nebyl: tři souběžné frameworky už čtyři procesory zaplní.

### 3 — Obě sady souběžně, každá nad vlastní kopií `LdbcSnb`

Zamítáme. Druhá kopie SF 1 přidá 2,2 GB dat, která se s první do poolu 4 096 MB nevejdou, a loader, svazek i instanci aplikace navíc. Procesory, o které by se sady dělily, jsou souběhem uvnitř sady už plné.

### 4 — Každý framework úsek ve vlastním vlákně, další vkládání čeká, až všechny dočtou

### 5 — Jako 4, s `MAXDOP` 0 rozh. 164

Zamítáme. Při souběhu čte každý framework o 10–15 % déle než se sériovými plány a přehrání trvá déle (2,9–3,2 proti 2,6–2,8 min), protože paralelní plán jednoho dotazu bere jádra ostatním. `MAXDOP` 0 by jen udržel jednoduché poznání měřicího režimu v běhu FIBEN, a to je kontrola, ne důvod.

## Rozhodnutí

**Volíme variantu 4, s `MAXDOP` 1 v režimu soudce.**

**Přehrání.** Soudce sbírá čtení až k dalšímu vkládání. Před vkládáním a na konci sady přečte úsek každý framework sady ve vlastním vlákně, v pořadí řádků, se svými artefakty a se svými mutacemi IC 2 (rozh. 089); vkládání pak běží řídicím spojením jako dosud. Artefakt tak nikdy neběží ze dvou vláken a to, co si framework zapíše jako první neshodu nebo jako první zachycení mutace, je to, co přečetl první — výsledek přehrání je týž jako za sebou. Vkládání, kompenzace, zámek i počty řádků se nemění.

**Režim soudce** (`judge.env`) má `MAXDOP` 1: všechny procesory VM plní souběžné sériové dotazy tří frameworků, žádný z nich jiným nebere jádra, a soudce navíc ověřuje plány téhož tvaru, jaký měřicí režim měří. Od měřicího režimu se tak liší procesory databáze (všechny procesory VM proti vlastním dvěma), turbem a souběhem.

**Běh FIBEN pozná měřicí režim podle obojího.** S `MAXDOP` 1 v obou režimech už samotný `MAXDOP` měřicí režim neodliší. Běh FIBEN proto měří, jen má-li server `MAXDOP` 1 a právě tolik procesorů, kolik mu výchozí `cpuset` compose dává v měřicím režimu (`FibenServer.MeasurementProcessors` = 2; `FibenRunTest` drží rovnost s `docker-compose.yml` i s javovým `FibenRun`). Server, který počet procesorů neřekne (přihlášení bez `VIEW SERVER STATE`), měření také vynechá. Kontrola je tím úplnější než v rozh. 164: hlídá dvě vrstvy měřicího prostředí rozh. 145 místo jedné.

### Proč jedno vlákno na framework

Je to největší souběh, který artefakty unesou beze změny, a na dnešní VM také největší, který se vyplatí. Souběh má přirozenou hranici — tři frameworky sady — a pořadí čtení uvnitř frameworku zůstává pořadím sady, takže zápisy soudce jsou deterministické a srovnatelné se záznamy sekvenčních běhů. Bariéra u vkládání je přesně požadavek rozh. 117 a nic víc: 7 041 vkládání celé sady SF 1 dělí 131 706 čtení na úseky průměrně po devatenácti.

### Co souběh stojí

Doba přehrání je součtem nejdelšího frameworku každého úseku, ne součtem frameworků. Framework, který je v nějakém úseku řádově pomalejší — dnes IC 14 v EF Core ve špatném plánu —, nechá ostatní čekat. Časy selectu, které soudce vypisuje, jsou časy pod zátěží ostatních frameworků; se záznamy sekvenčních běhů se nesrovnávají, srovnává se doba přehrání.

## Důsledky

- **Kód:** `LdbcJudge/LdbcReplay` (`ReadSegment`, `Catch` po frameworcích) a `ldbc/LdbcReplay` (`readSegment`, fond vláken podle počtu frameworků); `FibenServer.MeasurementLeftOut` a `FibenRun.Server.measurementLeftOut` s procesory, `MeasurementProcessors` a test proti compose.
- **Režim soudce:** `judge.env` `DATABASE_MAXDOP` = 1; rozh. 164 v části o `MAXDOP` 0 a o poznání měřicího režimu tím přestává platit.
- **Dokumentace:** `architecture.md` §6.2 a §8, `ORMConvertor/README.md` (*Docker*, *The LDBC judge*, záznam *Time by step*).
- **Čas celé sady:** podle prefixu 250 řádků (2,6–2,8 proti 7,2 min v měřicím režimu) se dá čekat zhruba den na sadu místo projektovaných 2,5–5 dní; delší, pokud IC 14 v EF Core chytí špatný plán na dlouhý úsek. Doloží to první běh.
