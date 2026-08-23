# 041 — Verzování, vydání a posun zafixovaných verzí

Datum: 2026-08-22
Stav: platí
Požadavky: S2, S4, S6
Podklad: rozhodnutí [007](007-documentation-structure.md), [013](013-target-framework-versions.md), [030](030-scope-of-version-1-0.md) a [034](034-central-version-management.md); stav značky `1.0` v repozitáři k 2026-08-22

## Kontext

Mechanismus verzování je hotový a čistý. `Directory.Build.props` nese `<Version>` jediným zápisem pro celé řešení, `.csproj` soubory žádné číslo neobsahují, `ToolRelease.Version` je čte ze sestavení a záznam běhu ji vydává v každé odpovědi `/convert` (rozhodnutí [034](034-central-version-management.md), `architecture.md` §5.1). Chybí to kolem: **co to číslo znamená, kdy se posune a čím se vydání liší od běžného commitu.**

Není to formalita, protože to číslo už dnes něco nárokuje. **S2 podmiňuje determinismus „stejnou verzí nástroje"** — uživatel se tedy podle verze rozhoduje, jestli může čekat týž artefakt jako minule. **S6 ji dává do záznamu běhu**, takže cestuje s každým výsledkem. Číslo, které se posouvá bez pravidla, na obě ta tvrzení odpovídá „nevím".

**U generátoru je navíc veřejnou plochou něco jiného než u knihovny.** Konzument v ruce nedrží nás, drží náš výstup. Změna tvaru vydávaného artefaktu je pro něj rozbíjející změna i tehdy, když se rozhraní nástroje nehnulo — a přesně to se už jednou stalo: rozhodnutí [028](028-assembly-name-is-not-ours-to-invent.md) přestalo vypisovat `assembly=` do NHibernate mapování, takže týž vstup dává od té chvíle jiný soubor. Kdo si na starý tvar postavil projekt, musí zasáhnout.

**Že proces vydání chybí, se už jednou projevilo.** Značka `1.0` sedí na commitu `40b69a3` a je vydaná na `origin`. Kontejnerovou konfiguraci, kterou README v sekci *Version 1.0 guarantees* výslovně nárokuje, ten commit neobsahuje — `git show 1.0:ORMConvertor/docker-compose.yml` nemá jediný profil a `architecture.md` §9 v něm kontejnerizaci naopak ze záruk *vyjímá*, jak tehdy stanovilo rozhodnutí [030](030-scope-of-version-1-0.md). Kdo si stáhne značku, dostane nástroj, který o sobě tvrdí něco jiného než dnešní README. Nikde přitom není řečeno, kdy se číslo razí, na který commit značka patří a co musí obsahovat.

**A rozhodnutí [007](007-documentation-structure.md) zamítlo changelog správně, jenže na jinou otázku.** „Co se změnilo kdy" umí git historie mechanicky a zadarmo — to platí dál a nemáme důvod to měnit. Otázka uživatele u druhé značky ale zní jinak: *co je nového ve 1.1 oproti 1.0 a změní se mi výstup, když projekt přeložím znovu?* Na tu git historie odpovídá jen tím, že si čtenář přečte čtyřicet commitů.

Poslední díl je opačný konec téhož: **zafixované verze cizích balíčků** (rozhodnutí [013](013-target-framework-versions.md) a [034](034-central-version-management.md)) jsou správně zafixované, protože na nich stojí S2 i platnost tvrzení v `docs/analysis/`. Nikde ale není řečeno, kdy se posouvají a kdo hlídá, že proti některé z nich nevyšlo bezpečnostní hlášení.

## Zvažované varianty

1. **Nechat verzi jako pořadové číslo vydání bez závazku.** Dnešní stav. Levné a nezavazující — jenže číslo už závazek nese, protože ho jmenuje S2 a vydává S6. Verze bez pravidla nedokáže odpovědět na jedinou otázku, kvůli které ji uživatel čte. Zamítáme.

2. **Sémantické verzování nad rozhraním nástroje.** Tedy semver, kde veřejnou plochou je REST kontrakt. Zní to jako standardní volba a je špatná: rozhraní má devět koncových bodů, je stabilní a nudné, kdežto to, co konzumenta skutečně rozbije, je tvar generovaného kódu. Rozhodnutí 028 by pod tímhle pravidlem bylo záplatou, protože se žádný endpoint nezměnil — a to je pravý opak toho, co uživatel potřebuje vědět. Zamítáme.

3. **Kalendářní verzování (`2026.08`).** Poctivé v tom, co říká — kdy vydání vzniklo —, a mlčenlivé v tom, na co se ptá S2. Datum nerozliší opravu od změny tvaru výstupu, takže by si uživatel stejně musel číst poznámky. Navíc by se hůř snášelo s tím, že verze cestuje v záznamu běhu jako součást tvrzení o kompatibilitě. Zamítáme.

4. **Sémantické verzování, kde veřejnou plochou je vydávaný artefakt, rozhraní i hranice záruk.**

## Rozhodnutí

**Volíme variantu 4. Verze je `MAJOR.MINOR.PATCH` a veřejnou plochou, kterou popisuje, jsou tři věci: tvar vydávaného artefaktu, REST kontrakt a hranice záruk podle `architecture.md` §9.**

**MAJOR** se zvýší, když týž vstup dá artefakt, kterému se konzument musí přizpůsobit; když se rozbije REST kontrakt; nebo když se hranice záruk zúží, tedy oblast z nároku vypadne.

