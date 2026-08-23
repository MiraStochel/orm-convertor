# Plnění požadavků a trasovatelnost

**Účel:** odpověď na otázku „požadavek F5 — kde je splněný a co to dokazuje?". Rejstřík zadání ([`requirements.md`](./requirements.md)) proti kódu a testům, v jednom směru: **požadavek → implementace → důkaz**. Opačný směr — rozhodnutí → požadavek — nese tabulka v [`decisions/README.md`](./decisions/README.md); scénáře, ze kterých požadavky vzešly, [`use-cases.md`](./use-cases.md).

Je to tvrzení o **současném stavu**, tedy týž žánr jako [`architecture.md`](./architecture.md); samostatný soubor je jen proto, že architektura má přes 130 kB a rejstřík se v ní nedá najít. Kanonický je nárok verze v `architecture.md` §9 — když se tahle tabulka s ním rozejde, platí §9 a chyba je tady; nad §9 samotným pak platí anglická sekce *Guarantees* v kořenovém [`README.md`](../README.md), jak říká úvod §9.

**Slovník stavu** je týž tříhodnotový jako v §9:

- **nárokované** — verze na to slibuje spoleh v celém rozsahu, jak požadavek zní;
- **nárokované v užším rozsahu** — slibuje méně, než jak požadavek čte samo zadání, a zúžení je v §9 vyslovené;
- **vyňaté** — verze netvrdí nic. Vyňato není totéž co chybějící: oblast může v repozitáři být a běžet.

Stav neříká „hotovo/nehotovo", ale **co o tom tvrdíme**. Číslo požadavku se v testech samotných vyskytuje jen tam, kde je zrovna po ruce, a nerovnoměrně; závazná vazba je tahle tabulka, ne komentáře v kódu.

---

## Funkční požadavky

