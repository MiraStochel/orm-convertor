# 098 — Číslo verze se rozhoduje jednou za vydání, ne u každé změny

Datum: 2026-09-22
Stav: platí
Požadavky: S2, S6
Podklad: rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md), které tímto nahrazujeme, a přes ně [041](041-versioning-and-release.md); dále [007](007-documentation-structure.md), [013](013-target-framework-versions.md) a [034](034-central-version-management.md); revize [2026-09-21](../audits/2026-09-21-pre-release-2-0-0-audit.md), nálezy 3.1, 3.2, 3.3 a 4.1; průběh vydání `2.0.0` z 2026-09-22

## Kontext

Rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) odpovědělo na otázku, **co číslo verze měří**: schopnosti nástroje, s první číslicí vyhrazenou milníku zadání. Ta odpověď platí, je dobře odůvodněná a přenášíme ji beze změny. Zpochybňujeme něco jiného — **obřad, který kolem čísla mezitím vyrostl.**

Ten obřad má čtyři části a všechny se do 069 přenesly z [041](041-versioning-and-release.md), aniž by je 069 samo zvažovalo: čtyřkrokový postup vydání s **vlastním commitem** pro posun `<Version>`; **pevná stavba anotace** značky o třech odstavcích v daném pořadí; **klasifikační věta**, kterou každé rozhodnutí od 071 dál zařazuje svou vlastní změnu na stupnici MAJOR/MINOR/PATCH; a **historie vydání**, která se odstavec po odstavci hromadí v sekci *Versioning and releases* kořenového [`README.md`](../../README.md) a podruhé v [`architecture.md`](../architecture.md) §9.

**První vydání vedené podle 069 ukázalo, co z toho měří skutečnost.** Značka `2.0.0` vznikla 2026-09-22 na commitu `a599e15`, kterým se `<Version>` i `CITATION.cff` posunuly na vydávané číslo — a ten commit není commit vlastní: posun čísla se vezl uvnitř úklidu dokumentace. Pravidlo o vlastním commitu se tedy porušilo hned při prvním použití a **nepřišli jsme o nic**, protože to jediné, na čem záleží, drží dál: značka sedí na commitu, který vydávané číslo skutečně nese, takže verze v sestavení, kterou vydává záznam běhu (S6), a verze na značce nemůžou tvrdit každá něco jiného. Vlastní commit tohle nechránil — chránil pořadí kroků, tedy tvar postupu, ne jeho výsledek.

**Klasifikační věta stojí víc, než vynese.** Skoro každé rozhodnutí od 071 uzavírá své důsledky větou „podle 069 je to MINOR" nebo „je to PATCH". Nerozhoduje se tím nic: o číslo se hraje až ve chvíli vydání, a to se ptá na **celek**. Rozhodnutí [075](075-unknown-language-type-is-a-reported-incompleteness.md), [079](079-fractional-second-precision-as-second-precision.md), [090](090-the-cross-ecosystem-matrix-counts-itself.md) i [093](093-unreadable-input-is-a-unit-failure.md) to o sobě říkají sama — každé dodává, že vydání je MINOR beztak z jiných důvodů. Co ta věta naopak vyrobila, je **adresa, která může zastarat**: revize [2026-09-21](../audits/2026-09-21-pre-release-2-0-0-audit.md) našla v rozhodnutí [070](070-a-parser-refuses-what-would-change-the-row-set.md) citaci nahrazeného 041 jako živého pravidla (nález 4.1), a protože se rozhodnutí nepřepisují ([007](007-documentation-structure.md)), opravit se to nedá. Dvacet rozhodnutí odkazuje na pravidlo verzování jen proto, aby vyslovila zařazení, které nikdo nečte, a každé z těch míst je kandidát na týž nález — tenhle soubor by jich dvacet vyrobil naráz.

**Historie vydání ve dvou dokumentech je třetí opis téhož textu.** Co vydání změnilo, říká anotace jeho značky; README a §9 to opakují, každý vlastními slovy a s každým dalším vydáním o odstavec navíc. Právě tam našla revize 2026-09-21 rozcházející se místa: README nenárokoval S7, který §9 i [`traceability.md`](../traceability.md) nárokují (nález 3.1), popisoval dotazovou větev jako „nine directions between the three frameworks", ačkoli směrů je šestatřicet (3.2), a nesl o vydání `1.2.0` větu, kterou obsah kolem ní už nesplňoval (3.3). **Tři místa téhož tvrzení nejsou tři záruky, ale tři příležitosti k rozporu** — a navíc příležitosti nesouměrné: rozpor v README se opraví commitem, kdežto anotace se neposouvá, takže rozchází-li se text s ní, je vadný text.

