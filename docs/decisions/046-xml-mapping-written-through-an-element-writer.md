# 046 — XML mapování píše zapisovač prvků, ne interpolace

Datum: 2026-08-24
Stav: platí
Požadavky: F11, S1, S2
Podklad: rozhodnutí [003](003-one-shot-migration.md), [004](004-unexpressible-facts-as-warnings.md) a [019](019-neutral-database-type-vocabulary.md); nález z 2026-08-23, oddíl 3.1

## Kontext

`NHibernateEntityBuilder` skládá celé XML mapování interpolací hodnot modelu přímo do řetězce se značkami. V celém řešení není jediný výskyt `SecurityElement.Escape`, `XmlConvert`, `XElement` ani `XDocument`; `AppendXml` hodnotu jen odsadí a přidá odřádkování. Takhle vzniká zhruba dvacet míst emise — `<class>`, `<id>`, `<key-property>`, `<column>`, `<property>`, `<version>`, `<generator>`, `<param>`, `<many-to-one>`, `<one-to-one>`, `<key>`, `<bag>`/`<set>`, `<one-to-many>`, `<many-to-many>` — a každé z nich zapisuje název tabulky, sloupce, schématu, jmenného prostoru nebo parametru generátoru bez jakéhokoli escapování.

**Důsledek je porušení F11.** F11 tvrdí syntaktickou správnost generovaných souborů. Tabulka, sloupec nebo parametr obsahující `&`, `<`, `>` nebo `"` vydá dokument, který není správně utvořené XML — NHibernate ho odmítne dřív, než z něj cokoli přečte, a ověření 2. stupně proti XSD (rozhodnutí [016](016-generated-artifact-verification-levels.md)) selže na parsování, ne na schématu.

**Není to jen otázka vstupu zvenčí.** Mapování s `&amp;` v názvu sloupce je legální vstup NHibernate: `XDocument.Parse` ho při čtení dekóduje na `&` a builder ho zapíše syrově zpátky. **Round-trip legálního vstupu tedy vyrobí nevalidní výstup** — a to je porušení téhož F11 na cestě, o které rozhodnutí 016 mluví jako o hlavní ověřovací smyčce. `threat-model.md` přitom pokrývá jen čtecí stranu (zákaz DTD); emisní strana v něm není zmíněná vůbec.

Escapování je navíc kategorie, kde „skoro vždycky správně" nemá cenu: buď platí na každém místě emise, nebo neplatí nikde, protože jediné zapomenuté místo vydá stejně nepoužitelný dokument jako dvacet zapomenutých.

## Zvažované varianty

