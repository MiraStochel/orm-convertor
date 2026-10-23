# Doba přehrání soudce LDBC po krocích, měřicí režim

- **Začátek:** 2026-10-18
- **Strom:** `32f0949`
- **Stroj a prostředí:** MIS3; obě sady v kontejnerech (`tests` a `java_tests` proti znovu sestavenému `ormconvertor` nad `LdbcSnb`), jedna po druhé proti `mssql_db` v zafixovaném prostředí rozh. [145](../decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md), s turbem jako soudce (nejvyšší stav procesoru `0x63`, nejnižší `0x05`)
- **Co běželo:** prvních 500 řádků SF 1 (476 čtení všech 21 souzených dotazů, 24 vkládání), pak prvních 250 (233 čtení, 17 vkládání), každá sada dvakrát. Co test kompenzace vypisuje, popisuje `architecture.md` §6.2; řádky po dotazech jsou ve výstupu testu.

## Výsledek

Každá buňka se ve všech čtyřech bězích shodla se soudcem. Projekce na celou sadu násobí průměr dotazu v každém frameworku počtem toho dotazu v sadě (6 818 čtení každého dotazu IC, 7 162 IS 1–3, 3 692 IS 4–7).

| | Dapper | EF Core | NHibernate | Hibernate | EclipseLink | MyBatis |
|---|---|---|---|---|---|---|
| překlad 21 buněk, běh 500 / 250 řádků | 2,4 s / 2,3 s | 2,3 s / 1,6 s | 1,5 s / 1,3 s | 3,9 s / 1,5 s | 1,6 s / 1,2 s | 1,4 s / 1,1 s |
| kompilace 21 buněk, 500 / 250 | 6,2 s / 5,0 s | 12,1 s / 12,1 s | 6,3 s / 6,2 s | 9,8 s (bootstrap 6,4 s) / 8,9 s (5,8 s) | 5,8 s (1,9 s) / 4,4 s (1,6 s) | 7,8 s (0,7 s) / 6,1 s (0,6 s) |
| select, 476 čtení řádků 1–500 | 316,2 s | 866,9 s | 276,4 s | 284,2 s | 271,2 s | 283,4 s |
| select, průměr čtení, řádky 1–500 | 664 ms | 1 821 ms | 581 ms | 597 ms | 570 ms | 596 ms |
| select, 233 čtení řádků 1–250 | 119,0 s | 131,5 s | 117,2 s | 120,7 s | 117,4 s | 122,2 s |
| select, průměr čtení, řádky 1–250 | 511 ms | 564 ms | 503 ms | 518 ms | 504 ms | 525 ms |
| celá sada selectů, projekce z řádků 1–500 | 23,0 h | 63,2 h | 20,1 h | 20,7 h | 19,8 h | 20,7 h |
| totéž, projekce z řádků 1–250 | 18,8 h | 20,7 h | 18,5 h | 19,0 h | 18,5 h | 19,3 h |

## Poznámky

- Přehrání trvalo nad 500 řádky 25,5 min (.NET) a 14,9 min (Java), nad 250 řádky 7,2 a 6,7 min; kroky mimo buňky ve všech čtyřech bězích pod 45 s.
- Projekce platí zhruba na 15 % u každé buňky kromě IC 14 v EF Core, jejíž čtení řádků 251–500 trvají v průměru asi 43 s, kdežto Dapper a NHibernate čtou tytéž parametry za 1,6–2,0 s; EF Core se tak promítá kamkoli mezi 21 a 63 h.
- **Celou sadu v zafixovaném prostředí je třeba čekat 2,5–5 dní u .NET sady a 2,5–3 dny u javové**; běhy z [2026-10-12 a 2026-10-14](2026-10-12-ldbc-judge.md), mimo ně, trvaly 20 h a 25 h. Soudce proto běží v režimu soudce ([2026-10-22](2026-10-22-ldbc-judge-mode.md)).
