# 037 — Vazbu deklarace a emise vynucených členů drží test

Datum: 2026-08-21
Stav: platí
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

- **Matice pokrývá každý framework a každou hodnotu `EnforcedMemberCondition` v obou pravdivostních hodnotách.** Dnes: tři frameworky × tvar klíče žádný/jednodílný/složený, čímž jsou `Always`, `CompositePrimaryKey` i `NoPrimaryKey` u každého frameworku jednou splněné a jednou nesplněné. Jediná legitimní výjimka je NHibernate bez klíče — brána úplnosti entitu odmítne dřív, než artefakt vznikne, a odmítnutí tvrdí jiný test.
- **Test má pozitivní i negativní polovinu:** člen s platnou podmínkou musí být přítomen (a zakázaná značka nepřítomna), člen s neplatnou podmínkou přítomen být nesmí. Bez negativní poloviny by prošel builder, který vypisuje všechno bezpodmínečně.
- **Artefakt musí členy skutečně zaměstnat.** Zakázané kolekční značky rozhodnutí 035 splní i entita bez kolekce — nepřítomností, ne builderem; testovací entita proto kolekční vlastnosti nese. Totéž pravidlo platí pro každou budoucí značku: podmínky splněné prázdnem se do matice nepočítají.
- **Nový člen nebo nová podmínka rozšiřuje matici v témž kroku, který je zavádí.** Pojistka proti tichému úniku existuje jen u podmínek: neznámou hodnotu `Applies` shodí výjimkou, takže podmínka bez vyhodnocení neprojde — ale obě pravdivostní hodnoty jí dodá až rozšířená matice, a to za kompilátor nikdo nepohlídá.

Vědomý strop z rozhodnutí 009 zůstává: značky se hledají podřetězcem, takže test odhalí, že `virtual` vypadl úplně, ne že chybí u jedné vlastnosti. Přísnější kontrola by znamenala výstup parsovat, což deskriptoru nepatří.

## Důsledky

**`architecture.md` §5 nese vyslovenou vazbu:** determinismus emise vynucených členů stojí na matici případů `EnforcedMembersTest`, s odkazem sem.

**Otevřená položka o vynucených členech se uzavírá.** Otázku „test, nebo čtení z deskriptoru" tímhle rozhodnutím zodpovídáme a chybějící kombinace podmínek v matici už nejsou — pokrytí popsané výše je stav testu, ne plán.

**Produkční kód se nemění a chování také ne.** Rozhodnutí 009 platí dál beze změny; tohle rozhodnutí k němu doplňuje, o co se jeho dělba opírá, což 009 nechalo nevyslovené.

**Pro nový framework (S1) znamená deskriptor, builder a řádek v matici.** Přidání frameworku do `Cases()` je součást jeho wrapperu stejně jako deskriptor sám.
