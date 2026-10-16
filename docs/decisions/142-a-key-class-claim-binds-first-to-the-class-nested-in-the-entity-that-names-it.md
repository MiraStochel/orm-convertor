# 142 — Nárok klíčové třídy se váže nejdřív na třídu vnořenou v entitě, která ho vyslovila, a teprve potom prostým jménem

Datum: 2026-10-16
Stav: platí
Požadavky: F1, F2, F11, S2
Podklad: rozhodnutí [001](001-entity-reference-by-name.md), [031](031-key-class-as-declaration-of-key-parts.md) a [094](094-entity-identity-inside-a-conversion.md); nález z výňatku Keycloaku ([`analysis/real-world-sources.md`](../analysis/real-world-sources.md), *Nálezy*, drobnost u V3); Java Language Specification §6.3 (rozsah platnosti jména vnořeného typu)

## Kontext

Rozhodnutí [031](031-key-class-as-declaration-of-key-parts.md) čte jméno v `SourceKeyClass` jako odkaz na třídu téhož převodu a fáze rozpuštění ji podle toho najde a rozpustí do klíče. Hledá ji tak, jak mezireprezentace hledá všechno ostatní — prostým jménem, první přečtenou mapu (rozhodnutí [001](001-entity-reference-by-name.md)). Rozhodnutí [094](094-entity-identity-inside-a-conversion.md) pak dalo obalující třídě místo v totožnosti entity, takže `Customer.Key` a `Order.Key` jsou dvě mapy, ne jedna — ale nárok obou entit je dál dohledával prostým jménem `Key`, obě skončily u první přečtené a druhá klíčová třída zůstala v seznamu jako entita. Rozhodnutí 094 to vědělo a místo opravy zvolilo záznam: dvě entity téhož prostého jména jsou `Conflict`, „právě dvě klíčové třídy téhož jména jsou ten pár, o kterém nástroj mlčel nejtíž", a dodalo, že rozhodnout tu nejednoznačnost umí až kvalifikované jméno v modelu, tedy jeho zamítnutá varianta 4.

Výňatek Keycloaku ukázal, jak to vypadá nad reálným kódem: jedenáct entit, každá s vnořenou třídou `Key` a `@IdClass(Entita.Key.class)`, dá jedenáct map jménem `Key`, jeden záznam `Conflict` s jedenácti místy deklarace a deset map, které rozpuštění nevzalo. Výstup je správný jen proto, že u formy `Mirrored` jsou části klíče na entitě a klíčová třída jim dodává jen to, co už mají; u formy `Embedded` (`@EmbeddedId` s vnořenou třídou `Key` ve dvou entitách) dostane druhá entita klíčové části té první — což je přesně vada, kvůli které 094 vzniklo, jen přesunutá o krok dál a ohlášená.

Přitom zdroj nic nejednoznačného neříká. `@IdClass(UserRoleMappingEntity.Key.class)` jmenuje třídu kvalifikovaně, a i holé `Key` uvnitř třídy `UserRoleMappingEntity` je podle pravidel jazyka (JLS §6.3) její vnořená třída, ne stejnojmenná třída jinde. Totéž platí pro `<composite-id class="Order+Key">` v C#. Nejednoznačné je jen naše dohledání.

## Zvažované varianty

1. **Nechat stav podle 094.** Záznam `Conflict` mluví pravdu o tom, co nástroj dělá, a u formy `Mirrored` je výstup správný. Jenže u formy `Embedded` je výstup špatný a záznam o tom říká jen nepřímo; a u případové studie T1 nad Keycloakem by každý převod nesl záznam o jedenácti entitách, které entitami nejsou — hlášení o vlastní neschopnosti rozlišit, co zdroj rozlišuje. Zamítáme.

2. **Kvalifikované jméno v modelu** (varianta 4 rozhodnutí 094). Rozhodlo by to i cíl vztahu a jméno typu, ale je to zásah do všech wrapperů a builderů kvůli jedinému vstupu, který z něj těží; 094 ji zamítlo a důvody trvají. Zamítáme.

3. **Nést obalující třídu v `SourceKeyClass`.** Pole modelu navíc, které by čtečky plnily a buildery musely ignorovat; a nárok holým jménem zevnitř entity by se stejně musel rozhodovat podle toho, kdo ho vyslovil, ne podle toho, co nese. Zamítáme.

4. **Dohledat nárok v builderu podle toho, kdo ho vyslovil: nejdřív třídu vnořenou v entitě, která klíčovou třídu jmenuje, teprve potom prostým jménem.**

## Rozhodnutí

Volíme variantu 4:

> **Nárok klíčové třídy se váže nejdřív na třídu téhož prostého jména vnořenou v entitě, která ho vyslovila, a teprve když tam žádná není, na první třídu toho jména v převodu (rozhodnutí 001). Klíčovou třídu, kterou entita takto našla ve vlastním rozsahu, záznam o sdíleném jménu nepočítá.**

Je to pravidlo rozsahu platnosti, které má jazyk sám, aplikované na jediný odkaz, který ho potřebuje. Obalující třída je už od rozhodnutí 094 faktem builderu — podle ní `DeclareEntity` entitu hledá —, takže dohledání se děje tamtéž a model se nemění: `SourceKeyClass` nese dál jen prosté jméno, buildery ho čtou jako dosud. Nárok `@EmbeddedId` i `@IdClass` jde touž cestou (`FindKeyClassMap`), protože oba končí v témž signálu.

**Záznam o sdíleném jménu zůstává a dál se píše před rozpuštěním**, jak žádá 094 — jen nad seznamem bez klíčových tříd, které si entity našly ve vlastním rozsahu. Dvě klíčové třídy téhož jména na nejvyšší úrovni ve dvou balících, na něž entity sahají holým jménem zvenčí, se dál vážou na první přečtenou a dál se hlásí: pro ně platí 094 beze změny, a rozhodnout je umí jen kvalifikované jméno v modelu.

## Důsledky

**Jedenáct vnořených tříd `Key` Keycloaku je jedenáct klíčových tříd, ne jedenáct entit a jeden záznam.** Každá se rozpustí do klíče své entity a převod výňatku je bez `Conflict`. Forma `Embedded` s vnořenou třídou dá každé entitě její části; test rozhodnutí 094, který očekával záznam, očekává teď dvě správné entity, a případ, pro který záznam zůstává, má test vlastní (`Combined/EntityIdentityTest`).

**Rozhodnutí 094 platí dál ve všech ostatních větách.** Mění se jediný jeho důsledek — že pár vnořených klíčových tříd téhož jména je záznam —, a věta, že nejednoznačnost rozhodne až kvalifikované jméno v modelu, platí už jen pro třídy mimo rozsah entity. `architecture.md` §4.2 to říká u fáze rozpuštění.

**Nic se nemění na pořadí fází ani na idempotenci.** Dohledání je deterministické nad množinou jednotek bez ohledu na jejich pořadí (S2): vnořená třída je buď v entitě, nebo není.
