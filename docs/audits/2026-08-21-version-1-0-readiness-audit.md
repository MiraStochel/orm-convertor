# Audit připravenosti verze 1.0, 2026-08-21

Revize stavu před posledním krokem vydání. Rozhodnutí [030](../decisions/030-scope-of-version-1-0.md) postavilo kritérium — *verze 1.0 je hotová, když je každá její část buď opravdu dodělaná, nebo vcelku a nahlas vyňatá ze záruk* — a devatenáctibodové pořadí prací. Body 1–18 jsou odbavené, na řadě je poslední: verze nástroje a značka vydání. Tenhle audit se ptá na jedinou věc: **drží to kritérium, když se seznam projde pozpátku?** Tedy sedí množina věcí, které verze nárokuje, s množinou toho, co je v repozitáři otevřené — a je hranice záruk vyslovená tam, kde ji čtenář hledá?

Na rozdíl od auditu z 2026-08-15, který mířil na soudržnost dokumentace mezi sebou, tenhle míří na soudržnost **tvrzení o verzi** s **kódem a otevřenými položkami**. Nálezy jsou číslované po kapitolách. Co z nich plyne, je v kapitole 6.

## Zafixované verze a stav, ke kterému audit platí

Pracovní kopie ke dni 2026-08-21. Audit vznikl v Coworku, který na tomto stroji nemá shell, takže **stav gitu jsme neověřili** — commit, ke kterému audit platí, doplňuje Míra při zařazení; tvrzení o kódu platí k obsahu pracovní kopie k témuž dni.

Prošli jsme `open-items.md`, `architecture.md`, `requirements.md`, `decisions/README.md` a všech třicet čtyři rozhodnutí, oba `README.md` a centrální soubory verzí. Tvrzení o kódu jsme ověřovali čtením zdrojů, ne z dokumentace; co konkrétně, je v kapitole 5.

Verze frameworků jsou centrálně v `ORMConvertor/Directory.Packages.props`, cílová platforma v `Directory.Build.props`, pásmo SDK v `global.json` (rozhodnutí [034](../decisions/034-central-version-management.md)). Audit je nesnímkuje.

## 1. Nároky verze proti otevřeným položkám

Rozhodnutí 030 vyjmenovává, co verze **nárokuje**: F1–F6, F11, F14 v rozsahu vícesouborového vstupu, S1, S2, S3, S6, S7 a druhou větu S4. `open-items.md` k tomu říká vlastní pravidlo: *„Položka bez čísla do verze 1.0 nepatří."* Obojí je jednotlivě v pořádku. Průnik ale není prázdný: **tři otevřené položky nesou požadavek, který verze nárokuje, a přitom číslo nemají**, takže jsou podle vlastního pravidla souboru mimo vydání. To je přesně stav, který kritérium z 030 zakazuje — část, která není ani dodělaná, ani vyňatá.

### 1.1 Generovaná entita pro NHibernate deklaruje kolekci konkrétním typem

Kritické. Položka „Kolekční vlastnosti pro NHibernate se generují konkrétním typem" nese požadavek F3, a **F3 verze nárokuje**. Žádná z vyňatých oblastí rozhodnutí 030 ji nepokrývá: nejde o dědičnost, komponenty ani `<join>`, nejde o dotazovou větev, benchmarking ani Advisor.

Ověřeno v kódu: `CSharpTypeConvertor.CollectionName` vykresluje `List` pro `CollectionKind.List` i `Unspecified` a `HashSet` pro `Set`, a `NHibernateEntityBuilder.BuildPropertySignature` bere název typu bez úpravy z `CSharpTypeConvertor.ToString(langType)` — přidává jen `virtual`. Generovaná entita tedy nese `public virtual List<Order> Orders { get; set; }`, kdežto NHibernate 5.7.0 vyžaduje u perzistentní kolekce deklaraci rozhraním, protože vlastnost za běhu nahrazuje vlastní implementací.

Vada je neviditelná pro dnešní testy a je to vlastnost stupňů ověření, ne opomenutí: `NHibernateAcceptance.BuildSessionFactory` staví session factory nad konfigurací bez připojení (3. stupeň rozhodnutí [016](../decisions/016-generated-artifact-verification-levels.md)) a vazbu kolekce na CLR typ v té chvíli neprovádí. Selhání by se ukázalo až načtením entity, tedy na 4. stupni — a ten **nemá jediného zástupce**: `TestSchemaFixture.OpenConnection()` je v repozitáři i s popsanou hranicí transakce, ale nikdo ho nevolá (ověřeno hledáním v `Tests/`).