Nic z toho nezpochybňuje samo číslo. Zpochybňuje to, že kolem tříslovného pravidla — *značka, číslo, poznámky* — stojí postup o čtyřech krocích, předpis tvaru textu a povinnost, kterou plní dvacet souborů a nečte nikdo.

## Zvažované varianty

1. **Ponechat 069 a jen zkrátit README a §9.** Text by se zkrátil, pravidlo ne: vlastní commit, tři odstavce anotace i klasifikační věta by dál platily a dál by se nedodržovaly — vydání `2.0.0` porušilo první z nich, aniž by si toho kdokoli všiml. Pravidlo, které se porušuje bez následku, je horší než žádné, protože kazí soud o všech ostatních pravidlech vedle sebe. Zamítáme.

2. **Opustit i význam čísla a vrátit se k běžnému sémantickému verzování.** 069 to zvážilo a zamítlo z důvodu, který platí beze změny: nástroj není publikovaný, takže slib o kompatibilitě nemá adresáta, kdežto otázka „kam práce došla" adresáta má — text práce a záznam běhu podle S6. Navíc by to spálilo číslo `3.0.0`, které je slíbené třetímu cíli. Zamítáme.

3. **Zrušit čísla a vydávat datované značky.** Nejjednodušší možné pravidlo, a přesně proto nedostatečné: S2 podmiňuje determinismus „stejnou verzí nástroje" a datum tu otázku sice zodpoví, ale `CITATION.cff` i záznam běhu nesou číslo jako údaj o **dosažené schopnosti**, ne jen jako identitu běhu. Datum navíc nerozliší vydání, které zavřelo cíl, od vydání, které opravilo překlep. Zamítáme.

4. **Ponechat význam čísla z 069 a zkrátit pravidlo na to, co se dá zkontrolovat příkazem:** číslo na dvou zápisech, značka na commitu, který je nese, poznámky v anotaci volným tvarem, klasifikace jednotlivé změny se nepíše a historie vydání žije jedině v anotacích.

## Rozhodnutí

**Volíme variantu 4. Pravidlo vydání má napříště čtyři věty.**

1. **Číslo je `MAJOR.MINOR.PATCH` a měří schopnosti nástroje** (beze změny z 069). **MAJOR** — uzavřel se celý cíl z [`open-items.md`](../open-items.md); **MINOR** — uvnitř cíle přibyla schopnost; **PATCH** — všechno ostatní, tedy oprava i změna veřejné plochy, která schopnost nepřináší. Vydání nese nejvyšší pozici, kterou pohnula kterákoli jeho změna. Poslední vydání je `2.0.0` za javovský ekosystém, **příští MAJOR je `3.0.0`** za Advisor nad všemi frameworky a za experimentální část (F15, T1–T7).

2. **Číslo se píše na dvě místa a značka se vyráží na commitu, který ho už nese:** `<Version>` v `Directory.Build.props` (rozhodnutí [034](034-central-version-management.md)) a pole `version` s `date-released` v `CITATION.cff`. **Jestli ten commit dělá ještě něco jiného, je jedno.** Zkontrolovat se to dá jedním příkazem: `git show <značka>:ORMConvertor/Directory.Build.props` musí vydávané číslo obsahovat.

3. **Poznámky k vydání bydlí v anotaci značky** (`git tag -n99`), ne v souboru v repozitáři (rozhodnutí [007](007-documentation-structure.md)) a ne v README. Mají říct, **co se změnilo na tvaru generovaného artefaktu, co přibylo a co se pohnulo na hranici záruk** — to jsou tři plochy, o kterých vydání mluví, a přenášíme je z 069. **Pořadí ani počet odstavců nepředpisujeme**; píše se to, co se skutečně změnilo, a čím je vydání menší, tím je anotace kratší.

4. **Značka se nikdy neposouvá a číslo se nikdy nepoužije podruhé.** Vydání, které se ukáže být chybné, se opraví dalším číslem.

**Co se ruší, rušíme jmenovitě:**

