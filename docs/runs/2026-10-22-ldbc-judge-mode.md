# Doba přehrání soudce LDBC v režimu soudce

- **Začátek:** 2026-10-22
- **Strom:** `1bc69c3` a změna rozh. [165](../decisions/165-a-cell-warms-up-by-one-cold-execution.md) a [166](../decisions/166-the-judge-reads-a-segment-between-inserts-in-every-framework-at-once.md)
- **Stroj a prostředí:** MIS3, `mssql_db` v režimu soudce (rozh. [164](../decisions/164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md) a 166) se všemi procesory VM (čtyřmi); před každým během prázdná cache plánů a data v buffer poolu; turbo zapnuté (nejvyšší stav procesoru `0x64`, nejnižší `0x05`)
- **Co běželo:** prvních 250 řádků SF 1. *Postupné* je přehrání před rozh. 166, *souběžné* přehrání podle něj.

## Výsledek

Každý běh prošel všemi 90 testy .NET a 82 javovými.

| Sada | Přehrání | MAXDOP | Doba přehrání | Select jednoho frameworku přes 233 čtení |
|---|---|---|---|---|
| .NET | postupné | 0 | 3,6 min | 58–60 s |
| .NET | postupné | 1 | 6,8 min | 71–74 s; EF Core 225 s (IC 14 ve špatném plánu) |
| .NET | souběžné | 0 | 2,9 a 3,2 min | 130–150 s |
| .NET | souběžné | 1 | 2,8 a 2,6 min | 118–128 s |
| .NET | souběžné | 2 | 3,1 a 3,1 min | 131–147 s |
| Java | postupné | 0 | 3,4 min | 58–62 s |
| Java | souběžné | 1 | 2,6 min | 120–123 s |
| Java | souběžné | 0 | 2,8 min | 135–141 s |

## Poznámky

- Tři postupné běhy .NET s MAXDOP 0 mimo tento protokol tabulka vynechává: hned po přepnutí, se studeným buffer poolem, 4,2 min; další, nad plány, které první nechal v cache, 9,4 min, IC 14 v EF Core 27,6 s na čtení; a jeden pořízený, zatímco hostitel na pozadí stahoval, 5,6 min.
- Proti 7,2 a 6,7 min měřicího režimu ([2026-10-18](2026-10-18-ldbc-judge-time-by-step.md)) je režim soudce 2,6–2,8krát rychlejší; celou sadu je třeba čekat zhruba den na sadu, déle tam, kde IC 14 v EF Core drží špatný plán přes dlouhý úsek (v některých bězích 1,0–2,5 s na čtení, v jiných 13,7–27,6 s).