Je to tedy neplatný výstup pro platný vstup uvnitř zaručované oblasti — táž třída vady, podle které rozhodnutí 030 řadilo kolekce jako položku 2 a převodní tabulku typů jako 6.

### 1.2 Priorita zdrojů se nevynucuje u primárního klíče

Kritické. Položka nese F5 a S2; **verze nárokuje obojí**, a k tomu F14 v rozsahu vícesouborového vstupu, což je přesně to, co vadu činí dosažitelnou.

Zápisové cesty builderu vyplňují od rozhodnutí [017](../decisions/017-source-precedence-for-mapping-facts.md) jen prázdný fakt a odlišné pozdější tvrzení hlásí záznamem `Conflict`. `AddPrimaryKey` je z pravidla vyňaté — opakované volání klíč i s detaily strategie nahradí. Dvě mapovací XML NHibernate nad toutéž třídou, každé s jiným klíčem, tedy skončí u toho pozdějšího a převod o rozporu neřekne nic. Výsledek závisí na pořadí artefaktů, což je proti S2, a rozpor se nehlásí, což je proti F5.

Vyjmutí se na tenhle případ vztáhnout nedá: vícezdrojový vstup je součástí toho, co verze nárokuje, a `ConversionHandler` i `ConvertRequest` seznam zdrojů berou.

### 1.3 Deskriptor deklaruje vynucené členy, které nikdo kromě testu nečte

Závažné. Položka nese S2, který verze nárokuje. Ověřeno hledáním: `EnforcedMembers` a `EnforcedMembersFor` čte v celém řešení jedině `Tests/Combined/EnforcedMembersTest.cs`. Produkční kód deklaraci nepoužívá — každý builder má vlastní `BuildEnforcedMembers` a vypisuje si členy nezávisle (`virtual` v `BuildPropertySignature` u NHibernate, `[Serializable]` u klíčové třídy, `[Keyless]` u EF Core).

Rozhodnutí [009](../decisions/009-target-framework-descriptor.md) tuhle dělbu zvolilo záměrně a test je v ní tou vazbou, která deklaraci a emisi drží u sebe. Nález není, že je to špatně; nález je, že **záruka determinismu se opírá o pokrytí testem, a nikde není vysloveno, že se o něj opírá.** Rozejde-li se deklarace s emisí u členu nebo podmínky, kterou test nepokrývá, nikdo se to nedozví.

### 1.4 Položka je vedená jako práce, ačkoli je to rozhodnutí

Drobné, ale patří k 1.3. Táž položka je v `open-items.md` pod nadpisem *Otevřená práce*, a přitom její vlastní text končí větou *„Zbývá rozhodnout, jestli má vazbu držet test, nebo jestli si má builder vynucené členy z deskriptoru brát."* Podle úvodu souboru je rozlišení praktické — rozhodnutí končí souborem v `decisions/`, práce kódem — takže zařazení posílá čtenáře špatným směrem.

### 1.5 Zbylé otevřené položky s nárokem nekolidují

Pro úplnost, aby z nepřítomnosti nešlo usoudit na opomenutí. Devět zbylých položek kolizi nemá: čtyři říkají vyňatost vlastním textem (kontejnerová konfigurace, cílový dialekt, poddotazy a množinové operace, Advisor a benchmarking bez testů, sjednocení ADO.NET provideru), dvě jsou blokované frameworky mimo rozsah a jako takové je pojmenovává samo rozhodnutí 030 (kritérium pro širší čtení konvencí, vlastní precedence frameworku), jedna je krytá vyňatím Advisoru (build nativní knihovny pro Windows) a jedna je právě rozpracovaná (verze nástroje a značka vydání).

## 2. Hranice záruk je vyslovená na dvou místech a pokaždé jinak

Rozhodnutí 030 uložilo: *„Vyjmutí se vyslovuje jednou, na úrovni celé oblasti, v tomto rozhodnutí a v `architecture.md` §9."* První polovina platí, druhá jen zčásti.

### 2.1 §9 jmenuje jako hranici záruk jedinou ze sedmi vyňatých oblastí

