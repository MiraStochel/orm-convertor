# 179 — EclipseLink nenese verzi nad binárním sloupcem: verzi, kterou vyrábí databáze, píše profil EclipseLinku jako sloupec jen pro čtení bez `@Version` a se ztrátou

Datum: 2026-10-24
Stav: platí
Požadavky: F9, F10, F11, T3
Podklad: rozhodnutí [004](004-unexpressible-facts-as-warnings.md), [030](030-concurrency-token-as-a-version-flag.md), [076](076-java-frameworks-through-a-shared-jpa-layer.md), [080](080-eclipselink-as-the-second-profile-over-the-jpa-layer.md), [116](116-concurrency-check-is-the-version-the-application-keeps.md) a [173](173-the-experiment-run-judges-the-tools-artifact-through-the-foreign-path-and-measures-mapping-completeness-by-reading-the-artifact-back.md); [první záznam experimentálního běhu](../runs/2026-10-24-experiment.md); Jakarta Persistence 3.2 §11.1.54 (`@Version`); položka *EclipseLink odmítne verzi nad `byte[]`, kterou dává `[Timestamp]` EF Core* v `open-items.md`

## Kontext

Model nese verzi řádku jako příznak vlastnosti (`IsVersion`, rozhodnutí 030) a rozlišuje dva druhy podle toho, kdo hodnotu vyrábí: verzi, kterou zvyšuje framework (JPA `@Version int`, NHibernate `<version>`), a verzi, kterou udržuje aplikace (`[ConcurrencyCheck]`, rozhodnutí 116). Třetí druh model nejmenuje, ale buildery ho poznávají podle typu: **verze nad binárním sloupcem** — `[Timestamp] byte[]` v EF Core, tvar sloupce `rowversion` SQL Serveru — je hodnota, kterou vyrábí **databáze** při každém zápisu. NHibernate builder ji proto píše s `generated="always"` a EF Core builder jako `[Timestamp]` (`IsStoreGeneratedVersion`); ani jeden framework ji nezvyšuje, oba ji jen čtou zpět a porovnávají při zápisu.

Sdílená vrstva JPA píše každou verzi jako `@Version` nad vlastností s jejím jazykovým typem, takže `[Timestamp] byte[] RowVersion` vyjde do obou profilů jako `@Version private byte[] RowVersion`. [Experimentální běh z 2026-10-24](../runs/2026-10-24-experiment.md) změřil, že se oba profily v tom rozcházejí: **Hibernate 7.4.5 takovou jednotku přijme** (příklady `helpdesk-application`, `marketplace-fluent`, `order-book` a `order-repository` prošly třetím stupněm), **EclipseLink 5.0.0 ji odmítne při nasazení** chybou `EclipseLink-7168`: atribut verze smí být jen `int`, `Integer`, `short`, `Short`, `long`, `Long` nebo `Timestamp`. Specifikace sama (§11.1.54) jmenuje tytéž typy a binární typ mezi nimi není; Hibernate je nad rámec specifikace vstřícný, EclipseLink ji vymáhá. Deskriptor vede `VersionColumn` jako vyjádřitelný pro oba profily a profil EclipseLinku typ verze neomezuje, takže čtyři příklady končí v tabulce T3 jako *nepřijaté* — vada, ne mez.

Je to týž tvar otázky, jakou řešilo rozhodnutí 116 pro `[ConcurrencyCheck]` nad typem, který cíl jako verzi neunese ([`subset.md`](../subset.md), řádek o `[ConcurrencyCheck]` mimo podmínky): token chrání sloupec, verzí v cíli být nemůže, a odpovědí byl obyčejný sloupec se záznamem `Loss` kategorie `VersionColumn`. Rozdíl je v tom, že tady se rozcházejí dva profily nad jednou vrstvou — a profil je přesně místo, kde se takový rozdíl vede (rozhodnutí 076, 080): jako změřený fakt implementace s testem, který ho drží.

## Zvažované varianty

1. **Nechat `@Version byte[]` a vyslovit mez.** Nic nestojí, ale artefakt, který cíl při nasazení odmítne, není překlad s mezí, je to nepoužitelný artefakt; F9 by pro verzi `[Timestamp]` platilo jen s výhradou, kterou běh ukázal na čtyřech z jednadvaceti příkladů. Zamítáme.

2. **Přepsat verzi na typ, který EclipseLink bere** (`long` či `Timestamp`). Sloupec `rowversion` je binární a databáze ho vyrábí; vlastnost jiného typu by nad ním nešla ani přečíst, a `@Version` by navíc znamenalo, že hodnotu zvyšuje framework — což nad sloupcem, který databáze při zápisu přepíše sama, selže při prvním zápisu. Byl by to jiný artefakt, ne překlad. Zamítáme.

