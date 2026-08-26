# 066 — Záznam se připisuje vstupní jednotce, ze které vzešel

Datum: 2026-08-26
Stav: platí
Požadavky: F11, F14, S1, S2, S6, S7
Podklad: rozhodnutí [045](045-a-conversion-that-produced-nothing-says-so.md), [047](047-content-type-reaches-the-query-parser.md), [025](025-query-language-as-content-type.md) a [010](010-diagnostics-as-returned-data.md)

## Kontext

Rozhodnutí [045](045-a-conversion-that-produced-nothing-says-so.md) zavřelo ticho na úrovni běhu a výslovně nechalo otevřený případ mezi tím: **jednotka, ze které nic nevzejde, zatímco jiná jednotka téhož běhu artefakt vydá.** Volající dostane výstup a o té jedné jednotce se nedozví nic, přestože ji poslal — přesně to ticho, kterému mají rozhodnutí 010 a 045 předcházet, jen o jednotku níž.

Orchestrace ten případ dnes rozeznat neumí, protože entitní parsery vracejí `void`: všechny jednotky se nasypou do jednoho builderu a nikdo neříká, co z které vzešlo. Počítat `EntityMaps` před a po každém parseru nejde — `NHibernateXMLMappingParser` mapování obohacuje entitu, kterou už vytvořil parser třídy, takže novou mapu nepřidá a poctivá jednotka by dostala nepoctivý záznam. Tohle už jednou konstatovalo rozhodnutí 045; tady z toho vyvozujeme důsledek.

S touž mezerou souvisí zúžení, které u F14 vyslovuje [`architecture.md`](../architecture.md) §9: **diagnostika je po entitách, ne po souborech**, protože jednotka `/convert` žádné jméno nenese a server soubory nezná. Frontend přitom jméno souboru u každé jednotky drží — jen ho neposílá. A na pojmenované jednotky čeká i odložená položka o vstupu vedle výstupu (rozhraní), která na tuhle položku výslovně odkazuje.

Dotazová větev tenhle problém strukturálně nemá: každá dotazová jednotka dostává vlastní builder a vlastní průchod smyčkou, takže orchestrace u každého záznamu ví, ze které jednotky je. Jen to nikam nezapisuje — dva neúspěšné dotazy vydají dva záznamy `Failure` a volající nemá jak poznat, který patří kterému.

## Zvažované varianty

1. **Počítat `EntityMaps` kolem každého volání parseru.** Zamítnuto už v rozhodnutí 045: obohacující parser nepřidává mapu a poctivá jednotka by vyšla jako jalová. Zamítáme.

2. **Příznak aktivity na builderu.** Builder by počítal zápisová volání a orchestrace by si stav odečetla kolem každého `Parse`. Žádná změna rozhraní parserů — ale je to rekonstrukce odhadem o patro níž, tedy táž třída vady, kterou rozhodnutí [047](047-content-type-reaches-the-query-parser.md) z řešení odstranilo: informaci, kterou zná jedině parser, by orchestrace odvozovala z vedlejších účinků. Degenerovaný vstup ji zradí — `<class>` opakující jen už tvrzené fakty nezapíše nic, a jednotka, která entitu skutečně jmenovala, by dostala záznam o jalovosti. A jména entit pro atribuci po souborech tudy nikdy nepotečou. Zamítáme.

3. **Parser si záznam vydá sám.** Parser ví, že nic nepřečetl, tak ať to ohlásí. Jenže pravidlo „jednotka, ze které nic nevzešlo, je záznam" je tvrzení o běhu a o výběru parserů, a jeho domovem je orchestrace — táž úvaha, kterou rozhodnutí 025 a 045 udělala dvakrát; každý budoucí parser (celá javová strana podle F7–F10) by pravidlo musel opisovat. Zamítáme.

4. **`Parse` vrací, co z jednotky přečetl; jednotka dostává jméno; záznam dostává jednotku.**

## Rozhodnutí

**Volíme variantu 4 a je z ní jedna změna rozhraní a dvě pole kontraktu.**

