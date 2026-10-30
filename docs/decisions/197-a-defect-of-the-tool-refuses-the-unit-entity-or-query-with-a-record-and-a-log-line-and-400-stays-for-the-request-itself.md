# 197 — Výjimka nástroje při čtení jednotky, generování entity nebo dotazu je záznam `Failure` té jednotky, entity či dotazu a řádek logu; 400 zůstává chybě požadavku samého

Datum: 2026-10-30
Stav: platí
Požadavky: F11, F14, S6, S7, S4
Podklad: rozhodnutí [044](044-error-response-as-problem-details.md), [045](045-a-conversion-that-produced-nothing-says-so.md), [066](066-records-attributed-to-the-input-unit.md), [081](081-a-unit-may-be-a-mapping-and-a-query-at-once.md), [160](160-credentials-are-in-no-generated-source-and-no-text-about-a-run-and-progress-has-the-grain-of-the-processing.md), [191](191-the-depth-of-the-model-is-capped-by-the-same-number-as-the-text-and-a-reader-that-runs-out-of-stack-refuses.md); revize odolnosti [2026-10-30](../audits/2026-10-30-robustness-audit.md), nálezy 1.1 a 1.2

## Kontext

Orchestrace (`ConversionHandler`) zachycovala v obou průchodech jedinou výjimku: `InsufficientExecutionStackException` čtečky, které dochází zásobník, a tu měnila v `Failure` jednotky (rozh. 191). Každá jiná výjimka — z parseru, z doplnění, z kroku builderu — vypadla z `Convert` celá, koncový bod ji zachytil jako každé jiné selhání a odpověděl 400 s textem výjimky v `detail` (rozh. 044). Nic se nezalogovalo: pomocná metoda `Failure` v `Endpoints` text jen vrátila.

Revize odolnosti z 2026-10-30 prohnala 14 148 převodů vadných, podivných a nadměrných vstupů všemi 36 směry a našla tři, které takhle vypadly — všechny z jednoho vstupu, fluent konfigurace EF Core, která vlastní člen klíče (`OwnsOne(x => x.Id)`): vyloučení podle rozh. 177 vzalo vlastnost z entity, část klíče nad ní zůstala, a buildery Hibernatu, EclipseLinku a MyBatisu psaly přístupovou metodu člena, který entita už neměla (`Sequence contains no matching element`). Vada sama je opravená (část klíče odchází s vlastností). Podstatné je, co se při ní dělo:

- **Zdravé jednotky zmizely s vadnou.** Požadavek s deseti jednotkami, z nichž jedna narazila na vadu nástroje, nevrátil nic — ani artefakty těch devíti, ani záznamy, které už byly napsané. Rozh. 045 i 066 stojí na tom, že výsledek převodu je po jednotkách a že co se nestalo, se řekne u té jednotky.
- **Uživatel dostal větu, která není o vstupu.** `Sequence contains no matching element` je věta LINQ, ne věta o jednotce; uživatel ji čte jako chybu svého vstupu a hledá ji tam (S7).
- **Na instanci nezůstala stopa.** Chyba programu a chyba vstupu odpovídaly stejným 400 a žádný z nich se nelogoval; provozovatel nemá, podle čeho vadu najít, a záznam běhu (S6) o ní mlčí.

Model hrozeb vedl odolnost parserů vůči nepřátelskému vstupu jako neanalyzovanou; tohle je její obyčejná, nenepřátelská polovina — vstup, se kterým nástroj sám nepočítal.

## Zvažované varianty

### 1 — Nechat 400 pro celý požadavek (dnešní stav)

Jednoduché a poctivé v jednom smyslu: nástroj nic nepředstírá. Zamítáme pro tři věci výš. Navíc je to v rozporu s tím, jak nástroj zachází s každým jiným selháním jedné jednotky: syntaktická chyba C#, Javy, XML, SQL, HQL i JPQL je `Failure` té jednotky (rozh. 128 a sourozenci), přetečení zásobníku také (191), nečitelný katalog je `Incompleteness` běhu (015). Vada nástroje by byla jediný druh selhání, který maže výsledek ostatních.

### 2 — Zachytit jen známé typy výjimek

Vyjmenovat, co parser či builder smí vyhodit, a jen to měnit v záznam. Zamítáme: množina není znatelná. Jediné místo, které revize našla, byl `First()` nad vlastnostmi entity — nikdo by ho do seznamu nenapsal, protože nikdo nevěděl, že tam je. Chyba programu je přesně ta, kterou nikdo nevyjmenoval.

### 3 — Globální obsluha výjimek rámce (500 místo 400)

Middleware ASP.NET, který z neošetřené výjimky udělá 500 s `ProblemDetails`. Řeší jen status: částečný výsledek se ztratí stejně a o jednotce se neřekne nic. Mohla by stát vedle varianty 4 jako poslední síť; nepřidáváme ji, protože koncové body už každou výjimku chytají samy a zbylo by pro ni jen to, co rámec vyhodí mimo obsluhu (vadné tělo), kde 400 rámce stačí.

### 4 — Záchyt po jednotce, entitě a dotazu: záznam pro volajícího, výjimka pro log

Tam, kde orchestrace a builder už dnes znají, čeho se práce týká — čtení jedné jednotky v obou průchodech, sedm kroků nad jednou entitou v `Build()`, `Build()` jednoho dotazu —, zachytit cokoli a napsat `Failure` té jednotky, entity či dotazu; výjimku samu i se zásobníkem nést v `ConversionResult` jako `TranslationDefect` a nechat ji koncový bod zalogovat (`Error`, přes začerňující formátovač rozh. 160). Práce pokračuje: ostatní jednotky, entity a dotazy se přeloží jako jindy. Volíme ji.

