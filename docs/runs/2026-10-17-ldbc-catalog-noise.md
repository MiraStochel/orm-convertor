# Šum měření katalogu LDBC

- **Začátek:** 2026-10-17
- **Strom:** `6f2c1a2` + následující commit
- **Stroj a prostředí:** MIS3, `mssql_db` (SF 1); jednou před zafixovaným prostředím rozh. [145](../decisions/145-the-measurement-environment-and-the-iteration-policy-are-fixed.md) s turbem, jednou v něm s vypnutým turbem
- **Co běželo:** 21 dotazů katalogu stejně jako [2026-10-15](2026-10-15-ldbc-catalog-times.md) — text s příkladovými parametry přes Dapper —, každý ve třech průchodech

## Výsledek

- **Před zafixovaným prostředím, s turbem,** se mediány IC 5 a IC 9 mezi průchody lišily 2–4× (IC 5: 936, 214 a 608 ms).
- **V něm, s vypnutým turbem,** o méně než 5 % (IC 5: 1 810, 1 790 a 1 782 ms), při absolutních časech 4–9× vyšších, z toho asi polovinu dělá MAXDOP 1.
- Bez indexů `indexes.sql` se žádný krátký dotaz nad mez nezvedl.
