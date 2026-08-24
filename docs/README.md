# Dokumentace

Autoritativní a živě udržovaný popis projektu. **Členěný podle žánru, ne podle času** (rozhodnutí [007](decisions/007-documentation-structure.md)): každý dokument odpovídá na jednu otázku a jinou zodpovídat nemá. Changelog tu proto není — „co se změnilo kdy" umí git historie mechanicky a zadarmo.

| Dokument | Odpovídá na otázku | Životní cyklus |
|---|---|---|
| [`architecture.md`](architecture.md) | Jak nástroj funguje **dnes**. | živé; aktualizuje se s každou změnou chování |
| [`../ORMConvertor/README.md`](../ORMConvertor/README.md) | Jak se nástroj **spouští, nasazuje, konfiguruje a testuje** a co je na které cestě ověřené — nasazovací pohled architektury (rozhodnutí [057](decisions/057-deployment-view-in-the-operating-manual.md)). Jediný živý dokument sady mimo `docs/`, a proto anglicky. | živé; aktualizuje se se změnou běhové cesty |
| [`open-items.md`](open-items.md) | Co **zbývá** — otevřená rozhodnutí i rozhodnutá, ale nenapsaná práce. Značka `Na řadě` říká, kde se pokračuje. | živé; položka mizí, jakmile je hotová |
| [`decisions/`](decisions/README.md) | **Proč** je nástroj takový, jaký je. Jedno rozhodnutí = jeden soubor. | neměnné; mění se jen pole `Stav` |
| [`audits/`](audits/README.md) | Co jsme **kdy věděli**. Datované revize stavu k jednomu dni. | neměnné |
| [`analysis/`](analysis/README.md) | Jak se chovají **frameworky samotné**. Podklad pro rozhodnutí a materiál pro analytickou část práce. | přibývá podle potřeby |
| [`threat-model.md`](threat-model.md) | Čemu je nástroj **vystavený** — vstupní body, co po nich přichází a co ho chrání. | živé |
| [`use-cases.md`](use-cases.md) | **Kdo** nástroj používá, v jaké situaci a co tím řeší. Vrstva nad požadavky. | živé |
| [`requirements.md`](requirements.md) | Co **zadal vedoucí** — požadavky číslované F1–F15, S1–S7, T1–T7. | zmražené |
| [`traceability.md`](traceability.md) | **Kde** je který požadavek splněný a co to dokazuje. | živé; řádek se mění s chováním |
| [`quality-model.md`](quality-model.md) | Jak S1–S7 sedí na **model kvality** ISO/IEC 25010:2023 a co v něm nemá požadavek. | živé; mění se zřídka |
| [`baseline.md`](baseline.md) | V jakém stavu byl projekt **při převzetí**. | zmražené |

Čtyři dokumenty uprostřed tabulky drží dohromady jednu vrstvu: scénář → požadavek → důkaz, a vedle nich model kvality jako pohled z normy. Nárok verze stojí ve dvou patrech: když se `traceability.md` rozejde s `architecture.md` §9, platí §9, a když se §9 rozejde s anglickou sekcí *Guarantees* v kořenovém [`README.md`](../README.md), platí README — kanonické znění nároku je tam, protože jeho adresátem je konzument nástroje, kdežto §9 nese totéž česky i s odůvodněním.

Dvě věci, které z toho členění plynou a pletou se nejčastěji. **Nálezy auditu nejsou seznamem toho, co zbývá** — ten je jen v `open-items.md`, a audit se cituje jen tam, kde nese plnější odůvodnění, než se do položky vejde. A **mezera v kódu není automaticky chyba**: obvykle za ní stojí zdůvodněný blokátor, který popisuje položka v `open-items.md` a rozhodnutí, na které odkazuje.

## Pravidla, podle kterých tahle sada vzniká

- **Volba se nejdřív zapíše, pak naprogramuje.** Změna, která *volí* mezi možnostmi, začíná novým souborem v `decisions/` a řádkem v jeho rejstříku. Ne každá změna je volba: provedení už rozhodnutého, oprava chyby, doplnění testu k existujícímu chování ani přijetí licence rozhodnutí nejsou. Zkouška je otázka, jestli se čtenář později zeptá *proč tohle a ne něco jiného*.
- **Rozhodnutí se nepřepisují.** Změněná volba znamená nový soubor a starému stav `nahrazeno NNN`; opravit na místě se smí jen doplnění případu, na který se nemyslelo (stav `revidováno`), a jen dokud podle rozhodnutí nevznikl kód. Podrobněji [`decisions/README.md`](decisions/README.md).
- **Každá změna chování končí v `architecture.md`** — a jde-li o spuštění, nasazení, konfiguraci nebo testy, v [`../ORMConvertor/README.md`](../ORMConvertor/README.md) (rozhodnutí [057](decisions/057-deployment-view-in-the-operating-manual.md)) — a odbavená položka mizí z `open-items.md`. Bez toho je dokumentace nesprávná, ne jen neúplná.
- **Zmražené dokumenty se nepřepisují vůbec** — `requirements.md`, `baseline.md` a hotové audity jsou snímky ke dni svého vzniku.
- **Žánr se nemíchá.** Do `architecture.md` nepatří odůvodnění volby, do `decisions/` popis současného stavu a do `audits/` seznam zbývající práce.

Obsah tohoto adresáře je česky; zbytek repozitáře — kód, komentáře, README — anglicky. Zafixované verze frameworků jsou kanonicky v tabulce v [`architecture.md`](architecture.md), části „Zafixované verze"; audity i analýzy je uvádějí jen jako snímek ke dni svého vzniku.