3. **Zámek výrobce** (`@OptimisticLocking(type = SELECTED_COLUMNS)` z `org.eclipse.persistence.annotations`). Je to politika zamykání entity, ne příznak vlastnosti: parser JPA ji jako `VersionColumn` nečte, takže by se fakt v měření úplnosti mapování (rozhodnutí 173) stejně počítal za ztracený; drží hodnoty zámku v keši relace, ne v atributu, a zda po zápisu, při němž databáze sloupec přepíše, chrání další zápis, nikdo neměřil — a podle pravidla, že fakt frameworku se měří dřív, než se o něj opře rozhodnutí, na neměřeném chování stavět nelze. Zamítáme; kdyby o ni někdo stál, je to měření a nové rozhodnutí.

4. **Verzi nad binárním sloupcem píše profil EclipseLinku jako obyčejný sloupec jen pro čtení, bez `@Version`, se záznamem `Loss` kategorie `VersionColumn`; profil Hibernatu píše `@Version` dál.**

## Rozhodnutí

**Volíme variantu 4.** Profil implementace (`JpaImplementationProfile`) dostává změřený fakt `RefusesBinaryVersion`: EclipseLink 5.0.0 `true`, Hibernate 7.4.5 `false`. Verze je binární tam, kde ji binární poznávají ostatní buildery: rodina sloupce `Binary`, `VarBinary` nebo `Blob`, a bez rodiny jazykový typ `byte[]` — totéž pravidlo jako `IsStoreGeneratedVersion` v EF Core a volba `generated="always"` v NHibernatu.

Pod profilem, který binární verzi odmítá, sdílený builder (`AbstractJpaEntityBuilder.BuildProperties`):

- **nepíše `@Version`** — anotace, kterou nasazení odmítne, v artefaktu nemá co dělat;
- **píše sloupec jen pro čtení**: `@Column(name = …, insertable = false, updatable = false)`, protože hodnotu vyrábí databáze a `INSERT` či `UPDATE` s ní by SQL Server odmítl (do sloupce `rowversion` se zapsat nedá); je to týž idiom, kterým builder píše sloupec cizího klíče, který mapuje i vztah (rozhodnutí 012);
- **hlásí `Loss` kategorie `VersionColumn`** se jménem entity a vlastnosti: EclipseLink nese verzi jen nad celočíselným a časovým typem (`EclipseLink-7168`, změřeno), binární verze, kterou vyrábí databáze, nemá v tomto cíli nosič, a optimistické zamykání nad ní se nepřenáší. Záznam je ztráta, ne konvence: artefakt chrání méně než zdroj (rozhodnutí 004).

Verze, kterou udržuje aplikace (rozhodnutí 116), sem nedojde: `[ConcurrencyCheck]` nad `byte[]` je už při čtení `Loss` podle 116. Verze nad celočíselným či časovým typem se nemění v žádném profilu.

**Měření drží test vedle háčku.** Javová sada dostává `eclipselink/BinaryVersionClaimsTest` (jednotka s `@Version byte[]` je při nasazení odmítnuta s `7168`, táž s `@Version int` přijata) a `hibernate/BinaryVersionClaimsTest` (Hibernate 7.4.5 `@Version byte[]` přijme), takže upgrade, který fakt změní, selže tam; xUnit drží zápis obou profilů (`EclipseLink/EclipseLinkEntityBuilderTest`, `Hibernate/HibernateEntityBuilderTest`).

## Důsledky

- **Tabulka T3 ztrácí čtyři nepřijaté řádky:** příklady s `[Timestamp]` do EclipseLinku vyjdou přijaté, se záznamem, který říká, co se nepřeneslo. Měření úplnosti mapování (rozhodnutí 173) počítá fakt `VersionColumn` v tomto cíli za zahozený podle záznamu, ne za nepřečtený — to je rozdíl mezi mezí a vadou.
- **F9 platí pro verzi `[Timestamp]` bez výhrady**, v užším rozsahu, který zápis vyslovuje: EclipseLink nese verzi jen nad typy, které bere; [`subset.md`](../subset.md) to vede jako vyslovenou mez v řádku o verzi a v tabulce faktů.
- **Hibernate se nemění.** Že 7.4.5 `@Version byte[]` přijme, je změřeno; zda nad sloupcem `rowversion` i správně zapisuje, čtvrtý stupeň nad příklady neměří (příklady končí třetím) — je to táž hranice jako u ostatních faktů profilu, které měří jen nasazení.
- **Dotčená místa:** `JpaImplementationProfile`, `EclipseLinkDescriptor.Profile`, `HibernateDescriptor.Profile`, `AbstractJpaEntityBuilder.BuildProperties`; `architecture.md` (tabulka profilů, §5), `subset.md` (řádky o verzi), `traceability.md` (F9).
