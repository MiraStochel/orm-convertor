# 212 — Vlastnost, jejíž getter by byl `getClass()`, je v Javě členem s podtržítkem, a dotaz MyBatisu, který materializuje entitu převodu, ji mapuje jejím vlastním `<resultMap>`

Datum: 2026-11-09
Stav: platí; nahrazuje zápis dotazu nad entitou převodu s `resultType` rozh. 082 a větu *Dotaz bez projekce zůstává `resultType="balík.Entita"`* rozh. 104
Požadavky: F7, F8, F9, F10, F11, T3
Podklad: rozhodnutí [067](067-a-derived-convention-is-a-statement-a-default-is-not.md), [076](076-java-wrappers-in-csharp-jvm-in-containers.md), [082](082-t-sql-read-and-written-by-a-shared-project.md), [084](084-mybatis-wrapper-over-the-shared-sql-reading.md), [104](104-a-projection-into-a-sql-target-materializes-as-an-untyped-row.md), [150](150-a-part-of-a-decision-is-replaced-by-a-new-file-and-both-statuses-say-so.md), [181](181-an-alias-in-a-mybatis-select-with-a-result-type-states-the-column-of-the-property.md) a [208](208-sede-as-the-fourth-reference-domain-over-the-dba-stack-exchange-dump-judged-by-a-judge-of-our-own.md); [záznam běhu SEDE 2026-11-08](../runs/2026-11-08-sede-2.md)

## Kontext

Tabulka `Badges` katalogu SEDE má sloupec `Class`. Javové buildery píšou vlastnost jako soukromé pole se getterem a setterem podle konvence JavaBeans, tedy `getClass()` — a ten javac odmítne, protože `Object.getClass()` je `final`. [Běh SEDE z 2026-11-08](../runs/2026-11-08-sede-2.md) proto skončil u 51 dotazů nad `Badges` v Hibernatu, EclipseLinku i MyBatisu jako *nezkompilováno*. Žádný jiný člen `java.lang.Object` tvar accessoru nemá, takže kolize nastává jen pro vlastnost `Class` (i `class`). Položka práce `open-items.md` už řekla, co s tím: jméno člena změnit a sloupec pojmenovat mapováním.

Přejmenovaný člen ale v MyBatisu odhalil vadu, která tam byla i bez něj. Dotaz nad celou entitou píše `MyBatisSqlQueryBuilder` jako `<select resultType="balík.Entita">` s textem `SELECT * …` (rozh. 082, 104) a MyBatis řádek naplní automatickým mapováním: sloupec do vlastnosti **téhož jména**. Vlastnost, jejíž sloupec se jmenuje jinak — `Class_` nad `Class`, ale stejně tak `firstName` nad `first_name` ze zdroje JPA —, zůstane `null` a dotaz o tom nic neřekne. Mapper entity přitom správnou dvojici sloupec–vlastnost nese: uzavřený `<resultMap>` vypisuje každý sloupec (rozh. 084). Rozhodnutí 082 volilo `resultType`, protože „`resultMap` by ukazoval do druhého dokumentu, který převod dotazu sám nemá" — jenže dotaz materializuje entitu převodu jen tehdy, když převod tu entitu má, a s ní i její mapper.

Změřeno nad MyBatis 3.5.19 (javová sada, `mybatis/ResultMappingClaimsTest`):

| Tvar | Výsledek |
|---|---|
| `resultType` nad třídou, jejíž vlastnost se jmenuje jinak než sloupec | dotaz proběhne, vlastnost **`null`** (a nemapuje-li v řádku nic jiného, je `null` celý řádek) |
| `<select resultMap="ns.EntityMapper.Row">` nad `<resultMap>` jiného dokumentu, ten přečtený dřív | vlastnost naplněná podle `<result column>` |
| totéž, dokument dotazu přečtený dřív | naplněná stejně — MyBatis odkaz dořeší po přečtení všech dokumentů |
| dokument entity chybí | příkaz selže: „Result Maps collection does not contain value for …" |

## Zvažované varianty

1. **Jméno člena nechat a vynechat jen getter**, nebo ho pojmenovat jinak než pole. JPA (přístup přes pole) by getter nepotřeboval, ale JPQL by vlastnost jmenoval `b.Class` a `class` je vyhrazený identifikátor JPQL (`JpqlNames`) i slovo HQL (`.class` je diskriminátor typu); MyBatis by v `<result property>` jmenoval vlastnost, kterou vidí jen přes pole. Dvě pravidla pro tři cíle a v JPQL tvar, který nikdo neměřil. Zamítáme.
2. **Přejmenovat člena a dotazy MyBatisu nechat s `resultType`.** Třída by se zkompilovala a dotaz nad `Badges` by vracel `Class` jako `null` — tichá ztráta místo hlasité, horší výsledek než dnešní. Zamítáme.
3. **Přejmenovat člena a v MyBatisu přepsat `SELECT *` na výčet sloupců s aliasy podle členů.** Text dotazu by se lišil od textu, který píše Dapper a který vydáváme vedle, a stejnou vadu by řešil jinak než mapper, který dvojice už nese. Zamítáme.
4. **Přejmenovat člena na `Class_` ve všech třech cílech a dotaz MyBatisu, který materializuje entitu převodu, mapovat `<resultMap>` z mapperu té entity.**