Závažné. Odstavec „Hranice záruk verze 1.0" v §9 vyjmenovává dědičnost, komponenty a `<join>` v NHibernate mapování — a končí. Zbylé vyňaté oblasti buď v §9 vůbec nejsou, nebo jsou rozpuštěné v následujícím odstavci „Stručně", který popisuje **rozsah implementace**, ne hranici spolehnutí. Pro čtenáře jsou to dvě různá tvrzení: „tohle nástroj neumí" a „tohle sice v repozitáři je, ale nespoléhej na to". §9 je dnes rozlišuje jen u jedné oblasti ze sedmi.

### 2.2 Vyňatí Advisoru a benchmarkingu ze záruk v §9 chybí úplně

Kritické. Je to nejsilnější vyjmutí celého rozhodnutí 030 — verze kvůli němu **nenárokuje F15 ani T7 a nenárokuje první větu S4** — a v `architecture.md` po něm není stopa. §9 o Advisoru říká jen, že *„pracuje jen s Dapperem a EF Core"*, což je tvrzení o rozsahu, ne o záruce. Čtenář, který §9 otevře, se nedozví, že celá oblast stojí mimo to, na co verze slibuje spoleh.

Rozhodnutí 030 tenhle stav samo předvídalo: *„`architecture.md` §9 dostane hranici záruk. […] Píše se to až s prací, ke které patří, ne dopředu."* Prací, ke které to patří, je poslední položka vydání — tedy teď.

### 2.3 Podmínka u F4 a F6 není v §9

Závažné. Rozhodnutí 030 i `open-items.md` shodně nesou větu, že bez databáze v CI se databázově závislé testy dál přeskakují, a tedy že *kritéria F4 a F6 platí jen tam, kde běh s lokální databází skutečně proběhl*. Obě místa navíc říkají, že ta věta patří i do textu práce. V `architecture.md` — dokumentu, který jediný popisuje stav — není. §6.1 popisuje testovací databázi, §6.2 stupně ověření, ale podmíněnost dvou nárokovaných požadavků nikde.

### 2.4 Seznam toho, co verze nárokuje, nežije nikde mimo rozhodnutí

Závažné. Výčet F1–F6, F11, F14 (částečně), S1, S2, S3, S6, S7 a druhé věty S4 je jen v rozhodnutí 030. Podle rozhodnutí [007](../decisions/007-documentation-structure.md) ale rozhodnutí zaznamenává **volbu**, ne stav; stav odpovídá `architecture.md`. Nárok verze je tvrzení o stavu vydaného nástroje a jako takový nemá dnes žádného živého nositele — což je nepříjemné právě proto, že se na něj odvolávají S2 i S6, a že přesně tohle má podle popisu položky 19 říct `README.md`.

## 3. Oba `README.md` jsou zděděné z prototypu

Položka 19 jmenuje průchod `README.md` jako součást vydání. Rozsah je větší, než položka říká: soubory jsou dva a druhý z nich není zmíněný.

### 3.1 Kořenový `README.md` popisuje frontend, který neexistuje

Kritické pro vydání. Tabulka struktury repozitáře uvádí `ORMConvertor/` jako *„ASP.NET Core REST API + Angular frontend"*. Rozhodnutí [032](../decisions/032-frontend-as-static-pages-without-a-build.md) Angular odstranilo; `wwwroot` dnes drží ručně psané HTML, ES moduly a CSS bez buildu, s Pico CSS a highlight.js vendorovanými pod `wwwroot/vendor/`. Sekce „Getting started" navíc odkazuje na *„frontend compilation"*.

### 3.2 Kořenový `README.md` tvrdí jeden směr překladu dotazů

Kritické pro vydání. Stojí tam *„Query translation, currently one direction: EF Core LINQ → Dapper SQL."* Dotazová větev je hotová ve všech devíti směrech v rozsahu kategorií projekce, filtrace, join, agregace a řazení (rozhodnutí 022–027, `architecture.md` §9). Je to nejhorší jednotlivá věta obou souborů: podhodnocuje hotovou práci, o kterou se opírá celá dotazová část textu práce.

### 3.3 Roadmapa jmenuje jako budoucí práci to, co je hotové

Závažné. Ze čtyř bodů roadmapy jsou tři odbavené: pokrytí dotazových parserů a builderů pro všechny tři frameworky, vícesloupcové cizí klíče s N:M jako explicitní junction entitou (rozhodnutí 005 a 012) a doplňování z databázového katalogu (rozhodnutí 015). Jako budoucí zůstává rozšíření Advisoru a javový ekosystém.