**MINOR** se zvýší, když přibude schopnost a dosavadní výstup zůstane beze změny: nový framework, nová kategorie dotazů, nový druh diagnostického záznamu, nový koncový bod, nebo když oblast do nároku naopak vstoupí.

**PATCH** se zvýší, když oprava změní výstup jen tam, kde byl špatně. Hranice mezi PATCH a MAJOR je tedy jediná otázka: **byl předchozí výstup správný?** Opravit vadný artefakt je záplata, i když se text výstupu změní; změnit artefakt, který byl v pořádku, je rozbíjející změna, i když je nový tvar hezčí.

**Vydání je čtyřkrokový postup a pořadí je jeho podstatou:**

1. Práce je hotová, pracovní kopie čistá a CI zelené.
2. `<Version>` v `Directory.Build.props` se posune na vydávané číslo **vlastním commitem**.
3. Anotovaná značka `MAJOR.MINOR.PATCH` vzniká **na tomhle commitu**, ne na jiném.
4. Anotace značky nese poznámky k vydání.

Krok 2 před krokem 3 je celý smysl seznamu: verze v sestavení a verze ve značce pak nemůžou tvrdit každá něco jiného, a záznam běhu podle S6 vydává totéž číslo, které je na značce. **Značka se nikdy neposouvá a číslo se nikdy nepoužije podruhé.** Jakmile je značka na `origin`, může ji mít někdo staženou; přesunout ji znamená, že „1.0" označuje dva různé stromy podle toho, kdy se kdo díval. Vydání, které se ukáže jako chybné, se opravuje **dalším číslem**, ne opravenou značkou — je to totéž pravidlo, jaké pro rozhodnutí zavedlo 007: co bylo jednou vydáno, zůstává čitelné i s tím, proč tehdy dávalo smysl.

**Poznámky k vydání bydlí v anotaci značky, ne v souboru.** Rozhodnutí 007 tím zůstává nedotčené — changelog jako soubor v repozitáři nevzniká —, a odpověď se přitom ocitne přesně tam, kde je verze: `git tag -n99` ji vypíše a GitHub ji vykreslí v přehledu vydání. Struktura je pevná a krátká, tři odstavce v tomhle pořadí: **co se změnilo na tvaru výstupu** (první, protože jen kvůli tomu čtenář poznámky otevřel), **co přibylo**, **co se pohnulo na hranici záruk**. Nic dalšího; „co se změnilo kdy" zůstává prací git historie.

**Zafixované verze cizích balíčků se posouvají ze dvou důvodů, a „vyšla novější" mezi nimi není.** Prvním je bezpečnostní hlášení proti referencovanému balíčku, druhým potřeba rozdělané práce. Fixace není konzervatismus: stojí na ní S2 a platnost všeho, co `docs/analysis/` a `architecture.md` o chování frameworků tvrdí, takže posun je změna tvrzení, ne údržba. **Posun verze cílového frameworku proto nikdy není PATCH** — verze putuje deskriptorem ([013](013-target-framework-versions.md)) do záznamu běhu a je součástí tvrzení o tom, proti čemu artefakt platí.

**Hlídání hlášení je práce CI, ne paměti.** Workflow dostává úlohu `dependencies`, která spouští `dotnet list package --vulnerable --include-transitive` a selže, jakmile výpis nějaký balíček ohlásí. Běží při každé změně řešení a navíc jednou týdně podle rozvrhu, protože hlášení vzniká bez toho, aby někdo něco commitnul.

## Důsledky

**Značka `1.0` se neposouvá a stav se narovná dalším vydáním.** Je publikovaná a nese, co nese; podle pravidla výše se opravuje číslem, ne přepsáním. Následující vydání je **1.1.0**: proti stavu, který značka skutečně obsahuje, vstoupila do nároku oblast — kontejnerová konfigurace a s ní S5 (rozhodnutí [039](039-container-configuration-of-the-environment.md)) —, a to je přesně definice MINOR. Číslo v `Directory.Build.props` se přitom **posouvá až krokem 2 při vydání**, ne teď; dokud vydání nenastalo, je 1.0.0 správná hodnota rozdělané práce. Co všechno k tomu vydání patří, nese [`open-items.md`](../open-items.md).

**`architecture.md` §9 a README dostávají u vydání jedinou pravdu.** Krok 1 postupu znamená, že nárok verze musí být sepsaný dřív, než značka vznikne — tedy že text §9 popisuje commit, na kterém značka sedí. Právě tahle vazba u 1.0 chyběla.

**Uživatel dostane odpověď na otázku, kterou dnes položit nemůže.** Po druhé značce bude `git tag -n99` vypisovat, co se změnilo na tvaru výstupu — což je u nástroje, jehož produktem je generovaný kód, ta jediná otázka, kterou před aktualizací potřebuje zodpovědět.

**CI může nově zčervenat bez jediného commitu.** To je záměr, ne vada: hlášení proti zafixovanému balíčku je zpráva, kterou chceme dostat v týdnu, kdy vyjde, ne v den, kdy se náhodou sáhne na řešení. Cenou je jedna úloha navíc v každém běhu workflow a jeden naplánovaný běh týdně.

**Nevzniká tím proces pro předběžná vydání ani pro větve.** Značky jako `1.1.0-rc1`, podpora starší řady a hotfix větve jsou nástroje pro tým a pro uživatele, kteří nemůžou aktualizovat; vývoj je sólo a na `main` (rozhodnutí 003 a `CLAUDE.md`). Až se objeví někdo, kdo na verzi 1.x zůstane, bude to nové rozhodnutí, ne rozšíření tohohle.
