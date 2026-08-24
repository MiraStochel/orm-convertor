# 052 — Doslovný SQL typ dojde i do anotace EF Core

Datum: 2026-08-24
Stav: platí
Požadavky: F2, F5, F11, S2
Podklad: rozhodnutí [019](019-neutral-database-type-vocabulary.md), [004](004-unexpressible-facts-as-warnings.md) a [013](013-target-framework-versions.md); nález z 2026-08-23, oddíl 3.7

## Kontext

Rozhodnutí [019](019-neutral-database-type-vocabulary.md) zavedlo `SourceSqlType` jako **únikovou cestu doslovného typu**: co se do slovníku typových rodin nevejde nebo je hrubší než tvrzení zdroje, se uloží tak, jak to zdroj napsal, jako záznam vedle definice.

Ta cesta je dnes **jednosměrná**. NHibernate builder ji vypisuje na třech místech (`sql-type` na `<column>` u `<id>`, `<property>` a `<version>`), EF Core builder ji nečte vůbec — `BuildPropertyAttributes` skládá `[Column(TypeName=…)]` výhradně z `DatabaseTypeConvertor.ToEFCore(rodina, unicode)`.

Konkrétně: `[Column(TypeName="money")]` na vstupu se přečte jako rodina `Decimal` s `Precision = 19`, `Scale = 4` a `SourceSqlType = "money"`, a zapíše se jako

```csharp
[Column(TypeName="decimal")]
[Precision(19, 4)]
```

To je **jiný typ sloupce** — `money` a `decimal(19,4)` mají v SQL Serveru různé chování při dělení a různý zápis literálů —, a hlavně **bez záznamu**: `DatabaseType` je u EF Core `Expressible`, takže mechanické `ReportLosses` mlčí a specifické zúžení nikdo nehlásí, protože se o něm neví.

Nebijektivitu jako takovou `architecture.md` §5 dokumentuje a hájí — *„vědomá výměna identity zápisu za pravdivost tvrzení"*. Jenže tady nejde o nebijektivitu; jde o **asymetrii mezi dvěma cíli téhož nástroje**, kterou nic nevysvětluje: NHibernate doslovný typ vypíše, EF Core ho zahodí, přestože oba mají čím ho vyjádřit. A F11 zakazuje tiché vynechání nepodporované konstrukce; tady je konstrukce podporovaná a přesto vynechaná.

## Zvažované varianty

1. **Nechat, jak je, a zapsat asymetrii do §5.** Poctivé vůči čtenáři, ale nechává v nástroji rozdíl, pro který není důvod. Popisovat mezeru místo jejího zavření dává smysl, když je zavření drahé nebo sporné; tady je to jeden `??`. Zamítáme.

2. **Zahození vydat záznamem `Loss` a asymetrii dopsat.** Ticho by zmizelo, ale výstup by dál nesl jiný typ sloupce než vstup, přestože cíl umí ten správný napsat. Rozhodnutí [004](004-unexpressible-facts-as-warnings.md) hlásí to, co cíl **nevyjádří**; tohle cíl vyjádří. Zamítáme.

3. **Anotace ponese doslovný typ, když ho model má.**

## Rozhodnutí

**Volíme variantu 3: `[Column(TypeName=…)]` vypisuje `SourceSqlType`, je-li v modelu, a odvozený název rodiny jinak.** Přednost doslovného zápisu je totéž pravidlo, které NHibernate builder uplatňuje u `sql-type`, takže **oba .NET cíle nově odpovídají na tentýž model stejně**.

**Facety zůstávají vedle typu.** `[MaxLength]` a `[Precision]` se vypisují dál, i když `TypeName` nese doslovný typ. Je to týž tvar, jaký má NHibernate `<column sql-type="…" precision="…" scale="…"/>`, a jsou to fakty, které model nese samostatně; vynechat je proto, že „jsou v názvu typu obsažené", by znamenalo hádat, co který doslovný název obsahuje.

**Únikovou cestu nebereme jako čtení dialektu.** `SourceSqlType` je řetězec, kterému nerozumíme — nevíme, jestli platí v cílovém databázovém systému, a nezkoumáme to. To je vědomé a shodné s tím, co dnes dělá NHibernate builder; dokud není deklarovaný cílový dialekt (otevřená položka *Cílový databázový dialekt v deskriptoru*), je jediným dialektem SQL Server a nástroj nic víc netvrdí. **Doslovný zápis přenesený beze změny je přitom bezpečnější než odvozený**: odvozený tvrdí typ, který jsme vybrali my, doslovný tvrdí typ, který vybral zdroj.

**Kde `SourceSqlType` chybí, se nic nemění.** Rodina se vypíše přes `ToEFCore` jako dosud, včetně větvení podle `IsUnicode`.

**Záznam nevzniká.** Nic se neztrácí a nic se nevymýšlí — do výstupu jde přesně to, co řekl zdroj. Ubývá naopak tichá záměna, kterou dosud nehlásil nikdo.

## Důsledky

**Round-trip EF Core → EF Core se u typů na únikové cestě uzavírá.** `[Column(TypeName="money")]` se nově vrátí jako `[Column(TypeName="money")]`; k tomu přibude `[Precision(19, 4)]`, protože parser tu přesnost z názvu typu materializoval do modelu. Přesnost navíc je tvrzení pravdivé a s `money` konzistentní, takže výstup zůstává správný, jen upovídanější než vstup — a to je přesně ta výměna, kterou §5 popisuje.

**Převod NHibernate → EF Core přestává měnit typ sloupce.** `sql-type="money"` v `hbm.xml` dojde až do anotace; dosud se cestou stal `decimal`.

**Verifikace 2. stupně to zachytí, kdyby se to rozešlo.** Generovaná entita se kompiluje a `[Column(TypeName="…")]` je řetězec, takže překladač o něm nic neřekne; test proto porovnává vydaný text — což je forma, kterou pro tuhle třídu tvrzení používají i ostatní testy anotací.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to PATCH:** opravuje se obsah generované anotace, rozhraní ani tvar odpovědi se nemění.
