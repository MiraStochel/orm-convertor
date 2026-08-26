# 069 — MAJOR označuje milník zadání, ne rozbitou plochu

Datum: 2026-08-26
Stav: platí
Požadavky: S2, S6, F7–F10, F15, T7
Podklad: rozhodnutí [041](041-versioning-and-release.md), které tímto nahrazujeme, a jeho důsledek nesený rozhodnutím [044](044-error-response-as-problem-details.md); dále [007](007-documentation-structure.md), [013](013-target-framework-versions.md), [030](030-scope-of-version-1-0.md) a [034](034-central-version-management.md); stav repozitáře k 2026-08-26

## Kontext

Rozhodnutí [041](041-versioning-and-release.md) postavilo kolem čísla verze mechanismus a ten je v pořádku: jediný zápis `<Version>` pro celé řešení (rozhodnutí [034](034-central-version-management.md)), čtyřkrokový postup vydání, poznámky v anotaci značky, značka, která se nikdy neposouvá, a pravidlo pro posun zafixovaných verzí cizích balíčků. Nic z toho tady nezpochybňujeme a všechno se to přenáší beze změny.

Zpochybňujeme jednu jedinou věc, kterou 041 vedle mechanismu zvolilo: **kritérium, podle kterého se posouvá první číslice.** 041 řeklo, že veřejnou plochou je tvar vydávaného artefaktu, REST kontrakt a hranice záruk, a že rozbití kterékoli z nich je MAJOR. Je to standardní úvaha sémantického verzování a byla zvolená z dobrého důvodu: u generátoru drží konzument v ruce výstup, ne nás.

**První vydání připravené podle toho kritéria ukazuje, co ta volba stojí.** Vydání, které uzavírá cíl 1, by bylo 2.0.0, a bylo by jím kvůli jediné změně: tělo chybové odpovědi přestalo být holým řetězcem a stalo se `ProblemDetails` podle RFC 9457 (rozhodnutí [044](044-error-response-as-problem-details.md)). Vedle toho stojí v témž vydání dotazová větev dotažená ve všech devíti směrech, čtení holého HQL, vyslovená bezklíčovost, unikátní omezení, atribuce záznamů vstupním jednotkám a dvě celé oblasti, které vystoupily z hranice záruk. Číslo by o ničem z toho nemluvilo.

**Slib sémantického verzování je ale adresovaný.** MAJOR znamená „zastav se, přečti si to a přizpůsob se" a je to věta pronesená k někomu. Tenhle nástroj nemá ke komu ji pronést: **není publikovaný.** Není v žádném registru balíčků, nemá jiný vydávaný artefakt než značku v gitu a nemá klienta mimo tenhle repozitář. Rozhodnutí 044 to o sobě říká samo — „jediný známý konzument je náš vlastní frontend" — a ten frontend žije v témž commitu jako změna a týž commit ho přizpůsobil. Číslo posunuté kvůli rozbití, které nikdo nemůže zažít, je číslo vydané za nic.

**A vydané za nic právě tam, kde je potřeba.** Čtenáři, kteří to číslo opravdu čtou, jsou tři. **S2** podmiňuje determinismus „stejnou verzí nástroje", takže uživatel dvě čísla porovnává, aby věděl, jestli má čekat týž artefakt. **S6** ho dává do záznamu každého běhu, takže cestuje s každým výsledkem. Oběma stačí, aby číslo **pojmenovalo stav nástroje**; ani jeden nepotřebuje, aby v sobě neslo zakódované rozbití. Třetí čtenář je text práce a ten potřebuje víc: zadání je posloupnost schopnostních milníků — tři .NET frameworky (F1–F6, F11, F14), tři javovské ORM s cross-ecosystem překladem (F7–F10, F12–F13) a Advisor s experimentální částí (F15, T1–T7) —, a [`open-items.md`](../open-items.md) zbývající práci do těch tří cílů přesně seskupuje. Číslo, které dojde na 2.0.0 proto, že se změnil tvar chybového těla, tomuhle čtenáři neřekne nic; číslo, které dojde na 2.0.0 ve chvíli, kdy existuje javovský překladač, mu jedním pohledem řekne, kde práce stojí.

**Na tomtéž měří i článek.** Příspěvek, který JSS článek tvrdí, je vyslovený v pokrytí — které frameworky se překládají, která pravidla E1–E10 a Q1–Q15 platí a co rozhoduje ILP model —, ne ve tvaru chybové odpovědi po drátě. Verze, která měří totéž, na čem se práce posuzuje, je verze, kterou lze v textu citovat.