### 3.4 `ORMConvertor/README.md` má celou sekci pro Angular build

Kritické pro vydání. Sekce „Frontend" popisuje `npm install` a `ng build` v adresáři `ORMConvertorAPI/frontend`, který v repozitáři není, a příkazy, které mažou a znovu zakládají `wwwroot` — tedy dnes zdrojový adresář frontendu. Návod je nejen neplatný, ale destruktivní. Popis Docker image v téže sekci mluví o multi-stage buildu, který *„compiles the Angular frontend"*.

Zbytek souboru zastaralý není: sekce PM2 popisuje `ecosystem.config.js`, Advisor prerequisites sedí s §8 architektury, spouštění a testy taky.

### 3.5 Kořenový `README.md` neříká, co je ze záruk vyňaté

Závažné. Odkazuje na `open-items.md` jako na místo, kde je *„current implementation status, including known limitations"*. To byl platný postup, dokud verze neexistovala; s vydáním je hranice záruk vlastnost verze, ne seznam zbývající práce. Podle popisu položky 19 má `README.md` říct, co nástroj v této verzi umí, co je z jeho záruk vyňaté a jak se spouští — dnes neříká ani jedno z prvních dvou.

## 4. Zbytky v pracovní kopii

### 4.1 `ORMConvertor/package-lock.json`

Drobné. Dvacet sedm bajtů obsahu `{ "lockfileVersion": 1 }`, pozůstatek po Angularu. Po rozhodnutí 032 v repozitáři není nic, co by npm potřebovalo, a soubor u kořene řešení tvrdí opak.

### 4.2 Ochrana proměnné `Version` v `ecosystem.config.js` je jen v komentáři

Drobné, ale s termínem splatnosti. `ecosystem.config.js` vynuluje proměnné `version` i `Version`, aby zděděná proměnná prostředí neprosákla jako vlastnost MSBuildu do `dotnet run` a nepřepsala verzi sestavení. Dnes je to opatření naprázdno — `Directory.Build.props` nese jen `TargetFramework`, `ImplicitUsings` a `Nullable`, žádnou verzi (ověřeno). Jakmile podle položky 19 verze do centrálních vlastností přibude, stane se ta ochrana nosnou, a její jediné vysvětlení bude komentář v konfiguračním souboru procesního manažera. Patří do `architecture.md` k popisu nasazení.

## 5. Co bylo ověřeno proti kódu

Aby bylo zřejmé, která tvrzení tohoto auditu stojí na zdrojích a která na dokumentaci.

- `Common/Convertors/CSharpTypeConvertor.cs` — `CollectionName` vrací `List` / `HashSet`, tedy konkrétní typy, pro všechny cíle (nález 1.1).
- `NHibernateWrappers/NHibernateEntityBuilder.cs` — `BuildPropertySignature` přebírá název typu z `CSharpTypeConvertor.ToString` a doplňuje jen `virtual`; kolekci nepřepisuje (nález 1.1).
- `Tests/Verification/NHibernateAcceptance.cs` — 3. stupeň staví session factory bez připojení, entitu nenačítá (nález 1.1).
- `Tests/Database/TestSchemaFixture.cs` — `OpenConnection()` nemá v `Tests/` volajícího; 4. stupeň nemá zástupce (nález 1.1).
- Hledání `EnforcedMembers` napříč `Model`, `Common`, `AbstractWrappers`, všemi třemi wrappery a `Tests` — jediné čtení je `EnforcedMembersTest` (nález 1.3).
- `ORMConvertor/Directory.Build.props` — neobsahuje žádnou vlastnost verze; premisa položky 19 potvrzena (nález 4.2).
- `ORMConvertor/ORMConvertorAPI/wwwroot` — statické stránky, `vendor/pico-2.1.1` a `vendor/highlightjs-11.12.0`, žádný adresář `frontend`, žádný build (nálezy 3.1 a 3.4).
- `ORMConvertor/package-lock.json` — obsah ověřen (nález 4.1).

Neověřeno: stav gitu a průchodnost testů, protože Cowork na tomto stroji nemá shell.

## 6. Co z auditu plyne

### Opravy

