# 005 — N:M jako explicitní junction entita

Datum: 2026-07-20
Stav: platí
Požadavky: F3, F10, T1

## Kontext

Relační vazba N:M nemá v objektovém modelu jednoznačnou reprezentaci — frameworky ji vyjadřují různě. Pro EF Core jako první cíl existují dvě hlavní varianty a je třeba zvolit výchozí. Rozhodnutí má přesah i do dalších frameworků, protože určuje, jestli spojovací entita v IR po překladu zůstává viditelná, nebo se rozpouští.

## Zvažované varianty

### A — Skip navigation (junction bez třídy)

EF Core 5+ umí M:N bez explicitní třídy spojovací tabulky: na obou entitách je jen navigační kolekce protistrany, tabulku spravuje EF Core interně. Název tabulky a sloupců se určuje fluent konfigurací v `DbContext.OnModelCreating`; bez ní si je EF Core odhadne z konvence.

Výhodou je idiomatický, čistý kód entit. Nevýhody: informace se dělí mezi entity a `DbContext`, který dnešní builder negeneruje; spojovací tabulka je v kódu skrytá, takže se při zpětném a cross-ecosystem překladu ztrácí, pokud ji parser nezrekonstruuje; „bohatou" spojovací tabulku s extra sloupci skip navigation neuveze a potřebovala by druhou cestu.

### B — Explicitní junction entita

Spojovací tabulka je běžná entita se dvěma vztahy typu N:1.

## Rozhodnutí

**Varianta B jako výchozí a jediná povinná cesta.**

1. **Symetrie s modelem IR.** N:M je v mezireprezentaci zachycená jako junction `EntityMap` plus dvě relace N:1. Varianta B tuto strukturu promítá jedna k jedné; varianta A ji při generování rozpouští, což je krok stranou od zvoleného modelu.
2. **Obousměrnost a cross-ecosystem (F3, F10).** Spojovací tabulka je nejnižší společný jmenovatel N:M napříč všemi frameworky. Udržení junction entity viditelné v IR umožňuje generovat kterýkoli cílový tvar bez rekonstrukce; skrytí by před každým dalším překladem vyžadovalo entitu znovu odvodit.
3. **Bohatá spojovací tabulka (F3, T1).** Reálné spojovací tabulky mívají extra sloupce a explicitní entitu vyžadují i v EF Core. Varianta B je řeší stejným kódem jako čistou M:N; varianta A by potřebovala dvě cesty a rozhodovací logiku mezi nimi.

## Důsledky

`IsJunctionTable = true` zůstává v modelu jako **volitelný signál**, že spojovací tabulka je „čistá", tedy obsahuje jen sloupce cizích klíčů. Výchozí generování ho nevyžaduje a vždy vytvoří explicitní entitu. Jako budoucí rozšíření může builder pod tímto signálem nabídnout zploštění do skip navigation pro frameworky, které to umí — ale jako opt-in, ne jako výchozí chování.

Dopad na jednotlivé frameworky:

- **NHibernate** už dnes umí `<many-to-many>` v kolekci i explicitní entitu se dvěma `<many-to-one>`. Explicitní junction entita je konzistentní s tím, jak builder generuje ostatní vztahy.
- **Dapper** řeší M:N ručními joiny; junction entita jako POCO odpovídá tomu, jak by se to psalo ručně. Klíče a vztahy Dapper negeneruje (rozhodnutí 004) — ponese strukturované varování.
- **Hibernate a EclipseLink (F7, F9).** JPA nabízí `@ManyToMany` s `@JoinTable` i junction entitu; explicitní entita z IR se do obou tvarů přeloží. Volba tvaru je generační rozhodnutí, které tento dokument nechává otevřené pro budoucí rozšíření.
