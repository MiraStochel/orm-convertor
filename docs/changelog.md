# Changelog

Časová osa změn v repozitáři. Dokumenty v `docs/` (kromě tohoto) popisují stav a rozhodnutí; tady je jejich historie a historie navazujících změn v kódu. Nejnovější nahoře.

Formát data: RRRR-MM-DD.

---

## 2026-07-20 — Design doc 001, kroky 1–4 (query model + kompozitní klíče + vztahy)

Implementace prvních čtyř kroků z `design/001-query-model-and-composite-keys.md`:

- **Krok 1 – podmínkový strom.** `SelectInstruction`/`HavingInstruction` nově nesou `ConditionNode` (rekurzivní strom: `ComparisonCondition`, `LogicalCondition`, `NotCondition`) místo plochého AND. `IS NULL`/`IS NOT NULL` jako `ComparisonOperator` (rozhodnutí 7.2). Zaniká `BooleanOperator`. Dotčeno: `IQueryVisitor`, Dapper SQL visitor, EF Core LINQ parser.
- **Krok 2 – vícesloupcový JOIN.** `JoinInstruction` má `ConditionNode` ON klauzuli místo páru sloupců; kompozitní equi-join = `AND` rovností. EF Core parser umí kompozitní klíče v LINQ joinu (anonymní typy).
- **Krok 3a – model kompozitních klíčů.** `PrimaryKey`/`PrimaryKeyPart` na `EntityMap` (pořadí částí, strategie per-part). `AddPrimaryKey` přijímá seznam částí; pohodlný overload pro jednoduchý klíč.
- **Krok 3b – kompozitní klíče ve wrapperech.** EF Core `[PrimaryKey(...)]` atribut, NHibernate `<composite-id>` (parser i builder). Odstraněno lešení duálního zápisu příznaků z 3a. Opravena latentní chyba v `SetPropertyDatabaseMapping` (osiřelá `PropertyMap` se nepřidávala do `PropertyMaps`).
- **Krok 4 – vztahy na entitě.** `Relation` přesunut z `PropertyMap` na `EntityMap.Relations`, redesign dle §4.2 (role Owning/Inverse, `ColumnPairs`, `IsUnique`, `SourceNavigationProperty`). Přidán `IsJunctionTable` na `EntityMap`. Tři odchylky od původního návrhu §4.2: `ColumnPair` je třída (ne tuple – serializace), přibylo pole `SourceNavigationProperty`, `ColumnPairs` mají default prázdný seznam.

Pokrývá požadavky F1–F3 z `requirements.md`. Zbývá krok 5 (N:M přes junction entitu).