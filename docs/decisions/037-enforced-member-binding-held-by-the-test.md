# 037 — Vazbu deklarace a emise vynucených členů drží test

Datum: 2026-08-21
Stav: revidováno
Požadavky: S1, S2
Podklad: audit 2026-08-21, nález 1.3; rozhodnutí [009](009-target-framework-descriptor.md)

## Kontext

Deskriptor cílového frameworku deklaruje vynucené členy — co builder přidává ke generovanému artefaktu, ačkoli to není fakt o doméně (rozhodnutí [009](009-target-framework-descriptor.md)). Deklaraci ale v produkčním kódu nikdo nečte: `EnforcedMembers` a `EnforcedMembersFor` volá jedině `EnforcedMembersTest` a test tvaru deskriptoru. Každý builder si vynucené členy vypisuje sám a nezávisle — `virtual` v `BuildPropertySignature`, `[Serializable]` s identitními členy u kompozitního klíče, `[Keyless]` u EF Core — a podmínku, za které člen platí, vyhodnocuje vlastním kódem (`HasCompositeKey`), ne dotazem na deskriptor.

Rozhodnutí 009 tuhle dělbu zvolilo záměrně: deskriptor deklaruje, builder implementuje, test je váže. Audit 2026-08-21 (nález 1.3) pojmenoval, co v té volbě zůstalo nevyslovené: **záruka determinismu (S2) se u vynucených členů opírá o pokrytí testem, a nikde není řečeno, že se o něj opírá, ani co to pokrytí musí obsahovat.** Rozejde-li se deklarace s emisí u členu nebo podmínky, kterou test nepokrývá, nikdo se to nedozví. Otevřená položka k tomu kladla otázku, jestli má vazbu držet test, nebo si má builder vynucené členy z deskriptoru brát.

## Zvažované varianty

1. **Builder si vynucené členy bere z deskriptoru.** Deklarace a emise by byly jedním místem a rozejít by se nemohly z principu.

2. **Vazbu drží test — a vysloví se, že ji drží, i co jeho pokrytí musí obsahovat.**

## Rozhodnutí

**Volíme variantu 2: vazbu deklarace a emise drží `EnforcedMembersTest`. Nově je vyslovené, že se o něj záruka S2 u vynucených členů opírá, a je vyslovený kontrakt jeho pokrytí. Produkční kód se nemění.**

Varianta 1 vypadá bezpečněji jen do chvíle, než se domyslí, co by builder z deklarace vlastně četl. Deskriptor záměrně nepopisuje syntaxi (rozhodnutí 009): deklaruje, *že* NHibernate vyžaduje identitní členy u kompozitního klíče, ale těla `Equals`/`GetHashCode` zná jedině builder — z deklarace se vygenerovat nedají. Polovina dnešních vynucených členů navíc není vkládaný text, ale nepřítomnost (`sealed`, deklarovaný konstruktor, konkrétní typ kolekce podle rozhodnutí [035](035-nhibernate-collections-declared-by-interface.md)) — „emitovat" nepřítomnost z deklarace nejde, dá se jen kontrolovat, a kontrola je přesně to, co dělá test. Zbylo by tedy párování člen–emise podle názvu, a to vrací riziko, kterým rozhodnutí 009 tuhle cestu zamítlo: překlep v názvu generování tiše vypne a test porovnávající tentýž překlep na obou stranách to neodhalí. Vazba přes společný kód by tedy byla iluzí jednoho místa — skutečná vazba by dál visela na řetězci, jen skrytém.

Test je naopak vazbou přiznaně vnější, a proto kontrolovatelnou. Jeho síla je přesně silou matice případů, takže ta se tímto rozhodnutím stává kontraktem:

