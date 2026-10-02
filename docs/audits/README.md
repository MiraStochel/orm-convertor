# Audity

Datované revize stavu k jednomu dni — **snímek, který se zpětně nepřepisuje**. Nálezy nejsou seznam zbývající práce: oprava jde do kódu a [`architecture.md`](../architecture.md), volba do [`decisions/`](../decisions/README.md), zbytek do [`open-items.md`](../open-items.md) (rozh. [007](../decisions/007-documentation-structure.md)). Soubor: zafixované verze a stav → nálezy po kapitolách se závažností → opravy, potřebná rozhodnutí, delší horizont.

| Datum | Audit | Co z něj vzešlo |
|---|---|---|
| [2026-08-02](2026-08-02-post-step-4-audit.md) | Stav po kroku 4 (kód, verze, analýza .NET ORM) | odbaveno; v `open-items.md` zbývá sjednocení ADO.NET provideru (kap. 3.4.2). Popisuje strukturu dokumentace před rozh. 007. |
| [2026-08-15](2026-08-15-documentation-coherence-audit.md) | Soudržnost dokumentace | opravy vč. přeznačení E→T; stav `nahrazeno 015` u [008](../decisions/008-database-as-metadata-source.md) |
| [2026-08-21](2026-08-21-version-1-0-readiness-audit.md) | Připravenost verze 1.0 | rozh. [035](../decisions/035-nhibernate-collections-declared-by-interface.md), [036](../decisions/036-primary-key-under-source-precedence.md), [037](../decisions/037-enforced-member-binding-held-by-the-test.md); hranice záruk v §9 |
| [2026-08-23](2026-08-23-post-release-1-1-0-audit.md) | Stav po vydání 1.1.0 (nedoložené, nepopsané, zbytečné) | rozh. [042](../decisions/042-measured-benchmark-output-out-of-git.md), [043](../decisions/043-rest-contract-guarded-over-http.md); v `open-items.md` vynucení stylu a reprodukovatelnost sestavení (kap. 8.4). Git ověřený, build ani testy neběžely (kap. 9). |
| [2026-09-21](2026-09-21-pre-release-2-0-0-audit.md) | Připravenost vydání 2.0.0 | rozh. [095](../decisions/095-a-dated-run-record-names-its-commit.md), [096](../decisions/096-a-rule-of-the-paper-is-cited-where-it-argues.md), [097](../decisions/097-an-exception-type-lives-where-it-is-thrown.md), později [101](../decisions/101-a-join-along-an-association-path-is-derived-from-the-relation.md); v `open-items.md` plocha entitní báze a trvalý identifikátor vydání. Ověřený spuštěním (kap. 7); kap. 8 nese revizi celého repozitáře. |

## Opravy zmražených souborů

- **2026-08-21, kap. 5:** `TestSchemaFixture.OpenConnection()` volajícího má (`TestSchemaFixtureTest`), jen žádný nespouští generovaný artefakt; závěr *4. stupeň nemá zástupce* tím nepadá.
- **2026-09-21, kap. 9:** nález 5.1 (`QueryBuilderException`) není oprava, ale volba — 053 typ v `Model` nechalo vědomě; rozhodlo [097](../decisions/097-an-exception-type-lives-where-it-is-thrown.md).
- **Rozhodnutí [070](../decisions/070-a-parser-refuses-what-would-change-the-row-set.md)** cituje nahrazené 041; [069](../decisions/069-major-marks-a-milestone-not-a-break.md) kritérium PATCH přenáší beze změny, takže závěr platí (revize 2026-09-21, nález 4.1).
- **Commit `a975a41`** přeznačil E→T v auditech 2026-08-02 a 2026-08-15 a u druhého přepsal citaci a řádek *Oprav* (vypadly cíle „v repozitáři i v souborech projektu"). Dodatkem po zmrazení prošly i `requirements.md` (`1b9af82`) a `baseline.md` (`3326336`), s odůvodněním v auditu 2026-08-15.

Verze frameworků jsou kanonicky v `architecture.md`, „Zafixované verze"; audity je uvádějí jako snímek.
