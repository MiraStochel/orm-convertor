# 217 — Znak, který hostitel nativního SQL čte uvnitř hranatého jména jako svůj, se píše za jeho únikovým znakem, a kde žádný nemá, dotaz se odmítá

Datum: 2026-11-14
Stav: platí; nahrazuje důsledek rozh. 211 o `?` uvnitř identifikátoru
Požadavky: F9, F10, F11, T3
Podklad: rozhodnutí [082](082-t-sql-read-and-written-by-a-shared-project.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) a [211](211-a-native-query-of-eclipselink-whose-text-holds-a-hash-names-another-parameter-mark-by-a-hint.md); [revize 2026-11-13](../audits/2026-11-13-pre-release-2-4-1-audit.md), nález 3.2

## Kontext

Rozhodnutí 211 vyřešilo `#` v nativním SQL EclipseLinku a mezi důsledky nechalo jako vyslovenou mez `?` uvnitř identifikátoru: artefakt se vydá a dotaz odmítne až implementace, když běží. Revize 2026-11-13 našla, že tu mez žádný test nedrží. Měření nad připnutými verzemi ukázalo, že mez je širší, než ji rozh. 211 vyslovilo, že se týká i Hibernatu a NHibernatu a že u jednoho ze tří hostitelů mezí být nemusí.

Sdílený zapisovač T-SQL (rozh. 082) píše každé jméno, které není regulárním identifikátorem, v hranatých závorkách (`SqlNames.Delimited`); jiný tvar uvozeného jména ani komentář nepíše. Hranaté jméno je tak jediné místo textu mimo literál, kde stojí znak, který si zapisovač nevybral — alias `[Is answered?]`, sloupec `[a:b]`. Týž text dostávají tři hostitelé, kteří v něm před odesláním hledají vlastní parametry: `createNativeQuery` obou implementací JPA a `CreateSQLQuery` NHibernatu. Co v hranatém jméně který čte:

