# Soudce LDBC nad celou validační sadou SF 1 v režimu soudce

- **Začátek:** .NET 2026-11-02 22:56 UTC, Java 2026-11-04 04:31 UTC (konec 2026-11-05 07:42 UTC)
- **Strom:** `63d0b81`
- **Stroj a prostředí:** MIS3; kroky 7–9 úplného běhu ([metodika](README.md#pořadí-úplného-běhu)) po [běhu FIBEN](2026-11-02-fiben.md) nad týmž stromem, obrazy z jeho začátku; `mssql_db` znovu vytvořený v režimu soudce (rozh. [164](../decisions/164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md): všechny čtyři procesory virtuálního stroje, `MAXDOP` 1, 4096 MB) nad týmž svazkem, data i validační sada přeskočeny loaderem; čtení úseku ve všech frameworcích sady najednou (rozh. [166](../decisions/166-the-judge-reads-a-segment-between-inserts-in-every-framework-at-once.md)); javová sada proti instanci nad `LdbcSnb`. Nejvyšší i nejnižší stav procesoru `0x63` po celou dobu (turbo vypnuté — soudce není měření, stav jen zapisujeme).
- **Co běželo:** verdikt rozh. [117](../decisions/117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) — celá `validation_params-sf1`, 138 747 řádků, z toho 7 041 vkládání; všech 21 dotazů Interactive je `AsSpecified`, IC 13 a IC 14 poprvé nad celou sadou v textech rozh. [140](../decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) a [141](../decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md), souzené verdiktem, ne mírou

## Výsledek

| Sada | Testy | Podle specifikace (21 dotazů) | Přehrání | Select na čtení (průměr) |
|---|---|---|---|---|
| .NET: Dapper, EF Core, NHibernate | 90 prošlo, 0 selhalo, 0 přeskočeno | každé čtení ve shodě ve všech třech | 1 775,6 min (29,6 h), 78 řádků za minutu | Dapper 733 ms, EF Core 735 ms, NHibernate 677 ms |
| Java: Hibernate, EclipseLink, MyBatis | 82 prošlo, 0 selhalo, 0 přeskočeno | každé čtení ve shodě ve všech třech | 1 630,9 min (27,2 h), 85 řádků za minutu | Hibernate 666 ms, EclipseLink 708 ms, MyBatis 627 ms |

Dotazy na cesty, průměrný select jednoho čtení (6 818 čtení každého):

| Dotaz | Dapper | EF Core | NHibernate | Hibernate | EclipseLink | MyBatis |
|---|---|---|---|---|---|---|
| IC 13 | 203 ms | 207 ms | 191 ms | 188 ms | 195 ms | 193 ms |
| IC 14 | 2 261 ms | 2 389 ms | 2 116 ms | 2 048 ms | 2 198 ms | 1 897 ms |

## Poznámky

- Oba běhy také odmítly pět mutací IC 2 v každém frameworku a vrátily `LdbcSnb` do načteného stavu.
- IC 14 v EF Core držel po celou sadu plán v řádu sekund; úsek se špatným plánem (desítky sekund na čtení, [odhad](2026-10-22-ldbc-judge-mode.md)) nenastal. Nejdražší čtení je IC 9 (4,2–4,7 s v .NET, 3,5–4,1 s v Javě).
- Doba přehrání odpovídá odhadu „zhruba den na sadu" z [2026-10-22](2026-10-22-ldbc-judge-mode.md); proti [2026-10-12](2026-10-12-ldbc-judge.md) (1 226 a 1 518 min na osmi vláknech hostitele s turbem, před oběma režimy) je delší o dnešní texty IC 13 a IC 14, čtení pod zátěží ostatních frameworků (rozh. 166) a vypnuté turbo.
