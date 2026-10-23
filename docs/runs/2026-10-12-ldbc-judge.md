# Soudce LDBC nad celou validační sadou SF 1

- **Začátek:** .NET 2026-10-12, Java 2026-10-14
- **Strom:** .NET `580ab90`, Java `5656945`
- **Stroj a prostředí:** MIS3, `mssql_db` (SF 1), obraz sestavený `build` těsně předtím; před oběma režimy serveru (rozh. [164](../decisions/164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md)), na všech osmi vláknech hostitele bez omezení paralelismu
- **Co běželo:** verdikt rozh. [117](../decisions/117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md) — celá `validation_params-sf1`, 138 747 řádků, z toho 7 041 vkládání

## Výsledek

| Sada | Testy | Podle specifikace (19 dotazů) | IC 13, IC 14 (míra) | Přehrání |
|---|---|---|---|---|
| .NET: Dapper, EF Core, NHibernate | 86 prošlo, 0 přeskočeno | každé čtení ve shodě ve všech třech | 6 818 z 6 818 čtení (100,0 %) každý, ve všech třech | 1226 min, 113 řádků za minutu |
| Java: Hibernate, EclipseLink, MyBatis | 81 prošlo, 0 přeskočeno | každé čtení ve shodě ve všech třech | 6 818 z 6 818 čtení (100,0 %) každý, ve všech třech | 1518 min, 91 řádků za minutu |

## Poznámky

- Oba běhy také odmítly pět mutací IC 2 v každém frameworku a vrátily `LdbcSnb` do načteného stavu.
- IC 13 a IC 14 soudily v jejich textech s mezí hloubky, ne v textech rozh. [140](../decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) a [141](../decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md).