| Znak uvnitř `[…]` | Hibernate 7.4.5 | EclipseLink 5.0.0 | NHibernate 5.7.0 |
|---|---|---|---|
| `?` | poziční parametr: „No argument for ordinal parameter", vedle `?1` „Cannot mix parameter styles" | poziční parametr: „The index 1 is out of range", s hintem rozh. 211 i bez něj | poziční parametr: „Expected positional parameter count: 1" |
| `:` | `:jméno` je pojmenovaný parametr („No argument for named parameter"); `: ` a `::` jsou text | text | pojmenovaný parametr, ať za ní stojí cokoli — i prázdné jméno („Not all named parameters have been set") |
| `\` | spolkne ho a další znak pustí jako text: `[c\d]` dojde databázi jako `cd`, bez chyby | text | text |
| `'` | otevře literál, ve kterém se parametr za ním nepozná: „No parameter labelled '?1'" | text | otevře literál, ve kterém se parametr za ním nepozná: „Parameter id does not exist as a named parameter" |
| `"` | jako `'` | text | text |
| `{`, `}` | text | text | text |
| `#` | text | značka pojmenovaného parametru (rozh. 211) | text |
| týž znak za `\` | text: `[a\?b]`, `[c\:d]`, `[e\\f]`, `[g\'h]`, `[k\"l]` dojdou jako `a?b`, `c:d`, `e\f`, `g'h`, `k"l` a poziční parametry vedle nich se navážou | únik není: `[a??b]` i `[a\?b]` selžou jako `[a?b]`; s `BIND_PARAMETERS = false` se do jména dosadí `NULL` | únik není: `[a\:b]`, `[a\?b]` i `[a??b]` se čtou stejně |

Hibernate a NHibernate hranaté závorky T-SQL neznají a čtou v nich text jako kdekoli jinde; EclipseLink čte `?` všude mimo řetězcový literál. Mimo hranaté jméno: v literálu je `?` i `:` u všech tří text; jméno v uvozovkách (`"a?b"`) čte Hibernate jako text, EclipseLink a NHibernate jako parametr; v komentáři čte EclipseLink `?` i `#` jako parametr (`#` jen bez hintu), Hibernate a NHibernate nic. Řetězcový literál jako alias (`SELECT 1 AS 'a?b'`) EclipseLink spustí, odkaz na takový sloupec (`t.[a?b]`) už ne.

Dvě z těch čtení nejsou selhání, které by na sebe upozornilo: zpětné lomítko ve jménu sloupce u Hibernatu mlčky změní jméno, pod kterým konzument sloupec čte, a apostrof u NHibernatu rozhodí čtení literálů ve zbytku textu — dvě jména s apostrofem se náhodou vyruší a dotaz proběhne, jedno ne.

Fakta drží javová sada (`NativeSqlReadingClaimsTest` v balíčcích `hibernate` a `eclipselink`, bez tabulek) a u NHibernatu `NHibernate/NHibernateNativeSqlReadingTest`, který čte parametry, jež v textu najde NHibernate sám, bez připojení.

## Zvažované varianty

1. **Nechat vyslovenou mez rozh. 211** a jen ji rozšířit o další dva hostitele. Zamítáme: vydávali bychom artefakt, o kterém víme, že ho hostitel nespustí, bez záznamu, který by to řekl — výstup, který T3 počítá mezi vydané a nespustitelné, ačkoli nástroj předem ví proč. U Hibernatu navíc tvar, který běží, existuje, a jeden z pěti znaků neselže, ale změní výsledek.
2. **Odmítnout dotaz u všech tří.** Zamítáme pro Hibernate ze stejného důvodu, z jakého rozh. 211 zamítlo odmítnutí dotazu s `#`: ztratili bychom celý dotaz, který implementace spustit umí.
3. **Psát pro Hibernate jméno v uvozovkách standardu SQL** (`"a?b"`), které čte jako text. Zamítáme: tvar závisí na nastavení `QUOTED_IDENTIFIER` připojení konzumenta, EclipseLink ani NHibernate uvozovky jako hranici nečtou, takže by šlo o druhý tvar jména jen pro jednoho hostitele, a přepis by se týkal každého uvozeného jména, ne jen těch, která znak drží.
4. **Psát alias jako řetězcový literál**, který EclipseLink i NHibernate čtou jako text. Zamítáme: platí jen pro alias výběrového seznamu, na který se dotaz už neodkáže; sloupec tabulky ani alias mezivýsledku jím zapsat nejde, a o osudu dotazu by rozhodovalo, kde jméno stojí.
5. **U NHibernatu odmítat apostrof jen tam, kde čtení opravdu rozhodí** — spočítat, zda se apostrofy jmen před parametrem vyruší. Zamítáme: builder by v sobě nesl kopii čtení NHibernatu znak po znaku a o dotazu by rozhodoval tvar, který autor dotazu nevidí; jméno s apostrofem je dost vzácné, aby jeho odmítnutí nestálo za druhé pravidlo.
6. **Hostitel říká, které znaky uvnitř hranatého jména čte a čím se před ním schovají; builder píše únik, kde je, a odmítá, kde není.**

## Rozhodnutí

**Volíme variantu 6.** Krok je jeden a sdílený, `AbstractSqlQueryBuilder.SpellBracketedNamesFor(text, čtené znaky, únik)`: projde hranatá jména textu (`SqlNames.RespellDelimited`: literály přeskočí, zdvojenou zavírací závorku čte jako znak jména) a v každém, které drží čtený znak,

- **s únikem** napíše únikový znak před každý z nich — dvojtečku před každou, ne jen před tu, za kterou stojí jméno, aby pravidlo nemělo výjimku —, takže jméno dojde databázi tak, jak ho zdroj vyslovil;
- **bez úniku** vydá `Failure` (`Projection`), který jmenuje jméno, hostitele i znak, a artefakt nevznikne: „The native SQL names [a?b], and EclipseLink reads the ? inside a bracketed name as its own - a parameter of the query or the start of a literal - and has no spelling that keeps it text; no artifact was generated."

Co hostitel čte, je jeho fakt, a stojí tam, kde stojí jeho ostatní fakta:

| Hostitel | Čtené znaky | Únik | Kde |
|---|---|---|---|
| Hibernate | `?`, `:`, `\`, `'`, `"` | `\` | `JpaImplementationProfile.NativeSqlReadsInIdentifiers`, `NativeSqlIdentifierEscape` |
| EclipseLink | `?` | není | tamtéž; `#` řeší hint rozh. 211 |
| NHibernate | `?`, `:`, `'` | není | `NHibernateNativeSqlQueryBuilder` |

Je to tentýž tvar jako přepínače rozh. 113 a 211: fakt hostitele, jeden krok builderu, žádná větev podle jména frameworku. Odmítnutí je záznam v místě, kde nástroj ví proč, místo chyby ovladače u konzumenta, která příčinu nejmenuje; dotaz se počítá jako nepřeložený se záznamem, ne jako vydaný výstup, který neběží (T3). Holé SQL vedle metody (rozh. 025) se nemění. Zpětné lomítko v textovém bloku Javy zdvojuje `JpaQueryMethod.TextBlock`, takže metoda nese `[a\\?b]` a Hibernate čte `[a\?b]`.

Zápis a odmítnutí drží `Combined/CatalogRunDefectTest` (JPA) a `NHibernate/NHibernateNativeSqlReadingTest`, hodnoty profilu `Combined/TargetFrameworkDescriptorTest`.

## Důsledky

- **Hibernate spustí nativní dotaz se jménem, které drží `?`, `:`, `\`, `'` nebo `"`,** a jméno sloupce se zpětným lomítkem se už mlčky nemění.
- **EclipseLink odmítá dotaz s `?` ve jménu, NHibernate s `?`, `:` nebo `'`,** záznamem; mez rozh. 211 „artefakt se vydá, odmítne implementace" tím zaniká. Ostatní znaky oba píší, jak stojí.
- **NHibernate tím odmítne i dotaz, který by proběhl** — jméno s apostrofem, za kterým nestojí parametr ani literál (varianta 5).
- **`SqlPlaceholders.Respell` čte hranaté jméno jako jméno:** apostrof v něm neotvírá literál a `@` v něm není zástupný symbol. Dosud apostrof ve jménu vypnul přepis parametrů za ním ve všech čtyřech obalech nativního SQL; je to oprava, ne volba.
- **Komentář a jméno v uvozovkách zůstávají změřeným faktem bez kódu:** zapisovač je nepíše.
- **MyBatis a EF Core krok nevolají:** MyBatis čte v textu jen `#{…}` a `${…}`, EF Core jen díry interpolace, které jeho builder hlídá sám; nic z toho jsme uvnitř hranatého jména neměřili.
- **Dotčená místa:** `AbstractSqlQueryBuilder`, `SqlNames`, `SqlPlaceholders`, `JpaImplementationProfile`, `HibernateDescriptor.Profile`, `EclipseLinkDescriptor.Profile`, `JpaNativeSqlQueryBuilder.Emit`, `NHibernateNativeSqlQueryBuilder.Emit`; `architecture.md` (§5), `subset.md`, `traceability.md` (F7, F9).
