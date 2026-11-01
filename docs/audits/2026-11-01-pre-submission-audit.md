# Revize před odevzdáním, 2026-11-01

Revize jedné otázky: **stojí repozitář tam, kde ho schválený [záměr](../zamer.tex) a [specifikace](../specifikace.tex) chtějí mít, a zbývá do odevzdání opravdu jen verdikt soudce LDBC a odevzdání samo?** Postup je týž jako u revizí [2026-10-18](2026-10-18-whole-repository-audit.md) a [2026-10-30](2026-10-30-robustness-audit.md): úkoly záměru a požadavky specifikace proti nároku na třech místech (sekce *Guarantees* kořenového README, `architecture.md` §9, `traceability.md`), dokumenty proti pravidlům svého žánru, ověření spuštěním. Co revize našla, v téže změně i opravila; žádný nález nevolil mezi možnostmi, takže rozhodnutí nevzniklo.

Nálezy jsou číslované po kapitolách a označené závažností jako v předchozích revizích — **kritické**, **závažné**, **drobné**. Kapitola 4 odpovídá na otázku revize, kapitola 6 shrnuje, co z ní plyne.

## Zafixované verze a stav, ke kterému revize platí

Pracovní kopie ke dni 2026-11-01 na MIS3, commit **`25df739` „Write a CASE nested in an ELSE as one CASE for EclipseLink"**, strom čistý. Poslední značka `2.2.0` (2026-10-12); `<Version>` v `Directory.Build.props`, pole `version` v `CITATION.cff` i `openapi.json` nesou `2.2.0`. .NET SDK 10.0.403 na hostiteli (`global.json` pin `10.0.100` s `latestFeature`), SQL Server LocalDB s databázemi `ORMConvertorTests` a `ORMConvertorCaseStudies`; Docker neběžel, javová sada běžela v CI nad týmž commitem (kapitola 5).

Tabulka „Zafixované verze" v [`architecture.md`](../architecture.md) §1 souhlasí s `Directory.Packages.props`, `Directory.Build.props`, `global.json`, `JavaTests/pom.xml`, základními obrazy obou Dockerfilů i obrazem SQL Serveru v compose a v CI, řádek po řádku; verze, které specifikace jmenuje v kapitole *Platformy a technologie* (Dapper 2.1.79, NHibernate 5.7.0, EF Core 10.0.10, JDK 25, Jakarta Persistence 3.2, Hibernate 7.4.5, EclipseLink 5.0.0, MyBatis 3.5.19, SQL Server 2022), jsou ty připnuté.

---

## 1. Záměr

Pět úkolů řešitele vede [`traceability.md`](../traceability.md), *Úkoly záměru*, každý s tím, co ho plní a jak je zúžený; revize je prošla proti kódu a nároku a čte je stejně.

| Úkol záměru | Stav |
|---|---|
| 1 složitější mapovací konstrukce, komplexní identifikátory a cizí klíče | splněný (F1–F3 nárokované); dědičnost a komponenty jsou vyňatá oblast 2, vyslovená na všech třech místech |
| 2 doplňování a slučování metadat ze zdroje a katalogu | splněný (F4–F6 nárokované); jediný dialekt je vyslovená oblast 5 |
| 3 Hibernate, EclipseLink a MyBatis | splněný v užším rozsahu, které nárok vyslovuje (F7–F9: tři frameworky, ne ekosystém; dynamický příkaz MyBatisu; líné načtení v EclipseLinku) |
| 4 překlad read-only dotazů | splněný: 36 směrů, úniková cesta nativním SQL se záznamem, podmnožina celá v [`subset.md`](../subset.md) — „případy bez úplného nebo jednoznačného překladu", které záměr slibuje zdokumentovat, jsou jeho záporná půlka |
| 5 testovací a běhové prostředí pro ověřování a experimenty | splněný v ověřovací polovině celý (S5, oba profily compose, CI obou sad) a v experimentální v rozsahu T1–T3 (rozh. 183, 184); protokol srovnání s LLM stojí (rozh. 175, 189), experimenty samy specifikace podmínkou nedělá |

Očekávaný výsledek záměru — rozšířený proof of concept s dokumentací, kontejnerovou konfigurací a automatizovanými testy — v repozitáři stojí. Vědomou odchylku od věty „vhodné nástroje pro Java frameworky budou vybrány na základě analýzy" (vlastní parser Javy, JVM jen v sadě) traceability vede jménem s důvodem (rozh. 076). **Bez nálezu.**

## 2. Specifikace

### 2.1 Požadavky F1–F15, S1–S7, T1–T7 — bez nálezu