## Zvažované varianty

1. **Ponechat kritérium 041 a vydat 2.0.0.** Poctivé vůči konzumentovi, který neexistuje. Cena je dvojí: první číslice se vydá za změnu, kterou nikdo nemůže pocítit, a zároveň se spálí právě to číslo, které má nést javovský milník — pak by cíl 2 musel být 3.0.0 a čísla by přestala odpovídat cílům. Zamítáme.

2. **Kritérium ponechat a jen ho zatím neuplatňovat, dokud nástroj není publikovaný.** Pravidlo se stojící výjimkou není pravidlo. Pozdější čtenář by u každého MINOR musel hádat, jestli znamená „nic se nerozbilo", nebo „rozhodli jsme se to nepočítat", a S2 čte číslo doslova. Zamítáme.

3. **Opustit sémantické verzování a vydání datovat (`2026.08`).** 041 kalendářní verzování zamítlo a důvod platí dál: datum nerozliší opravu od nové schopnosti, takže na otázku, kterou pokládá S2, neodpovídá vůbec. Zamítáme.

4. **Přepsat, co ty tři pozice měří: MAJOR milník zadání, MINOR schopnost uvnitř milníku, PATCH oprava i rozbití bez nové schopnosti — a rozbití vyslovit v poznámkách k vydání, ne v čísle.**

## Rozhodnutí

**Volíme variantu 4. Číslo verze měří schopnosti nástroje, ne to, co se na jeho ploše rozbilo.**

**MAJOR** se zvýší, když nástroj překročí milník zadání, tedy když je hotový celý cíl [`open-items.md`](../open-items.md): nový ekosystém (Hibernate, MyBatis a EclipseLink spolu s cross-ecosystem překladem, F7–F10 a F12–F13), nebo Advisor pracující nad všemi podporovanými frameworky spolu s experimentální částí (F15, T1–T7). Cíl, který se zavírá dnes, je cíl 1 a MAJOR není: tři .NET frameworky jsou to, o čem verze 1 celou dobu byla. **Příští MAJOR je tedy 2.0.0 za javovský ekosystém.**

**MINOR** se zvýší, když uvnitř cíle přibude schopnost: další kategorie dotazů, další druh diagnostického záznamu, další koncový bod, nebo když do nároku vstoupí oblast hranice záruk.

**PATCH** se zvýší u opravy — a nově **i u každé změny veřejné plochy, která s sebou nenese novou schopnost**: týž vstup dá jinak tvarovaný artefakt, z REST kontraktu zmizí pole, tělo chybové odpovědi změní tvar. Tady je celá změna proti 041 a sem padá i změna z rozhodnutí 044.

**Vydání nese nejvyšší pozici, kterou pohnula kterákoli jeho změna.** Vydání složené z oprav a rozbití je PATCH; jakmile v něm přibude jediná schopnost, je MINOR a opravy i rozbití se v něm vezou. Právě proto se rozbití tvaru výstupu **nevydávají zvlášť**: dokud není komu je oznámit, není důvod kvůli nim razit číslo, a nastane-li jich v jednom cíli víc, jsou všechna v poznámkách toho vydání, které cíl uzavře.

**Veřejná plocha se nemění, mění se jen to, co s ní číslo dělá.** Tři plochy z 041 — tvar vydávaného artefaktu, REST kontrakt a hranice záruk podle §9 — jsou dál přesně to, co vydání popisuje, a poznámky je popisují v témž pořadí, tvarem výstupu napřed. Neplatí už jen to, že změna na nich hne první číslicí.

**Předpoklad, za kterého tohle platí, je vyslovený a dá se zkontrolovat: nástroj není publikovaný.** Žádný balíček v registru, žádný vydávaný artefakt kromě značky v gitu, žádný konzument mimo tenhle repozitář. **Až to přestane platit, tohle rozhodnutí se nahrazuje, ne dovysvětluje** — publikovaný konzument je právě ten adresát, kterému slib sémantického verzování patří, a vrací tím 041 jeho důvod.

**Z 041 se přenáší beze změny všechno ostatní:**