| # | Stav | Kde je implementovaný | Co to dokazuje |
|---|---|---|---|
| **F1** — komplexní identifikátory v IR | nárokované | `Model` — `PrimaryKey`, pořadí a typy částí klíče, strategie generování (rozhodnutí [011](./decisions/011-key-generation-strategy-vocabulary.md)); `architecture.md` §4.2 | `Combined/CompositeKeyTest`, `Combined/PrimaryKeyTest`, klíče o dvou až čtyřech částech ve schématu `Database/TestSchemaFixtureTest` |
| **F2** — překlad klíčů ve všech frameworcích | nárokované | parsery a buildery všech tří wrapperů; ploché vykreslení a identitní členy (rozhodnutí [006](./decisions/006-flat-composite-key-rendering.md)), klíčová třída (rozhodnutí [031](./decisions/031-key-class-as-declaration-of-key-parts.md)) | `NHibernate/NHibernateCompositeIdTest`, `NHibernate/NHibernateEmbeddedKeyClassTest`, `EFCore/EFCoreKeyStrategyTest`, `Combined/EnforcedMembersTest`; 3. stupeň ověření `Verification/EmbeddedKeyClassVerificationTest` — výstup se přeloží a framework ho přijme |
| **F3** — cizí klíče na složené identifikátory | nárokované | `Relation` na `EntityMap` (rozhodnutí [001](./decisions/001-entity-reference-by-name.md)), vykreslení FK (rozhodnutí [012](./decisions/012-foreign-key-rendering.md)), N:M jako spojovací entita (rozhodnutí [005](./decisions/005-many-to-many-as-explicit-junction-entity.md)) | `Combined/RelationModelTest`, `Combined/JunctionEntitySynthesisTest`, `NHibernate/NHibernateKeyManyToOneTest`, `EFCore/EFCoreForeignKeyTest`, `Verification/ManyToManyJunctionVerificationTest`, `Verification/KeyManyToOneVerificationTest`. **Kritérium F3 žádá spuštěný test pro každý typ vztahu** — to je 4. stupeň ověření a ten zástupce nemá ([`open-items.md`](./open-items.md)) |
| **F4** — načtení metadat z katalogu | nárokované | `DatabaseCatalog` — `SqlServerCatalogReader`, jediné místo, které čte databázi (rozhodnutí [015](./decisions/015-mapping-fact-completion-from-the-catalog.md)) | `Catalog/SqlServerCatalogReaderTest` proti skutečnému SQL Serveru: typy, délky, přesnost, nullabilita, identita, pořadí čtyřdílného klíče, vícesloupcové cizí klíče, spojovací tabulky. Očekávanou odpovědí je skript schématu (rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md)), takže podíl podle F4 je měřitelný — jako číslo se ale nevykazuje |
| **F5** — sloučení metadat z více zdrojů | nárokované | fáze doplňování mezi parserem a builderem; priorita zdrojů (rozhodnutí [017](./decisions/017-source-precedence-for-mapping-facts.md), pro klíč [036](./decisions/036-primary-key-under-source-precedence.md)) | `Combined/SourcePrecedenceTest` — 13 scénářů včetně konfliktu nullability, názvu sloupce, typu a primárního klíče, tedy všech čtyř, které kritérium F5 jmenuje; `Combined/DiagnosticsTest` |
| **F6** — úplné mapování z neúplného vstupu | nárokované | doplnění z katalogu nad dapperovským vstupem; `architecture.md` §5.2 | `Verification/DapperToEFCoreVerificationTest`, `Verification/DapperToNHibernateVerificationTest` (2.–3. stupeň), `Catalog/CatalogCompletionTest`, `Catalog/CatalogManyToManyDetectionTest`, `Catalog/CatalogInverseCollectionTest` |
| **F7** — Hibernate | vyňaté | — | Dotazová větev .NET je jeho předpokladem (rozhodnutí [022](./decisions/022-native-query-syntax-in-builders.md)–[027](./decisions/027-query-artifact-verification.md)), sám požadavek verze netvrdí |
| **F8** — MyBatis | vyňaté | — | — |
| **F9** — EclipseLink | vyňaté | — | — |
| **F10** — cross-ecosystem překlad | vyňaté | — | — |
| **F11** — validace cílových artefaktů | nárokované v užším rozsahu | kontrola úplnosti před generováním a strukturovaná diagnostika (rozhodnutí [004](./decisions/004-unexpressible-facts-as-warnings.md), [010](./decisions/010-diagnostics-as-returned-data.md)); stupně ověření (rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md), [027](./decisions/027-query-artifact-verification.md)) | `Combined/DiagnosticsTest` včetně `EmptyConversionTest` — převod, ze kterého nic nevyšlo, to hlásí záznamem místo ticha (rozhodnutí [045](./decisions/045-a-conversion-that-produced-nothing-says-so.md)) —, `Combined/InventedFactsTest`, `Combined/EnforcedMembersTest`, `NHibernate/NHibernateUnreadElementTest`; celá složka `Verification/`; chybová odpověď rozhraní má tvar, který dokument slibuje (`Api/ConvertEndpointTest`, rozhodnutí [044](./decisions/044-error-response-as-problem-details.md)). **Zúžení:** syntaktická správnost se ověřuje testy, ne za běhu — překladová cesta cizí kód nekompiluje (§9) |
| **F12** — testovací sada pro Javu | vyňaté | — | — |
| **F13** — diferenční ověření výsledků | vyňaté | — | Ověření tvaru dotazu (rozhodnutí 027) je jeho .NET předstupeň, ne jeho splnění |
| **F14** — celé třídy a dávkové vstupy v UI | nárokované v užším rozsahu | vícejednotkový převod, výstup po souborech, `/archive`, obrazovky podle rozhodnutí [033](./decisions/033-shape-of-the-static-frontend-screens.md) | `Combined/TranslationPerformanceTest` — jeden převod nad 200 jednotkami; `Combined/ApiContentContractTest`; `Api/ArchiveEndpointTest` — položky se z archivu rozbalí pod týmiž názvy a s týmž obsahem. **Zúžení:** diagnostika se váže k entitě a vlastnosti, ne ke zdrojovému souboru, a zobrazení mezireprezentace verze nenárokuje (§9) |
| **F15** — výběr cíle a optimalizace | vyňaté | `Advisor` (ILP přes GLPK) a `AdvisorBenchmarking` běží a jsou v rozhraní | Netestované vcelku (§9, oblast 1) — proto vyňaté |

