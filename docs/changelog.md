# Changelog

Časová osa změn v repozitáři. Dokumenty v `docs/` (kromě tohoto) popisují stav a rozhodnutí; tady je jejich historie a historie navazujících změn v kódu. Nejnovější nahoře.

Vznik nebo rozšíření dokumentu v `docs/` je změnou repozitáře a zaznamenává se sem odkazem na dokument, ne převyprávěním jeho obsahu — rozhodnutí zůstávají v `design/`. Aktuální stav projektu je tedy dán `current-state.md` a tímto souborem dohromady.

Formát data: RRRR-MM-DD.

---

## 2026-08-02 — Audit po kroku 4: opravy kompozitních klíčů a typové tabulky

Analýza tří .NET ORM frameworků proti zafixovaným verzím, audit stavu po krocích 1–4 a opravy, které z něj vyplynuly.

- **Nová dokumentace.** `analysis/` s tutoriály pro NHibernate, EF Core a Dapper a s tematickým srovnáním všech tří; `audits/2026-08-02-post-step-4-audit.md` se seznamem nálezů, oprav (O1–O6), rozhodnutí k zapsání (R1–R4) a úprav s delším horizontem (Ú1–Ú5).
- **Design doc 001 rozšířen** o §3.5 (sémantika identity u kompozitního klíče) a o rozhodnutí 5 (forma kompozitního klíče ve výstupu).
- **O1 – identitní členy pro NHibernate.** `NHibernateEntityBuilder` generuje u kompozitního klíče `Equals`, `GetHashCode` a `[Serializable]`, plus `using System;` v generovaném souboru. Bez nich NHibernate odmítal zkompilovat mapování hláškou `composite-id class must override Equals()`, takže výstup z kroku 3b nebyl spustitelný.
- **O2 – testy nad generovaným kódem.** `CompositeKeyTest` dosud ověřoval jen XML, proto chyba z 3b prošla. Doplněny asserce nad C# výstupem včetně pojistky, že se rovnost neporovnává přes `GetType()` (proxy je potomek entity), a negativní test, že jednoduchý klíč zůstává prostým POCO.
- **O3 – opravy převodní tabulky typů.** `CLRType.Byte` na `TinyInt` místo `Int`, `CLRType.Float` na `Real` místo `Float` a oprava casingu ve větvi `"datetimenoMs"`, která kvůli velkému `M` nemohla nikdy zabrat.
- **O4 – invariant `Order`.** Řazení částí klíče přesunuto z `AbstractEntityBuilder.AddPrimaryKey` do typu `PrimaryKey`, aby platilo i pro přímé přiřazení mimo builder.

Odblokovává krok 5a: junction entita nese kompozitní klíč, takže bez O1 by každý překlad N:M do NHibernate skončil pádem při stavbě session factory.

## 2026-07-20 — Normalizace konců řádků

Kořenový `.gitattributes` s `* text=auto eol=crlf` a `*.sh text eol=lf`, normalizováno 27 souborů (commit `abb777f`). Pravidlo pro `*.sh` je nutné: `database/init-db.sh` běží uvnitř linuxového kontejneru SQL Serveru podle `database.Dockerfile` a s CRLF by selhal na `bad interpreter`.

Ověřeno přes `git ls-files --eol` — všechny soubory `i/lf`, žádný `i/crlf`. Pozor: od zavedení `.gitattributes` aplikuje `git archive` konverzi konců řádků, takže tarbally z `codeload` neodpovídají uloženému stavu. Pro ověření konců řádků je nutný `git clone`; pro čtení obsahu tarball stačí.

## 2026-07-20 — Design doc 002 (generování N:M)

Vznikl `design/002-many-to-many-generation.md`. Rozhoduje, že se vztah N:M generuje jako explicitní junction entita (varianta B), nikoli jako skip navigation; `IsJunctionTable` na `EntityMap` zůstává volitelným signálem pro pozdější zploštění.

## 2026-07-20 — Design doc 001, kroky 1–4 (query model + kompozitní klíče + vztahy)

Implementace prvních čtyř kroků z `design/001-query-model-and-composite-keys.md`:

- **Krok 1 – podmínkový strom.** `SelectInstruction`/`HavingInstruction` nově nesou `ConditionNode` (rekurzivní strom: `ComparisonCondition`, `LogicalCondition`, `NotCondition`) místo plochého AND. `IS NULL`/`IS NOT NULL` jako `ComparisonOperator` (rozhodnutí 7.2). Zaniká `BooleanOperator`. Dotčeno: `IQueryVisitor`, Dapper SQL visitor, EF Core LINQ parser.
- **Krok 2 – vícesloupcový JOIN.** `JoinInstruction` má `ConditionNode` ON klauzuli místo páru sloupců; kompozitní equi-join = `AND` rovností. EF Core parser umí kompozitní klíče v LINQ joinu (anonymní typy).
- **Krok 3a – model kompozitních klíčů.** `PrimaryKey`/`PrimaryKeyPart` na `EntityMap` (pořadí částí, strategie per-part). `AddPrimaryKey` přijímá seznam částí; pohodlný overload pro jednoduchý klíč.
- **Krok 3b – kompozitní klíče ve wrapperech.** EF Core `[PrimaryKey(...)]` atribut, NHibernate `<composite-id>` (parser i builder). Odstraněno lešení duálního zápisu příznaků z 3a. Opravena latentní chyba v `SetPropertyDatabaseMapping` (osiřelá `PropertyMap` se nepřidávala do `PropertyMaps`).
- **Krok 4 – vztahy na entitě.** `Relation` přesunut z `PropertyMap` na `EntityMap.Relations`, redesign dle §4.2 (role Owning/Inverse, `ColumnPairs`, `IsUnique`, `SourceNavigationProperty`). Přidán `IsJunctionTable` na `EntityMap`. Tři odchylky od původního návrhu §4.2: `ColumnPair` je třída (ne tuple – serializace), přibylo pole `SourceNavigationProperty`, `ColumnPairs` mají default prázdný seznam.

Pokrývá požadavky F1–F3 z `requirements.md`. Zbývá krok 5 (N:M přes junction entitu).