- Číslo nese jediným zápisem `<Version>` v `Directory.Build.props` (rozhodnutí [034](034-central-version-management.md)) a jediným dalším strojově čitelným nositelem je `CITATION.cff`.
- **Vydání je čtyřkrokový postup a pořadí je jeho podstatou:** (1) práce hotová, pracovní kopie čistá, CI zelené; (2) `<Version>` se posune vlastním commitem spolu s poli `version` a `date-released` v `CITATION.cff`; (3) anotovaná značka `MAJOR.MINOR.PATCH` vzniká na tomhle commitu; (4) anotace nese poznámky k vydání. Doklady, o které se poznámky opírají, se pořizují až po posledním commitu, který do vydání patří.
- **Poznámky bydlí v anotaci značky, ne v souboru** (rozhodnutí [007](007-documentation-structure.md) changelog zamítlo a zůstává v platnosti), a mají pevnou stavbu tří odstavců: co se změnilo na tvaru výstupu, co přibylo, co se pohnulo na hranici záruk.
- **Značka se nikdy neposouvá a číslo se nikdy nepoužije podruhé.**
- **Zafixované verze cizích balíčků** se posouvají ze dvou důvodů — bezpečnostní hlášení a potřeba rozdělané práce —, „vyšla novější" mezi nimi není, a **posun verze cílového frameworku není nikdy PATCH**: verze putuje deskriptorem ([013](013-target-framework-versions.md)) do záznamu běhu a mění to, proti čemu artefakt platí, což je tvrzení o schopnosti, ne oprava.
- **Hlídání hlášení je práce CI**, úloha `dependencies` nad `dotnet list package --vulnerable --include-transitive`, při každé změně řešení a jednou týdně podle rozvrhu.
- **Nevzniká proces pro předběžná vydání ani pro větve**; vývoj je sólo a na `main`.

## Důsledky

**Vydání uzavírající cíl 1 je 1.2.0.** Jeho obsah se nemění, mění se jen číslo: přibyly schopnosti uvnitř cíle 1 a do nároku vstoupily dvě celé oblasti hranice záruk, což je MINOR, a změny tvaru výstupu i tvaru chybové odpovědi se v něm vezou jako PATCH.

**Důsledek rozhodnutí 044 o čísle příštího vydání tímhle padá.** 044 ve svých důsledcích říká „příští vydání je MAJOR, tedy 2.0.0"; platí místo toho tohle rozhodnutí. **Rozhodnutí 044 se ale nepřepisuje a nemění stav**: jeho volba — chybová odpověď je `ProblemDetails` podle RFC 9457 — platí a je implementovaná, přebité je jedině to, co 044 předpovědělo o číslu. Rozhodnutí se nepřepisují (rozhodnutí [007](007-documentation-structure.md)) a na rozpor platí pozdější; kdo čte 044 samotné, najde v jeho posledních odstavcích číslo, které už neplatí, a odsud se dozví proč.

**Klasifikační věty rozhodnutí 043–068 zůstávají čitelné a nepřepisují se.** Každé z nich říká, co změna udělala s artefaktem, s kontraktem a s hranicí záruk, a ten fakt se nemění — poznámky k vydání ho potřebují přesně v té podobě. Mění se jedině převod na pozici čísla, a mění se u jediného z nich, totiž u 044.

**Rozhodnutí 041 dostává stav `nahrazeno 069`** a zůstává čitelné jako záznam volby, která platila mezi 2026-08-22 a dneškem, včetně toho, že podle ní vzniklo vydání 1.1.0.

**Kanonické anglické znění se přepisuje.** Sekce *Versioning and releases* v kořenovém [`README.md`](../../README.md) nese pravidlo pro konzumenta a musí říkat totéž co tohle rozhodnutí; `architecture.md` §9 a §6.5 na ně odkazují a odkaz se přesměrovává sem.

**Cena je vyslovená, ne zamlčená.** Konzument, který by se objevil dřív než cíl 2, by dostal jako PATCH změnu, kvůli které musí sáhnout do svého kódu. Bereme to vědomě: takový konzument dnes neexistuje, a až se objeví, mění se předpoklad, ne výklad. Co mu zůstává i pak, je první odstavec poznámek k vydání — ten, který mluví o tvaru výstupu —, a je to místo, kde se to dozví dřív a přesněji, než by mu řekla první číslice.

**Číslo přestává být samo o sobě čitelné jako slib kompatibility, a to je skutečná ztráta proti standardnímu semveru.** Poctivé znění je tohle: číslo tohohle projektu odpovídá na otázku „co už nástroj umí", ne na otázku „co se mi rozbije, když aktualizuju". S2 tím netrpí, protože se ptá jedině na to, jestli jsou dvě čísla stejná.
