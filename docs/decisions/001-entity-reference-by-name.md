# 001 — Odkaz na entitu ve vztahu jménem, ne referencí

Datum: 2026-07-16
Stav: platí
Požadavky: F3, F10, F11

## Kontext

Vztah mezi entitami (`Relation`) musí identifikovat zdrojovou a cílovou entitu. Mezireprezentace putuje přes API DTO do Angular frontendu, takže tvar modelu musí být serializovatelný.

## Zvažované varianty

1. **Řetězec s názvem entity** — jednoduché, ale vyhledávání podle jména je křehké.
2. **Přímá reference na `EntityMap`** — robustnější, ale je nutné ošetřit cykly při serializaci do API DTO.

## Rozhodnutí

**Řetězec s názvem entity.**

Vede k tomu víc důvodů:

- **Konzistence.** Celá mezireprezentace identifikuje věci jmény — tabulky, sloupce, vlastnosti. Řetězcový odkaz na entitu z toho nevybočuje.
- **Serializace.** Přímá reference by vytvořila cyklus `EntityMap → Relation → EntityMap`, který System.Text.Json bez speciálního zacházení (`ReferenceHandler.Preserve`, vlastní konvertory) neserializuje. Stejné komplikace by se přenesly i do Angularu.
- **Neutralita do budoucna.** Řetězcová identifikace zůstane funkční i pro plánovaný Java ekosystém (F10), kde objektové reference přes hranici serializace stejně sdílet nejde.

## Důsledky

Známou slabinu řetězce — křehké vyhledávání podle jména, které rozbije překlep nebo přejmenování — neřešíme robustnějším typem, ale validací: před generováním se všechny názvy entit ve vztazích rozresolvují proti množině `EntityMaps` a nenalezený název je chyba úplnosti IR se strukturovanou diagnostikou, v duchu F11.

Kdyby se později hodilo traverzovat graf vztahů přes reference, lze nad IR postavit rozresolvovanou vrstvu, aniž by se měnil serializovaný tvar modelu.
