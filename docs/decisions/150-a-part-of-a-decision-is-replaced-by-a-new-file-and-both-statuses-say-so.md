# 150 — Část rozhodnutí nahrazuje nové rozhodnutí a stav obou souborů to vyslovuje; staré se cituje dál pro to, co z něj platí

Datum: 2026-10-18
Stav: platí
Požadavky: žádné
Podklad: rozhodnutí [007](007-documentation-structure.md) (dokumentace podle žánru, rozhodnutí neměnná), [018](018-work-order-as-item-marker.md) (vzor rozhodnutí o způsobu práce); revize celého repozitáře z [2026-10-18](../audits/2026-10-18-whole-repository-audit.md), kapitola 3; rejstřík [`README.md`](README.md)

## Kontext

Rejstřík rozhodnutí zná tři stavy: `platí`, `revidováno` a `nahrazeno NNN`. Rozhodnutí se nepřepisuje; když se volba změní, vznikne nový soubor a starý dostane `nahrazeno NNN` — jediná úprava, kterou starý soubor smí dostat, je jeho řádek `Stav`.

Praxe ale potřebuje změnit **část** rozhodnutí častěji než celé, a dělá to dvojím způsobem:

- **Nahrazením celku s převzetím zbytku.** Řetěz obrazovek 124 → 125 → 135 → 137: každé nové rozhodnutí změnilo jeden rys obrazovky, starému dalo `nahrazeno` celé a o zbytku napsalo „všechno ostatní platí beze změny". Platné rozhodnutí o knize tvrzení s pozicemi je tak dnes rozhodnutí o šířce textu (137), a každá citace 124, 125 a 135 v živých dokumentech i v kódu se stala citací nahrazeného rozhodnutí — revize z 2026-10-18 jich jen v `docs/` napočítala přes dvacet.
- **Nahrazením části, vysloveným jen v novém souboru.** 113 vyřadilo věty z 065, 107 a 112, 120 jednu odrážku 061, 142 jeden důsledek 094, 144 odstavec 101 a 148 odstavec 144. Řádek rejstříku nového rozhodnutí to někdy řekne („platí; nahrazuje jeden odstavec 101"), řádek starého a jeho soubor zůstanou `platí`. Čtenář, který otevře 101, se nedozví, že odstavec *Cesta o víc než dvou částech se odmítá* neplatí.

Ani jeden způsob rejstřík nepopisuje, a každý má svou vadu: první zneplatňuje citace, druhý mlčí na straně, kde čtenář začíná.

## Zvažované varianty

1. **Jen nahrazení celku, s vším platným přepsaným do nového souboru.** Nový soubor je soběstačný a starý jednoznačně neplatí. Cena je dvojí: rozhodnutí o jednom odstavci by muselo opsat celé staré rozhodnutí, a každá citace starého v živých dokumentech a v kódu by se musela přepsat na nové — u rozhodnutí, která se cituje desítkykrát (125 v kódu přes sedmdesátkrát), práce, která nic nemění na tom, co platí. Zamítáme.

2. **Nahrazení celku s převzetím zbytku odkazem (řetěz obrazovek).** Levné na psaní, ale nový soubor soběstačný není — co platí, se čte z řetězu — a citace se zneplatní stejně jako u varianty 1. Zamítáme jako obecný postup; pro řetěz obrazovek, kde se opravdu měnila celá volba podoby obrazovky, zůstává, jak je.

3. **Nahrazení části, vyslovené na obou stranách.** Nové rozhodnutí jmenuje, kterou část starého nahrazuje (odstavec, odrážku, důsledek — jeho nadpisem či první větou); starý soubor a jeho řádek rejstříku dostanou stav `platí; <část> nahrazuje NNN`. Citace starého rozhodnutí zůstávají platné pro všechno, co z něj platí, a čtenář starého souboru vidí v hlavičce, kam jít pro zbytek.

## Rozhodnutí

**Volíme variantu 3. Mění-li nové rozhodnutí jen část staršího, je to nahrazení části: nový soubor jmenuje nahrazenou část a starý soubor i jeho řádek v rejstříku dostanou stav `platí; <část> nahrazuje NNN`. Celé nahrazení (`nahrazeno NNN`) zůstává pro změnu celé volby.** Starý soubor se tím neupravuje víc než u celého nahrazení — mění se jen řádek `Stav`, jak to rejstřík od rozhodnutí 007 dovoluje —, a obsah zůstává čitelný i s tehdejší úvahou.

Pravidla:

- **Část se jmenuje tak, aby se dala najít**: nadpisem odstavce (*LINQ zůstává mimo*), jeho první větou nebo číslem odrážky; nový soubor ji jmenuje stejně.
- **Stav nového rozhodnutí** — v souboru i v řádku rejstříku — nese `platí; nahrazuje <část> rozh. NNN`, stav starého `platí; <část> nahrazuje NNN`; soubor a rejstřík říkají totéž, takže je strojová kontrola porovná. Nahrazuje-li jedno rozhodnutí části několika, stojí to u každého z nich.
- **Živý dokument a kód cituje rozhodnutí, které fakt argumentuje a platí** — u zčásti nahrazeného staré pro to, co z něj platí, nové pro nahrazenou část; u celého nahrazení to nové.
- **`revidováno`** se nemění: doplnění nepředvídaného případu před implementací.

## Důsledky

- Hlavička rejstříku [`README.md`](README.md) a pravidla v [`docs/README.md`](../README.md) dostávají čtvrtý tvar stavu.
- Stav zčásti nahrazených rozhodnutí se doplní: 061 (odrážka o těle množinové operace, 120), 065, 107 a 112 (věty, které vyřadilo 113), 094 (jeden důsledek, 142), 101 (odstavec *Cesta o víc než dvou částech se odmítá*, 144), 144 (odstavec *LINQ zůstává mimo*, 148; věta o tom, co se odmítá v projekci a řazení, 152) a 148 (druh joinu u NHibernatu a v řetězu, 151); stav nahrazujících 113, 120, 142, 144 a 148 jmenuje, co nahradily.
- Řetěz obrazovek 124 → 125 → 135 → 137 zůstává, jak je; živé dokumenty, které 124, 125 a 135 citovaly jako platné, citují 137.
- Mechanická kontrola revize dostává další bod: každá nahrazená část má svůj stav na obou stranách.