1. **Escapovat na místě emise pomocnou funkcí `X(hodnota)`.** Nejmenší zásah: dvacet interpolací dostane obal. Jenže správnost pak drží *disciplína* — každé budoucí místo emise si musí vzpomenout. To je přesně ten druh pravidla, který jsme právě odstranili u názvů sestavení, kde jsme dohodu o pojmenování nahradili zámkem drženým kódem, a přesně ten druh pravidla, proti kterému rozhodnutí [009](009-target-framework-descriptor.md) argumentuje u hlášení ztrát („builder, který by si musel vzpomenout, si jednou nevzpomene"). Zamítáme.

2. **Skládat mapování přes `XDocument`/`XElement` a `AppendXml` opustit.** Strukturální a používá zapisovač platformy. Má ale dvě vážné vady proti tomuhle projektu. Za prvé, **o výsledný text by přestal rozhodovat builder a začal rozhodovat serializátor**: prázdný `<class>` by se sám uzavřel, prolog by ztratil mezeru před `?>`, bílé znaky uvnitř atributů by se přepsaly na číselné entity. Vygenerovaný text je přitom u nás kontrakt — S2 žádá byte-wise shodné artefakty a doslovné znění mapování hlídá přes dvacet testovacích souborů. Za druhé, `EntityArtifact.Mapping` je `StringBuilder` v `AbstractWrappers`, tedy ve sdílené ploše, o které S1 tvrdí, že se do ní nový framework vejde beze změny; udělat z ní XML strom by tu plochu svázal s formátem, který jeden ze tří dnešních cílů nepoužívá vůbec. Zamítáme.

3. **Zapisovač prvků: emise bere název prvku a dvojice název–hodnota, ne hotový řetězec se značkami.**

## Rozhodnutí

**Volíme variantu 3.** Vzniká `Common.Xml.XmlEmitter` se čtyřmi operacemi — otevírací prvek, prázdný prvek, prvek s textovým obsahem, uzavírací prvek — plus prolog; hodnoty do něj vstupují jako `XmlAttribute(Name, Value)`, ne jako kus markupu. Escapování je uvnitř, na jediném místě, a **žádná cesta, kterou by šlo zapsat neescapovanou hodnotu, nezbývá**: `AppendXml`, který takovou cestu představoval, mizí (rozhodnutí [003](003-one-shot-migration.md) — jednorázový přepis, ne přechodné období).

Správnost tím přestává být disciplinární a stává se strukturální: **místo emise už neumí vyrobit nesprávně utvořený dokument, protože markup nesestavuje ono.** Zároveň zůstává vygenerovaný text náš — zapisovač vydává přesně tvar, který builder vydával dosud, takže S2 ani existující testy se nehnou.

**Domovem je `Common`, ne `NHibernateWrappers`.** `Common` je podle struktury řešení místo pro framework-nezávislé převodníky sdílené všemi buildery a zapisovač XML je přesně to. NHibernate je dnes jediný cíl, který XML vydává, ale JPA `orm.xml` (F7, F9) i mapper MyBatisu (F8) jsou XML také — s domovem v `Common` je dostanou hotové a S1 platí i pro tuhle záruku: nový framework nepřidává escapování, dědí ho.

**Escapujeme minimální množinu, deterministicky.** V hodnotě atributu `&`, `<`, `>`, `"` a bílé znaky, které by parser jinak zahladil (`\r`, `\n`, `\t`), jako číselné entity; v textovém obsahu `&`, `<`, `>`. Apostrof se neescapuje — atributy uvozujeme vždy dvojitou uvozovkou —, protože každý znak navíc je rozdíl v bytech oproti dnešnímu výstupu bez jakéhokoli přínosu pro platnost.

**Ověřuje to test s ostrým znakem.** Entita se sloupcem, jehož název obsahuje `&` a `"`, projde 2. stupněm ověření podle rozhodnutí [016](016-generated-artifact-verification-levels.md), tedy validací proti XSD — ta na neescapované verzi selže už při parsování, takže test skutečně hlídá to, co má.

## Důsledky

**Pro dnešní vstupy se nemění ani byte.** Escapování se projeví jedině tam, kde model nese znak, který by dokument rozbil; jinde je zapisovač shodný s dosavadní interpolací. Testy doslovného znění mapování proto platí dál a S2 je tím spíš splněné, že tvar výstupu už neurčuje ruka.

**Round-trip NHibernate → NHibernate se uzavírá i na ostrých názvech.** Co `XDocument.Parse` na vstupu dekódoval, zapisovač na výstupu zakóduje zpátky — dosud se v tom místě ztrácela platnost dokumentu.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to PATCH:** opravuje se chybný výstup, žádné rozhraní ani tvar odpovědi se nemění.

**Co tím pokryté není:** C# strana generovaných artefaktů. Řetězcový literál v inicializátoru vlastnosti prochází stejnou interpolací a uvozovka v něm by rozbila kompilaci; dnes se ale do inicializátoru dostane jen to, co Roslyn přečetl jako C# výraz, takže zdroj vady tam není. Až vznikne cesta, kterou se do C# artefaktu dostane hodnota z databázového katalogu, patří sem táž úvaha.
