# 044 — Chybová odpověď jako `ProblemDetails`

Datum: 2026-08-23
Stav: platí
Požadavky: F11, S6, S7
Podklad: rozhodnutí [043](043-rest-contract-guarded-over-http.md), které rozpor našlo, [041](041-versioning-and-release.md) a [010](010-diagnostics-as-returned-data.md); [`threat-model.md`](../threat-model.md), hrozba 4

## Kontext

Každý koncový bod, který může selhat, deklaruje `.ProducesProblem(StatusCodes.Status400BadRequest)`. Vydávaný dokument OpenAPI proto o `/convert`, `/archive`, `/advisor/run` i `/advisor-test` tvrdí, že chybová odpověď má typ obsahu `application/problem+json` a tělo tvaru `ProblemDetails`. Obsluhy ale odpovídají `Results.BadRequest(e.Message)`, tedy `application/json` a **holý řetězec** v uvozovkách. Zjistily to testy přes HTTP podle rozhodnutí [043](043-rest-contract-guarded-over-http.md); do té doby to nemělo jak vyjít najevo, protože in-process testy chybovou odpověď nikdy nesestavovaly.

**Není to kosmetika a není to jen nepřesnost.** Dokument OpenAPI je ta strojově čitelná podoba kontraktu, kterou rozhodnutí [041](041-versioning-and-release.md) chrání číslem verze a kterou §6.5 kvůli tomu vydává i v `Production`. Klient vygenerovaný z toho dokumentu si pro stav 400 připraví deserializaci `ProblemDetails` a na řetězci **spadne**. Kontrakt tedy není neúplný, je **nesprávný** právě vůči tomu, komu je určen.

**Která ze dvou stran je omyl, je přitom zřejmé.** `.ProducesProblem(400)` je zápis autora u každého selhatelného bodu, tedy vyslovený úmysl; holý řetězec je to, co `Results.BadRequest(string)` shodou okolností vydá. Deklarace popisuje, co jsme chtěli, kód dělá něco jiného.

**Cena srovnání je přitom malá, protože jediný známý konzument je náš vlastní frontend** — a ten už dnes umí obojí: `wwwroot/js/api.js` čte tělo a testuje `typeof parsed === "string"` i `parsed.title`. Nerozbije se; jen by uživateli místo věcné hlášky ukázal obecný titulek, což je věc jednoho řádku.

## Zvažované varianty

1. **Srovnat dokument k drátu, tedy `.Produces<string>(400)`.** Nejlevnější a nerozbíjející: drát se nehne, verze zůstane MINOR. Publikovali bychom tím ale slabý chybový kontrakt — holý řetězec nenese stav, titulek, typ ani místo, kam by se dalo cokoli přidat, a proti RFC 9457 by nástroj byl výjimkou v ekosystému, kde ho každý generátor klienta i každá knihovna čekají. Zahodili bychom navíc jediné strukturované místo, které nástroj pro „proč to selhalo" má. Zamítáme.

2. **Vlastní chybové DTO.** Vznikl by třetí slovník vedle diagnostických záznamů (rozhodnutí [010](010-diagnostics-as-returned-data.md)) a `ProblemDetails`. Nic, co by nástroj potřeboval, přitom RFC 9457 nenabízí — rozšiřující pole má standard vlastní. Zamítáme.

3. **Rozpor nechat a popsat ho v dokumentaci.** Kontrakt, který popisuje něco jiného, než nástroj odpovídá, je horší než žádný: 041 existuje právě proto, aby se na něj dalo spolehnout. Zamítáme.

4. **Srovnat drát k dokumentu — obsluhy budou vracet `ProblemDetails`.**

## Rozhodnutí

**Volíme variantu 4. Chybová odpověď je `ProblemDetails` podle RFC 9457,** vydaná přes `Results.Problem(detail: e.Message, statusCode: StatusCodes.Status400BadRequest)`. Zpráva jde do pole `detail`, `title` zůstává rámcové „Bad Request", `status` je 400 a `type` odkaz na příslušnou sekci RFC 9110. **Deklarace endpointů se nemění**, protože dokument přesně tohle popisoval celou dobu — mění se kód, aby jí dostál.

**Nemění se, co ta zpráva obsahuje.** Text je pořád táž zpráva výjimky, jen v jiném poli. Hrozba 4 v [`threat-model.md`](../threat-model.md) — že v ní může uniknout jméno serveru nebo instance — platí beze změny a tohle rozhodnutí se nesmí číst jako její náprava. Zúžit obsah zprávy je samostatná otázka a samostatná volba.

**Frontend se upravuje týmž zásahem.** `api.js` dnes zkouší nejdřív `parsed.title`; u `ProblemDetails` je titulek obecný, takže by uživatel místo „Source ORM not supported" viděl „Bad Request". Pořadí se proto obrací na `detail`, pak `title`, pak řetězec — třetí větev zůstává, protože nestojí nic a čte i starší odpověď.

**Chyby, které vzniknou dřív, než se dojde k obsluze, zůstávají, jak jsou.** Vadné JSON tělo nebo chybějící tělo odpoví prázdnou čtyřistovkou z rámce; popsat i je by znamenalo `AddProblemDetails()` a `UseStatusCodePages()`, tedy zásah do celé roury, ne do téhle otázky. Dokument popisuje odpovědi koncového bodu, a k tomu se takový požadavek nedostane.

## Důsledky

**Příští vydání je MAJOR, tedy 2.0.0.** Rozhodnutí 041 řadí rozbití REST kontraktu mezi tři důvody pro MAJOR a tvar chybové odpovědi do kontraktu patří: kdo si na řetězcové tělo postavil klienta, musí zasáhnout. Číslo v `Directory.Build.props` se posouvá až krokem 2 postupu vydání, ne teď.

**Testy podle rozhodnutí 043 se mění spolu s tím** — asercí na `application/problem+json` a na pole `detail`. Je to přesně ta viditelnost, kvůli které kontraktní testy vznikly: změna tvaru odpovědi musí být úmyslná úprava testu, ne tichý posun.

**Nástroj získává místo, kam chybová odpověď může růst.** RFC 9457 připouští rozšiřující pole, takže až bude co říct strukturovaně — která jednotka selhala, na kterém řádku —, je kam to dát, aniž se kontrakt láme podruhé.