Stavy požadavků (nárokované, v užším rozsahu, vyňaté) souhlasí na všech třech místech a každé zúžení v README má protějšek v §9 i v traceability. Tři místa, kde se specifikace čte jinak, než zní, jsou vyslovená jako sporná čtení ([`subset.md`](../subset.md), část 3) nebo zúžení: „dynamicky parametrizované dotazy" F8 jako parametr (rozh. 084), T1 v obou zněních najednou (rozh. 174, 184) a „zobrazení mezireprezentace" z F14 a z kapitoly *Milníky / Výstupy*, které se nenárokuje ani neplánuje (rozh. 154) — na všech třech místech. F15 a T7 jsou vyňaté s odkazem na větu specifikace „podle časových možností" a na rozh. 171, T4–T6 na větu, že plné LLM experimenty podmínkou dokončení nejsou.

Ověřovací kritéria, která zadání dává čísly, drží sestavení (F7–F9 počty testů, F10 nejméně 30 překladů přes hranici, F12 `SuiteSizeTest`, T1 `CaseStudyCriteriaTest`) a revize je nepřepočítávala — běh sady v kapitole 5 je prošel.

### 2.2 §9 jmenoval příští MAJOR za „experimenty (F15, T1–T7)" — drobné

`architecture.md` §9 říkal, že příští MAJOR `3.0.0` patří Advisoru nad všemi frameworky a experimentům (F15, T1–T7). T1, T2 a T3 ale z vyňaté oblasti vystoupily v užším rozsahu (rozh. 183, 184) a kořenové README v *Beyond this version* mluví o experimentech *za* T1–T3. Věta přebírala rozh. 098 doslova, které je snímkem z doby před oběma rozhodnutími.

*Opraveno:* §9 říká „experimentům, které zbývají (F15, T4–T7)"; rozh. 098 se jako snímek nepřepisuje.

### 2.3 Kapitoly *Milníky / Výstupy* a *Harmonogram*

Co specifikace slibuje pod dokončenou .NET částí, analýzou, javovými frameworky a testy s experimenty, v repozitáři stojí a je doložené (kapitola 1 a 2.1); výjimkou je zobrazení mezireprezentace (rozh. 154, vyslovené) a základní srovnání Advisoru, které specifikace podmiňuje časem. Odevzdání samo — výsledky, finální dokumentace, obhajoba s živou ukázkou — je kapitola 4. Konec projektu je podle záměru 2026-12-01.

## 3. Dokumenty proti pravidlům žánru

### 3.1 Sedm odkazů v poli *Podklad* pěti rozhodnutí vedlo na neexistující soubory — drobné

Rozhodnutí 162, 179, 180, 185 a 196 odkazovala v řádku *Podklad* na soubory rozhodnutí 030, 067, 076, 084, 103 a 118 pod názvy, které parafrázují jejich nadpis místo opsaného názvu souboru (`076-java-frameworks-through-a-shared-jpa-layer.md` místo `076-java-wrappers-in-csharp-jvm-in-containers.md` a podobně). Číslo rozhodnutí bylo ve všech sedmi případech správné; u 030 to potvrzuje text 179, který z něj cituje příznak `IsVersion`, a text 030, který `[ConcurrencyCheck]` do rozsahu verze 1.0 bere.

*Opraveno:* cíle odkazů jsou opravené na skutečné soubory, text beze změny, stejně jako u rozh. 148 v revizi 2026-10-18 (nález 3.4); [`audits/README.md`](README.md), *Opravy zmražených souborů*, to zaznamenává.

### 3.2 Co drželo — bez nálezu

- Každý relativní odkaz a každá kotva ve všech souborech Markdown repozitáře vede na existující soubor a nadpis (po opravě 3.1; kontrola skriptem nad všemi 272 soubory).
- Rejstřík rozhodnutí má řádek pro každý soubor v `decisions/`; rejstříky `runs/`, `audits/` a `analysis/` jmenují každý soubor své složky.
- Každá testovací třída, kterou živé dokumenty jmenují (traceability, architecture, subset, README obou úrovní, přehled běhů), v sadě existuje — tři z nich jako třídy v souboru jiného jména (`LimitSeriesTest`, `LimitSeriesChildTest` v `Experiment/LimitSearchTest.cs`, `ExperimentSummaryTest` v `Experiment/ExperimentRunTest.cs`).
- Žádný sledovaný soubor nejmenuje soubor, který v repozitáři není; `ORMConvertorAPI/Dockerfile` kopíruje `.csproj` a zámek každého projektu řešení; v kódu není žádný `TODO` ani `FIXME`.
- Živé dokumenty nenesou vyprávění o průběhu ani počty, které další změna posune; verze nástroje stojí jen v `Directory.Build.props`, `CITATION.cff` a `openapi.json`.
- Značky pořadí v `open-items.md`: `Na řadě` nese jediná položka (verdikt soudce), `Potom` žádná — po této revizi dvě (kapitola 4).

### 3.3 Položka odevzdání říkala sama o sobě, že má být rozepsaná — drobné

`open-items.md` vedl odevzdání jedinou položkou *Odevzdání není rozepsané do položek*, která opisovala sliby specifikace a končila větou „je třeba to do seznamu rozepsat". Položka tak nebyla prací, kterou lze udělat a odškrtnout, a odpověď na otázku, co do odevzdání zbývá, z ní nešla přečíst.

