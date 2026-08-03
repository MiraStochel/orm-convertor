# 002 — `IS NULL` jako porovnávací operátor

Datum: 2026-07-16
Stav: platí
Požadavky: F7–F10
Podklad: JSS článek §5.3 (podmínkové stromy), obr. 4

## Kontext

Podmínkový strom (`ConditionNode`) reprezentuje filtrační a spojovací predikáty. Test na `NULL` je v SQL unární operace, ale v LINQ se zapisuje jako porovnání (`== null`). Je třeba zvolit, jakým uzlem ho strom vyjádří.

## Zvažované varianty

1. **`ComparisonOperator.IsNull` / `IsNotNull`** — operátor v běžném `ComparisonCondition`.
2. **Samostatný uzel `IsNullCondition`.**
3. **`NULL` jako konstantní operand běžného `==` / `!=`** — takto to znázorňuje doprovodný článek (obr. 4: uzel `==` s operandy `NULL` a `c.CreditLimit`).

## Rozhodnutí

**Operátory `ComparisonOperator.IsNull` / `IsNotNull`.**

Varianta 3 vypadá nejjednotněji, ale je to sémantická past: naivně vygenerované `= NULL` v SQL není `IS NULL` — porovnání s NULL vrací UNKNOWN a dotaz by potichu nevracel řádky. To je přesně druh tiché chyby, které se chceme vyhnout. Každý builder by proto NULL operand stejně musel detekovat a ošetřit zvláštní větví, čímž se domnělá jednotnost jen přesouvá z modelu do všech builderů.

Varianta 2 je sémanticky čistá, ale rozšiřuje `IQueryVisitor` o další metodu, kterou musí implementovat každý současný i budoucí builder včetně plánovaných javových — přestože jde pořád o porovnání, jen unární.

## Důsledky

Tvar stromu zůstává uniformní: `ComparisonCondition` s operátorem `IsNull` / `IsNotNull` a nevyužitou pravou stranou.

Parser do této podoby normalizuje jak `== null` / `!= null` z LINQ, tak `IS [NOT] NULL` ze SQL. Builder má pro tyto operátory povinnou explicitní větev — omylem vygenerovat `= NULL` tak nejde.
