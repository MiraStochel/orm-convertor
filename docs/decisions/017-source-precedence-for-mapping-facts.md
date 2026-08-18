# 017 — Priorita zdrojů uvnitř vstupu

Datum: 2026-08-18
Stav: platí
Požadavky: F2, F4, F5, F6, F11, S1, S2

## Kontext

Požadavek F5 žádá, aby se informace ze **zdrojového kódu, anotací, externích mapovacích souborů a databázového katalogu** slévaly do jediné mezireprezentace „s dokumentovanou prioritou zdrojů a hlášením konfliktů". Rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md) na to odpovědělo z poloviny: zavedlo tři stupně priority — **zdroj → katalog → konvence cíle** —, dalo zdroji přednost před katalogem, rozpor prohlásilo za záznam `Conflict` a původ doplněného faktu za záznam `Supplied` (viz [`architecture.md`](../architecture.md), §5.1 a §5.2).

Nedořečená zůstala první z těch čtyř položek F5, a přitom je to ta, se kterou nástroj pracuje pokaždé: **„zdroj" není jeden artefakt.** Vstupem převodu z NHibernate je C# třída a k ní `hbm.xml`; vstupem z EF Core je třída s anotacemi a — jakmile se začne číst — fluent konfigurace v `DbContext`u; vstupem z MyBatisu bude javová třída a XML mapper. Dnes o pořadí mezi nimi rozhoduje **pořadí parserů v seznamu, který vrací `ParserFactory`**, a uvnitř jedné entity to, kdo do `AbstractEntityBuilder` zapíše naposled. Ani jedno není zapsané jako pravidlo.

Že to zatím nevadí, je shoda okolností, ne návrh. `ParserFactory` vrací pro NHibernate dvojici v pořadí entitní parser, XML parser, a `NHibernateXMLMappingParser` si entitu **vyhledá podle jména třídy** mezi už rozparsovanými a novou zakládá jen tehdy, když žádnou nenajde; jmenný prostor z mapování zapíše jen tam, kde ho třída neuvedla. To je přesně chování, které chceme — jenže plyne z implementace, ne z pravidla, takže se přehozením dvou řádků v seznamu tiše obrátí. Opačným směrem jde `SetPropertyDatabaseMapping`, které hodnoty přepisuje bezpodmínečně: kdyby dva vstupní artefakty tvrdily o téže vlastnosti různý sloupec, vyhraje pozdější zápis a **nevznikne žádný záznam** — na rozdíl od téhož rozporu s katalogem, který 015 hlásit umí.

Pro překlad z Dapperu je otázka prázdná, protože artefakt je jediný. Pro Javu prázdná nebude: JPA připouští anotace i `orm.xml`, MyBatis anotace i XML mapper, a u obou je běžné, že projekt používá obojí naráz. Rozhodnout se to tedy musí dřív, než se sáhne na F7–F10, ne až u prvního rozporu.

## Zvažované varianty

1. **Ponechat pořadí implicitní v pořadí parserů.** Nic nestojí a dnes se chová správně. Jenže nezapsané pravidlo se nedá porušit ani dodržet — mění se s každým přeuspořádáním seznamu a nikdo nepozná, že se změnilo. F5 navíc žádá prioritu *dokumentovanou*, tedy takovou, kterou lze citovat, a hlášení konfliktů, které dnes mezi vstupními artefakty nevzniká vůbec. Zamítáme.

2. **Dát přednost mapovacímu artefaktu před textem entity.** Odpovídá tomu, jak precedenci řeší EF Core (fluent API přebíjí anotace), a dá se hájit tím, že mapovací soubor je explicitnější. Cenou je, že nejsilnějším zdrojem se stává artefakt, který **nemusí být k dispozici**: výsledek převodu by pak závisel na tom, kolik souborů uživatel přiložil, a přidání mapování by změnilo i fakty, které mapování neřeší. Entitní text je přitom jediný artefakt přítomný vždy a jediný, který nese jazykovou stranu modelu (rozhodnutí [014](014-language-type-model.md)). Zamítáme jako obecné pravidlo; zbývající případ viz Důsledky.

3. **Považovat vstupní artefakty za rovnocenné a každý rozdíl jen hlásit.** Poctivé, ale nepoužitelné: bez pravidla není co zapsat do mezireprezentace, takže by převod buď skončil, nebo by stejně musel jednu stranu vybrat — jen bez odůvodnění. Pro S2 je to horší než libovolné pevné pořadí, protože výsledek by závisel na pořadí vstupů.

4. **Uspořádané stupně: vstupní text frameworku → pomocné mapovací artefakty → databázový katalog → konvence cíle.** Vyšší stupeň se nikdy nepřepisuje nižším, nižší jen zaplňuje mezery, rozdíl je záznam. Rozšiřuje 015 v jeho vlastní ose, místo aby jeho volbu měnilo.

## Rozhodnutí

