# Dokumentace

Autoritativní a živě udržovaný popis projektu. **Členěný podle žánru, ne podle času** (rozhodnutí [007](decisions/007-documentation-structure.md)): každý dokument odpovídá na jednu otázku a jinou zodpovídat nemá. Changelog tu proto není — „co se změnilo kdy" umí git historie mechanicky a zadarmo.

| Dokument | Odpovídá na otázku | Životní cyklus |
|---|---|---|
| [`architecture.md`](architecture.md) | Jak nástroj funguje **dnes**. | živé; aktualizuje se s každou změnou chování |
| [`open-items.md`](open-items.md) | Co **zbývá** — otevřená rozhodnutí i rozhodnutá, ale nenapsaná práce. Značka `Na řadě` říká, kde se pokračuje. | živé; položka mizí, jakmile je hotová |
| [`decisions/`](decisions/README.md) | **Proč** je nástroj takový, jaký je. Jedno rozhodnutí = jeden soubor. | neměnné; mění se jen pole `Stav` |
| [`audits/`](audits/README.md) | Co jsme **kdy věděli**. Datované revize stavu k jednomu dni. | neměnné |
| [`analysis/`](analysis/README.md) | Jak se chovají **frameworky samotné**. Podklad pro rozhodnutí a materiál pro analytickou část práce. | přibývá podle potřeby |
| [`requirements.md`](requirements.md) | Co **zadal vedoucí** — požadavky číslované F1–F15, S1–S7, T1–T7. | zmražené |
| [`baseline.md`](baseline.md) | V jakém stavu byl projekt **při převzetí**. | zmražené |

Dvě věci, které z toho členění plynou a pletou se nejčastěji. **Nálezy auditu nejsou seznamem toho, co zbývá** — ten je jen v `open-items.md`, a audit se cituje jen tam, kde nese plnější odůvodnění, než se do položky vejde. A **mezera v kódu není automaticky chyba**: obvykle za ní stojí zdůvodněný blokátor, který popisuje položka v `open-items.md` a rozhodnutí, na které odkazuje.

Obsah tohoto adresáře je česky; zbytek repozitáře — kód, komentáře, README — anglicky. Zafixované verze frameworků jsou kanonicky v tabulce v [`architecture.md`](architecture.md), části „Zafixované verze"; audity i analýzy je uvádějí jen jako snímek ke dni svého vzniku.