- **Matice pokrývá každý framework a každou podmínku, kterou nějaký deskriptor u některého členu vyslovuje, v obou pravdivostních hodnotách.** Dnes: tři frameworky × tvar klíče žádný/jednodílný/složený, čímž `CompositePrimaryKey` i `NoPrimaryKey` jednou platí a jednou neplatí. Výjimky jsou dvě a obě plynou z výčtu, ne z mezery v matici. `Always` nepravdivá být nemůže, takže negativní polovinu z definice nemá — u ní kontrakt žádá jen to, aby platná polovina byla v matici u každého frameworku, jehož deskriptor takového člena nese. A NHibernate bez klíče v matici není, protože brána úplnosti entitu odmítne dřív, než artefakt vznikne; odmítnutí tvrdí jiný test. Podmínka, kterou u daného frameworku nepodmiňuje žádný člen, se nepočítá za pokrytou ani nesplněnou — deskriptor Dapperu vynucené členy nemá vůbec a EF Core má jediný, takže se jich týká jen ta jejich.
- **Test má pozitivní i negativní polovinu:** člen s platnou podmínkou musí být přítomen (a zakázaná značka nepřítomna), člen s neplatnou podmínkou přítomen být nesmí. Bez negativní poloviny by prošel builder, který vypisuje všechno bezpodmínečně.
- **Artefakt musí členy skutečně zaměstnat.** Zakázané kolekční značky rozhodnutí 035 splní i entita bez kolekce — nepřítomností, ne builderem; testovací entita proto kolekční vlastnosti nese. Totéž pravidlo platí pro každou budoucí značku: podmínky splněné prázdnem se do matice nepočítají.
- **Nový člen nebo nová podmínka rozšiřuje matici v témž kroku, který je zavádí.** Pojistka proti tichému úniku existuje jen u podmínek: neznámou hodnotu `Applies` shodí výjimkou, takže podmínka bez vyhodnocení neprojde — ale obě pravdivostní hodnoty jí dodá až rozšířená matice, a to za kompilátor nikdo nepohlídá.

Vědomý strop z rozhodnutí 009 zůstává: značky se hledají podřetězcem, takže test odhalí, že `virtual` vypadl úplně, ne že chybí u jedné vlastnosti. Přísnější kontrola by znamenala výstup parsovat, což deskriptoru nepatří.

## Důsledky

**`architecture.md` §5 nese vyslovenou vazbu:** determinismus emise vynucených členů stojí na matici případů `EnforcedMembersTest`, s odkazem sem.

**Otevřená položka o vynucených členech se uzavírá.** Otázku „test, nebo čtení z deskriptoru" tímhle rozhodnutím zodpovídáme a chybějící kombinace podmínek v matici už nejsou — pokrytí popsané výše je stav testu, ne plán.

**Produkční kód se nemění a chování také ne.** Rozhodnutí 009 platí dál beze změny; tohle rozhodnutí k němu doplňuje, o co se jeho dělba opírá, což 009 nechalo nevyslovené.

**Pro nový framework (S1) znamená deskriptor, builder a řádek v matici.** Přidání frameworku do `Cases()` je součást jeho wrapperu stejně jako deskriptor sám.

## Historie

**2026-08-21 — revidováno.** Kontrakt pokrytí byl v původním znění nesplnitelný: žádal každou hodnotu `EnforcedMemberCondition` „v obou pravdivostních hodnotách", jenže `Always` z definice nikdy neplatí nepravdivě, takže negativní polovina pro ni existovat nemůže a `GeneratedArtifactOmitsMembersWhoseConditionDoesNotHold` ji nikdy neproběhne. Zároveň znění mlčky předpokládalo, že každou podmínku vyslovuje každý framework, kdežto Dapper vynucené členy nemá žádné a EF Core jediný. Kontrakt je proto přeformulovaný na podmínky, které nějaký deskriptor skutečně vyslovuje, a obě výjimky — `Always` bez negativní poloviny, NHibernate bez klíče — jsou v něm pojmenované. Volba sama se nemění: vazbu deklarace a emise dál drží test a matice zůstává táž, mění se jen věta, která ji popisuje. Revize na místě je bezpečná, protože podle původního znění nevznikl žádný kód — rozhodnutí produkční kód výslovně neměnilo a matice existovala už před ním.
