# 063 — Vyslovená bezklíčovost je nesený fakt a rozpor s klíčem soudí sémantika zdroje

Datum: 2026-08-25
Stav: platí
Požadavky: F5, F11, S2
Podklad: rozhodnutí [010](010-diagnostics-as-returned-data.md), [015](015-mapping-fact-completion-from-the-catalog.md), [017](017-source-precedence-for-mapping-facts.md) a [055](055-unique-constraint-as-a-carried-mapping-fact.md); zdrojový kód EF Core, větev release/10.0, `KeyAttributeConvention`

## Kontext

`EFCoreEntityBuilder` anotaci `[Keyless]` vypisuje — entita bez klíče ji v EF Core potřebuje, jinak by si framework klíč odvodil konvencí z vlastnosti `Id` nebo `{NázevTypu}Id` —, ale `EFCoreEntityParser` ji nečte. Od rozhodnutí 055 aspoň nemizí potichu: třídní anotace, kterou parser nezná, vydá záznam `Loss`. Fakt se tím ale nedostane do modelu a důsledek je dvojí.

První je konvence. `FindConventionKey` běží pokaždé, když chybí `[Key]` i třídní `[PrimaryKey]`, takže třída označená `[Keyless]` a nesoucí vlastnost `Id` dostane primární klíč, který zdroj **výslovně popřel**. Konvence tu přebíjí tvrzení, což je přesný opak pravidla priority zdrojů (rozhodnutí 017): konvence smí mluvit jen tam, kde zdroj mlčí, a tady zdroj promluvil.

Druhý je katalog a leží o úroveň níž. Fáze doplnění (rozhodnutí 015, `CompletePrimaryKey`) dodává primární klíč všude, kde je `EntityMap.PrimaryKey` prázdný a tabulka klíč má. Jenže „nikdo klíč neuvedl" a „zdroj klíč popřel" jsou dvě různá tvrzení a model je dnes nerozliší — obě vypadají jako `PrimaryKey == null`. Oprava provedená jen v parseru by tedy zavřela konvenci a nechala katalog vymýšlet klíč přes totéž popření; je to stejná chyba, jen ze třetího stupně priority místo z konvence cíle.

Položka k tomu nese otevřenou otázku: co znamená `[Keyless]` vedle `[Key]` na téže třídě. Vstup si protiřečí a rozhodnutí 010 zná pro takový stav dva tvary — `Conflict` (vstup ke spravení, překlad pokračuje s jednou hodnotou) a `Failure` (artefakt nemá vzniknout).

Odpověď jsme nehádali, ale ověřili proti zafixované verzi cíle (rozhodnutí 013): `KeyAttributeConvention` v EF Core (větev release/10.0) rozlišuje **dva případy a dává jim různou váhu**:

- `[Key]` na vlastnosti třídy nesoucí `[Keyless]`: EF Core vydá varování (`ConflictingKeylessAndKeyAttributesWarning`) a anotaci **ignoruje** — model se postaví a typ je bezklíčový.
- Třídní `[PrimaryKey]` vedle `[Keyless]`: EF Core vyhodí `InvalidOperationException` (`ConflictingKeylessAndPrimaryKeyAttributes`) — model se **nepostaví vůbec**.

## Zvažované varianty

1. **Číst `[Keyless]` jen v parseru a fakt nemodelovat.** Nejmenší zásah: potlačit odvození konvenčního klíče, když třída anotaci nese. Zamítáme, protože nechává katalogovou díru popsanou výše — s připojenou databází by klíč dodala fáze doplnění a artefakt by dál tvrdil klíč, který zdroj popřel. Priorita zdrojů (pravidlo E9) vyžaduje, aby popření vidět bylo, a vidět je jen to, co model nese.

2. **Rozpor vždy `Failure`.** Jednotné a přísné: vstup, který si protiřečí, nevydá artefakt. Jenže vstup s `[Key]` vedle `[Keyless]` má v EF Core definované chování — přeložitelný, spustitelný, bezklíčový model — a odmítnout ho znamená přeložit méně, než zdroj znamená. Překlad má reprodukovat význam zdroje; význam vstupu určuje jeho vlastní framework, a ten tu význam dává.

3. **Rozpor vždy `Conflict` s pevným vítězem.** Vyžaduje pravidlo, kdo vyhraje, a žádné nemáme: priorita zdrojů (017) řadí úrovně vstupu, ne dvě anotace téhož artefaktu. Kdyby vítěze určilo pořadí zápisu v souboru, tvrdil by výstup volbu, kterou nikdo nezdůvodní; kdyby vždy vyhrála bezklíčovost, vydal by nástroj artefakt i tam, kde zdrojový framework model odmítá postavit — artefakt s významem, který zdroj nemá, tedy totéž, co rozhodnutí 053 zakazuje na dotazové straně.