**`IEntityParser.Parse` vrací `IReadOnlyCollection<EntityMap>` — mapy entit, které jednotka založila nebo obohatila.** Prázdná kolekce znamená „z jednotky nic nevzešlo" a je to výrok parseru, ne odhad orchestrace; obohacení se počítá stejně jako založení, čímž padá past, na které selhává počítání map. Návratová hodnota je povinná a nemá výchozí tvar, ze stejného důvodu, z jakého 047 odmítlo výchozí hodnotu parametru: výchozí tvar by byl odhad v zdrobnělé podobě. Zásah do `AbstractWrappers` je na místě, ne proti S1 — nejde o přidání frameworku, ale o opravu rozhraní, které nenese informaci, na níž stojí rozhodnutí 045, a po opravě je nový framework levnější: jeho parser prostě vrátí, co přečetl, a atribuci dostane zadarmo. `IQueryParser` se nemění — dotazová smyčka je po jednotkách a orchestrace u ní všechno ví i bez návratové hodnoty.

**`ConversionSource` dostává nepovinné pole `Name`** — popisek jednotky, jak ho zná klient, typicky jméno souboru. Nástroj s ním nic nedělá, jen ho vrací v záznamech; neposlané jméno nic nemění. Tím padá věta „server soubory nezná" ze zúžení F14 v §9 — frontend jména drží a začne je posílat.

**`ConversionRecord` dostává nepovinné pole `Unit`** — odkaz na jednotku: její jméno, jak přišlo, a bez jména `unit N` s pořadím v seznamu `sources` (od jedné), což je jediná souřadnice, kterou si volající umí spočítat sám. Pole nesou záznamy, jejichž původem je čtení jedné jednotky:

- **nový záznam `Failure` o jalové jednotce** — neprázdná jednotka, kterou parser přečetl a nic z ní nevzešlo. Píše ho orchestrace vždy, bez ohledu na to, co vydaly ostatní jednotky: příběh jednotky se nemění tím, že soused něco vydal. Vedle něj beze změny platí obě věty rozhodnutí 045 — záznam o běhu, ze kterého nevyšlo nic, mluví o běhu, tenhle o jednotce;
- **dosavadní záznamy orchestrace o jednotce** — jazyk, který zdroj nečte, chybějící dotazový parser, chybějící dotazový builder (rozhodnutí 025 a 045);
- **záznamy vzniklé během čtení jednotky** — entitní straně je orchestrace připíše podle toho, co přibylo v builderu přes volání `Parse` té jednotky; dotazové straně patří všechny záznamy builderu jednotky, protože builder pro ni vznikl a s ní končí.

**Záznamy fáze doplnění a generování jednotku nenesou, a je to poctivost, ne mezera:** entita je legitimně vyslovená víc jednotkami (třída + XML mapování) a záznam o sloučené entitě nebo o emisi žádné jedné jednotce nepatří. Atribuce po jednotkách tedy končí tam, kde končí čtení — což je přesně hranice, kterou umí nástroj tvrdit pravdivě.

**Prázdná jednotka se dál přeskakuje bez záznamu** (rozhodnutí 025 a 045) — a nově se prázdné jednotce nevolá ani parser, aby jalovost nevyráběla záznam tam, kde není tvrzení.

## Důsledky

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR:** do požadavku přibývá nepovinné pole `name` u jednotky, do odpovědi pole `unit` u záznamu; žádné pole nemizí a dosavadní klienti procházejí beze změny. Uvnitř řešení se mění veřejné rozhraní `IEntityParser` (dvě implementace: `CSharpEntityParser`, `NHibernateXMLMappingParser`) a `ConversionRecord` se stává `record`, aby šel záznam připsat kopií.

**Zúžení F14 v §9 se zmenšuje, nemizí.** Nově platí: jednotka smí nést jméno, záznamy vzniklé čtením jedné jednotky na ni ukazují, a po entitách zůstávají záznamy o sloučené entitě a o emisi — s odůvodněním výše. Artefakty výstupu jméno dál nenesou; jejich párování se vstupem je odložená položka rozhraní a tímhle rozhodnutím dostává polovinu předpokladů.

**Vstup, který dřív prošel mlčky, teď nese záznam:** jednotka bez jediné entity (XML s cizím kořenem, C# bez třídy) vedle úspěšných jednotek. Běh, kde nic nevyšlo, nese nově záznamy dva — o běhu a o jednotce — a říkají každý něco jiného.

**Frontend posílá jména, která už drží,** a v tabulce záznamů ukazuje sloupec jednotky; nic jiného se na obrazovce nemění.
