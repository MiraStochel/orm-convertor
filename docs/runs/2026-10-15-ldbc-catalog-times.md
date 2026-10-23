# Časy dotazů katalogu LDBC a hloubky dotazů na cesty

- **Začátek:** 2026-10-15
- **Strom:** `86a102b` + následující commit (pět dotazů na cesty v nových textech, ostatní beze změny)
- **Stroj a prostředí:** MIS3, Intel Core i7-1065G7 s turbem, SQL Server 2022 v Docker Desktop, `mssql_db` (SF 1); před zafixovaným prostředím rozh. [145](../decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), jehož čísla s těmito nejsou srovnatelná
- **Co běželo:** okno rozh. [140](../decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) — každý dotaz katalogu běží nad `LdbcSnb` mezi 0,2 s a 300 s, horní mez splní scale factor 1, dolní opakování při měření (rozh. [141](../decisions/141-one-data-set-for-the-ldbc-catalog-and-the-depth-of-a-path-query-as-a-measured-property-of-it.md)). Každý text s příkladovými parametry přes Dapper, čtyři spuštění, nejmenší ze tří teplých.

## Výsledek

| Dotaz | ms | Dotaz | ms | Dotaz | ms | Dotaz | ms |
|---|---|---|---|---|---|---|---|
| IS 1 | 2 | IC 1 | 58 | IC 12 | 1 001 | BI 9 | 341 |
| IS 2 | 2 | IC 2 | 85 | IC 13 | 170 | BI 10 | 1 004 |
| IS 3 | 3 | IC 3 | 1 446 | IC 14 | 2 243 | BI 11 | 5 205 (mezi běhy 2,5–14 s) |
| IS 4 | 2 | IC 4 | 61 | BI 1 | 235 | BI 12 | 603 |
| IS 5 | 2 | IC 5 | 210 | BI 2 | 305 | BI 13 | 257 |
| IS 6 | 2 | IC 6 | 266 | BI 3 | 479 | BI 14 | 38 469 |
| IS 7 | 3 | IC 7 | 68 | BI 4 | 412 | BI 15 | 5 259 |
| | | IC 8 | 8 | BI 5 | 2 820 | BI 16 | 3 959 |
| | | IC 9 | 237 | BI 6 | 26 785 | BI 17 | 1 488 |
| | | IC 10 | 173 | BI 7 | 130 | BI 18 | 11 807 |
| | | IC 11 | 19 | BI 8 | 835 | BI 19 | 6 623 |
| | | | | | | BI 20 | 128 |

Pod 0,2 s jsou vyhledání v indexu a malé joiny (IS 1–7, IC 1, IC 2, IC 4, IC 7, IC 8, IC 10, IC 11, IC 13, IC 14, BI 7, BI 20), které nad mez nezvedne žádný scale factor.

## Hloubky dotazů na cesty

Změřené z každé osoby nad týmž data setem; drží je `LdbcJudge/LdbcPathQueryTest`.

| Hrany | Osob s hranou | Průměrný stupeň | Komponent | Hloubka | Dvojic v té hloubce |
|---|---|---|---|---|---|
| `knows` (IC 13, IC 14) | 9 163 | 39,4 | 1 | 5, průměr grafu | 2 172 z 83 951 406 |
| `knows` mezi osobami, které si navzájem odpověděly (BI 19) | 8 674 | 32,4 | 1 | 5, nejvíc hran na nejlevnější cestě | 21 056 z 75 229 602 |
| `knows` přes společnou univerzitu (BI 20) | 3 038 | 4,9 | 443 | 7 | 12 z 70 146 |
| `knows` s cenou podle fór příkladového časového okna (BI 15) | 9 163 | 39,4 | 1 | 11 | 4 z 83 951 406 |
