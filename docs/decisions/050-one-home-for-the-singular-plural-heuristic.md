# 050 — Jedno místo pro heuristiku jednotného a množného čísla

Datum: 2026-08-24
Stav: platí
Požadavky: F5, F11, S1, S2
Podklad: rozhodnutí [005](005-many-to-many-as-explicit-junction-entity.md) a [015](015-mapping-fact-completion-from-the-catalog.md); nález z 2026-08-23, oddíl 3.5

## Kontext

Převod mezi názvem entity a názvem tabulky se u nás obejde bez pluralizační knihovny: platí heuristika koncového „s". Žije ale v **pěti místech ve třech variantách**:

| Místo | Směr | Chování |
|---|---|---|
| `TableNameCandidates.For` (`DatabaseCatalog`) | entita → tabulka | obě varianty, nezávisle na velikosti písmen, bez pojistky na délku |
| `AbstractEntityBuilder.DeriveJunctionEntityName` | tabulka → entita | pojistka `Length > 1`, nezávisle na velikosti písmen |
| `DapperSqlQueryBuilder.BuildSource` | tabulka → entita | pojistka `Length > 1`, jen malé `s` |
| `EFCoreLinqQueryBuilder.SingularOf` | tabulka → entita | bez pojistky, jen malé `s` |
| `NHibernateHqlQueryBuilder.SingularOf` | tabulka → entita | bez pojistky, jen malé `s`, tělo znak po znaku shodné s EF Core |

**Rozdíl je pozorovatelný, ne kosmetický.** Tabulka `ADDRESS` se ve fázi syntézy junction entit singularizuje na `ADDRES`, kdežto v dotazových builderech zůstane `ADDRESS`; jméno `S` o jediném znaku vyrobí v `TableNameCandidates` prázdného kandidáta, který se pak posílá do dotazu na katalog. Táž otázka („jak se z názvu tabulky jmenuje entita?") tedy dostane v jednom běhu dvě různé odpovědi podle toho, která fáze se ptá — a obě se vydávají záznamem `Convention`, takže volající dostane dvě různá tvrzení o téže konvenci.

To je přímý spor s S2: determinismus neznamená jen „dvakrát totéž", ale i „jedno pravidlo, jedna odpověď". A je to spor s vlastním důvodem, proč se odvození vůbec hlásí: záznam `Convention` říká *„tohle jsme si doplnili my"*, což dává smysl jen tehdy, když „my" znamená jedno pravidlo.

**Proč to zůstalo rozsypané.** `TableNameCandidates` bydlí v `DatabaseCatalog` a wrappery na něm podle S1 záviset nesmějí — mezireprezentace a wrappery o databázovém katalogu nevědí. Společné místo tedy musí být jinde a stěhování se dotkne čtyř projektů, což je změna, kterou má podle způsobu práce předcházet rozhodnutí.

## Zvažované varianty

1. **Nechat pět kopií a sjednotit jen jejich text.** Nic neřeší: pátá kopie se rozejde stejně jako těch pět dnešních, protože je nic nedrží pohromadě. Zamítáme.

2. **Zavést pluralizační knihovnu (Humanizer nebo obdobnou).** Odpovědi by byly lepší (`Addresses` → `Address`, `People` → `Person`). Jenže: přibývá závislost do jádra překladu kvůli heuristice, která je vědomě hrubá; pravidla jsou jazykově specifická a náš vstup jsou identifikátory, ne angličtina; a hlavně **výsledek by přestal být předvídatelný pro uživatele**, který dnes ví, že se ubírá jedno „s". Odvození se hlásí záznamem `Convention` právě proto, že je hrubé — chytřejší heuristika ten záznam nezruší, jen ho udělá hůř kontrolovatelným. Zamítáme.

3. **Jeden typ v `Common`, obě strany převodu na něm.**

## Rozhodnutí

**Volíme variantu 3.** Vzniká `Common.Naming.EntityTableNaming` se dvěma operacemi, které jsou navzájem opačné:

- `EntityNameFor(table)` — tabulka → entita, tedy singularizace. Z kvalifikovaného názvu nejdřív vezme holý (**schéma patří tabulce, ne entitě**, a tečka do názvu třídy nepatří vůbec — dosud to dělaly dotazové buildery a syntéza junction entit ne), pak vrátí název bez koncového `s`, má-li víc než jeden znak a končí na `s` nebo `S`; jinak název beze změny.
- `TableCandidatesFor(entityName)` — entita → tabulka, tedy seznam kandidátů v pořadí, ve kterém se zkoušejí: nejdřív název, jak je, pak druhé číslo.

Obě stojí na jediném predikátu „končí na `s`, nezávisle na velikosti písmen, a má víc než jeden znak", takže **z rozdílů v tabulce výše zbude jedna odpověď**.

**Domovem je `Common`.** Je to místo pro framework-nezávislé převodníky sdílené všemi buildery a `Common` referencuje `AbstractWrappers`, takže je dosažitelné z každého wrapperu i z `DatabaseCatalog`. `TableNameCandidates` se z `DatabaseCatalog` přesouvá sem celý — nechat ho tam a jen z něj volat by znamenalo, že katalog vlastní pravidlo, které používají i ti, kdo o katalogu nevědí. **S1 tím zůstává nedotčené**: wrappery nezískávají závislost na `DatabaseCatalog`, získávají ji na `Common`, kterou už mají.

**Singularizace platí i pro velké písmeno a jednoznakový název neztrácí znak.** To jsou dvě změny chování a obě volíme vědomě: v prvním případě proto, že `ADDRESS` a `Address` jsou tentýž identifikátor psaný jinak a odpověď se od velikosti písmen odvíjet nemá; ve druhém proto, že prázdný název tabulky nebo entity není odpověď, ale vada.

**Záznam `Convention` u odvozeného názvu zůstává všude, kde je dnes.** Pravidlo se sjednocuje, ne zamlčuje — F11 žádá, aby nepodporované a dopočtené věci nebyly potichu, a odvozený název entity je právě dopočtená věc.

## Důsledky

**Dotazové buildery začnou singularizovat velké `S`.** `FROM ADDRESS` bez namapované entity vydá výsledný typ `ADDRES` místo `ADDRESS`. Ani jedno není hezké — název entity odvozený z tabulky psané verzálkami je hrubý odhad tak jako tak —, ale nově je to **jeden odhad místo dvou**, a je to týž odhad, jaký o téže tabulce udělá syntéza junction entit.

**Čtení katalogu přestane posílat prázdného kandidáta** u jednoznakového názvu entity. Dotaz s prázdným názvem tabulky se dnes nikdy netrefí, takže se tím nic nerozbije; ubývá jen zbytečné kolo.

**Pátá kopie už nemá kde vzniknout.** Příští framework — javový podle F7–F10 — dostane obě strany hotové, což je táž úvaha, jakou vede rozhodnutí [046](046-xml-mapping-written-through-an-element-writer.md) u escapování XML.

**Podle rozhodnutí [041](041-versioning-and-release.md) je to MINOR:** mění se odvozený název ve dvou okrajových případech, veřejné rozhraní API ne.