Volíme variantu 4. **Fakt tvrzený na vyšším stupni platí; nižší stupeň smí jen doplnit, co chybí, a neshodu hlásí záznamem, místo aby ji potichu přepsal.**

Tři stupně z rozhodnutí 015 zůstávají a jejich číslování se nemění — co dosud znamenalo „konvence třetího stupně", znamená totéž dál. Mění se jen **první stupeň, který se dělí na dvě úrovně**:

| Stupeň | Úroveň | Co to je |
|---|---|---|
| 1. zdroj | 1a — vstupní text frameworku | artefakt, který se překládá: entitní třída včetně anotací |
| 1. zdroj | 1b — pomocné mapovací artefakty | soubory a konfigurace, které k němu mapování dodávají |
| 2. katalog | — | databázový katalog (rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)) |
| 3. konvence | — | konvence cílového frameworku |

Konkrétně u dnešních i plánovaných frameworků:

| Framework | 1a — vstupní text | 1b — pomocné mapování |
|---|---|---|
| Dapper | C# entitní třída | — (Dapper mapování nemá) |
| EF Core | C# třída včetně anotací | fluent konfigurace v `DbContext`u (zatím se nečte) |
| NHibernate | C# entitní třída | `hbm.xml` |
| Hibernate, EclipseLink (F7, F9) | javová třída s JPA anotacemi | `orm.xml` |
| MyBatis (F8) | javová třída | XML mapper, `resultMap` |

**Pravidlo se uplatní jen tam, kde dvě úrovně tvrdí totéž.** U většiny frameworků jsou úrovně věcně oddělené — třída nese jazykovou stranu, mapování databázovou —, takže se pořadí projeví zřídka. Právě proto se ale musí vyslovit: případy překryvu jsou vzácné, a tím pádem se na ně nepřijde vyzkoušením.

**Pořadí čtení je součástí definice frameworku, ne shoda okolností.** Seznam parserů, který `ParserFactory` vrací pro daný framework, je uspořádaný podle tohoto pravidla a je to jeho vyslovený fakt, ne pořadí, ve kterém někdo napsal řádky.

**Neshoda mezi úrovněmi je `Conflict`.** Používáme týž druh záznamu, který 015 zavedlo pro rozpor zdroje s katalogem, protože jde o tutéž událost: dva zdroje faktu tvrdí různé věci a my jeden z nich vybíráme podle zapsaného pravidla. Záznam říká, co tvrdila nižší úroveň, aby šlo výsledek zpětně obhájit — bez toho by rozdíl zmizel a F11 by neměla co vrátit.

**Pořadí vstupů od uživatele význam nemá.** Dva artefakty téže úrovně — dva mapovací soubory k jedné entitě — se nesmí rozhodovat tím, který přišel dřív. Takový rozpor je vždy `Conflict` a platí hodnota zapsaná jako první, aby byl výsledek deterministický (S2). Že to není uspokojivá odpověď, přiznáváme: uspokojivá by byla chyba vstupu, jenže odmítnout celý převod kvůli jednomu rozporu je proti rozhodnutí [010](010-diagnostics-as-returned-data.md), které chybu překladu vydává jako data.

## Důsledky

Mezireprezentace se nemění a **nezačíná evidovat původ faktu** — to zůstává, jak rozhodlo 010 a převzalo 015. Pravidlo se dá dodržet i bez toho: úrovně jsou uspořádané v čase (nejdřív se přečte celá 1a, pak 1b, pak běží fáze doplnění), takže „už tvrzeno výš" je totéž co „fakt je při příchodu nižší úrovně neprázdný". Právě tahle rovnost je důvod, proč pořadí čtení musí být vyslovené: s nezaručeným pořadím by neplatila a původ by se do modelu musel vrátit.

V kódu z toho plyne dvojí. `SetPropertyDatabaseMapping` a jeho obdoby musí přestat přepisovat bezpodmínečně a začít rozlišovat prázdný fakt od obsazeného — stejně jako to dělá fáze doplnění z katalogu, jejíž zápis je přírůstkový a idempotentní. A `ParserFactory` dostane pořadí jako komentovaný fakt, ne jako implementační detail. Fáze doplnění z katalogu se nemění, svou polovinu pravidla už implementuje.

**Jeden případ tohle rozhodnutí vědomě neuzavírá:** framework, který má **vlastní dokumentovanou precedenci** mezi svými artefakty. EF Core staví fluent API nad anotace, MyBatis řeší souběh anotací a XML vlastními pravidly. Tam naše pořadí říká opak toho, co zdrojový projekt znamená — a překlad má reprodukovat význam zdroje, ne naši preferenci. Dokud se čte jediný artefakt na framework, případ nenastává; nastane s prvním parserem fluent konfigurace. Je to samostatná otevřená položka, ne mlčení.

Vůči rozhodnutí 015 je tohle **doplnění, ne změna volby**: 015 dál platí ve všem, co říká, a jeho stav se nemění. Sahá se jen do místa, které nechalo nedořečené.