## Rozhodnutí

**Volíme variantu 4.**

- **Jméno člena.** `JavaMemberNames.Of` (projekt `JavaEntityParsing`) vrací jméno vlastnosti, a kde by getter byl `getClass()`, totéž s podtržítkem na konci — `Class_`, `getClass_()`, `setClass_()`; je to přepis, který vrstva JPA už používá pro alias, který je vyhrazeným identifikátorem (`JpqlNames`). Ptají se ho všechny tři javové buildery entit (pole, accessory, klíčová třída `@IdClass`, `mappedBy`) i zápis cest JPQL (`JpqlQueryVisitor.Property`, porovnání reference), takže entita i dotaz jmenují týž člen. Sloupec se nemění: JPA píše `@Column(name)` a MyBatis `<result column>` vždy (rozh. 067, 076), takže mapování ho pojmenuje bez dalšího zásahu. Přejmenování hlásí builder entity záznamem `Convention` u vlastnosti (jméno člena není mapovací fakt, ztráta to tedy není, ale jméno, které zdroj nevyslovil, ano — rozh. 067).
- **Dotaz MyBatisu.** Dotaz bez projekce, jehož výsledný typ je entita převodu, píše `MyBatisSqlQueryBuilder` jako `<select resultMap="<balík>.<Entita>Mapper.<Entita>">` — uzavřený `<resultMap>`, který mapper entity vždy nese pod jménem entity (`MyBatisEntityBuilder.MapperNamespace`). Typ odvozený ze jména tabulky, kterou žádná entita převodu nemapuje (záznam `Convention`, rozh. 082), mapper v převodu nemá, a zůstává proto u `resultType`. Projekce zůstává `resultType="map"` (rozh. 104). Deklarace metody se nemění (`List<Entita>`).

Tím se nahrazuje část dvou rozhodnutí (rozh. 150): v rozh. 082 zápis dotazu nad entitou převodu jako `<select>` s `resultType`, v rozh. 104 věta, že dotaz bez projekce zůstává `resultType="balík.Entita"`. Ostatní z obou platí — odvození typu z tabulky, záznam `Convention`, netypovaný řádek projekce.

Fakta drží `mybatis/ResultMappingClaimsTest` (ticho `resultType` nad přejmenovaným sloupcem, odkaz do jiného dokumentu v obou pořadích, selhání bez dokumentu entity); zápis drží `Combined/CatalogRunDefectTest` a `MyBatis/MyBatisBuilderTest`.

## Důsledky

- **Dotazy SEDE nad `Badges` se v Hibernatu, EclipseLinku i MyBatisu zkompilují** a sloupec `Class` přijde do `Class_`.
- **Dotaz MyBatisu nad entitou s přejmenovaným sloupcem vrací hodnotu**, kde dosud vracel `null` — týká se každého zdroje, který sloupec pojmenuje jinak než vlastnost (JPA `@Column(name)`, EF Core `[Column]`, `column` NHibernatu).
- **Dotaz MyBatisu nad entitou převodu potřebuje mapper entity** — jako každý artefakt převodu, který jmenuje třídu entity. Neuzavřený `<resultMap>` nikde nevzniká, takže vlastnost, kterou mapper vynechal (transientní, navigace), zůstává prázdná i tehdy, když dotaz sloupec téhož jména vrací; to je význam uzavřeného mapování (rozh. 084).
- **Překlad z Javy zpět** nese člen `Class_` jako vlastnost `Class_` se sloupcem `Class`; přepis se zpět neodvozuje, protože `Class_` může být i jméno, které zdroj zvolil sám.
- **Parser MyBatisu** čte `resultMap` dotazu jako dosud — jako ztrátu bez místa v mezireprezentaci — a aliasy výběru s `resultMap` za mapovací fakt nebere (rozh. 181).
- **Dotčená místa:** `JavaMemberNames`, `AbstractJpaEntityBuilder`, `JpqlQueryVisitor`, `MyBatisEntityBuilder`, `MyBatisSqlQueryBuilder`; `architecture.md` (§5 zápis JPA a MyBatisu), `subset.md`, `traceability.md` (F7, F8, F9).