4. **Nést fakt v modelu a rozpor soudit sémantikou zdroje.** Popření klíče je mapovací fakt kategorie `PrimaryKey`; rozpor s klíčem dopadne tak, jak dopadá ve zdrojovém frameworku — `Conflict` tam, kde EF Core varuje a model staví, `Failure` tam, kde ho odmítá.

## Rozhodnutí

**Volíme variantu 4.**

**Bezklíčovost je nesený fakt entity.** `EntityMap.HasNoKey` říká, že zdroj výslovně tvrdí, že entita klíč nemá — na rozdíl od prázdného `PrimaryKey`, který zaznamenává jen, že klíč nikdo neuvedl. Model zůstává permisivní (mezireprezentace nevaliduje): unese i `HasNoKey` vedle vyplněného `PrimaryKey` a soudí až brána úplnosti.

**Konvence mlčí, kde zdroj promluvil.** `EFCoreEntityParser` čte `[Keyless]` a konvenční klíč nad takovou třídou neodvozuje. Tím se zavírá i textový round-trip: `[Keyless]` typ s vlastností `Id` vyjde z převodu EF Core → EF Core zase jako `[Keyless]` typ.

**`[Key]` vedle `[Keyless]` je `Conflict` a klíčové tvrzení padá.** Parser zrcadlí chování zdrojového frameworku: EF Core anotaci ignoruje s varováním, my ji zahodíme se záznamem `Conflict` (kategorie `PrimaryKey`) a entita zůstává bezklíčová. Vlastnost sama zůstává vlastností entity, přesně jako v EF Core.

**Třídní `[PrimaryKey]` vedle `[Keyless]` je `Failure` a artefakt nevzniká.** Parser zapíše obě tvrzení a soudí brána úplnosti v `AbstractEntityBuilder`: entita nesoucí klíč i jeho popření se odmítne se záznamem `Failure` před generováním. Pravidlo je v bráně, ne v parseru, protože je frameworkově neutrální — žádný cíl nemůže vykreslit obojí — a protože brána je místo, kde rozhodnutí 010 odmítání artefaktů umístilo.

**Katalog popřený klíč nedodává.** `CompletePrimaryKey` nad entitou s `HasNoKey` klíč nedoplní; ví-li katalog o klíči tabulky, vydá záznam `Conflict` a vítězí zdroj (pravidlo E9, rozhodnutí 015). Záznam je informace, ne odsudek: bezklíčová entita nad tabulkou s primárním klíčem je v EF Core legitimní mapování — entita klíč jen nesleduje —, takže se nic neodmítá, jen se řekne, že schéma klíč zná a zdroj ho nést nechce.

**Cíl, který klíč vyžaduje, dostává pravý důvod.** NHibernate entitu bez klíče odmítá mechanicky už dnes (kategorie `PrimaryKey` ve stavu *vyžaduji*); záznam `Failure` nově rozlišuje „zdroj klíč popřel" od „nikdo ho nedodal", protože F11 žádá důvod selhání a ty dva důvody vedou k různým opravám vstupu.

**Vztah k otevřené položce o precedenci mezi artefakty frameworku.** Tahle volba ji neřeší ani nepředjímá: položka je o pořadí mezi *artefakty* (fluent API nad anotacemi, XML mapper vedle anotací), kdežto tady jde o dvě anotace *téhož* artefaktu, mezi kterými pravidlo 017 žádné pořadí nemá. Společný je jen princip, který se položce bude hodit: kde zdrojový framework svou sémantiku dokumentuje, je jeho dokumentované chování to, co překládáme.

## Důsledky

**Dotčená místa:** `EntityMap` (nový člen `HasNoKey`), `AbstractEntityBuilder` (`MarkNoKey` a rozšíření brány úplnosti), `EFCoreEntityParser` (čtení `[Keyless]`, potlačení konvence, `Conflict` za `[Key]`), `CatalogCompletion.CompletePrimaryKey`. Buildery se nemění: EF Core builder `[Keyless]` vypisuje z prázdného `PrimaryKey` jako dosud a rozporná entita se ke generování nedostane.

**Dapper se nemění.** Kategorie `PrimaryKey` je u něj *neumím vyjádřit*; popření klíče nenese nic, co by se dalo ztratit, takže bezklíčový vstup neprodukuje ani `Loss`.

**Verzování (rozhodnutí 041): MINOR uvnitř řešení, navenek nic.** Přibývá veřejný člen modelu a metoda builderu; REST kontrakt se nemění, protože mezireprezentace se odpovědí nevrací, a diagnostika nepřidává žádnou novou kategorii ani druh záznamu, takže frontendová tabulka popisků zůstává.

**Prvním dalším čtenářem bude javovská strana.** JPA `@Embeddable` bez identity ani MyBatis konvenční mapování bezklíčovost nevyslovují, takže fakt zatím plní jediný parser; člen je ale neutrální tvrzení „zdroj klíč popřel", ne otisk EF Core anotace.
