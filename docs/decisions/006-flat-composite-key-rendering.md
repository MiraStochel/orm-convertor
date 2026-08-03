# 006 — Ploché vykreslení kompozitního klíče a identitní členy jako odpovědnost builderu

Datum: 2026-08-02
Stav: platí
Požadavky: F1, F2, F7–F10, F11
Podklad: audit 2026-08-02, nálezy 1, 4.2 a 4.3

## Kontext

Kompozitní klíč je v IR seřazený seznam částí. Cílové frameworky ale nabízejí dvě různé formy: klíčové vlastnosti přímo na entitě, nebo samostatnou klíčovou třídu (`@EmbeddedId` v JPA, `<composite-id name="..." class="...">` u NHibernate). Je třeba rozhodnout, jakou podobu má klíč dostat v generovaném kódu a zda má IR nést pojem klíčové třídy.

Bezprostředním podnětem byl kritický nález auditu: NHibernate builder generoval `<composite-id>`, ale bez identitních členů na entitní třídě, takže výstup byl syntakticky správný a přitom nespustitelný — stavba session factory padala hláškou `composite-id class must override Equals()`.

## Zvažované varianty

1. **Ploché vykreslení** — klíčové vlastnosti zůstávají přímo na entitě: EF Core `HasKey` / `[PrimaryKey]`, NHibernate `<composite-id>` s `<key-property>`, JPA `@IdClass`.
2. **Klíčová třída** — `@EmbeddedId`, respektive vnořený identifikátor u NHibernate.

## Rozhodnutí

**Ploché vykreslení ve všech cílech, identitní členy jako odpovědnost builderu, klíčová třída jen jako volitelný signál.**

Proti klíčové třídě mluví dva důvody:

- **Strategie generování per-part.** `@GeneratedValue` lze v JPA použít jen na atributu anotovaném `@Id`. U `@EmbeddedId` není žádná část klíče takto anotovaná, takže kompozitní klíč s generovanými hodnotami tato forma vyjádřit neumí — model, který nese strategii generování per-part, by se do ní nevešel.
- **Dosah do dotazové větve.** U `@IdClass` se na část klíče odkazuje `o.orderId`, u `@EmbeddedId` `o.id.orderId`. Volba formy tedy není lokální pro entitní model, ale mění cesty k vlastnostem v generovaných dotazech.

Ploché vykreslení proto drží tvar entity i cesty k vlastnostem jednotné napříč všemi cíli a dotazová IR může dál odkazovat vlastnosti prostým jménem.

Členy, které si kompozitní klíč v cílovém frameworku vynucuje — u NHibernate `Equals`, `GetHashCode` a `[Serializable]` na persistentní třídě, u JPA celá ID třída s bezparametrickým konstruktorem, `equals`, `hashCode` a `Serializable` — do IR nepatří. Nejsou to fakta o doméně, ale požadavky cílového frameworku, ve stejné kategorii jako `virtual` na mapovaných členech u NHibernate. Generuje je tedy builder a odpovídá za ně stejně jako za zbytek boilerplate.

## Důsledky

Že chybějící generování identitních členů shodí u NHibernate stavbu session factory, není okrajový detail, ale doklad, že bez tohoto rozdělení odpovědností vzniká syntakticky správný a přitom nespustitelný výstup. Testy proto musí ověřovat použitelnost generovaného kódu, ne jen jeho tvar.

Ploché vykreslení zahazuje jednu informaci: pokud zdrojový projekt klíč vyjádřil klíčovou třídou, ztratí se její název i zvolená forma. `PrimaryKey` proto dostane nepovinný údaj o názvu a formě klíčové třídy, obdobně jako `IsJunctionTable` slouží v rozhodnutí 005 jako opt-in signál. Parser ho vyplní, když ho zdroj nese, builder ho použije, pokud je přítomný, a jinak odvodí název konvencí. Bez něj by se model při překladu javového projektu (F7–F10) potichu přetvaroval a F11 by neměla co ohlásit.

IR tím nezískává klíčovou třídu jako povinnou strukturu — nese ji jako záznam o zdroji, ne jako součást definice klíče.
