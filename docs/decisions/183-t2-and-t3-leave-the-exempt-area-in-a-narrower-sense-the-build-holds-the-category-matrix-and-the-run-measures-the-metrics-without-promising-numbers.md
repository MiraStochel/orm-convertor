# 183 — T2 a T3 vystupují z vyňaté oblasti 6 v užším rozsahu: T2 je matice kategorií ve všech směrech na čtyřech stupních a drží ji sestavení, T3 je měření pěti podílů experimentálním během a nárokuje se měření, ne jeho čísla

Datum: 2026-10-25
Stav: platí
Požadavky: T2, T3, F10, F13
Podklad: zadání ([`requirements.md`](../requirements.md), T2: „minimálně .NET → Java, Java → .NET a Java → Java, rozdělené podle kategorií dotazů: projekce, filtrace, joiny, agregace, stránkování, poddotazy, množinové operace", T3: „podíl parsovatelných, kompilovatelných a spustitelných výstupů, funkční ekvivalence, úplnost přenesených mapovacích vlastností"); schválená [specifikace](../specifikace.tex) (T2: „Projekt ověří směry … na reprezentativních kategoriích dotazů a mapovacích konstrukcí", T3: „Experimentální infrastruktura bude měřit alespoň podíl …"; *Milníky*: „cross-language matice T2 s metrikami T3"); rozhodnutí [030](030-scope-of-version-1-0.md), [089](089-differential-verification-as-the-fourth-level-over-a-query.md), [090](090-the-cross-ecosystem-matrix-counts-itself.md), [095](095-a-dated-run-record-names-its-commit.md), [098](098-the-number-is-decided-once-per-release.md), [099](099-examples-are-content-not-a-choice.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md), [117](117-the-interactive-v1-validation-set-judges-the-ldbc-catalog-at-the-fourth-level.md), [149](149-fiben-as-a-third-reference-domain-run-on-demand-against-a-judge-of-our-own.md), [167](167-records-of-runs-and-measurements-have-a-folder-of-their-own.md), [170](170-rows-tied-on-the-ordering-key-compare-as-a-set-and-a-fiben-slice-gets-a-deciding-key.md), [172](172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md), [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md), [174](174-the-reference-databases-of-the-experiments-are-three-by-role-and-the-case-study-of-t1-is-the-keycloak-excerpt-judged-to-the-third-level.md), [176](176-the-schema-of-an-excerpted-project-is-an-excerpt-created-empty-in-the-case-study-database-and-the-case-study-runs-over-it-at-the-fourth-level.md), [179](179-eclipselink-carries-no-version-over-a-binary-column-so-the-store-generated-version-is-a-read-only-column-with-a-loss.md), [180](180-a-full-outer-join-into-eclipselink-goes-out-in-native-sql.md) a [181](181-an-alias-in-a-mybatis-select-with-a-result-type-states-the-column-of-the-property.md); [záznam experimentálního běhu z 2026-10-24](../runs/2026-10-24-experiment.md) a [běhu FIBEN z téhož dne](../runs/2026-10-24-fiben.md); [`architecture.md`](../architecture.md) §9; [`traceability.md`](../traceability.md), řádky T2 a T3; otevřená položka *Nárok T2 a T3 po prvním záznamu experimentálního běhu*

## Kontext

**První záznam experimentálního běhu stojí.** Běh rozh. 173 proběhl 2026-10-24 nad stromem `5caefc5` v zafixovaném prostředí: 606 vstupů do šesti cílů, 3636 řádků nástroje a tolik řádků úplnosti mapování, žádný případ žádné sady neselhal. Rozhodnutí 173 i 174 odložila nárok T1–T3 právě k tomuto okamžiku — „dokud záznam běhu v zafixovaném prostředí nedá čísla" —, a otevřená položka se ptala, zda s ním T2 a T3 vystoupí z vyňaté oblasti 6 v užším rozsahu. Její text doporučoval počkat na záznam, v němž bude každá červená buňka odmítnutím podle rozhodnutí nebo vyslovenou mezí.

**Každá červená buňka záznamu má svého nositele.**

| Buňky | V záznamu | Co jsou | Dnes |
|---|---|---|---|
| matice (55 kategorií, čtyři dvojice ekosystémů) | žádná | — | každá kategorie v každé dvojici přijatá, běží a shoduje se s kanonickým výsledkem |
| odmítnutí | 11 řádků | podle rozhodnutí: zdroj Dapper či MyBatis bez katalogu do cíle, který klíč vyžaduje, kolekční parametr v nativním SQL EF Core, sloupec výsledku bez typu (rozh. 105) | trvají; po rozh. 181 dostanou `Category` a `LineItem` JPetStore klíč z katalogu studie a `Account` zůstává odmítnutá podle vyslovené meze zdroje |
| nepřijaté artefakty | 5 řádků EclipseLinku | vady: `@Version byte[]` a `full join` v JPQL | opravené rozh. 179 a 180, s testy v javové sadě (`eclipselink/BinaryVersionClaimsTest`, `eclipselink/FullJoinClaimsTest`); experimentální běh je nepřeměřil |
| neshoda se soudcem FIBEN | 36 buněk (q24, q31, q36, q38, q178, q179 ve všech cílech) | soudce z doby před rozh. 170, ne překlad | soudce obnovil běh FIBEN z 2026-10-24 nad `b5b4106`; každá z 237 buněk každého cíle se v něm shodla se zdrojem |

Podmínka položky tedy platí věcně, jen ne jedním záznamem: běh nad stromem s opravami by dal tutéž tabulku bez pěti vad a bez 36 buněk soudce, ale zatím neproběhl.

**Otázka ale není, kdy jsou čísla dost čistá, nýbrž co znamená nárokovat experimentální požadavek.** Nárokované podle rozh. 030 znamená, že verze za tvrzení ručí, test ho drží a vstup, který ho vyvrátí, je vada s předností. U F-požadavků je tvrzením vlastnost nástroje. Zadání formuluje T2 a T3 jako experiment: T2 jako matici překladů rozdělenou podle kategorií ve třech směrech, T3 jako podíly. Podíl není vlastnost, kterou sestavení udrží bez rozporu — rozh. 173 (varianta 2) zamítlo právě sestavení, které by muselo zůstat zelené při 73 %. Schválená specifikace oba požadavky formuluje tak, že se rozpor rozplývá: T2 „Projekt **ověří** směry .NET→Java, Java→.NET a Java→Java na reprezentativních kategoriích", T3 „Experimentální **infrastruktura bude měřit** alespoň podíl …". T2 je ověření nad reprezentativním vzorkem, T3 schopnost infrastruktury.

**Je třeba rozhodnout**, zda T2 a T3 vystoupí z oblasti 6 už teď, a pokud ano, co přesně každý tvrdí, čím se to drží a co zůstává mírou.

## Zvažované varianty

**1 — Počkat na druhý záznam, v němž je každá červená buňka odmítnutím podle rozhodnutí nebo vyslovenou mezí.** Doporučení položky. Zamítáme. Váže nárok na to, co běh našel, ne na to, co nástroj a infrastruktura dělají: červená buňka je naměřená hodnota, ne selhání měření, a běh, který ji zapsal, udělal přesně to, co T3 žádá. Podmínka navíc nekonverguje: populace běhu roste — příklady, případové studie, katalogy — právě proto, aby našla, co matice nenajde (rozh. 173), a záznam bez nálezu by byl záznamem populace, která nic nového nezkouší. Každá červená buňka prvního záznamu svého nositele už má (tabulka výš) a druhý záznam přijde tak jako tak, protože běh je za verzi (rozh. 173) — jako záznam, ne jako podmínka.

**2 — Nárokovat čísla: podíly T3 jako prahy**, například funkční ekvivalence nad maticí 100 % a nad celou populací aspoň 95 %. Zamítáme. Práh nad celou populací by sestavení drželo jen během, který při sestavení neběží (FIBEN dvě až tři hodiny na sadu, rozh. 149), nebo by se četl ze záznamu, a pak je to tvrzení o minulém stromu. Příklady jsou obsah (rozh. 099): nový příklad, který najde mez, by práh posunul obsahovou změnou. Číslo v nároku je přesně ten údaj, který se s každou změnou rozejde se skutečností. Jediný práh, který sestavení udrží bez rozporu, je „každá buňka" — a to je T2 nad maticí ve variantě 4.

**3 — Nárokovat T2 a T3 nad celou populací záznamu jako test sestavení**, každou buňku katalogů, příkladů a případových studií jako aserci. Zamítáme. Katalog FIBEN není nárok, je míra, a jeho soudce je regresní, ne nezávislá pravda (rozh. 149); čtvrtý stupeň LDBC je verdikt soudce nad hodinami přehrávání (rozh. 117); případové studie končí odmítnutími podle rozhodnutí a jejich čtvrtý stupeň je jen *spustitelný* nad prázdnými tabulkami (rozh. 176); příklady jsou obsah. Tahle populace existuje, aby měřila, co sestavení nedrží; udělat z ní test by z každého jejího nálezu udělalo červené sestavení a z běhu, který má zapsat 73 %, test, který musí projít.

**4 — T2 a T3 vystoupí z oblasti 6 v užším rozsahu, každý tím, co tvrdí jeho znění ve specifikaci: T2 je matice kategorií ve všech směrech na čtyřech stupních, kterou drží sestavení; T3 je měření pěti podílů experimentálním během, týmž měřidlem pro artefakt nástroje i cizí; čísla záznamu jsou míra verze.** Volíme, níž.

## Rozhodnutí

**Volíme variantu 4. T2 a T3 vystupují z vyňaté oblasti 6 se stavem *nárokované v užším rozsahu*. T2 tvrdí matici kategorií manifestu `Tests/Database/QueryShapes/categories.txt`: každá kategorie z každého zdroje, který ji vysloví, do každého ze šesti cílů jazykem cíle, nativním SQL se záznamem `Fallback` (rozh. 113) nebo odmítnutím podle rozhodnutí, a na čtvrtém stupni tytéž řádky jako kanonický výsledek (rozh. 089); každá skupina, kterou T2 jmenuje, má pár matice v každém ze tří směrů, které T2 jmenuje, a drží to sestavení. T3 tvrdí měření: experimentální běh (rozh. 173) změří nad populací výpisu podíly, které T3 jmenuje, týmž měřidlem pro artefakt nástroje i cizí (rozh. 172), a záznam v zafixovaném prostředí je složí do tabulky; nárokuje se, že a jak se měří, ne kolik vyjde. Katalogy, příklady a případové studie jsou v tabulce mírou, ne nárokem. T1 a T4–T7 zůstávají vyňaté.**

### T2: matice, kterou drží sestavení

| Co se tvrdí | Čím se to drží |
|---|---|
| každá kategorie manifestu z každého zdroje, který ji vysloví, do každého cíle: jazykem cíle, nativním SQL, nebo odmítnutím podle rozhodnutí | `Combined/QueryShapeMatrixTest` (1. stupeň, 2. nad SQL a .NET cíli), javová `shapes/QueryCategoryTest` (2. a 3. nad javovými cíli) |
| na čtvrtém stupni tytéž řádky jako kanonický výsledek, nativní SQL včetně | `Differential/DifferentialVerificationTest` a javová `differential/`; `DifferentialMatrixTest.EveryCategoryOfTheManifestIsAQueryOfTheMatrix` |
| každá ze sedmi skupin, které T2 jmenuje — projekce, filtrace, joiny, agregace, stránkování, poddotazy, množinové operace —, má mezi svými kategoriemi pár matice ve směru .NET → Java, Java → .NET i Java → Java | `DifferentialMatrixTest.EveryGroupOfT2HasAPairInEveryDirectionItNames`, nový: skupina, jejíž kategorie by přišly o javové zdroje, by dva ze tří směrů tiše vyprázdnila a žádný dosavadní test by to nepoznal |

Matice je reprezentativní vzorek ve smyslu specifikace: kategorie nad doménou, kterou jsme navrhli tak, aby nesla tvary hodné měření — složené klíče o dvou až čtyřech částech, vícesloupcové cizí klíče, duplicity pro `DISTINCT` (rozh. 174: fixtura odpovídá na otázku, *co nástroj umí s tvary, které jsme vymysleli*). Řádky matice v experimentálním běhu jsou tatáž populace měřená cestou cizího artefaktu; v prvním záznamu prošly ve všech čtyřech dvojicích celé. Červená buňka matice, v sestavení i v záznamu, je vada a opravuje se přednostně.

**Zúžení T2:**
- **Vzorek, ne celek.** Katalogy LDBC a FIBEN, příklady a případové studie jsou v tabulce T2 × T3 mírou; jejich buňky nárok nevyvracejí ani nepotvrzují a stav každé je záznam verze, jako je dnes u katalogu FIBEN (rozh. 149).
- **Co zdroj nevysloví, buňkou není** ([`subset.md`](../subset.md) 1.3–1.4), například stránkování s vázanými počty se zdrojem NHibernate či EclipseLink nebo množinová operace se zdrojem NHibernate.
- **Mapovací konstrukce**, které specifikace u T2 jmenuje vedle kategorií dotazů, nesou doména matice a scénář `cross` (F10, rozh. 090) a měří je sloupec úplnosti mapování T3; tabulka se po nich nedělí — souhrn sčítá fakta skupiny a chybějící fakt jmenuje jen řádek (`detail`).
- **.NET → .NET** stojí v tabulce vedle tří směrů, které T2 jmenuje, a nárok ho drží také; zadání ho nežádá.

### T3: měření, ne jeho čísla

**Tvrdí se, že experimentální běh změří nad populací výpisu (rozh. 172, 173) tyto podíly, a jak:**

| Metrika T3 | Měřidlo | Jmenovatel |
|---|---|---|
| parsovatelný | čtení jazykem cíle (Roslyn, `javac`, parser XML, `TSql160Parser`), 1. stupeň | překlady |
| kompilovatelný | 2. stupeň | překlady |
| spustitelný | 4. stupeň běží | překlady klíčů se čtvrtým stupněm (`n4`): matice, FIBEN, případové studie |
| funkčně ekvivalentní | shoda s měřítkem: kanonický výsledek matice, soudce FIBEN (rozh. 089, 149, 170) | ty z `n4`, které měřítko mají |
| úplnost přenesených mapovacích vlastností | zpětné čtení artefaktu parserem cíle proti knize tvrzení převodu (rozh. 173) | fakta, která deskriptor cíle vyjádří |

Mezi kompilovatelným a spustitelným stojí *přijatý* (3. stupeň): zadání ho nejmenuje, ale u NHibernatu a javových cílů odlišuje artefakt, který se zkompiluje a framework ho odmítne, a právě tam první záznam našel obě vady EclipseLinku. Totéž měřidlo soudí artefakt nástroje i cizí, takže srovnání s modelem podle T4 je srovnání dvou řádků se společným klíčem (rozh. 175). Sestavení drží měřidlo, ne výsledek: `Experiment/ExperimentPathTest` (odmítnutí jako řádek na stupni 0, projekt do třetího stupně, čtvrtý stupeň případové studie, souhrn ze řádků), `Experiment/MappingCompletenessTest` (zpětné čtení ve 36 směrech s odchylkami jménem) a `ForeignArtifactPathTest` v obou sadách. Vadou T3 je měření, které selže — metrika, kterou souhrn nespočítá, řádek, který běh nezapíše, skupina, kterou souhrn sečte špatně —, ne červená buňka.

**Zúžení T3:**
- **Funkční ekvivalence jen tam, kde je měřítko**: nad maticí proti pravdě, kterou jsme zapsali, nad FIBEN proti regresnímu soudci (rozh. 149: shoda s doloženým během, ne správnost čtení otázky). Dotazy LDBC jdou během do třetího stupně a jejich čtvrtý stupeň je verdikt soudce rozh. 117 (F13); případová studie je *spustitelná* nad prázdnými tabulkami svého schématu bez ekvivalence (rozh. 176); příklady a scénáře končí třetím stupněm (rozh. 172, 174).
- **Úplnost mapování měří, co artefakt vysloví tak, aby to přečetl parser téhož frameworku** (rozh. 173): kde builder fakt napíše tvarem, který parser nečte — `<id>` MyBatisu, nullabilita klíče, strategie `Auto` profilu, databázový typ JPA z javového typu —, je podíl nižší, než co artefakt nese, a odchylky stojí jménem v `MappingCompletenessTest`. Dapper jmenovatel nemá.
- **Čísla jsou záznam verze** (rozh. 095, 167): nárok neříká, kolik vyjde. Text práce cituje záznam vydané verze s commitem; záznam nad starším stromem zůstává platným měřením toho stromu.

### Kde nárok stojí

Jako javový nárok (§9) na zeleném běhu obou sad v zafixovaném prostředí, v němž se přeskočí jen to, co z prostředí vyjímá rozhodnutí — tím stojí matice T2 a měřidlo T3 —, a k tomu na záznamu experimentálního běhu v zafixovaném prostředí, který dokládá, že měřidlo prošlo celou populací a dalo tabulku. Prvním takovým záznamem je záznam z 2026-10-24; další přichází za verzi podle rozh. 173.

## Důsledky

- **§9 a sekce *Guarantees* kořenového README:** řádek T1–T7 se dělí na T2 a T3 (*nárokované v užším rozsahu*, s tím, co se tvrdí, a se zúženími výš) a na T1 a T4–T7 (*vyňaté*); oblast 6 drží T1 a T4–T7 a říká, že T2 a T3 z ní vystoupily v užším rozsahu, jako dřív javový ekosystém. Podle rozh. 098 je oblast vstupující do záruk schopnost, tedy MINOR; nese ji příští vydání a jeho anotace.
- **[`traceability.md`](../traceability.md):** řádky T2 a T3 dostávají stav, důkaz a zúžení; úkoly záměru 4 a 5 a oddíl o kritériích, která nikdo nespočítal, se opravují.
- **Kód:** test `EveryGroupOfT2HasAPairInEveryDirectionItNames` v `Tests/Differential/DifferentialMatrixTest.cs`; nic dalšího — matice, běh i souhrn už stojí.
- **[`open-items.md`](../open-items.md):** položka rozhodnutí *Nárok T2 a T3 po prvním záznamu experimentálního běhu* mizí. Do oddílu *Užitečné, ne nutné* přibývají dvě položky, na kterých žádná věta nároku nestojí: úplnost mapování po kategoriích faktů, tedy mapovací konstrukce jako rozměr tabulky, a odchylky zpětného čtení — zda je má řešit parser cíle, měřidlo, nebo zda zůstanou vyslovenou mezí.
- **Co toto rozhodnutí neurčuje:** T1 — jeho dvě čtení a nosiče dalo rozh. 174 a jeho nárok je samostatná otázka; T4–T6 nese navazující práce (rozh. 175); T7 patří Advisoru (rozh. 171).
