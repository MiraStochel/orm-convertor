# 029 — Připojení do databáze je fakt konzumentského projektu

Datum: 2026-08-20
Stav: platí
Požadavky: F5, F11, S4
Podklad: rozhodnutí [028](028-assembly-name-is-not-ours-to-invent.md) a [004](004-unexpressible-facts-as-warnings.md); `EFCoreLinqQueryBuilder`, `DapperBenchmarkHarnessBuilder`, `EfCoreBenchmarkHarnessBuilder`, `AdvisorRunCoordinator`, `RoslynBenchmarkCompiler`

## Kontext

Požadavek S4 má dvě věty. První žádá, aby se cizí a generovaný kód sestavoval a spouštěl v izolovaném prostředí s omezením CPU, paměti a času. Druhá zakazuje, aby přihlašovací údaje k databázi byly v generovaných zdrojových souborech nebo v lozích. Ani jedna z nich neříká, **čí** generované soubory má na mysli — a v tomhle nástroji jsou dva druhy, které spolu nemají nic společného.

**Překladový artefakt** je to, co nástroj vydává uživateli: entitní třída, mapování `.hbm.xml`, dotazová metoda. Žádný z builderů do něj připojení nevypisuje. Dotazový artefakt EF Core má tvar `public static … Query(DbContext ctx)`, tedy spojení si dodává volající; NHibernate mapování element pro spojení nemá, protože ten patří do `hibernate.cfg.xml`, který negenerujeme; výstupem Dapperu je třída a holý dotaz. Endpoint `/convert` navíc nic nekompiluje ani nespouští.

**Benchmarkový harness** je něco jiného: kód, který si `AdvisorBenchmarking` generuje sám, aby změřil běh a paměť. Ten se bez spojení obejít nemůže, a taky se neobchází — `DapperBenchmarkHarnessBuilder` i `EfCoreBenchmarkHarnessBuilder` vypisují řetězec do zdrojového textu jako `private const string ConnectionString`. Ten řetězec pochází ze serverové konfigurace (`ConnectionStrings:AdvisorDatabase`), ne od uživatele, a nikam neodchází: harness se nikam neukládá, `AdvisorRunResult` nese jen výběr frameworků a naměřené hodnoty, `RoslynBenchmarkCompiler` při chybě vydá Roslyn diagnostiky, které nesou pozici a ne text řádku, a logy logují počty dotazů a časy.

Za tímhle nesouladem je ale hlubší otázka, kterou požadavek nezodpovídá. **Kdyby vstup připojení nesl, věrnost překladu by žádala, aby ho nesl i výstup — a S4 by to zakázala.** Překlad má reprodukovat význam zdroje, ne naši preferenci; a zároveň nemá vydávat tajemství. Dokud se nevysloví, čeho se zákaz týká, stojí ta dvě pravidla proti sobě a rozpor se vyřeší pokaždé jinak.

## Zvažované varianty

1. **Číst S4 doslova na všechno generované.** Pak by harness musel dostat spojení jinudy — třeba tak, že by generovaný kód četl proměnnou prostředí za běhu. Řeší to ale problém, který není: řetězec z procesu neodchází a je to serverova vlastní databáze. Hlavně to ale na podstatnou otázku vůbec neodpovídá — o překladovém artefaktu, tedy o tom jediném, co uživatel dostane do ruky, se takové čtení nevysloví. Zamítáme.

2. **Vzít připojení jako nepovinný vstup převodu a vypsat ho do cílového artefaktu.** Dotazový artefakt by pak nesl vlastní `DbContext` s `UseSqlServer(…)`, NHibernate by dostal konfigurační soubor. To je přesně to, co rozhodnutí [028](028-assembly-name-is-not-ours-to-invent.md) zamítlo u názvu sestavení, a text 028 připojovací řetězec dokonce jmenuje jako příklad téhož druhu faktu. Navíc by nástroj začal vydávat druh artefaktu, který dnes nevydává — konfiguraci —, což je rozšíření rozsahu, ne oprava. A vydávat přihlašovací údaje ve zdrojovém souboru je právě to, co S4 zakazuje. Zamítáme.

3. **Prohlásit připojení za fakt konzumentského projektu a zákaz vztáhnout na předávaný artefakt.**

## Rozhodnutí