## Rozhodnutí

**Vada nástroje při práci nad jednou jednotkou, entitou nebo dotazem je `Failure` té jednotky, entity nebo dotazu a řádek logu, ne odpověď 400.** Konkrétně:

- **Čtení jednotky** (oba průchody `ConversionHandler`, včetně měření stropů rozh. 191 a 194 hned po čtení): výjimka jiná než přetečení zásobníku jednotku odmítne pro oba průchody — stejně jako 191, a ze stejného důvodu: druhý průchod by četl týž text nad modelem, který selhané čtení mohlo nechat zpola vyplněný. Mapy, které selhané čtení přidalo, z modelu odejdou; co vyslovilo o mapách jiných jednotek, rozlišit nejde a zůstává. Záznamy, které parser stihl napsat, se jednotce připíší (066).
- **Generování entity** (`AbstractEntityBuilder.Build()`): výjimka v sedmi krocích nad jednou entitou odmítne tu entitu — `Failure` s entitou, kniha tvrzení říká, že její fakty nic neneslo (124) — a ostatní entity se vydají. Výjimka ve fázích nad celým modelem před smyčkou (rozpuštění tříd klíče, vyloučení, junction entity, jména) odmítne entitní artefakty vcelku záznamem běhu bez jednotky.
- **Generování dotazu** (`AbstractQueryBuilder.Build()` v orchestraci): výjimka odmítne ten dotaz — `Failure` s jednotkou a jménem dotazu — a záznamy, které builder už nese, odejdou s ním.
- **Věta záznamu** je jedna pro všechny tři případy (`TranslationDefect.Reason`): co nástroj dělal, typ a zpráva výjimky, a výslovně *vada nástroje, ne vstupu* — aby uživatel chybu nehledal ve svém vstupu a aby dva běhy téhož vstupu byly srovnatelné (S2).
- **Výjimka sama** nejde do odpovědi; `ConversionResult.Defects` ji nese pro koncový bod, který každou zaloguje na úrovni `Error` s `RunId`, směrem, jednotkou, entitou a dotazem, přes začerňující formátovač konzole (160). Text zprávy v záznamu prochází jako každý text o běhu: cizí text, který by nesl údaj, začerní odpověď jen tam, kde ho nástroj skládá sám — čtečka katalogu začerňuje vlastní (160); jiné místo, které by do zprávy výjimky dostalo údaj, revize nenašla.
- **400 zůstává chybě požadavku samého**, o které se rozhodne dřív, než se cokoli čte: neznámý zdrojový či cílový framework (045), chybějící seznam jednotek, jednotka, která je `null`, chybějící seznam souborů archivu. Věta je nástroje a jmenuje, co má klient změnit („Source unit 2 is null; a unit declares a content type and a content."), ne text nulové reference, do které obsluha dřív narazila. I tato odmítnutí se logují (`Warning`), s výjimkou tam, kde nějaká byla, takže vada nástroje za 400 nechává stopu na instanci a ne jen v okně klienta.

**Proč záchyt stojí tam, kde stojí, a ne výš.** Jediné místo, které ví, čeho se výjimka týkala, je to, které práci nad jednotkou, entitou nebo dotazem právě dělá; o úroveň výš už je známý jen celý požadavek. Záchyt v orchestraci a v template metodě builderu (rozh. 009) proto nese jméno jednotky, entity či dotazu zadarmo a nezasahuje do žádného wrapperu — nový framework ho dostává tím, že stojí nad týmž `Build()` (S1).

**Co rozhodnutí netvrdí.** Že by výsledek po vadě byl úplný: jednotka, entita či dotaz, na nichž vada nastala, chybí a záznam to říká. Že by vada nástroje přestala být vadou: záznam je pro uživatele, log pro opravu, a opravená vada dostává test jako každá jiná (`Combined/OwnedKeyMemberTest`).

## Důsledky

- **Kód:** `AbstractWrappers/Diagnostics/TranslationDefect` (záznam s výjimkou, jednotkou, entitou, dotazem; věta `Reason`); `AbstractEntityBuilder.Build()` se smyčkou po entitách nad `BuildOne` a `Defects`; `ConversionHandler` se záchytem v obou průchodech (`RefuseDefective`, jednotka `Refused` pro oba důvody), kolem `Build()` entit a kolem `Build()` každého dotazu, `ConversionResult.Defects`; `Endpoints` s `ILoggerFactory`, logováním vad (`Error`) i odmítnutí (`Warning`) a kontrolou `sources` a `files` před prací. `RemoveProperty` bere s vlastností i část klíče nad ní (nález 1.1).
- **Testy:** `Combined/TranslationDefectTest` (builder, jehož `FinalizeBuild` vyhazuje — sada má takový vlastní, `DummyEntityBuilder` —, dá `Failure` s entitou a `Defects` s výjimkou, ostatní entity nerušené), `Combined/OwnedKeyMemberTest` (vlastněný člen klíče je `Loss` člena do všech šesti cílů, ne 400), `Api/ConvertEndpointTest` (jednotka `null` a chybějící seznam jsou 400 s větou nástroje).
- **Dokumenty:** `architecture.md` §5 (společná pravidla čtení) a §6.5 (selhání, log); `threat-model.md`, hrozba 2 a 4; `traceability.md` S6 a S7.
- **Co zůstává:** log vad nemá test — zachytit výstup konzole v testu přes `WebApplicationFactory` by stálo víc, než co by dokázalo; že řádek vzniká, bylo ověřeno spuštěním instance (revize 2026-10-30, kap. 4).
