# 123 — Nativní dotaz EclipseLinku rozvine kolekční parametr za běhu v generované metodě: jeden zástupný symbol na prvek, číslovaný za skaláry a vázaný po prvcích, jako to uvnitř dělá Dapper

Datum: 2026-10-10
Stav: platí
Požadavky: F9, F11, F13, T2, S2
Podklad: rozhodnutí [106](106-a-bare-parameter-after-in-is-dappers-collection-parameter.md), [113](113-native-sql-as-the-escape-path-and-the-vocabulary-ldbc-needs.md) (odstavec o kolekčním parametru v nativním SQL, „u EclipseLinku je to třeba ověřit"), [083](083-parameter-as-the-fifth-operand-shape.md); profil `JpaImplementationProfile.NativeQueryExpandsCollection`; měření javovou sadou 2026-10-03 (EclipseLink 5.0.0 předá `List` vázaný na `?1` ovladači jako jednu hodnotu); dokumentace Dapperu k rozvinutí `IN @ids`

## Kontext

Úniková cesta (113) píše dotaz, který jazyk cíle nevysloví, celý nativním SQL přes API frameworku. Nativní dotaz EclipseLinku 5.0.0 váže parametry pozičně (`?1`) a kolekci vázanou na jednu pozici předá ovladači jako jednu hodnotu, místo aby ji rozvinul do seznamu, nad kterým `IN` stojí — změřeno javovou sadou; Hibernate 7.4.5 ji rozvine. Od 113 proto nativní dotaz EclipseLinku s kolekčním parametrem **odmítá** (`Failure`, `QueryParameter`): to je jediné odmítnutí v katalogu LDBC (BI 12 — odvozená tabulka, tedy nativní SQL, a k tomu seznam jazyků v `IN`) a platí pro každý vstup, který obojí spojí.

Dapper má tentýž problém a řeší ho uvnitř knihovny: `IN @ids` rozepíše před odesláním na `IN (@ids1, @ids2, …)` a každý prvek naváže zvlášť (106). SQL se tím nemění, jen počet zástupných symbolů; typ parametru zůstává typem prvku.

## Zvažované varianty

### 1 — Odmítat dál

Jediné odmítnutí katalogu a mez, kterou `subset.md` 2.11 vede. Zamítáme: odmítnutí stojí na vlastnosti jedné implementace, kterou generovaný kód umí obejít, a bere překlad vstupům, které EclipseLink za běhu bez potíží spustí.

### 2 — `STRING_SPLIT` nebo `OPENJSON` nad jedním spojeným řetězcem

Kolekce by se předala jako jeden řetězec a `IN` by stálo nad `(SELECT value FROM STRING_SPLIT(?1, ','))`. Zamítáme: mění SQL cíle (text únikové cesty by přestal být textem, který píše Dapper — `AFallbackEmitsTheStatementTheDapperTargetWrites`), mění typ parametru (seznam čísel jako text), oddělovač může být v hodnotě a `STRING_SPLIT` vrací `nvarchar`, takže porovnání s číselným sloupcem by šlo přes implicitní převod s jiným plánem.

### 3 — Rozvinutí zástupných symbolů za běhu v generované metodě

## Rozhodnutí

**Volíme variantu 3. Generovaná metoda EclipseLinku rozvine kolekční parametr sama, než dotaz vytvoří: skaláry dostanou pozice `?1`…`?s` v pořadí signatury, každá kolekce pak za nimi tolik pozic, kolik má prvků, v pořadí signatury; text nese za každou kolekci jeden zástupný symbol se jménem parametru, `IN (?ids)`, který metoda nahradí výčtem `?s+1, ?s+2, …` složeným podle délky kolekce, a po vytvoření dotazu naváže každý prvek na jeho pozici. Kolekce bez prvku dá `IN ()`, které SQL Server odmítne, stejně jako Dapper. Záznam zůstává jediný, `Fallback` únikové cesty; rozvinutí je způsob, jakým nativní dotaz EclipseLinku kolekci vyslovuje, jako je `(?1)` způsob Hibernatu a `(:ids)` se `SetParameterList` způsob NHibernatu.**

```java
public static Query query(EntityManager em, long lengthThreshold, Collection<String> languages) {
    var position = 1;
    var languagesPlaceholders = new StringBuilder();
    for (var i = 0; i < languages.size(); i++) {
        languagesPlaceholders.append(i == 0 ? "?" : ", ?").append(++position);
    }
    var query = em.createNativeQuery("""
            …
            WHERE m.Length < ?1 AND m.RootPostLanguage IN (?languages)
            …
            """.replace("(?languages)", "(" + languagesPlaceholders + ")"))
        .setParameter(1, lengthThreshold);
    position = 1;
    for (var element : languages) {
        query.setParameter(++position, element);
    }
    return query;
}
```

### Proč za skaláry, a ne v pořadí signatury

Pozice skaláru musí být v textu pevná, aby text byl čitelný a aby `setParameter` skaláru nezávisel na délce kolekce před ním; kolekce za skaláry dostávají pozice, které se počítají až za běhu, a běžící čítač `position` je jediné místo, kde se délka promítá — jednou při skládání výčtu, podruhé při vázání, oběma smyčkami v témže pořadí. Dvě kolekce jdou za sebou týmž čítačem.

### Proč `replace` nad textovým blokem

Text únikové cesty je text, který píše cíl Dapper, s `@ids` přepsaným na zástupný symbol API (`SqlPlaceholders.Respell`); u kolekce je symbolem `(?ids)`, jméno parametru za otazníkem, protože číslo pozice vzniká až za běhu. `replace` ho vymění za výčet před voláním `createNativeQuery`, takže EclipseLink symbol se jménem nikdy nevidí. Přepis textového bloku na konkatenaci po kouscích by rozbil odsazení bloku a čitelnost; výskyt téhož řetězce `(?ids)` v literálu SQL je vyloučený tím, že symboly píše nástroj a literály přepisovač přeskakuje.

### Co se nemění

Holé SQL vedle metody zůstává textem Dapperu s `IN @ids`, a proto `AFallbackEmitsTheStatementTheDapperTargetWrites` srovnává dál beze změny. EF Core svůj kolekční parametr v nativním SQL dál odmítá: interpolovaná kolekce je pro `SqlQuery` jedna hodnota (JSON), a rozvinutí by znamenalo skládat interpolovaný řetězec za běhu, což 113 vede jako LINQ nad nativním SQL — jiná otázka, nerozhodnutá tady. Profil `NativeQueryExpandsCollection` zůstává: implementace, která rozvíjí sama (Hibernate), píše `(?n)` a váže kolekci jednou; implementace, která nerozvíjí, dostane rozvinutí v metodě.

## Důsledky

- **Builder:** `JpaNativeSqlQueryBuilder.Emit` pod profilem bez `NativeQueryExpandsCollection` píše výčet, čítač a vázání po prvcích místo odmítnutí; `Combined/NativeSqlTest` to tvrdí (dřív tvrdil odmítnutí).
- **Katalog LDBC:** BI 12 jde do EclipseLinku nativním SQL (`EclipseLinkIntermediate`), odmítnutí `EclipseLinkNativeList` mizí; katalog nemá žádné odmítnutí.
- **Matice T2:** nová kategorie `CollectionParameterOverAnIntermediateResult` (mezivýsledek, v jehož těle stojí kolekční parametr) ze zdrojů Dapper, EF Core, Hibernate a MyBatis; NHibernate a EclipseLink ji píšou nativním SQL — EclipseLink poprvé s rozvinutou kolekcí —, obě sady ji soudí na všech čtyřech stupních.
- **Dokumentace:** `architecture.md` §5 (řádek únikové cesty JPA), `subset.md` 1.2 a 2.11 (kolekční parametr v nativním SQL odmítá už jen EF Core), `traceability.md` F9 a T2; otevřená položka *Seznamový parametr v nativním dotazu EclipseLinku* zaniká.
