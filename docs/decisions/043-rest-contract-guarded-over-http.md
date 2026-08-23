# 043 — REST kontrakt hlídaný testem přes HTTP

Datum: 2026-08-23
Stav: platí
Požadavky: S2, S6, S7
Podklad: rozhodnutí [041](041-versioning-and-release.md), [032](032-frontend-as-static-pages-without-a-build.md) a [016](016-generated-artifact-verification-levels.md); audit [2026-08-23](../audits/2026-08-23-post-release-1-1-0-audit.md), nález 8.2

## Kontext

Rozhodnutí [041](041-versioning-and-release.md) udělalo z REST kontraktu jednu ze **tří ploch, jejichž rozbití je změna MAJOR**, a kořenový `README.md` tu plochu nazývá „the tool's actual product surface". Přes HTTP ale nevedl jediný test. Obsluhy koncových bodů se ověřovaly přes orchestraci, takže `ORMConvertorAPI` mělo 6,0 % pokrytí řádků ([`architecture.md`](../architecture.md) §6.2) a v celém řešení nebyl `WebApplicationFactory` ani `HttpClient`.

Co se tím doopravdy hlídalo, bylo chování `ConversionHandler` — ne stavový kód, ne typ obsahu, ne tvar odpovědi a ne serializace. To je rozdíl, který není akademický: mezi orchestrací a klientem leží směrování, cesta `/orm`, vazba modelu z JSONu, převod výčtů na čísla a zabalení výjimky do odpovědi. Každý z těch kroků může kontrakt rozbít, aniž se `ConversionHandler` hne, a přesně na takovou změnu 041 předepisuje MAJOR. Nárok tedy stál na ploše, přes kterou nevedlo žádné měření.

Je to zároveň **jiný žánr testu, než jaký v sadě dosud byl.** Stupně ověření z rozhodnutí [016](016-generated-artifact-verification-levels.md) soudí *vygenerovaný artefakt* — jestli se přeloží a jestli ho cílový framework přijme. O rozhraní nástroje samotného neříkají nic a ani říkat nemají. Chybějící vrstva proto není další stupeň téhož žebříčku, ale samostatná kategorie: **nástroj odpovídá po drátě tak, jak o sobě tvrdí.**

## Zvažované varianty

1. **Ponechat stav a nic neměřit.** Nejlevnější a zároveň jediná varianta, která nechává rozpor stát: verze slibuje spoleh na plochu, o které sada nic netvrdí. Zamítáme.

2. **Porovnávat vygenerovaný `openapi.json` se souborem v gitu.** Lákavé, protože ten soubor už v repozitáři je a v diffu by byla vidět každá změna kontraktu. Jenže [`architecture.md`](../architecture.md) §6.5 o něm říká, že je to **čtecí artefakt, ne zdroj**, a že autoritativní je vždy běžící `/orm/openapi/v1.json`; test by z odvozeného souboru udělal očekávanou odpověď a tím i druhý zdroj pravdy. Hlavní vada je ale věcná: dokument popisuje **tvar, ne chování**. Zčervenal by na kosmetické změně generátoru schémat a zůstal zelený, kdyby endpoint začal vracet jiný stavový kód, jiný typ obsahu nebo prohodil pořadí artefaktů. Byl by to otisk výstupu — přesně ta slabina, kterou rozhodnutí 016 vytýká 1. stupni ověření. Zamítáme.

3. **Zúžit nárok a vyslovit, že verze chrání kontrakt jen v rozsahu, který hlídá orchestrace.** Poctivé, ale drahé jinde: nárok už je publikovaný v anotaci značky `1.1.0`, v `README.md` i v [`traceability.md`](../traceability.md), a zúžení hranice záruk je podle 041 samo změnou MAJOR. Vyměnili bychom měřitelné tvrzení za slabší, a to za cenu vydání. Zamítáme.

4. **Testy, které aplikaci nastartují v paměti a odpovídají si přes `HttpClient`.**

## Rozhodnutí

**Volíme variantu 4. REST kontrakt hlídá nová kategorie testů v `Tests/Api/`, které startují celou aplikaci přes `WebApplicationFactory` a mluví s ní skutečným `HttpClient`em.** Verdikt tak vynášejí i směrování, cesta `/orm`, vazba modelu, stavové kódy a serializace — tedy všechno, co leží mezi orchestrací a klientem a co dosud nikdo neověřoval.

**Prostředí je zafixované na `Production` a připojovací řetězec katalogu se vyprazdňuje.** Obojí je podstatné a obojí je vlastnost testu, ne jeho výbava:

- `WebApplicationFactory` startuje **implicitně v `Development`** (ověřeno spuštěním, ne z paměti), a v tom prostředí `WebApplication.CreateBuilder` načítá user secrets projektu `ORMConvertorAPI`. Na stroji, kde je katalog nakonfigurovaný, pak `/convert` odpoví `catalogState = Reached` a nenulovým časem čtení, na CI `NotConfigured` a `null`. **Kontraktní test, jehož odpověď závisí na tom, čí je to stroj, nehlídá nic** — proto se prostředí vyslovuje a řetězec přebíjí prázdnou hodnotou i proti proměnné prostředí.
- `Production` je navíc to prostředí, o kterém mluví předpoklad nasazení. Jediný rozdíl mezi oběma — Swagger UI je jen ve vývoji, dokument OpenAPI všude (§6.5) — se tím z věty v dokumentaci stává tvrzení testu: v `Production` je `/orm/swagger` **404** a `/orm/openapi/v1.json` **200**. Právě tohle je věcné jádro argumentu, kterým 041 dokument z `IsDevelopment()` vyvázalo.
- Bez katalogu jsou `catalogState = NotConfigured` a `catalogReadMilliseconds = null` **fakty kontraktu, ne prostředí**, a dají se tvrdit. Převod s katalogem přes rozhraní záměrně netestujeme: kritéria F4 a F6 dokládají ověřovací testy proti databázi (§6.2), a duplikovat je přes HTTP by znamenalo vázat kontraktní sadu na infrastrukturu, kvůli které by se přeskakovala.

**Jedno tvrzení váže obě větve k sobě: artefakt vrácený po drátě je bajtově týž jako artefakt z přímého volání `ConversionHandler`.** `Combined/RunRecordTest` tvrdí determinismus uvnitř procesu (S2); tohle tvrdí, že ho serializace nepokazí. Není to formalita — generovaný kód nese `\r\n`, a to je přesně ten druh detailu, který cesta přes JSON umí tiše změnit.

**Soubor `openapi.json` v repozitáři se neporovnává** — z důvodů varianty 2. Místo toho se **vydávaný** dokument kontroluje na dvě věci, které jsou tvrzením a ne otiskem: nese verzi ze sestavení (`ToolRelease.Version`, tedy totéž číslo jako záznam běhu podle S6 a rozhodnutí [034](034-central-version-management.md)) a popisuje právě ty cesty, které aplikace skutečně mapuje.

**Advisor zůstává mimo.** `/advisor/run` a `/advisor-test` jsou vyňaté ze záruk vcelku (§9, oblast 1) a mimo Linux navíc selhávají na chybějící `libadvisor.so`. Test nad nimi by tvrdil něco, co verze netvrdí.

**Frontend zůstává mimo** — rozhodnutí [032](032-frontend-as-static-pages-without-a-build.md) říká, že automatické testy nemá, a to se nemění. Co se testuje, je **statická cesta serveru**, ne stránka: že `/orm/` odpoví vstupním dokumentem, je tvrzení o `UsePathBase`, `UseDefaultFiles` a `MapStaticAssets` — tedy o směrování hostitele, které je součástí téže plochy jako endpointy.

## Důsledky

**`ORMConvertorAPI` přestává být nejhůř pokrytým projektem řešení.** Číslo v §6.2 se posune při dalším měření; samo o sobě nic nehlídá (§6.2), doloží ale, že vyňaté oblasti v §9 zůstaly jediné netestované.

**Test pojmenoval jeden rozpor mezi dokumentem a drátem.** Endpointy deklarují `.ProducesProblem(400)`, takže dokument OpenAPI slibuje `application/problem+json` a objekt `ProblemDetails`; obsluhy přitom odpovídají `Results.BadRequest(e.Message)`, tedy `application/json` a **holý řetězec**. Test tvrdí skutečné chování a rozpor v komentáři jmenuje. Srovnat to je změna tvaru odpovědi, tedy zásah do REST kontraktu a podle 041 změna MAJOR — patří do rozhodnutí, ne do mimochodem provedené opravy.

**Do sady přibývá závislost `Microsoft.AspNetCore.Mvc.Testing`,** verzí centrálně v `Directory.Packages.props` (rozhodnutí 034). Testovací projekt s ní dostává i referenci na framework `Microsoft.AspNetCore.App`. **S1 tím netrpí:** wrappery se nemění a nic z toho se jich nedotkne — je to táž konstrukce jako u balíků cílových frameworků, které testovací projekt nese kvůli 3. stupni ověření (§6.2).

**Cena je jeden nastartovaný hostitel na kolekci,** řádově půl sekundy, a testy nepotřebují databázi ani síť. Kolekce je jedna, takže se hostitel nestartuje pro každou třídu zvlášť.

**Co tím doložené není:** nic o frontendu, nic o Advisoru a nic o chování rozhraní s dostupným katalogem. Zúžení §9 se tímhle rozhodnutím nemění; mění se jen to, že ta část plochy, kterou verze nárokuje, má poprvé měření.
