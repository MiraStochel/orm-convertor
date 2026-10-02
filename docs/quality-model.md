# Systémové požadavky proti modelu kvality ISO/IEC 25010:2023

Vztah S1–S7 k normě, ne nový požadavek ([`requirements.md`](./requirements.md) je zmražené); plnění nese [`traceability.md`](./traceability.md), nárok `architecture.md` §9. Norma 2023 má devět charakteristik (proti 2011 přibyla *Safety*; *usability* → *Interaction capability*, *portability* → *Flexibility*). Názvy podcharakteristik jsou ze sekundárních zdrojů — před citací v práci je ověřit proti normě.

| Požadavek | Charakteristika (podcharakteristika) | Čím je nesený |
|---|---|---|
| **S1** modulární rozšiřitelnost | *Maintainability* (Modularity, Modifiability), *Flexibility* (Adaptability) | wrapper na framework, rozhraní v `AbstractWrappers`, deskriptor (rozh. [009](./decisions/009-target-framework-descriptor.md)); nový framework nemění orchestraci |
| **S2** determinismus | *Functional suitability* (Functional correctness), *Reliability* (Faultlessness) | buildery nezávislé na prostředí, zafixované verze (rozh. [013](./decisions/013-target-framework-versions.md), [034](./decisions/034-central-version-management.md)), verze nástroje v záznamu běhu (rozh. [098](./decisions/098-the-number-is-decided-once-per-release.md)) |
| **S3** výkon překladu | *Performance efficiency* (Time behaviour) | 100 entit + 100 dotazů do 30 s, katalog vykázaný zvlášť |
| **S4** izolace a bezpečnost | *Security* (Confidentiality, Resistance) | druhá věta konstrukcí (rozh. [029](./decisions/029-database-connection-is-the-consumer-projects-fact.md), [040](./decisions/040-boundary-of-the-handed-over-artifact.md)); první nenárokovaná ([`threat-model.md`](./threat-model.md)) |
| **S5** přenositelné prostředí | *Flexibility* (Installability, Adaptability) | compose se dvěma profily (rozh. [039](./decisions/039-container-configuration-of-the-environment.md)) |
| **S6** pozorovatelnost | *Maintainability* (Analysability), *Security* (Accountability) | identifikátor běhu a strojový záznam (`architecture.md` §5.1) |
| **S7** přívětivost | *Interaction capability* (Operability, User error protection, User assistance) | statické obrazovky (rozh. [032](./decisions/032-frontend-as-static-pages-without-a-build.md), [033](./decisions/033-shape-of-the-static-frontend-screens.md)), pětikroková cesta UC4 ([`use-cases.md`](./use-cases.md)) |

*Functional suitability* nesou hlavně F1–F15; *Reliability* v části *Fault tolerance* nese diagnostika vracená místo výjimky (rozh. [010](./decisions/010-diagnostics-as-returned-data.md)). Pokryto je sedm z devíti charakteristik.

## Dvě charakteristiky bez požadavku

- **Safety** se nepoužije — nástroj neřídí nic, čím by způsobil újmu; limity cizího kódu (první věta S4) řadíme pod *Security*, ne pod *Operational constraint*.
- **Compatibility** požadavek nemá, ačkoli *Interoperability* je smysl nástroje; mezeru zavírají rozhodnutí [028](./decisions/028-assembly-name-is-not-ours-to-invent.md), [029](./decisions/029-database-connection-is-the-consumer-projects-fact.md) a [040](./decisions/040-boundary-of-the-handed-over-artifact.md), ne dopsaný S8. *Co-existence* je mimo rozsah vědomě (jedna aplikace, jedna databáze).
