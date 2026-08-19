# 028 — Název sestavení není náš, abychom ho vymýšleli

Datum: 2026-08-19
Stav: platí
Požadavky: F2, F11, S2
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md) a [010](010-diagnostics-as-returned-data.md); NHibernate 5.7.0, `NHibernateEntityBuilder.BuildTableSchema`

## Kontext

NHibernate hledá perzistentní třídu podle plně kvalifikovaného názvu, jehož druhá část je název sestavení. V mapování se ta dvojice dá zapsat dvěma způsoby: buď na každém `<class>` (`name="Shop.Customer, Shop"`), nebo jednou na kořenovém `<hibernate-mapping>` atributy `namespace` a `assembly`, a `<class>` pak nese holé jméno.

Builder dosud volil první způsob a **název sestavení odvozoval ze jmenného prostoru**. To jsou ale nezávislé věci. Jmenný prostor je vlastnost zdrojového kódu, název sestavení vlastnost projektu, který ten kód přeloží; shodují se jen tehdy, když se tak někdo rozhodl. Vygenerované mapování tedy tvrdilo o cílovém projektu něco, co ve většině případů neplatí, a NHibernate takové mapování odmítne hláškou `persistent class ... not found`.

**Že to neodhalily testy, je vlastnost testů, ne kódu.** Ověřovací testy 3. stupně kompilují generované entity pod názvem sestavení, který je shodný se jmenným prostorem zdroje — `EFCoreEntities`, `KeyPartEntities`, `JunctionEntities`. Výmysl builderu si tak samy potvrzují. `NHibernateAcceptance.QualifyAssembly` navíc dosavadní stav popisuje jako záměr („název, který builder už kvalifikoval, má přednost"), ačkoli jde o obcházení vady: metoda vznikla právě proto, že sestavení je fakt konzumentského projektu, a pak ustoupila builderu, který si ho vymyslel.

Otázka tedy zní, co má generované mapování o sestavení říkat, když to převod neví.

## Zvažované varianty

1. **Ponechat odvození ze jmenného prostoru.** Dnešní stav. Má jedinou přednost — v projektech, kde se sestavení jmenuje jako jmenný prostor, mapování rovnou funguje. Jenže to je konvence jednoho druhu projektů, ne pravidlo, a když neplatí, výsledek se nespustí. Rozhodnutí [004](004-unexpressible-facts-as-warnings.md) přitom zakazuje generovat náhražku za fakt, který nemáme, právě proto, aby chyba nevypadala jako hotový výstup. Zamítáme.

2. **Vzít název sestavení jako vstup převodu.** Uživatel by ho zadal a builder vypsal. Fakticky správné, ale zavádí do překladu parametr, který se týká jediného cílového frameworku, a rozšiřuje vstupní kontrakt kvůli údaji, který si konzument stejně doplní na jednom řádku. Zamítáme jako předčasné; pokud se ukáže, že to lidem vadí, je to přidání nepovinného vstupu, ne návrat sem.

3. **Nevypisovat sestavení a říct to.**

## Rozhodnutí

**Volíme variantu 3. `<class>` nese holé jméno třídy, jmenný prostor zůstává na kořenovém `<hibernate-mapping>` a atribut `assembly` se nevypisuje vůbec.**

**Holé jméno je úplnější tvrzení, ne chudší.** Mapování s `namespace` na kořeni a holým `<class name="Customer">` říká přesně to, co převod ví: třída se jmenuje `Customer` a leží ve jmenném prostoru `Shop`. Kvalifikovaný zápis k tomu přidával čtvrtou informaci, kterou nikdo neposkytl. Ubíráme tedy výmysl, ne fakt.

**Záznam o chybějícím `assembly` nevzniká, a je to vědomé.** Napsat ho by znamenalo vydat jeden `Incompleteness` u každé entity každého převodu do NHibernate — tedy vždy. Záznam, který platí bez výjimky, nic o daném převodu netvrdí; je to popis formátu, a ten patří do dokumentace, ne do diagnostiky. Rozhodnutí [010](010-diagnostics-as-returned-data.md) zavedlo záznamy jako odpověď na to, *co se stalo s tímto vstupem*, a zaplavit je konstantou by hodnotu ostatních záznamů snížilo. Název sestavení je příspěvek konzumentského projektu stejně jako soubor projektu nebo připojovací řetězec — a ty také nehlásíme.

**Ověřovací harness přestává být výmluvou a stává se tím, čím měl být.** `QualifyAssembly` dosud doplňovala `assembly` jen tam, kde ho builder nekvalifikoval sám; nově se uplatní vždy, protože builder nekvalifikuje nikdy. Tím se ověření 3. stupně přestává potvrzovat samo sebou: název sestavení dodává harness jako konzument, a kdyby ho nedodal, NHibernate mapování odmítne — což je správně.

**Parser se nemění.** Kvalifikovaný zápis je legitimní tvar mapování NHibernate a reálné projekty ho běžně používají, takže ho `NHibernateXMLMappingParser` musí číst dál. Mění se jen to, co vypisujeme my.

## Důsledky

**Převod NHibernate → NHibernate přestává být znakově identickým round-tripem u zdrojů, které používají kvalifikovaný zápis.** Vstup `<class name="Shop.Customer, Shop">` vyjde jako `<class name="Customer">` pod kořenem se jmenným prostorem `Shop`. Je to táž entita popsaná druhým zákonným zápisem — S2 žádá artefakty shodné *nebo po normalizaci ekvivalentní* a tohle je přesně ten druhý případ. Ukázka v `SampleData` se proto přepisuje na holý tvar, aby neučila zápis, který nevypisujeme.

**Testy tvaru se opravují na čtyřech místech**, všechna nad výstupem; vstupní mapování v testech zůstávají kvalifikovaná, protože právě ta ověřují, že parser obojí čte.

**Pro javový ekosystém se tím uzavírá jedna past dopředu.** Hibernate ani JPA pojem sestavení nemají, takže kdyby tahle logika zůstala, první javový builder by ji buď zkopíroval do prostředí, kde nedává smysl, nebo by musel vysvětlovat, proč ji nemá. Odstraněním problém mizí.
