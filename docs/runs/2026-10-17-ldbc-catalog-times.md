# Časy dotazů katalogu LDBC v zafixovaném prostředí

- **Začátek:** 2026-10-17
- **Strom:** `d3d1a9a`
- **Stroj a prostředí:** MIS3, z hosta proti `mssql_db` (SF 1) v zafixovaném prostředí rozh. [145](../decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md): MAXDOP 1, 4096 MB, procesory 0–1, VM se čtyřmi procesory a 8 GB; nejvyšší i nejnižší stav procesoru `0x63`
- **Co běželo:** celý katalog stejně jako [2026-10-15](2026-10-15-ldbc-catalog-times.md); medián teplých spuštění po jednom studeném, čtyři spuštění u dotazů pod 5 s, tři u delších, dvě u BI 6. BI 14 se nespustil: podle jeho času mohl být kdekoli mezi 90 s a nad oknem.

## Výsledek

| Dotaz | ms | Dotaz | ms | Dotaz | ms | Dotaz | ms |
|---|---|---|---|---|---|---|---|
| IS 1 | 3 | IC 1 | 121 | IC 12 | 2 331 | BI 9 | 1 272 |
| IS 2 | 3 | IC 2 | 274 | IC 13 | 269 | BI 10 | 2 199 |
| IS 3 | 4 | IC 3 | 2 804 | IC 14 | 4 927 | BI 11 | 3 141 |
| IS 4 | 3 | IC 4 | 124 | BI 1 | 2 200 | BI 12 | 1 358 |
| IS 5 | 3 | IC 5 | 1 737 | BI 2 | 1 071 | BI 13 | 245 |
| IS 6 | 3 | IC 6 | 625 | BI 3 | 1 035 | BI 14 | nespuštěn |
| IS 7 | 4 | IC 7 | 334 | BI 4 | 3 591 | BI 15 | 12 406 |
| | | IC 8 | 16 | BI 5 | 6 702 | BI 16 | 912 |
| | | IC 9 | 1 259 | BI 6 | 60 507 | BI 17 | 3 304 |
| | | IC 10 | 977 | BI 7 | 273 | BI 18 | 36 310 (mezi běhy 26,6–36,3 s) |
| | | IC 11 | 35 | BI 8 | 3 320 | BI 19 | 117 773 |
| | | | | | | BI 20 | 290 |

Každý spuštěný dotaz je pod horní mezí. Pod 0,2 s zůstávají IS 1–7, IC 1, IC 4, IC 8 a IC 11, které politika rozh. 145 opakuje až do svého cíle 1 000 ms.