**Volíme variantu 3. Připojení do databáze není fakt překladu. Do mezireprezentace nevstupuje, do výstupu se nevypisuje, a zákaz z S4 se vztahuje na artefakt, který nástroj předává.**

**Je to týž druh faktu jako název sestavení.** Rozhodnutí 028 uzavřelo, že název sestavení je vlastnost projektu, který kód přeloží, ne vlastnost kódu, a proto si ho překlad nevymýšlí; ve svém odůvodnění řadí do téže třídy soubor projektu i připojovací řetězec. Tady tu poznámku povyšujeme na pravidlo a doplňujeme, co z ní plyne pro S4. Konzumentský projekt si spojení dodá tak, jak je u jeho frameworku zvykem — `appsettings.json` a `AddDbContext` u EF Core, `hibernate.cfg.xml` u NHibernate, `SqlConnection` v místě volání u Dapperu —, a je to jeho volba, ne naše.

**Na překladové cestě tím S4 platí konstrukcí.** Není to shoda okolností: tvar artefaktů, které buildery vydávají, spojení nepřipouští. Chybí jediné — tvrzení, které to hlídá. Dnes to neověřuje žádný test, takže by se to dalo porušit nepozorovaně, a to je otevřená položka, ne součást tohoto rozhodnutí.

**Připojení ve vstupu je aplikační kód, ne mapovací fakt.** Když se do vstupu dostane text, který připojení obsahuje — typicky `OnConfiguring` s `UseSqlServer(…)` uvnitř vloženého `DbContext`u —, parsery ho nečtou a číst ho nezačnou. Záznam o ztrátě z toho nevzniká: v mezireprezentaci není co ztratit, protože se tam ta informace nikdy nedostala, a rozhodnutí [004](004-unexpressible-facts-as-warnings.md) hlásí ztracené fakty, ne nepřečtený kód. Že vložený `DbContext` dnes projde entitním parserem jako další entita, je samostatná vada vstupní validace a patří k F14, ne sem.

**Harness není předávaný artefakt.** Je to vnitřní prostředek měření: vzniká v paměti, zkompiluje se, načte do kolektibilního kontextu a zahodí. Údaje v něm jsou serverovy vlastní a proces neopouštějí. Zákaz z S4 na něj proto nedopadá — dopadá na to, co dostane uživatel. Zdrojový text je přesto horší místo pro tajemství než parametr metody a přepsat to na parametr by bylo lepší; kdy se to udělá, je otázka rozsahu verze, ne tohoto rozhodnutí.

**První věta S4 se týká téhož jediného místa.** Cizí kód nástroj spouští právě na jedné cestě: `/advisor/run` přes `AdvisorBenchmarking`. Překladová cesta nekompiluje ani nespouští nic — kompilace generovaných artefaktů žije v testech (rozhodnutí [016](016-generated-artifact-verification-levels.md)), ne v běžícím nástroji. Celý požadavek na izolaci se tím soustředí do jedné oblasti, a co s tou oblastí bude, se rozhoduje jinde.

## Důsledky

**Vzniká otevřená položka na ověření.** Tvrzení „překladový artefakt nenese přihlašovací údaje" má být aserce nad výstupem převodu, ne úsudek čtenáře kódu. Bez ní je S4 splněná náhodou.

**`architecture.md` dostává větu o tom, že překladová cesta cizí kód nespouští.** Dnes to z textu plyne nepřímo, přes popis `Common.Compilation` a ověřovacích stupňů, a čtenář si to musí složit sám.

**Pro javový ekosystém se tím jedna past zavírá dopředu.** JPA nese připojení v `persistence.xml` a ten soubor vypadá jako přirozený výstup javového builderu. Není: je to artefakt konzumentského projektu stejně jako `hibernate.cfg.xml`. Kdyby se to nevyslovilo teď, první javový builder by ho začal generovat i s údaji, které mu nikdo nedal.

**Vedoucímu to nepředkládáme jako otázku, ale zmiňujeme jako výklad.** Nezužuje se tím nic, co požadavek slíbil — jen se pojmenovává, o čích artefaktech mluví. Co naopak zmínit je třeba, je osud první věty S4, protože ta se celá váže na oblast, o jejíchž zárukách se rozhoduje samostatně.
