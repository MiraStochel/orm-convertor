# Audity

Datované revize stavu k jednomu dni. Audit je **snímek, ne živý dokument**: zpětně se nepřepisuje ani neaktualizuje, takže ho lze číst jako doklad toho, co jsme kdy věděli a proti čemu jsme rozhodovali.

Z toho plyne, jak se audit čte. **Nálezy v něm nejsou seznamem toho, co zbývá.** Co z auditu vzešlo, se rozděluje do tří míst (rozhodnutí [007](../decisions/007-documentation-structure.md)): oprava jde do kódu a do [`architecture.md`](../architecture.md), věc, kterou je potřeba nejdřív rozmyslet, do [`decisions/`](../decisions/README.md), a zbytek do [`open-items.md`](../open-items.md). **Otázku „co zbývá" zodpovídá `open-items.md`, ne tenhle adresář.** Audit se cituje jen tam, kde nese plnější odůvodnění, než se do položky vejde.

Struktura souboru: zafixované verze a stav, ke kterému audit platí → nálezy číslované po kapitolách a označené závažností → závěrečná kapitola, která je dělí na opravy, potřebná rozhodnutí a delší horizont.

| Datum | Audit | Na co mířil | Kde žije to, co z něj zbylo |
|---|---|---|---|
| [2026-08-02](2026-08-02-post-step-4-audit.md) | Stav po kroku 4 | kód po krocích 1–4, po zafixování verzí frameworků a po analýze tří .NET ORM | dvě položky v `open-items.md` ho uvádějí jako podklad — sjednocení ADO.NET provideru (kap. 3.4.2) a poddotazy s množinovými operacemi (kap. 8); zbytek je odbavený a promítnutý do rozhodnutí a `architecture.md` |
| [2026-08-15](2026-08-15-documentation-coherence-audit.md) | Soudržnost dokumentace | `docs/` jako celek: sedí spolu rozhodnutí, `architecture.md` a `open-items.md` navzájem i se stavem kódu | opravy provedené v dotčených souborech, včetně přeznačení experimentálních požadavků E→T; jeho otázku o částečném nahrazení uzavřel stav `nahrazeno 015` u rozhodnutí [008](../decisions/008-database-as-metadata-source.md), takže slovník stavů zůstal tříhodnotový |
| [2026-08-21](2026-08-21-version-1-0-readiness-audit.md) | Připravenost verze 1.0 | soudržnost **tvrzení o verzi** s kódem a otevřenými položkami před poslední položkou vydání | tři rozhodnutí, která si vyžádal ([035](../decisions/035-nhibernate-collections-declared-by-interface.md), [036](../decisions/036-primary-key-under-source-precedence.md), [037](../decisions/037-enforced-member-binding-held-by-the-test.md)), a hranice záruk zapsaná jako stav v `architecture.md` §9 |

Audit z 2026-08-02 vznikl ještě za předchozí struktury dokumentace, kterou nahradilo rozhodnutí [007](../decisions/007-documentation-structure.md); zmínky o changelogu a design docech v něm popisují tehdejší uspořádání a soubor se kvůli nim nepřepisuje.

Verze frameworků jsou kanonicky v tabulce v [`architecture.md`](../architecture.md), části „Zafixované verze". Audity je uvádějí jen jako snímek ke dni svého vzniku.