## Systémové požadavky

| # | Stav | Kde je implementovaný | Co to dokazuje |
|---|---|---|---|
| **S1** — modulární rozšiřitelnost | nárokované | wrapper na framework, společné rozhraní v `AbstractWrappers`, deskriptor cíle (rozhodnutí [009](./decisions/009-target-framework-descriptor.md)); sdílené čtení v `LinqParsing` a `CSharpEntityParsing` (rozhodnutí [026](./decisions/026-home-of-shared-query-reading.md)) | `Combined/TargetFrameworkDescriptorTest` — framework přidaný bez deskriptoru neprojde; `Combined/EnforcedMembersTest` váže deklaraci a emisi (rozhodnutí [037](./decisions/037-enforced-member-binding-held-by-the-test.md)). Strukturálně: wrappery nereferencují `DatabaseCatalog` |
| **S2** — determinismus a opakovatelnost | nárokované | buildery bez závislosti na prostředí; zafixované verze (rozhodnutí [013](./decisions/013-target-framework-versions.md), [034](./decisions/034-central-version-management.md)); verze nástroje v záznamu (rozhodnutí [041](./decisions/041-versioning-and-release.md)) | `Combined/RunRecordTest` — dva běhy nad týmž vstupem dávají bajtově shodné artefakty i shodné záznamy; `NHibernate/NHibernateEmbeddedKeyClassTest` (pořadí vstupu nerozhoduje); `Api/ConvertEndpointTest` — artefakt vrácený po drátě je bajtově týž jako z přímého volání orchestrace, takže determinismus nekončí na serializaci (rozhodnutí [043](./decisions/043-rest-contract-guarded-over-http.md)) |
| **S3** — výkon překladu | nárokované | jednofázová pipeline bez kompilace na překladové cestě | `Combined/TranslationPerformanceTest` — 100 entit a 100 dotazů EF Core → NHibernate proti třicetivteřinovému limitu; čtení katalogu se měří odděleně a vydává se v `CatalogReadTime` |
| **S4** — izolace a bezpečnost | první věta **vyňatá**, druhá **nárokovaná** | druhá věta konstrukcí: žádný builder nevypisuje připojení (rozhodnutí [029](./decisions/029-database-connection-is-the-consumer-projects-fact.md), [040](./decisions/040-boundary-of-the-handed-over-artifact.md)) | `Combined/ArtifactCarriesNoCredentialsTest` ve všech devíti směrech, `Combined/ConsumerProjectFactsTest`. **První věta** — izolace spouštění s limity CPU, paměti a času — se nenárokuje; co z ní plyne, popisuje [`threat-model.md`](./threat-model.md) |
| **S5** — přenositelné prostředí | nárokované v užším rozsahu | jeden compose soubor se dvěma profily (rozhodnutí [039](./decisions/039-container-configuration-of-the-environment.md)), `ORMConvertorAPI/Dockerfile`, `database.Dockerfile` | `docker compose --profile test run --rm tests` reprodukuje celou sadu na stroji, kde není nic než Docker; CI běží proti SQL Serveru s `ORMCONVERTOR_REQUIRE_TEST_DATABASE=1`, takže tiché přeskočení neprojde. Kontejnerový běh z 2026-08-23 proběhl nad **dnešní** sadou — celý, bez jediného přeskočení a nad týmž počtem testů, jaký uvádí §6.2 (`architecture.md` §6.4). **Zúžení:** javové projekty ani experimentální pipeline neexistují, takže není co kontejnerizovat (§9) |
| **S6** — pozorovatelnost | nárokované | `ConversionResult` — identifikátor běhu, verze nástroje a obou frameworků, záznamy, stav katalogu; `architecture.md` §5.1 | `Combined/RunRecordTest`; `Api/ConvertEndpointTest` — záznam dorazí přes HTTP kompletní, s verzí ze sestavení a čerstvým identifikátorem u každého volání; `Api/OpenApiDocumentTest` — dokument OpenAPI nese totéž číslo verze. Verze frameworků pocházejí z deskriptorů, takže záznam nemůže tvrdit nic jiného než generátor (rozhodnutí [013](./decisions/013-target-framework-versions.md)) |
| **S7** — uživatelská přívětivost | nárokované v užším rozsahu | statické stránky bez buildu (rozhodnutí [032](./decisions/032-frontend-as-static-pages-without-a-build.md)), podoba obrazovek [033](./decisions/033-shape-of-the-static-frontend-screens.md); pětikrokový scénář popisuje [`use-cases.md`](./use-cases.md), UC4 | `Combined/ApiContentContractTest` — každá jednotka, kterou rozhraní vyžaduje, má ukázku a jazyk, který zdroj umí přečíst; `Api/RestContractTest` a `Api/ArchiveEndpointTest` — táž vazba přes HTTP a stažení celého výstupu jako ZIP (rozhodnutí [043](./decisions/043-rest-contract-guarded-over-http.md)). **Zúžení:** validace na klientovi je pomocník, ne brána; chyba na úrovni řádku platí pro vstupní syntaxi (§9). Automatizovaný test **frontendu** neexistuje (rozhodnutí [032](./decisions/032-frontend-as-static-pages-without-a-build.md)); REST rozhraní pod ním hlídané je |

