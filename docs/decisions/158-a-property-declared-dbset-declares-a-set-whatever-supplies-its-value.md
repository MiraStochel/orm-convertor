# 158 — Vlastnost deklarovaná `DbSet<T>` je deklarací sady, ať její hodnotu dodá cokoli; kořen, který je její hodnotou, dotaz nepředává

Datum: 2026-10-20
Stav: platí
Požadavky: F11, F14, S1, S2
Podklad: rozhodnutí [026](026-home-of-shared-query-reading.md), [053](053-a-query-that-would-return-other-rows-is-not-emitted.md), [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [109](109-a-code-unit-carries-every-query-it-hands-over.md), [111](111-a-unit-is-a-whole-source-file-that-declares-only-its-language.md), [114](114-what-the-unit-states-about-a-name-decides-an-ef-core-root.md) a [132](132-ef-core-fluent-configuration-is-read-as-a-bounded-syntactic-subset.md); měření 2026-10-20 proti EF Core 10.0.10 nad modelem, který EF Core sestaví bez připojení (`EFCore/EFCoreDbSetDeclarationProviderTest`); sonda přes `ConversionHandler.Convert` z 2026-10-20; dokumentace EF Core, *Working with Nullable Reference Types*, oddíl *DbContext and DbSet*

## Kontext

**Sada kontextu má v C# víc zápisů a nástroj je čte různě.** Kontext EF Core vystavuje entitu vlastností typu `DbSet<T>`. Vedle automatické vlastnosti `public DbSet<Order> Orders { get; set; } = null!;`, kterou EF Core při vzniku kontextu sám naplní, dokumentace EF Core k nulovatelným referencím uvádí zápis výrazem `public DbSet<Order> Orders => Set<Order>();`, který varování překladače obchází bez `null!`. V projektech se potkají i jeho obměny: getter s tělem (`get { return Set<Order>(); }`), getter výrazem, vlastnost jen pro čtení přiřazená v konstruktoru (`Orders = Set<Order>();`) a líné uložení do pole (`=> orders ??= Set<Order>();`).

**Sonda z 2026-10-20 ukázala, co z toho dnes vyjde.** Kontext se čtyřmi sadami ve čtyřech zápisech, s polem typu `DbSet<T>` a se dvěma metodami, které nad sadami dotazují, vydal do Dapperu pět dotazů: `Query01` až `Query03` jsou `SELECT * FROM …` celé tabulky za sadu přiřazenou v konstruktoru, za sadu výrazem a za línou sadu, teprve `Query04` a `Query05` jsou dotazy, které zdroj klade. Sada zapsaná `{ get; set; }` nevydá nic. Příčinou je, že rozhodnutí 111 čte uvnitř kontextu volání `Set<T>()` jako kořen, stejně jako `ctx.Set<T>()` zvenku, a kořen, který je voláním, zůstává kořenem i bez kroku LINQ (109, 114) — každé `Set<T>()` v textu je tedy místem předání, ať stojí kdekoli.

**Co z toho plyne, je vada ve čtyřech podobách.** Dotaz, který zdroj neklade, je krajní případ toho, co rozhodnutí 053 a 114 zakazují: artefakt, který vrací řádky, o které zdroj nežádá. Dva zápisy jedné deklarace dávají různý výstup, což rozhodnutí 111 vylučuje („tvar textu rozhoduje jen o tom, jak se text rozparsuje, nikdy o roli"). Pořadová jména dotazů (108, 109) se posunou podle toho, jak je napsaná sada, se kterou dotaz nemá nic společného: dotaz, který je v jednotce první, se jmenuje `Query04`. A jednu deklaraci čtou dva průchody jako dvě věci: čtení konfigurace podle rozhodnutí 132 bere jméno vlastnosti jako tabulku entity (ve všech zápisech, sonda to potvrdila), dotazový průchod z jejího těla dělá dotaz.

**Co je sada, se ptá EF Core i C#, a oba odpovídají stejně.** Měření z 2026-10-20 proti EF Core 10.0.10 (`EFCore/EFCoreDbSetDeclarationProviderTest`) sestavilo model kontextu se sadami ve čtyřech zápisech — automatická vlastnost, výraz nad `Set<T>()`, getter s tělem, vlastnost jen pro čtení přiřazená v konstruktoru: entita je v modelu ve všech čtyřech a její tabulka se jmenuje po vlastnosti; dotaz nad sadou zapsanou výrazem čte `FROM [Orders]`. Pole téhož typu přiřazené v konstruktoru do modelu nedá nic. EF Core tedy sadu pozná podle deklarovaného typu a jména vlastnosti a tělo nečte; tělo jen dodá instanci, kterou getter vrátí. C# říká totéž o hodnotě: vlastnost typu `DbSet<T>` vrací `DbSet<T>`, tedy kořen sám, a kořen nic nespouští — dotaz vznikne teprve řetězem nad ním, který kód vyčíslí, a o tom, co je dotaz, rozhoduje typ, ne místo (114). Řetěz nad sadou nástroj čte tam, kde stojí: `ctx.Orders.Where(…)` vedle kontextu, `Orders.Where(…)` uvnitř (111).

Rozhodnout je třeba, zda je tělo vlastnosti typu `DbSet<T>` deklarací sady, nebo předáním dotazu, které části textu se to týká a jak se to vysloví v katalogu podmnožiny.

## Zvažované varianty

### 1 — Ponechat čtení a vyslovit mez

Řádek v [`subset.md`](../subset.md) 2.3 by řekl, že sada zapsaná výrazem vydá dotaz celé tabulky. Zamítáme. Nejde o konstrukci, kterou nástroj nepřečte, ale o dotaz, který si vymyslí; rozhodnutí 053 a 114 to nedovolují vyslovením meze vyřešit, a F11 zakazuje nepodporovanou konstrukci potichu vynechat, natož ji potichu nahradit jinou. Mez by navíc rozdělovala zápisy, které EF Core i C# čtou jako jeden.

### 2 — Výjimka pro tvar `=> Set<T>()`

Nečíst jako dotaz právě tělo `=> Set<T>()`, které uvádí dokumentace. Opravilo by nejčastější zápis a nic víc. Zamítáme, protože tatáž deklarace má další zápisy, které měření ukázalo jako rovnocenné — getter s tělem, getter výrazem, přiřazení v konstruktoru, líné pole —, a výjimka podle tvaru by je nechala vadné. Je to přesně ta vada, kterou rozhodnutí 111 vytklo pravidlu podle tvaru textu: role by závisela na tom, jak je deklarace napsaná, a výčet výjimek by rostl s každým zápisem, na který někdo narazí.

### 3 — Kořen bez kroku dotazem není nikdy

Nejširší pravidlo: dotazem je jen řetěz s krokem, holé `Set<T>()` nikde. Zamítáme, protože by ztratilo skutečné dotazy. `foreach (var o in ctx.Set<Order>())` vyčíslí celou tabulku na místě; metoda `IQueryable<Order> All() => Set<Order>();` předává dotaz volajícímu, jak to rozhodnutí 109 čte u každé metody vracející `IQueryable`; lokální proměnná nebo pole s `Set<T>()`, které kód neprodlužuje, je podle 109 dotaz, který drží. Všechny tři jsou předání celé tabulky a nástroj je dnes správně čte — pravidlo by je potichu zahodilo (F11). Rozhodnutí 114 kořen, který je voláním, nechalo kořenem i bez kroku právě proto.

### 4 — Celé tělo vlastnosti typu `DbSet<T>` nečíst

Nic v těle sady by nebylo místem předání. Jednoduché, ale zamítáme. Typ říká, co je *hodnotou* vlastnosti, ne co všechno její tělo dělá: getter, který si vedle vrácení sady spočítá dotazem něco jiného, ten dotaz při každém přístupu opravdu pošle. Je to nepravděpodobný kód, ale pravidlo, které by ho zamlčelo, by bylo tichým místem bez důvodu, když přesné pravidlo stojí stejně.

### 5 — Hodnota vlastnosti deklarované `DbSet<T>` je sada

Kořen bez kroku, který je hodnotou vlastnosti deklarované `DbSet<T>`, dotazem není; dotazem je řetěz nad sadou tam, kde ho kód píše, a co dalšího tělo dělá, čte se jako každý jiný kód.

## Rozhodnutí

**Volíme variantu 5. Vlastnost deklarovaná `DbSet<T>` je deklarací sady kontextu, ať její hodnotu dodá cokoli. Kořen bez kroku, který je její hodnotou — tělo výrazem, getter výrazem, hodnota, kterou getter vrací, a pravá strana přiřazení do vlastnosti —, předáním dotazu není. Dotazem je řetěz nad sadou, a čte se tam, kde ho kód píše.**

**Co je hodnotou vlastnosti.** Výraz těla vlastnosti (`=> …`) a výraz getteru (`get => …`); výraz příkazu `return` v getteru, není-li uvnitř vnořené lambdy či lokální funkce; pravá strana přiřazení, jehož levá strana jmenuje tu vlastnost holým jménem nebo přes `this` a jehož třída vlastnost deklaruje. Hodnota projde beze změny závorkami, značkou `!`, operandy `??` a přiřazením, takže i `orders ??= Set<Order>()` — sada uložená do pole a vrácená — je hodnotou vlastnosti. Kořenem je cokoli, co wrapper pozná jako kořen-volání: `Set<T>()`, `this.Set<T>()`, `base.Set<T>()` i `ctx.Set<T>()` jiného kontextu. Je to výčet toho, kudy hodnota v C# beze změny prochází, ne seznam tvarů: zápis, který hodnotu mění (`Set<T>().Where(…)`), sadu nevrací a deklarací být nemůže, protože by se nepřeložil.

**Proč typ, a ne tělo.** Obě autority, na které se otázka ptá, rozhodují podle deklarace. EF Core sestaví model z typu a jména vlastnosti, tělo nečte (změřeno ve čtyřech zápisech). C# naváže hodnotu vlastnosti typu `DbSet<T>` jako kořen, který nic nespouští, a dotaz, který dostane poskytovatel, je strom řetězu nad ním — přesně argument, kterým rozhodnutí 114 oddělilo dotaz od navigace. Pravidlo je proto souměrné s 114: tam typ členu říká, že `x.M` je kořen, navigace nebo dotaz složený přes člen, tady typ vlastnosti říká, že její hodnota je kořen sám.

**Co platí dál beze změny.**

- **Řetěz nad sadou** se čte tam, kde stojí, v každém zápisu sady: uvnitř kontextu `Orders.Where(…)` a `this.Orders.Where(…)` (111), zvenku `ctx.Orders.Where(…)` (026, 114). Pořadová jména dotazů jednotky nezávisí na tom, jak jsou sady napsané.
- **Sada je konfigurace** podle rozhodnutí 132: jméno vlastnosti je tabulka entity (materializovaná podle 067) v každém zápisu; kontext dál není entitou a nese svůj záznam `Loss` (111).
- **Kořen bez kroku jinde** zůstává, čím ho dělají rozhodnutí 109 a 114: vyčíslení `Set<T>()` v cyklu, metoda vracející `Set<T>()` jako `IQueryable`, lokální proměnná a **pole** s `Set<T>()`. Pole typu `DbSet<T>` deklarací sady není — EF Core ho do modelu nebere (změřeno) — a kód ho čte jako proměnnou, kterou rozhodnutí 109 sleduje: řetěz nad ním se čte od kořene, který drží, a nepokračuje-li nad ním nic, je dotazem, který drží.

**Kde pravidlo bydlí.** Že hodnota vlastnosti určitého typu je kořen sám, je fakt jazyka a patří do sdíleného čtení LINQ (026), jako pravidlo proměnné prvku (109) a statického typu (114). Který typ je zdrojem dotazů poskytovatele, říká wrapper háčkem, který zavedlo rozhodnutí 114 (`IsProviderQuerySource`): u EF Core `DbSet<T>`. NHibernate háček nepřepisuje a jeho kořen je volání `Query<T>()` na session, které žádná vlastnost jeho API nedeklaruje; pro něj se nic nemění. Oba průchody dostanou tutéž odpověď týmž hledáním předání (111); kontext entitou není tak jako tak.

**V katalogu podmnožiny je to kladné čtení, ne mez.** Sada v každém zápisu se přeloží celá — jako tabulka entity a jako kořen řetězů nad ní —, nic se neztrácí a nic se neodmítá. Do části 2.3 [`subset.md`](../subset.md) proto nepatří žádný řádek; zápisy sady vyjmenuje řádek EF Core v části 1.6.

## Důsledky

**Podle rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) je to MINOR navenek.** Pole odpovědi ani požadavku nepřibývá. Kontext, jehož sady jsou napsané výrazem, getterem nebo přiřazením v konstruktoru, přestane vydávat dotazy celých tabulek, a dotazy jednotky dostanou čísla, jako by sady byly automatické vlastnosti. Je to oprava vady, ne změna kontraktu.

**Co se mění v kódu.** Sdílené `LinqQueryParser.FindQueries` vynechá kořen bez kroku, který je hodnotou vlastnosti deklarované typem zdroje poskytovatele; háček `IsProviderQuerySource` se ptá i na tu vlastnost. Wrapper EF Core, orchestrace, `AbstractWrappers` ani entitní průchod se nemění (S1).

**Chování ostatních vstupů se nemění.** Matice směrů, kategorií a diferenční matice, katalog LDBC, katalog FIBEN i vzorky nesou sady jen jako automatické vlastnosti nebo kontext vůbec — žádná sada v nich není napsaná výrazem, getterem ani přiřazením. Jejich artefakty a záznamy musí zůstat stejné.

**Měření zůstává u háčku.** `EFCore/EFCoreDbSetDeclarationProviderTest` drží fakt EF Core, na kterém pravidlo stojí: změní-li nová verze EF Core, jak sadu pozná, selže tam.

**Testy** (`Combined/DbSetDeclarationTest`): každý zápis sady — výraz, výraz přes `this`, getter výrazem, getter s tělem, líné pole, `base.Set<T>()` se závorkou a `!`, přiřazení v konstruktoru holým jménem i přes `this` — nevydá dotaz, řetěz nad sadou se přečte a jmenuje se jako jediný dotaz jednotky; tabulka entity se jmenuje po sadě ve všech zápisech; vyčíslení `Set<T>()` v cyklu, metoda vracející `Set<T>()`, pole s `Set<T>()` bez pokračování a dotaz, který getter sady spouští vedle vrácení sady, zůstávají dotazy.

**Co tohle rozhodnutí neřeší.** Sonda ukázala tiché místo jiného druhu: `foreach (var o in ctx.Orders)` — vyčíslení sady zapsané jménem, bez volání — se nečte v žádném zápisu sady, dotaz zmizí beze slova a třída kolem něj vyjde jako entita. Hledání předání bere za místo jen volání, takže kořen podle místa bez kroku nevidí. Je to vada čtení, ne otázka deklarace sady, a položka k ní je v [`open-items.md`](../open-items.md).
