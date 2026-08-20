# 031 — Klíčová třída je deklarací částí klíče, ne entitou převodu

Datum: 2026-08-20
Stav: platí
Požadavky: F1, F2, F5, F7–F10, F11, F14, S1, S2
Podklad: rozhodnutí [006](006-flat-composite-key-rendering.md), [014](014-language-type-model.md), [015](015-mapping-fact-completion-from-the-catalog.md) a [017](017-source-precedence-for-mapping-facts.md); audit 2026-08-02, kap. 3.2 a 4.1; JSS §6.1, pravidla E1, E2, E5 a E9

## Kontext

Rozhodnutí [006](006-flat-composite-key-rendering.md) zvolilo ploché vykreslení kompozitního klíče ve všech cílech a klíčovou třídu zdroje ponechalo jako nepovinný signál `SourceKeyClass` vedle klíče. Mapovací stranu podle toho NHibernate XML parser čte: `<composite-id class=>` uloží jako formu `Mirrored`, `<composite-id name= class=>` jako `Embedded`. Tam ale čtení skončilo.

U formy `Mirrored` to nevadí, protože klíčové vlastnosti zůstávají na entitě a třída je jen zrcadlí. U formy `Embedded` mapování jmenuje části klíče, které entitní třída nedeklaruje — jsou vlastnostmi klíčové třídy — a entita místo nich nese jedinou vlastnost jejího typu:

```xml
<class name="OrderLine" table="OrderLines">
  <composite-id name="Id" class="OrderLineId">
    <key-property name="OrderID" column="OrderID"/>
    <key-property name="LineNo"  column="LineNo"/>
  </composite-id>
  <property name="Quantity" column="Quantity"/>
</class>
```

```csharp
public class OrderLine
{
    public virtual OrderLineId Id { get; set; }
    public virtual int Quantity { get; set; }
}

[Serializable]
public class OrderLineId
{
    public virtual int OrderID { get; set; }
    public virtual int LineNo { get; set; }
    // Equals, GetHashCode
}
```

Co s takovým vstupem nástroj udělá dnes, je trojí a pokaždé špatně. `AddPrimaryKey` si pro `OrderID` a `LineNo` založí vlastnosti s `Type = null` — vlastnost, kterou zná jen mapování, ne třída —, takže je brána úplnosti odmítne záznamem `Failure` a artefakty entity nevzniknou. Vlastnost `Id` projde parsováním jako `Unknown("OrderLineId")` (rozhodnutí [014](014-language-type-model.md)) a zůstane v modelu jako běžná vlastnost, ačkoli ploché vykreslení klíčovou třídu ruší a držet ji není v čem. A třída `OrderLineId`, pokud do převodu vstoupí, se stane druhou entitou: entitní parser volá `BeginEntity` pro každou deklaraci třídy, kterou ve zdroji najde.

Formálně je to porušení pravidla **E5**. To žádá `PK(E) ⊂ Props(E)`, tedy že části klíče jsou vlastnostmi entity — a přesně to forma `Embedded` na straně zdroje nesplňuje. (Kardinalitu `|PK(E)| = 1` tento fork rozšiřuje, v tom je celý smysl F1; inkluze platí beze změny.) Přečíst formu `Embedded` proto neznamená doplnit parser o další atribut, nýbrž **obnovit E5**: dostat části klíče tam, kam podle mezireprezentace patří. Rozhodnutí 006 řeklo, jakou podobu má klíč na výstupu; neřeklo, jak se do té podoby dostane vstup, který ji nemá.

Druhá formální vazba je **E1**: „each translation unit defines exactly one application-level entity mapped to exactly one database table". Klíčová třída žádnou tabulku nemá a mít nemůže. Není to tedy entita s chybějícím faktem, kterou by dorovnal katalog — je to část překladové jednotky entity, která ji jmenuje.

