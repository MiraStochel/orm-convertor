# 096 — Pravidlo článku se cituje tam, kde odůvodňuje volbu; druhá mapa nevzniká

Datum: 2026-09-21
Stav: platí
Požadavky: žádné
Podklad: revize [2026-09-21](../audits/2026-09-21-pre-release-2-0-0-audit.md), nález 6.2; rozhodnutí [007](007-documentation-structure.md); článek §5 a §6

## Kontext

Rozhodnutí i [`architecture.md`](../architecture.md) argumentují překladovými pravidly článku jménem: „pravidlo E9", „pravidlo Q5", „§5.4 článku výslovně připouští". Je to jeden ze čtyř způsobů, jak tahle sada dokumentů podpírá svá tvrzení — vedle požadavků F/S/T, vedle měření a vedle odkazů na jiná rozhodnutí.

Revize z 2026-09-21 se zeptala, jak úplné to podepření je, a odpověď je nepříjemná: **pět z pětadvaceti pravidel se napříč celým `docs/` nevyskytuje ani jednou.** Jsou to **E6** (co je vztah: vlastnost, která odkazuje na jinou entitu nebo je kolekcí parametrizovanou jinou entitou, s kardinalitou), **E7** (určení vlastnící strany, která emituje cizí klíč), **Q6** (join jako čtveřice levá strana, pravá strana, druh, podmínkový strom), **Q7** (odvození podmínky implicitního joinu z metadat vztahu, `FK(levá) = PK(pravá)`) a **Q9** (filtrace po agregaci jako `HAVING`).

Mlčení není důkazem opomenutí — E7 je doslova invariant `Role` a Q9 je `HavingInstruction` —, ale **je důkazem nedohledatelnosti**. Konzistence s článkem patří mezi čtyři kritéria, podle kterých se hodnotí výzkumný artefakt, a čtenář, který drží článek vedle repozitáře, nemá u těch pěti co porovnat: nedozví se ani že jsou splněná, ani že jsou vyňatá.

A jedno z nich se za tím mlčením schovalo po právu. **Q7 implementované není.** Implicitní join po asociační cestě — `from Customer c join c.orders o` v HQL a JPQL — obě čtečky od rozhodnutí [070](070-a-parser-refuses-what-would-change-the-row-set.md) **odmítají** záznamem `Failure`, ačkoli mezireprezentace všechno potřebné nese: vztah je na `EntityMap`, dvojice sloupců v `ColumnPairs` a `Role` říká, která strana nese cizí klíč. Že se ta mezera dosud nikde nejmenuje, je přímý důsledek toho, že se nejmenuje ani pravidlo.

## Zvažované varianty

### 1 — Nechat to tak, jak to je

Pravidla se citují, kde se zrovna hodí, a úplnost nikdo netvrdí. Zamítáme: vada, kterou revize našla u Q7, je přesně ta, které tenhle stav nechá vyrůst. Nevyslovená mezera proti článku, ze kterého projekt vychází, je horší než vyslovené zúžení.

### 2 — Založit druhou mapu: pravidlo → místo v repozitáři

Tabulka pětadvaceti řádků vedle [`traceability.md`](../traceability.md). Zamítáme, a ze tří důvodů. Je to **druhé místo téhož tvrzení** — mapa by vedle požadavku F/S/T vedla druhou osu, obě přes tentýž kód, a rozešly by se, protože se rozcházejí vždycky (viz rozhodnutí [095](095-a-dated-run-record-names-its-commit.md), které řeší přesně tohle u velikosti sady). Je to **mapa proti nepohyblivému cíli**: článek je zmrazený, kdežto kód se mění, takže údržba by tekla jedním směrem bez protihodnoty. A hlavně **není za co ji brát k odpovědnosti**: práce se hodnotí proti požadavkům vedoucího, ne proti číslům pravidel článku, takže by vznikl dokument, který nikdo nečte a který přesto musí někdo udržovat.

### 3 — Pravidlo se cituje tam, kde odůvodňuje volbu, a pět nevyslovených se dopíše k mechanismu, který popisují

## Rozhodnutí

**Volíme variantu 3. Pravidlo článku se cituje tam, kde nese odůvodnění volby nebo kde od něj nástroj vystupuje — ne jako položka kontrolního seznamu. Druhá trasovací mapa nevzniká. Pět pravidel, která se dosud nevyskytovala, dostane jednu zmínku u mechanismu, který popisují, a u Q7 je tou zmínkou přiznaná mezera.**

**Kritériem citace je rozhodnutelnost, ne úplnost.** Pravidlo se jmenuje, když čtenář potřebuje vědět, **odkud se vzalo** to, co právě čte, nebo **v čem se od článku lišíme**. Pravidlo, které nástroj plní tak, jak článek popisuje, a o kterém se nikdy nerozhodovalo, zmínku nepotřebuje — a přesně takové čtyři z těch pěti jsou.

**Kam ty čtyři patří.** E6 a E7 do §4.3 [`architecture.md`](../architecture.md) k popisu vztahů a `Role`, Q6 do §4.4 k `JoinInstruction` a Q9 tamtéž k `HavingInstruction`. Je to jediná věta u každého a její cena je nulová; hodnota je, že se dají dohledat.

**Q7 dostane zmínku jiného druhu, protože je to mezera.** Věta u joinu v §5 řekne, že implicitní join po asociační cestě, který článek odvozuje z metadat vztahu, nástroj **neodvozuje a odmítá** — a proč: rozhodnutí 070 zvolilo odmítnutí pro konstrukce, které čtečka nepřečte, a asociační cesta mezi ně spadla, ačkoli model na její přeložení má všechno. Odbavení té mezery je vlastní práce a vlastní volba; patří do [`open-items.md`](../open-items.md), ne sem.

**Co se tím nemění:** [`traceability.md`](../traceability.md) zůstává jedinou mapou a jeho osou zůstává požadavek. Pravidla článku v něm mohou stát jako odůvodnění řádku, jak tam dnes stojí u F5 a T2, ale řádek na pravidlo nevzniká.

## Důsledky

**Pět vět do `architecture.md` a jedna položka do `open-items.md`.** Víc práce z tohohle rozhodnutí neplyne — a to je jeho smysl: odpovědí na „něco nesedí s článkem" nemá být nový dokument.

**Mezera u Q7 se tím stává vyslovenou.** Dokud nebyla pojmenovaná, byl to tichý rozdíl proti článku; od teď je to zapsané zúžení jako každé jiné a dá se citovat v textu práce, což je proti dnešku zlepšení, i kdyby se nikdy neodbavila.

**Prahem pro budoucí pravidla je táž otázka.** Až se článek někde rozejde s nástrojem podruhé, kritérium je totéž: pokud jsme se rozhodovali, patří to do rozhodnutí; pokud je to mezera, patří to do `architecture.md` a do `open-items.md`; pokud ani jedno, nepatří to nikam.

**Riziko, které bereme na sebe:** bez seznamu se nedozvíme, že jsme na nějaké pravidlo zapomněli, dokud si toho někdo nevšimne. Je to táž cena, jakou platíme u všeho ostatního, co nehlídá test, a vážíme ji proti jistotě, že druhá mapa se rozejde — jedno je možnost, druhé zkušenost.
