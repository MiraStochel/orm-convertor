# Systémové požadavky proti modelu kvality ISO/IEC 25010:2023

**Účel:** ukázat, jak sedm systémových požadavků zadání sedí na standardní model kvality produktu — a hlavně, kde nesedí. Je to tvrzení o **vztahu k normě**, ne nový požadavek: [`requirements.md`](./requirements.md) je zmražené a nesahá se do něj. Stav plnění nese [`traceability.md`](./traceability.md), nárok verze `architecture.md` §9.

Vydání 25010 z roku 2023 má **devět** charakteristik: *Functional suitability*, *Performance efficiency*, *Compatibility*, *Interaction capability*, *Reliability*, *Security*, *Maintainability*, *Flexibility* a *Safety*. Oproti vydání z roku 2011 přibyla *Safety* jako samostatná charakteristika, *usability* se přejmenovala na *interaction capability* a *portability* na *flexibility*.

> Text normy je placený. Názvy podcharakteristik níž odpovídají vydání 2023 podle sekundárních zdrojů; před citací v textu práce je namístě ověřit je proti samotné normě.

---

## Mapování S1–S7

| Požadavek | Charakteristika (podcharakteristika) | Čím je v projektu nesený |
|---|---|---|
| **S1** modulární rozšiřitelnost | *Maintainability* (Modularity, Modifiability) + *Flexibility* (Adaptability) | Wrapper na framework, společné rozhraní v `AbstractWrappers`, deskriptor cíle (rozhodnutí [009](./decisions/009-target-framework-descriptor.md)); invariant, že nový framework nesmí vynutit změnu v orchestraci |
| **S2** determinismus a opakovatelnost | *Functional suitability* (Functional correctness) + *Reliability* (Faultlessness) | Buildery bez závislosti na prostředí, zafixované verze (rozhodnutí [013](./decisions/013-target-framework-versions.md), [034](./decisions/034-central-version-management.md)), verze nástroje v záznamu běhu (rozhodnutí [069](./decisions/069-major-marks-a-milestone-not-a-break.md)) |
| **S3** výkon překladu | *Performance efficiency* (Time behaviour) | Měřený limit 100 entit + 100 dotazů do 30 s; čtení katalogu se vykazuje odděleně |
| **S4** izolace a bezpečnost | *Security* (Confidentiality, Resistance) | Druhá věta konstrukcí (rozhodnutí [029](./decisions/029-database-connection-is-the-consumer-projects-fact.md), [040](./decisions/040-boundary-of-the-handed-over-artifact.md)); první věta nenárokovaná, viz [`threat-model.md`](./threat-model.md) |
| **S5** přenositelné prostředí | *Flexibility* (Installability, Adaptability) | Jeden compose soubor se dvěma profily (rozhodnutí [039](./decisions/039-container-configuration-of-the-environment.md)) |
| **S6** pozorovatelnost a auditovatelnost | *Maintainability* (Analysability) + *Security* (Accountability) | Identifikátor běhu a strojově čitelný záznam u každého převodu (`architecture.md` §5.1) |
| **S7** uživatelská přívětivost | *Interaction capability* (Operability, User error protection, User assistance) | Statické obrazovky (rozhodnutí [032](./decisions/032-frontend-as-static-pages-without-a-build.md), [033](./decisions/033-shape-of-the-static-frontend-screens.md)), pětikrokový scénář v [`use-cases.md`](./use-cases.md) |

*Functional suitability* pokrývají vedle S2 hlavně funkční požadavky F1–F15; *Reliability* v části *Fault tolerance* nese návrat diagnostiky místo výjimky (rozhodnutí [010](./decisions/010-diagnostics-as-returned-data.md)). Sedm požadavků tedy pokrývá sedm z devíti charakteristik, a to bez natahování.

## Dvě charakteristiky bez požadavku

**Safety — nepoužije se, a je to v pořádku.** Charakteristika se ptá, co systém udělá, aby nezpůsobil újmu na zdraví, majetku nebo prostředí. Překladač ORM nic takového neřídí. Za zmínku stojí jediná souvislost: do *Safety* patří i podcharakteristiky *Operational constraint* a *Fail safe*, tedy tvary, ve kterých by se dala číst nenárokovaná první věta S4 (omezení CPU, paměti a času při spouštění cizího kódu). Řadíme ji ale pod *Security*, protože motivací není bezpečnost provozu, ale cizí kód jako hrozba.

**Compatibility — chybí, a chybí zrovna to hlavní.** Charakteristika má dvě podcharakteristiky, *Co-existence* a *Interoperability*, a interoperabilita znamená schopnost produktu fungovat spolu s cizím produktem. **U tohohle nástroje je to celý smysl existence:** vygenerovaný artefakt musí zapadnout do cizího projektu, který nástroj nikdy neuvidí. Přesto k ní seznam S1–S7 nemá jediný požadavek.

Že to není teoretická díra, dokládá rozhodovací stopa: dvakrát jsme na tu otázku narazili v praxi a pokaždé jsme si museli založit rozhodnutí bez požadavku, o který by se opřelo.

- **[028](./decisions/028-assembly-name-is-not-ours-to-invent.md)** — „název sestavení není náš, abychom ho vymýšleli".
- **[029](./decisions/029-database-connection-is-the-consumer-projects-fact.md)** — „připojení do databáze je fakt konzumentského projektu".

Obě jsou o tomtéž: kde končí náš výstup a začíná cizí projekt. To je doslovná definice *Interoperability*. Další otázky téhož druhu stály ve frontě — jmenné prostory, soubor projektu a jeho závislosti, konfigurace ekosystému, registrace v kontejneru, cílová verze jazyka — a odpověď na ně dnes dává [rozhodnutí 040](./decisions/040-boundary-of-the-handed-over-artifact.md), které pravidlo vyslovuje a nechává ho držet testem.

**Mezeru tedy zavíráme rozhodnutím, ne novým požadavkem.** `requirements.md` je zmražený snímek zadání vedoucího a dopisovat do něj S8 by znamenalo tvrdit, že tam bylo. Do textu práce patří tahle tabulka i s oběma nepokrytými charakteristikami: ukazuje, že seznam S1–S7 nebyl přijat naslepo, a *Co-existence* — souběh s jinými produkty na jednom stroji — zůstává mimo rozsah vědomě, protože nástroj je jedna webová aplikace a jedna databáze.

## K čemu je to dobré dál

Model kvality dává jazyk pro dvě věci, které se jinak popisují ad hoc. **Zúžení nároku** (§9) se dá vyslovit jako „charakteristika X v rozsahu podcharakteristiky Y", což je přesnější než próza. A **prázdné místo** je vidět dřív, než se do něj spadne: kdyby tahle tabulka existovala dřív, otázka po interoperabilitě by se položila před rozhodnutím 028, ne po něm.
