# 204 — Odpověď modelu je celý soubor — s `using`, `namespace`, `package`, `import` a třídou okolo dotazové metody — a cesta ho soudí jako celek vedle holé metody nástroje; varianta `zero-shot` se soudí, jak přišla, a u každé jiné varianty import opraví obálku tím, co konzument dával holé metodě, a zapíše to do manifestu a do souhrnu

Datum: 2026-11-02
Stav: platí; nahrazuje tvar dotazové jednotky odpovědi rozh. 189
Požadavky: T3, T4, T5, T6
Podklad: zadání ([`requirements.md`](../requirements.md), T4: „porovnat pravidlový přístup alespoň s jedním silným LLM v zero-shot a few-shot režimu", T5: „varianta poskytující modelu dokumentaci cílového frameworku, informace o schématu a relevantní příklady", T6: „samostatně vyhodnotit vliv databázových metadat, validace, opravného cyklu a RAG"); rozhodnutí [027](027-verification-levels-for-the-query-branch.md), [040](040-consumer-project-contract.md), [172](172-a-foreign-artifact-enters-the-verification-levels-as-a-directory-with-a-manifest-beside-the-client.md), [175](175-the-comparison-with-a-language-model-is-a-protocol-over-the-foreign-path.md), [189](189-a-web-frontend-attempt-is-a-new-conversation-with-one-self-contained-prompt-answered-in-text-without-the-web-and-paired-with-the-tool.md), [202](202-the-driver-over-an-api-is-an-on-demand-run-over-an-openai-compatible-endpoint-and-the-pilot-model-is-qwen35-9b.md) a [203](203-the-strong-model-is-claude-fable-5-1-over-the-vendors-openai-compatible-endpoint-and-the-temperature-of-the-driver-is-optional.md); záznam [2026-11-01](../runs/2026-11-01-llm.md) a jeho kontrola nad artefakty z 2026-11-02; otevřená položka *Obálka odpovědi modelu rozhoduje verdikt srovnání*

## Kontext

**Prompt žádal tvar nástroje, ne tvar cíle.** Dotazový artefakt nástroje je holá metoda — `public static IQueryable<T> MethodName(DbContext ctx, …)` bez `using`, `namespace` a třídy okolo —, protože třídu, direktivy a projekt kolem ní dává konzument (rozh. 027 a 040; `GeneratedQueryCompiler.Holder`, javový `JavaSources.wrapQuery`). Šablony `web-zero-shot-2` a `web-few-shot-1` (rozh. 189) žádaly od modelu touž holou metodu, „bez třídy, jmenného prostoru a direktivy okolo", a soudce ji obaloval jako artefakt nástroje.

**Model píše soubor.** Záznam [2026-11-01](../runs/2026-11-01-llm.md) ukázal, že všech 39 odpovědí pilotního modelu drží bloky `FILE:` a `REPORT:` šablony, a přesto 36 z nich skončilo na stupni 0: v .NET nesla dotazová jednotka `using`, `namespace` a `public static class` (`CS1513 } expected`, `CS1529 A using clause must precede…` — soudce vložil celý soubor do své třídy), v Javě aspoň řádky `import` před metodou (`illegal start of type`). Jediná buňka s holou metodou došla na druhý stupeň. Kontrola nad týmiž artefakty z 2026-11-02 (záznam, oddíl *Kontrola*) ukázala, že po mechanické úpravě obálky dojdou tři buňky na nejvyšší stupeň hned a dalších pět po opravách, které řekne zpráva frameworku; pět zůstává na obsahu. Verdikt záznamu tedy měřil, jak model drží tvar, který žádný vývojář nepíše — soubor bez direktiv a třídy není jednotka žádného z šesti cílů —, a o překladu řekl málo.

**Otevřená položka nabídla tři cesty:** (a) import obálku normalizuje vždy a verdikt měří obsah, (b) obálka zůstává měřená a souhrn dostane sloupce *tvar držen* a *obálka držena*, (c) varianta `repair` dostane mechanické opravy jako nulté kolo a `zero-shot` zůstane. Míra k tomu určil (2026-11-02), že model má vracet kód i s `using` a tím, co k souboru patří, že `zero-shot` se soudí, jak přišel, a že u ostatních variant se `using` a další fakta obálky opravují.

**Je třeba rozhodnout** tvar dotazové jednotky v odpovědi, jak ji cesta soudí vedle holé metody nástroje, co je obálka a co ne, u kterých variant se opravuje a jak záznam opravu nese.

## Zvažované varianty

### Tvar dotazové jednotky v odpovědi

**1 — Holá metoda zůstává.** Tvar, který soudí nástroj, bez výjimky pro model. Zamítáme: tvar je harnessu, ne cíle. Žádný konzument EF Core, Hibernate ani MyBatisu nemá v projektu metodu bez souboru; model se při něm pere s tím, co se naučil, a záznam pak měří zápas s promptem. Nástroj holou metodu píše z důvodu rozh. 027 — konzument dodá třídu a direktivy sám —, a ten důvod pro model neplatí: model je konzumentem, který překládá celý projekt.

**2 — Celý soubor: `using`, `namespace`, třída okolo metody; `package`, `import`, třída či rozhraní.** Volíme, níž. Je to jednotka, kterou cíl čte (kompilační jednotka C# a Javy), kterou píše vývojář a kterou model píše sám od sebe. Cesta ji soudí jako celek — zkompiluje soubor, jak je, s entitami artefaktu —, a holou metodu nástroje soudí dál obalenou, protože obě jsou artefakty téže cesty (rozh. 172): kterou má před sebou, pozná jazyk cíle sám (Roslyn přečte text jako kompilační jednotku a najde v ní direktivu, jmenný prostor, nebo samé typy — artefakt nástroje, který vedle holé metody deklaruje třídu řádku nativního SQL, zůstává holou metodou; v Javě začíná soubor `package`, `import` nebo deklarací typu).

### Co cesta dělá s obálkou

**1 — Import obálku normalizuje vždy** (cesta (a) položky). Zamítáme pro `zero-shot`: rozh. 175 soudí odpověď, jak přišla, a `zero-shot` T4 je *model sám*; normalizace by zakryla, že model tvar neudrží, a číslo by přestalo být číslem varianty bez pomoci.

**2 — Obálka zůstává měřená ve všech variantách a souhrn dostane sloupce** (cesta (b)). Zamítáme jako jediný mechanismus: sloupce by problém popsaly, ne odstranily. Model varianty `few-shot`, `rag` nebo `repair`, který padne na chybějící `using`, by nikdy neukázal obsah, a právě obsah ty varianty měří (T5, T6: vliv dokumentace, schématu, příkladů a opravného cyklu).

**3 — Mechanické opravy jen jako nulté kolo varianty `repair`** (cesta (c)). Zamítáme jako příliš úzkou: `few-shot` dostane příklady tvaru a přesto může vynechat direktivu, a `rag` i `agent` jsou na tom stejně; nulté kolo by muselo být u každé z nich.

**4 — `zero-shot` jak přišel; každá jiná varianta s obálkou opravenou importem, uzavřeným seznamem oprav, zapsaným v manifestu a v souhrnu.** Volíme, níž. Důvod je v tom, co varianty měří: `zero-shot` je model bez pomoci, a každá jiná varianta rozh. 175 je *model s pomocí protokolu* — příklady, korpus, schéma, verdikt. Mechanická oprava obálky je nejmenší pomoc, kterou vývojář cizímu kódu dá dřív, než ho vůbec posoudí, je deterministická, stejná pro každý model a každý pokus, a celá zapsaná, takže čtenář ví, kolik pokusů ji potřebovalo. Rozdíl `zero-shot` − `few-shot` tím nese příklady i opravu; pole manifestu (níž) je od sebe oddělí.

### Co je obálka

Oprava smí dát celému souboru **přesně to, co konzument dával holé metodě**, a nic víc — to je hranice, která se dá vyslovit a držet:

| Oprava | Co konzument dával holé metodě | Kde |
|---|---|---|
| direktivy: chybějící `using` cíle do souboru C#, chybějící `import` cíle do souboru Javy | `using Dapper;`/`System.Data;`, `using Microsoft.EntityFrameworkCore;`, `using NHibernate;`/`NHibernate.Linq;` (`PreparedQuery.Compile`); `import jakarta.persistence.*`, typy parametrů `java.math`, `java.time`, `java.util` (`JavaSources.wrapQuery`, `wrapMapperInterface`); u entit jmenné prostory anotací cíle (EF Core `System.ComponentModel.DataAnnotations`, `…Schema`, `Microsoft.EntityFrameworkCore`; JPA `jakarta.persistence.*`) — jediná direktiva, kterou entitní soubor cíle potřebovat může | dotazové i entitní soubory |
| třída okolo metody: soubor, který nese jen direktivy a holou metodu, dostane veřejnou statickou třídu (C#), veřejnou třídu (Java) nebo rozhraní pojmenované podle `namespace` dokumentu (MyBatis) | `GeneratedQueryCompiler.Holder`, `wrapQuery`, `wrapMapperInterface` | dotazové soubory |
| jmenný prostor entit v dohledu: `using <namespace entit>;` do dotazového souboru C# v jiném jmenném prostoru; `import <package entit>.*;` do dotazového souboru Javy v jiném balíčku | třída okolo metody stála ve jmenném prostoru entit (`Usings(entities, …)`, `wrapQuery(entityPackage, …)`) | dotazové soubory |
| jeden veřejný typ na soubor Javy: soubor se dvěma veřejnými typy se rozdělí, každý do souboru svého jména | `JavaProject.add` pojmenuje soubor podle veřejné třídy (JLS §7.6) | soubory Javy |
| přípona podle role: `.cs`, `.java`, `.xml`, `.sql`, `.hql`, `.jpql` | role určuje jazyk jednotky (rozh. 111, 172) | každý soubor |
| jmenný prostor mapperu MyBatisu: `namespace` dokumentu, který nejmenuje žádné rozhraní artefaktu, se nastaví na `<package>.<rozhraní>` jediného rozhraní artefaktu | `wrapMapperInterface` pojmenoval rozhraní podle `namespace` dokumentu | dokumenty mapperu |

Co obálka **není**, protože to konzument holé metodě nedával: mapovací fakta (`[PrimaryKey]` místo několika `[Key]`, `Schema` v `[Table]`, `Equals`/`GetHashCode` nad `composite-id`), třída místo rozhraní mapperu, `javax.persistence` místo `jakarta.persistence`, vymyšlené parametry, text dotazu, typ parametru, entity, které model nenapsal. To jsou výsledky modelu, u každé varianty.

## Rozhodnutí

**Dotazová jednotka odpovědi modelu je celý soubor: v C# direktivy `using`, `namespace` entit a jedna veřejná statická třída s jednou veřejnou statickou metodou; v Javě `package` entit, `import` a jedna veřejná třída s metodou, u MyBatisu veřejné rozhraní mapperu pojmenované jako `namespace` jeho dokumentu. Cesta cizího artefaktu soudí takový soubor jako celek — zkompiluje ho, jak je, s entitami artefaktu, metodu najde podle handle cíle, ne podle jména třídy — a holou metodu nástroje soudí dál obalenou; obě pozná jazyk cíle. Pokus varianty `zero-shot` se soudí, jak přišel. U každé jiné varianty import před zápisem artefaktu opraví obálku tím, co konzument dával holé metodě — chybějící direktivy, třída okolo metody, která nese jen direktivy, jmenný prostor či balíček entit v dohledu, jeden veřejný typ na soubor Javy, přípona podle role, `namespace` mapperu —, každou opravu zapíše do manifestu (`envelope`: `as written` / `intact` / `repaired`, `envelopeRepairs`) a souhrn dá u skupiny podíl pokusů s opravenou obálkou. Šablony `web-zero-shot-3` a `web-few-shot-2` žádají tento tvar; příklady varianty `few-shot` ho mají, soubor pojmenovaný podle třídy.**

### Cesta nad celým souborem

| Stupeň | .NET cíle | Java cíle |
|---|---|---|
| 1. čtení | Roslyn nad souborem, jak je; holá metoda dál uvnitř `Holder` | `JavacTask.parse()` nad souborem, jak je; holá metoda dál uvnitř `wrapQuery`/`wrapMapperInterface` |
| 2. překlad | soubor jako vlastní kompilační jednotka vedle entit, s implicitními direktivami projektu (`global using System`, `System.Collections.Generic`, `System.Linq` — fakt projektu jako reference), bez direktiv konzumenta: ty jsou v souboru | soubor přidaný do projektu dotazů, jak je (`JavaProject.add` ho pojmenuje podle veřejné třídy) |
| 3. přijetí | metoda = jediná veřejná statická metoda, jejíž první parametr přijme handle cíle (`IDbConnection`, `DbContext`, `ISession`); žádná nebo víc než jedna je *not accepted* se jmény; model EF Core registruje jako entity veřejné třídy bez takové metody — třída okolo dotazu entitou není | metoda = jediná veřejná statická metoda s prvním parametrem `EntityManager`; u MyBatisu jediná metoda rozhraní, nebo ta, jejíž jméno je `id` příkazu dokumentu |
| 4. běh | beze změny | beze změny |

### Co záznam a souhrn nesou

| Kde | Co |
|---|---|
| manifest pokusu | `envelope`: `as written` (`zero-shot`: import obálky nesáhl), `intact` (jiná varianta, nebylo co opravit), `repaired` (jiná varianta, opraveno); `envelopeRepairs`: seznam oprav, každá větou se jménem souboru |
| souhrn, oddíl *beside the tool over the same cells* | sloupec *repaired envelopes*: pokusy s `repaired` z pokusů, u nichž import obálku opravuje (`intact` + `repaired`); u skupiny `zero-shot` `—` |
| záznam běhu | u každé skupiny mimo `zero-shot` podíl opravených obálek a které opravy to byly, aby rozdíl proti `zero-shot` šel číst jako příklady *a* oprava |

### Co se netvrdí

Tvar artefaktu nástroje se nemění: holá metoda zůstává tím, co nástroj píše a co konzument obaluje (rozh. 027, 040, 172); cesta jen přijímá obě podoby. Záznam 2026-11-01 stojí nad šablonami, které jmenuje, a znovu se nesoudí — další běh pilotního modelu i běh silného modelu (rozh. 203) jdou nad novými šablonami do nového adresáře. Oprava obálky není opravný cyklus T6: nedostává verdikt, nemění text dotazu a neběží v kole; varianta `repair` ji dostane jako každá jiná varianta mimo `zero-shot` a svůj cyklus k tomu. T4–T6 zůstávají vyňaté (§9, oblast 6).

## Důsledky

- **Kód, cesta:** `Tests/Verification/GeneratedQueryCompiler.IsCompilationUnit`, `Compile` s celým souborem vedle `Holder`; `Tests/Foreign/ForeignArtifactRun.Unreadable`; `Tests/Differential/DotNetQueryRunner.PreparedQuery.GeneratedMethod` podle handle; `Tests/Verification/EFCoreAcceptance.EntityTypes` bez třídy okolo dotazu; javové `JavaSources.isCompilationUnit`, `ForeignArtifactRun.unreadable` a `compile`.
- **Kód, kanál:** `Tests/Foreign/WebEnvelope.cs` (opravy a jejich věty), `WebAttempts.Import` volá opravu u varianty mimo `zero-shot` a zapíše `envelope` a `envelopeRepairs`; `Experiment/ExperimentSummary` sloupec *repaired envelopes*; šablony `Foreign/Prompts/web-zero-shot-3.md` a `web-few-shot-2.md`, `WebTemplate.ForVariant` na ně; starší šablony zůstávají čitelné (rozh. 189).
- **Příklady `few-shot`:** dotazové soubory `Tests/Database/Foreign/few-shot-*` jako celé soubory, pojmenované podle třídy (`FindFilteringQuery.cs`, `FindFilteringQuery.java`; rozhraní MyBatisu beze změny jména); `expected.txt` beze změny.
- **[`open-items.md`](../open-items.md):** položka *Obálka odpovědi modelu rozhoduje verdikt srovnání* mizí; položka *Srovnání s jazykovým modelem* jmenuje nové šablony pro běh silného modelu.
- **[`architecture.md`](../architecture.md) §6.2:** stupně cesty nad celým souborem, řádky *šablona*, *příklady `few-shot`*, *import* a sloupec souhrnu.
- **`ORMConvertor/README.md`**, *Foreign artifacts* a *A language model in a web frontend*: obě podoby dotazové jednotky, tvar odpovědi, oprava obálky u variant mimo `zero-shot`.
- **[`traceability.md`](../traceability.md), T4:** šablony a oprava obálky ve sloupci implementace; stav `vyňaté` zůstává.
- **[`runs/README.md`](../runs/README.md):** záznam srovnání uvádí podíl opravených obálek po skupinách.
- **Rozh. 189:** řádek *tvar artefaktu* tabulky promptu (holá dotazová metoda s podpisem harnessu) nahrazen tímto rozhodnutím; zbytek platí.