**`architecture.md` §9 přepsat na jediné místo, kde je hranice záruk vyslovená.** Zrcadlit sedm vyňatých oblastí rozhodnutí 030 jedna ku jedné, oddělit je od popisu rozsahu implementace a doplnit obě chybějící tvrzení: vyňatí Advisoru a benchmarkingu vcelku i s tím, že verze nenárokuje F15, T7 ani první větu S4, a podmíněnost F4 a F6 během s lokální databází (nálezy 2.1, 2.2, 2.3).

**Do `architecture.md` doplnit, co verze nárokuje.** Seznam požadavků, na které verze slibuje spoleh, je dnes jen v rozhodnutí 030, tedy v dokumentu, který podle rozhodnutí 007 zaznamenává volbu, ne stav (nález 2.4).

**Kořenový `README.md` projít celý.** Odstranit Angular ze struktury i z „Getting started", opravit větu o jednosměrném překladu dotazů na devět směrů v pěti kategoriích, přeškrtnout tři odbavené body roadmapy a doplnit, co je ze záruk vyňaté (nálezy 3.1, 3.2, 3.3, 3.5).

**`ORMConvertor/README.md` — vyřadit sekci „Frontend"** a nahradit ji popisem statických stránek bez buildu; opravit popis Docker image, který mluví o kompilaci Angularu. Zbytek souboru je platný (nález 3.4).

**Smazat `ORMConvertor/package-lock.json`** (nález 4.1).

**Přesunout položku o vynucených členech z „Otevřené práce" do „Otevřená rozhodnutí"** v `open-items.md` (nález 1.4).

**Do `architecture.md` doplnit důvod nulování `Version` v `ecosystem.config.js`**, spolu s prací na verzování sestavení (nález 4.2).

### Potřebná rozhodnutí

**Kolekce v generované entitě pro NHibernate (nález 1.1).** Buď dodělat — vykreslit `IList<T>` / `ISet<T>` v NHibernate builderu a přepsat inicializátor, protože `= new()` se nad rozhraním nepřeloží a `= []` nad `ISet<T>` také ne —, nebo vyslovit vyňatou oblast, která to pokryje. Vyňatí ale nemá kolem čeho vést hranici: neplatná je entita, ne okrajový prvek mapování, a verze nárokuje F3. Doporučení je proto dodělat; sdíleného převodu jazykových typů se to netýká, náprava patří builderu, stejně jako vynucené `virtual`.

**Primární klíč a priorita zdrojů (nález 1.2).** Rozhodnout, jestli má `AddPrimaryKey` spadnout pod pravidlo rozhodnutí 017 celé, a čím nahradit kontrakt „opakované volání klíč nahrazuje" tak, aby na něm nedoplatila vnitřní volání builderu (syntéza junction entit) ani detaily strategie zapisované k částem až po definici klíče. Alternativou je zúžit nárok verze u F5 a S2 na jeden mapovací artefakt na framework — což je ale v rozporu s tím, že verze nárokuje vícesouborový vstup pod F14.

**Vazba deskriptoru a emise u vynucených členů (nález 1.3).** Rozhodnout, jestli vazbu drží test, nebo si builder členy z deskriptoru bere — s vědomím, že párování podle názvu vrací riziko překlepu, kterým rozhodnutí 009 tuhle variantu zamítlo. Minimální varianta je vyslovit v `architecture.md`, že determinismus emise vynucených členů stojí na pokrytí testem, a doplnit chybějící kombinace podmínek.

Všechna tři rozhodnutí patří **před** nastavení verze: dvě první proto, že se týkají nárokovaných požadavků, třetí proto, že bez něj se S2 opírá o vazbu, kterou nikdo nevyslovil.

### Delší horizont

Nic z tohoto auditu nepřidává práci mimo vydání. Nálezy 1.1 až 1.3 jsou uzavřené otázky uvnitř nárokované oblasti, kapitoly 2 až 4 jsou dokumentace a úklid, tedy náplň položky 19.

Za pozornost stojí jediná soustavná vada, kterou audit našel napříč nálezy: **hranice záruk se dobře píše v rozhodnutí a špatně udržuje v popisu stavu.** Rozhodnutí 030 svou hranici vyslovilo úplně a přesně; `architecture.md` z ní po devatenácti položkách nese jednu sedminu, a to jen proto, že ji rozhodnutí uložilo psát „až s prací, ke které patří". U dalšího vydání se tomu dá předejít tím, že se hranice zapíše do `architecture.md` v témž kroku, který ji rozhodne, a s prací se doplňují jen konkrétní odstavce.