Otevřená položka vedla tenhle stav jako trojici otázek: odkud vzít jazykové typy částí, jak zabránit tomu, aby se držící vlastnost dostala do mezireprezentace jako běžná vlastnost, a co udělat se zdrojem klíčové třídy, pokud do převodu vstoupí. Rozhodnutí [030](030-scope-of-version-1-0.md) je zařadilo dovnitř verze 1.0 s odůvodněním, že jde o jádro F1 a F2 a že věta „hlásíme, že neumíme přečíst klíčovou třídu" je v práci o kompozitních klíčích špatná věta. Otázky vypadají jako tři, ale mají jednu příčinu — mezireprezentace nemá pojem pro třídu, která není entitou —, a proto se dají zodpovědět jednou volbou.

## Zvažované varianty

1. **Klíčová třída jako struktura mezireprezentace (komponenta).** Nést klíč jako vnořený objekt a vykreslovat ho podle formy, kterou uvedl zdroj. Znamenalo by to změnit volbu rozhodnutí 006, tedy je nahradit, ne na ně navázat — a oba jeho důvody platí dál: `@EmbeddedId` neumí vyjádřit strategii generování per-part a volba formy mění cesty k vlastnostem v generovaných dotazech. Komponenty jsou navíc vyňaté ze záruk verze 1.0 (rozhodnutí 030). Hlavní námitka je ale věcná: **ani s komponentou v modelu by se otázka nezodpověděla.** Všechny cíle vykreslují klíč ploše, takže části klíče musí na entitě stát tak jako tak; komponenta by přidala strukturu, ne odpověď. Zamítáme.