- **vlastní commit** pro posun čísla a s ním celý čtyřkrokový postup — zbývá věta 2;
- **pevná stavba anotace** o třech odstavcích v daném pořadí — zbývá věta 3;
- **klasifikace jednotlivé změny.** Nové rozhodnutí ani `architecture.md` větu „podle rozhodnutí NNN je to MINOR" nepíše. Kde ji nese rozhodnutí starší, **zůstává**: rozhodnutí jsou snímky a nepřepisují se;
- **odstavec o každém vydání** v README a v §9. Co které vydání změnilo, vypíše `git tag -n99`.

**Z 069 se přenáší beze změny i to ostatní:** předpoklad, že nástroj **není publikovaný** — žádný balíček v registru, žádný vydávaný artefakt kromě značky, žádný konzument mimo repozitář —, a s ním pravidlo, že se tohle rozhodnutí **nahrazuje, ne dovysvětluje**, jakmile předpoklad přestane platit; posun **zafixovaných cizích verzí** ze dvou důvodů, totiž bezpečnostní hlášení a potřeba rozdělané práce, hlídaný úlohou `dependencies` v CI při každé změně řešení a jednou týdně; **posun verze cílového frameworku není nikdy PATCH** (rozhodnutí [013](013-target-framework-versions.md)), protože cestuje deskriptorem do záznamu běhu a mění to, proti čemu artefakt platí; a **žádná předběžná vydání ani větve** — vývoj je sólo a na `main`.

## Důsledky

**Rozhodnutí [069](069-major-marks-a-milestone-not-a-break.md) dostává stav `nahrazeno 098`** a zůstává čitelné jako záznam volby, která platila mezi 2026-08-26 a dneškem a podle které vznikla vydání `1.2.0` a `2.0.0`. Jeho úvaha o tom, co číslo měří, se tímhle rozhodnutím nemění — mění se jen to, co kolem čísla musí kdo udělat.

**Sekce *Versioning and releases* v kořenovém [`README.md`](../../README.md) se zkracuje** na pravidlo pro konzumenta — co tři pozice znamenají, kde číslo bydlí, že značka je vydání a že poznámky jsou v anotaci — a odstavce o `1.0`, `1.1.0`, `1.2.0` a `2.0.0` z ní mizí ve prospěch jedné věty s `git tag -n99`. Totéž se děje v [`architecture.md`](../architecture.md) §9, jejíž věta o vazbě nároku na značku zůstává: nárok popisuje současný stav `main` a vydání ho zmrazí spolu s číslem.

**Historie se tím neztrácí.** Anotace všech čtyř značek jsou na `origin` a `git tag -n99` je vypíše celé; jsou navíc jediným místem, které se nemůže rozejít samo se sebou, protože se neposouvá.

**Odkazy na pravidlo verzování se v živých dokumentech přesměrovávají sem** — v `architecture.md`, `open-items.md`, `traceability.md`, `quality-model.md`, kořenovém README a v `ORMConvertor/README.md`. V rozhodnutích a v revizích se nepřesměrovávají: obojí jsou snímky a přepsat je by porušilo [007](007-documentation-structure.md). Čtenář, který u staršího rozhodnutí narazí na 069 nebo 041, najde na jejich začátku stav `nahrazeno` a odsud se dostane k platnému znění.

**Klasifikační věty, které dnes nese `architecture.md`, se z ní odstraňují**, protože je to dokument o současném stavu, a to, do které pozice čísla kdysi spadla jedna změna, stavem není. Fakt, který ty věty nesly vedle zařazení — že se výstup změnil, že z kontraktu zmizela třída chyb —, zůstává; mizí jen jeho převod na číslo.

**Cena je vyslovená.** Kdo otevře README, uvidí napříště pravidlo a ne historii; k tomu, co se v kterém vydání změnilo, se dostane přes `git tag -n99`, tedy přes git, a nikoli přes webovou stránku repozitáře — byť GitHub tytéž anotace ukazuje u seznamu značek. Bereme to vědomě: adresátem README je konzument nástroje, který potřebuje vědět, co číslo znamená a kde hledat, a tři opisy téže historie mu nepomáhají ani v tom prvním, ani v tom druhém.

**Co se nemění vůbec:** hranice záruk a její kanonické znění (sekce *Guarantees* v README, §9 jako její český protějšek), obsah vydaných značek, způsob, jakým se číslo dostává do záznamu běhu, ani pravidlo, že changelog jako soubor v repozitáři nevzniká.
