# 035 — Kolekce v NHibernate entitě deklarované rozhraním

Datum: 2026-08-21
Stav: platí
Požadavky: F3, F11, S2
Podklad: audit 2026-08-21, nález 1.1

## Kontext

NHibernate vyžaduje, aby perzistentní kolekce byla na entitní třídě deklarovaná rozhraním (`IList<T>`, `ISet<T>`): při načtení entity nahrazuje hodnotu vlastnosti vlastní implementací (`PersistentGenericBag<T>`, `PersistentGenericSet<T>`) a ta se do konkrétního `List<T>` či `HashSet<T>` přiřadit nedá. Generovaná entita ale kolekce deklaruje konkrétně — `public virtual List<Order> Orders { get; set; }` —, protože sdílený převod jazykových typů (`CSharpTypeConvertor.CollectionName`) vykresluje `List`/`HashSet` pro všechny cíle a `NHibernateEntityBuilder.BuildPropertySignature` název typu přebírá beze změny.

Vada je neviditelná pro všechny dnešní stupně ověření: kompilace (2. stupeň) projde, protože konkrétní typ je platné C#, a stavba session factory (3. stupeň) kolekci na CLR typ vlastnosti neváže. Selhání by se ukázalo až načtením entity, tedy na 4. stupni rozhodnutí [016](016-generated-artifact-verification-levels.md), který nemá jediného zástupce. Je to tedy neplatný výstup pro platný vstup uvnitř oblasti, kterou verze 1.0 nárokuje (F3) — táž třída vady, kvůli které rozhodnutí [006](006-flat-composite-key-rendering.md) zavedlo identitní členy kompozitního klíče jako odpovědnost builderu.

## Zvažované varianty

1. **Vykreslit rozhraní ve sdíleném převodu typů** — `CSharpTypeConvertor` by vracel `IList<T>`/`ISet<T>` pro všechny cíle.
2. **Vyslovit vyňatou oblast** — kolekce v generované NHibernate entitě prohlásit za mimo záruky verze 1.0.
3. **Vykreslit rozhraní v NHibernate builderu** — deklarace rozhraním je vynucení cílového frameworku, stejné kategorie jako `virtual`.

## Rozhodnutí

**NHibernate builder deklaruje kolekční vlastnost rozhraním a přepisuje její inicializátor; sdílený převod typů se nemění. Požadavek je deklarovaný v deskriptoru jako vynucený člen a vázaný testem.**

Proti změně sdíleného převodu (varianta 1) mluví totéž, co v rozhodnutí 006 mluvilo proti přenesení identitních členů do IR: deklarace rozhraním není fakt o doméně, ale požadavek jednoho cílového frameworku. EF Core ani Dapper rozhraní nevyžadují a konkrétní typ je u nich obvyklý tvar; sdílený převod (rozhodnutí [014](014-language-type-model.md)) má zůstat neutrální. Vyjmutí ze záruk (varianta 2) nemá kolem čeho vést hranici: neplatná je entita sama, ne okrajový prvek mapování, a F3 verze nárokuje — vyňatá oblast by musela pokrýt celé kolekční mapování, které je jinak hotové.

Konkrétně:

- `NHibernateEntityBuilder.BuildPropertySignature` vykresluje kolekční vlastnost rozhraním podle druhu kolekce z modelu: `IList<T>` pro `List` i `Unspecified` (tvar, na který se váže `<bag>`), `ISet<T>` pro `Set` (vyžaduje ho `<set>`). Prvek kolekce se dál vykresluje sdíleným převodem — mění se jen hlava generického jména.
- **Inicializátor se přepisuje na prázdnou konkrétní instanci** — `= new List<T>();`, resp. `= new HashSet<T>();` —, kdykoli zdroj nějaký uvedl. Cílově typované tvary zdroje se nad rozhraním nepřeloží (`= new()` vůbec, `= []` nad `ISet<T>`), takže doslovný průchod není možný. Prázdné tvary (`[]`, `new()`, prázdný konstruktor konkrétního typu) znamenají totéž co náhrada, takže se nahrazují mlčky, jako každé jiné vynucení cíle; inicializátor jiného tvaru — naplněný nebo volající vlastní kód — náhradou svůj obsah ztrácí a hlásí se záznamem o ztrátě (rozhodnutí [004](004-unexpressible-facts-as-warnings.md)).
- **Deskriptor NHibernate deklaruje požadavek jako vynucený člen** se zakázanými značkami `virtual List<` a `virtual HashSet<` (stav `Always`): žádná deklarace v generované entitě nesmí být konkrétní. `EnforcedMembersTest` k tomu dostává kolekční vlastnost, takže vazba deklarace a emise je skutečně vykonaná, ne prázdně splněná. Substring je vědomý strop stejně jako u `virtual` (rozhodnutí [009](009-target-framework-descriptor.md)); právě proto je značka zakázaná, ne požadovaná — odhalí konkrétní deklaraci kdekoli, kdežto požadovaná značka `IList<` by byla splněná první kolekcí.

Vazbu testem volíme proto, že 3. stupeň ověření mlčí a 4. stupeň zástupce nemá: bez značky v deskriptoru by regrese neměla kde selhat. Je to táž dělba — deskriptor deklaruje, builder implementuje, test váže —, kterou drží ostatní vynucené členy.

## Důsledky

Parsery čtou rozhraní už dnes (`IList`, `ISet` i `ICollection` mapuje `CSharpTypeConvertor.FromString` na druhy kolekce), takže NHibernate → NHibernate zůstává na kolekcích stabilní a směr NHibernate → EF Core/Dapper dál vydává konkrétní typy — jejich buildery se nemění.

Očekávané výstupy testů 1. stupně se v NHibernate směrech mění z `List<T> … = new();` na `IList<T> … = new List<T>();`. Nový záznam o ztrátě u nepřenositelného inicializátoru je jediná nová diagnostika; prázdné inicializátory, tedy prakticky všechny, procházejí beze záznamu.

Zůstává pravdou, že požadavek je ověřený tvarem (1. stupeň), ne během (4. stupeň): až 4. stupeň dostane prvního zástupce, načtení entity s kolekcí patří mezi první scénáře.