*Opraveno:* oblast *Odevzdání* má čtyři položky, každou samostatnou (kapitola 4), a přehledová tabulka čtyři řádky.

## 4. Co zbývá do odevzdání

Odpověď na otázku revize: **ano, s jedním doplněním.** Jediná položka, bez které projekt podle specifikace dokončit nejde a která se dotýká nástroje, je verdikt soudce LDBC nad celou sadou SF 1 nad dnešními texty dotazů na cesty (`Na řadě`, beze změny). Všechno ostatní, co ze zadání v nároku stojí, stojí doložené; položky experimentů s LLM podmínkou nejsou, Advisor je sekundární (rozh. 171) a položky *Užitečné, ne nutné* nic neblokují.

Odevzdání samo se rozpadá do čtyř prací, které jsme zapsali do `open-items.md`, v tomto pořadí za verdiktem:

1. **Záznamy experimentů nad stromem vydání** (`Potom`). Specifikace slibuje u obhajoby výsledky experimentů s naměřenými ukazateli; nárok T2 a T3 je měřidlo a čísla jsou záznam verze. Poslední [záznam experimentálního běhu](../runs/2026-10-24-experiment.md) stojí nad `5caefc5`, nese buňky EclipseLinku, které rozh. 179 a 180 potom změnila, a předchází rozh. 196 a 201 i opravě řetězu množinových operací v JPQL, které výstup do EclipseLinku měnily také. To je doplnění k otázce revize: před vydáním se experimentální běh opakuje nad stromem, který ponese značku, a totéž se posoudí u běhu FIBEN. Záznam z 2026-10-24 sám o sobě chybný není — je datovaný a jmenuje commit —, proto drobné, ne závažné.
2. **Vydání `2.3.0`** (`Potom`) podle rozh. 098: obě sady v kontejnerech nad stromem vydání, číslo do `Directory.Build.props` a `CITATION.cff`, anotovaná značka; *Guarantees*, §9 a traceability projít proti sobě před značkou.
3. **Živá ukázka u obhajoby**: scénář nad běžící instancí z compose s příklady případových studií přes hranici ekosystémů, reprodukce sady jedním příkazem, záznamy běhů; scénář je podklad mimo repozitář.
4. **Odevzdání repozitáře**: veřejný repozitář se značkou, CI nad commitem značky zelená, `open-items.md` jen s tím, co po vydání zůstává.

## 5. Co bylo ověřeno spuštěním

- **Sestavení:** `dotnet build ORMConvertor.sln --configuration Release` nad `25df739` bez chyby.
- **.NET sada na hostiteli:** `dotnet test Tests/Tests.csproj --configuration Release` proti LocalDB: **13 912 testů, 0 selhaných, 12 092 prošlých, 1 820 přeskočených** za 3 min 30 s. Přeskočené patří do skupin, které [`runs/README.md`](../runs/README.md#velikost-sady-a-pokrytí) vyjmenovává — běh FIBEN bez `Fiben`, soudce LDBC a hloubky dotazů na cesty bez `LdbcSnb`, buňky `Omitted`, experimentální běh a běh cizích artefaktů bez adresáře, webový kanál bez `ORMCONVERTOR_LLM_RUN`; rozpis po skupinách revize nezachytila (výstup běhu držela jen jeho posledních čtyřicet řádků), a proto ho do přehledu běhů nezapisuje.
- **CI nad týmž commitem:** běh *ORMConvertor tests* nad `25df739` z 2026-11-01 skončil `success`, tedy obě úlohy — .NET sada s `ORMCONVERTOR_REQUIRE_TEST_DATABASE=1` i javová sada proti publikované instanci — zeleně; přečteno z GitHub API. Javová sada na stroji revize neběžela (Docker neběžel) a profil `test` compose revize nespouštěla, takže řádek velikosti sady za tento strom nevzniká.
- **Odkazy a kotvy:** skript mimo repozitář nad všemi soubory Markdown; 7 nálezů před opravou 3.1, 0 po ní.

## 6. Co z revize plyne

### Opravy, provedené v téže změně

- §9: příští MAJOR za „experimenty, které zbývají (F15, T4–T7)" (2.2).
- Cíle sedmi odkazů v poli *Podklad* rozhodnutí 162, 179, 180, 185 a 196 (3.1); záznam v `audits/README.md`.
- Odevzdání rozepsané do čtyř položek `open-items.md` se značkami `Potom` na prvních dvou (3.3, 4).

### Rozhodnutí

Žádné: žádný nález nevolil mezi možnostmi.

### Práce a delší horizont

- Verdikt soudce LDBC (`Na řadě`, beze změny) a za ním čtyři položky oblasti *Odevzdání* (kapitola 4).
- Experimentální běh nad stromem vydání před značkou `2.3.0`; u běhu FIBEN posoudit, zda ho změny výstupu od 2026-10-24 zneplatnily.