## Experimentální požadavky

Všechny jsou **vyňaté** — verze netvrdí T1–T7 vůbec (§9, oblast 6). Sloupec „co je hotové" proto neříká „splněno", ale „co z toho v repozitáři existuje".

| # | Co z toho existuje |
|---|---|
| **T1** — reálná případová studie | nic; scénář popisuje [`use-cases.md`](./use-cases.md), UC5 |
| **T2** — matice překladů | devět .NET směrů v rozsahu projekce, filtrace, join, agregace a řazení: `Combined/QueryMatrixTest`, `Combined/QueryTargetShapeTest`, `Verification/QueryVerificationTest`. Chybí javová strana a kategorie, které mezireprezentace nenese (stránkování, poddotazy, množinové operace) |
| **T3** — metriky korektnosti | čtyřstupňový model ověření (rozhodnutí [016](./decisions/016-generated-artifact-verification-levels.md)) dává, co se dá počítat; jako čísla se dnes nevykazují |
| **T4** — LLM baseline | nic |
| **T5** — RAG / agentní varianta | nic |
| **T6** — ablace | nic |
| **T7** — baseline optimalizace | ILP model v `Advisor` (GLPK přes P/Invoke) běží; heuristiky ani srovnání nejsou, testy žádné |

---

## Kritéria ověření, která zatím nikdo nespočítal

Kritéria u F1–F15 jsou napsaná jako měřitelná a u většiny z nich měření existuje. Tři jsou výjimka a je poctivější je jmenovat než je nechat čtenáře hledat:

- **F3** žádá „alespoň jeden zkompilovaný a **spuštěný** test pro každý typ vztahu". Máme zkompilované a frameworkem přijaté (3. stupeň), ne spuštěné proti databázi (4. stupeň) — položka v [`open-items.md`](./open-items.md).
- **F4** žádá „správně získáno ≥ 95 % sledovaných metadat". Očekávaná odpověď existuje (skript schématu), takže podíl spočítat lze; jako číslo se nikde nevykazuje.
- **T3** je celý o podílech, a ty se budou počítat až v textu práce.

Vedle toho platí, že **kolik kódu testy pokrývají, se od 2026-08-22 měří** — `dotnet test --collect:"XPlat Code Coverage"`, v CI při každém běhu. Naměřená čísla nese `architecture.md` §6.2; tady schválně nejsou, aby se dvě místa nerozcházela.

## Jak se to udržuje

Tabulka je součástí povinného uzavíracího kroku: změna chování aktualizuje `architecture.md` a **řádek zde**, pokud se dotkne toho, co požadavek tvrdí nebo čím se to dokazuje. Nový test do tabulky patří jen tehdy, když je *důkazem* požadavku — ne každý test jím je, a seznam všech testů je adresář `Tests/`.