2. **Nechat klíčovou třídu entitou a odstranit jen držící vlastnost.** Nejlevnější varianta — jedna podmínka nad `SourceKeyClass`. Zůstane po ní ale v `EntityMaps` třída bez tabulky a bez klíče. Proti NHibernate ji deskriptor odmítne, protože primární klíč uvádí jako *vyžaduji*, a záznam `Failure` bude mluvit o entitě, která nikdy entitou nebyla; proti EF Core projde jako bezklíčový typ, tedy jako artefakt, který zdroj neměl. Obojí je špatně z téhož důvodu, který vyslovuje E1. A varianta odpovídá jen na druhou ze tří otázek: části klíče zůstanou bez jazykového typu, takže F1 („IR musí zachovat … jejich datové typy") zůstane nesplněný a entita se stejně nevygeneruje. Zamítáme.

3. **Poznat klíčovou třídu konvencí v entitním parseru.** Podle tvaru: název končící na `Id`, žádná navigační vlastnost, přepsané `Equals` a `GetHashCode`, atribut `[Serializable]`. Láká to tím, že funguje nad jediným souborem a nepotřebuje žádnou novou fázi. Jenže **hádá tam, kde zdroj tvrdí**: mapování klíčovou třídu jmenuje, takže je to fakt prvního stupně priority (rozhodnutí [017](017-source-precedence-for-mapping-facts.md)) a odvozovat ho je proti pořadí, které tamtéž platí. Špatný odhad navíc z převodu potichu odstraní skutečnou entitu — přesně ta třída chyby, kterou F11 zakazuje. A neškáluje: `@IdClass` i `@EmbeddedId` klíčovou třídu rovněž jmenují, takže by heuristika byla druhým mechanismem vedle vysloveného faktu. Zamítáme.

4. **Jméno v `SourceKeyClass` je vazba na třídu téhož převodu a fáze mezi parsováním a generováním ji rozpustí do klíče.**

## Rozhodnutí

Volíme variantu 4:

> **Jméno, které mapování uvedlo v `SourceKeyClass`, je odkaz na třídu téhož převodu — a co ta třída deklaruje, jsou části klíče, ne vlastnosti další entity. Fáze mezi parsováním a generováním ji proto rozpustí do klíče entity, která ji jmenuje.**

Je to týž tvar, jakým mezireprezentace odkazuje na všechno ostatní: jménem, ne referencí (rozhodnutí [001](001-entity-reference-by-name.md)), a rozresolvovaným až nad úplnou množinou zdrojů převodu. Jedna volba tím odpovídá na všechny tři otázky.

**Jazykové typy částí bere fáze z klíčové třídy.** U formy `Embedded` jmenuje atribut `name` prvku `<key-property>` vlastnost klíčové třídy, ne entity, takže se hledá tam. Fáze přenese, co třída o členu deklaruje — jazykový typ včetně jazykové nullability, přístupový modifikátor, přístupové metody a inicializátor —, kdežto sloupcová strana zůstává, jak ji přečetlo mapování. Je to přesně dělba, kterou popisuje rozhodnutí 017: třída je úroveň 1a a nese jazykovou stranu, mapovací artefakt je 1b a nese databázovou. Zápis je přírůstkový jako ve fázi doplnění z katalogu — vyplní se jen prázdný fakt —, takže entita, která tutéž vlastnost deklaruje sama, o svůj údaj nepřijde.

Nedostupný typ se **nehádá**. Není-li klíčová třída mezi zdroji převodu, sáhne po typu fáze doplnění z katalogu (2. stupeň, rozhodnutí [015](015-mapping-fact-completion-from-the-catalog.md)) se záznamem `Convention`, jak to dělá u každé vlastnosti známé jen z mapování; a nedodá-li ho ani ta, odmítne entitu brána úplnosti záznamem `Failure`, který dnes existuje. Samotné jméno třídy, které v převodu nikdo nedeklaruje, hlásí fáze záznamem `Incompleteness` — bez něj by následný `Failure` říkal jen „vlastnost nemá jazykový typ" a zamlčel by proč.

**Držící vlastnost není vlastností entity.** U formy `Embedded` fáze z entity odstraní vlastnost, kterou `SourceKeyClass` jmenuje v `PropertyName`; ploché vykreslení ji nahrazuje samotnými částmi klíče, takže vypsat obojí by znamenalo vypsat týž sloupec dvakrát. U formy `Mirrored` žádná držící vlastnost není a odstraňovat se nemá co.

**Klíčová třída není entita převodu.** Fáze ji z `EntityMaps` vyjme. Odstraní ale jen třídu, která nenese vlastní mapování — žádnou tabulku, schéma, klíč ani vztah. Nese-li je, tvrdí dva zdroje prvního stupně dvě různé věci o téže třídě, a to je podle rozhodnutí 017 `Conflict`: třída zůstane entitou, převod pokračuje a záznam říká, že ji mapování jiné entity jmenovalo jako klíčovou. Tiché odstranění entity by bylo horší než nepřečtená klíčová třída. Člen klíčové třídy, který mapování jako část klíče nejmenuje, se částí klíče nestává ani vlastností entity — mapování ho nepersistuje — a fáze ho hlásí záznamem `Loss`.

**Fáze bydlí v `AbstractEntityBuilder` a běží první.** Sdílená, ne per-framework: `@IdClass` i `@EmbeddedId` skončí v témž signálu, takže javová větev přidá parser, ne fázi (S1, JSS §4.3). Pořadí není detail implementace, nýbrž součást volby:

- **Před rozresolvováním jmen entit.** Ta fáze povyšuje typ `Unknown`, jehož jméno je jménem entity převodu, na `Reference` — a `OrderLineId` tou dobou entitou převodu je. Bez správného pořadí by z držící vlastnosti vznikl vztah na třídu, která entitou není, a klíč by se rozpadl na dvě různé chyby.
- **Před fází doplnění z katalogu.** Jinak katalog hledá tabulku pro klíčovou třídu a sloupce pro vlastnosti, které entitě nepatří, a vydá o tom záznamy o něčem, co se za okamžik zruší.

Spouští se proto stejně jako materializace konvenčních navigací: **jako první krok fáze doplnění z katalogu a v `Build()` pro převody, které katalog nepotkají.** Je idempotentní — po prvním průchodu už žádný `SourceKeyClass` nejmenuje entitu převodu —, takže dvojí spuštění nevadí a na pořadí vstupních zdrojů od uživatele nezáleží (S2).

**`SourceKeyClass` zůstává zapsaný.** Fáze ho čte, nespotřebovává. Rozhodnutí 006 ho drží proto, aby JPA builder uměl ID třídu pojmenovat podle zdroje místo konvencí (F7–F10) a aby měla F11 co ohlásit; obojí platí dál. Sama změna formy je u .NET cílů ztráta a hlásí se záznamem `Loss`: výstup klíč vykreslí ploše, takže z něj mizí název třídy i cesta k části klíče (`o.Id.OrderID` se stává `o.OrderID`). Dosud to nebylo co hlásit, protože takový vstup se ke generování nikdy nedostal.

**Klíčová třída přichází jako další zdroj téhož převodu.** Nic nového to nevyžaduje: `ConversionHandler.Convert` i `ConvertRequest` berou seznam zdrojů, takže vícesouborový vstup na úrovni API existuje a F14 je dohnání rozhraní, ne předpoklad.

## Důsledky

**F1 a F2 se uzavírají i pro klíč vyjádřený třídou.** Vstup ve formě `Embedded` projde celou pipeline: části klíče dostanou pořadí, jazykové typy, názvy sloupců i strategii, entita vyjde plochá a přijme ji jak NHibernate, tak EF Core. Doteď takový vstup končil na bráně úplnosti, tedy odmítnutím — což bylo poctivé, ale u požadavku, který `@EmbeddedId` a XML mapování jmenuje výslovně, nestačí.

**Testy musí ověřit tři věci, které dosud neměly kde nastat.** Že dvojice entitní třídy a mapování ve formě `Embedded` projde 3. stupněm ověření, tedy stavbou session factory (rozhodnutí [016](016-generated-artifact-verification-levels.md)); že na pořadí zdrojů ve vstupu nezáleží — klíčová třída před entitou i po ní dá tentýž výstup (S2); a že chybějící klíčová třída skončí dvojicí záznamů, ne pádem. Negativní kontrola „mapování na neexistující vlastnost", kterou dnes 3. stupeň předvádí, tím o svůj případ nepřichází — přestává jen být tím, čím se forma `Embedded` projevovala.

**Kanonizace je jednosměrná a je fér to přiznat.** Nástroj klíčovou třídu čte, ale žádný .NET cíl ji nevypíše, takže převod NHibernate → NHibernate není u téhle formy identita: klíč se vrátí plochý. Není to vada, je to rozhodnutí 006 v chodu — ale znamená to, že textový round-trip kompozitního klíče není pevný bod a v textu práce to musí zaznít vedle stejné nesymetrie u dotazů (rozhodnutí [022](022-native-query-syntax-in-builders.md)). Formu klíčové třídy poprvé vyrobí až JPA builder, tedy mimo verzi 1.0.

**Přibývá jediné místo, které z modelu odebírá.** Dosavadní fáze mezi parsováním a generováním jen dopisují — syntéza junction entit přidává entitu, rozresolvování doplňuje páry a typy. Tahle jako první maže: vlastnost z entity a entitu ze seznamu. Proto je omezená podmínkou „jen třída bez vlastního mapování" a proto každé odebrání nese záznam. Kdo bude fázi číst později, má tím vysvětlené, proč není symetrická se svými sousedy.

**`architecture.md` se dotkne na třech místech**, až podle tohoto rozhodnutí vznikne kód: §4.2 končí u klíčové třídy větou „co z toho plyne pro entitu, je otevřená položka" a nahradí ji popis fáze, §4.3 dostane fázi do výčtu fází mezi parsováním a generováním a §5.1 nové důvody záznamů. Rozhodnutí zaznamenává volbu, ne hotovost; co zbývá udělat, říká [`open-items.md`](../open-items.md).
