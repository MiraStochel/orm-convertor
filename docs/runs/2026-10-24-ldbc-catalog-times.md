# Čas BI 14 v zafixovaném prostředí

- **Začátek:** 2026-10-24
- **Strom:** `07fc101`; text BI 14 je tentýž jako ve stromu `d3d1a9a` [záznamu z 2026-10-17](2026-10-17-ldbc-catalog-times.md)
- **Stroj a prostředí:** MIS3, z hosta proti `mssql_db` (SF 1) v měřicím režimu rozh. [164](../decisions/164-the-database-server-has-a-measurement-mode-and-a-judge-mode-switched-by-one-file.md), tedy v zafixovaném prostředí rozh. [145](../decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md): MAXDOP 1, 4096 MB, procesory 0–1, VM se čtyřmi procesory a 8 GB; nejvyšší i nejnižší stav procesoru `0x63`. `LdbcSnb` bez vložených řádků přehrání soudce.
- **Co běželo:** jen BI 14, dotaz, který [záznam z 2026-10-17](2026-10-17-ldbc-catalog-times.md) nespustil, stejně jako tam — text s příkladovými parametry přes Dapper, jedno studené a jedno teplé spuštění jako u BI 6.

## Výsledek

| Dotaz | Řádků | Studené ms | Teplé ms |
|---|---|---|---|
| BI 14 | 100 | 88 736 | 72 667 |

BI 14 je pod horní mezí okna rozh. [140](../decisions/140-ldbc-path-queries-as-breadth-first-search-in-non-recursive-definitions-with-the-depth-of-the-data.md) i pod mezí jednoho spuštění rozh. [146](../decisions/146-the-java-branch-measures-with-the-same-loop-in-its-own-container-and-an-execution-has-one-time-limit.md). S [záznamem z 2026-10-17](2026-10-17-ldbc-catalog-times.md) je tak v zafixovaném prostředí změřený každý dotaz katalogu.
