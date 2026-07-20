# 002 – Generování vztahů N:M do cílových frameworků

**Kontext:** navazuje na `001-query-model-and-composite-keys.md` §4.5, který v IR reprezentuje N:M jako spojovací `EntityMap` s `IsJunctionTable = true` a dvěma `Owning` `ManyToOne` relacemi. Tento dokument řeší, jak se taková struktura **generuje** do cílových frameworků – 001 popisuje model, 002 popisuje výstup.

**Pokrývá požadavky:** část F3 (N:M přes spojovací tabulku) z [../requirements.md](../requirements.md); s ohledem na F10 (cross-ecosystem překlad).

**Stav:** rozhodnuto, implementace probíhá po částech (viz `../changelog.md`).

---

## 1. Problém

Relační N:M vazba nemá v objektovém modelu jednoznačnou reprezentaci – frameworky ji vyjadřují různě. Pro EF Core (první cíl) existují dvě hlavní varianty a je třeba zvolit výchozí. Rozhodnutí má přesah i do dalších frameworků (NHibernate, později Hibernate/JPA), protože určuje, jestli spojovací entita v IR po překladu zůstává viditelná, nebo se rozpouští.

## 2. Zvažované varianty

### 2.1 Varianta A – Skip navigation (junction bez třídy)

EF Core 5+ umí M:N bez explicitní třídy spojovací tabulky: na obou entitách je jen navigační kolekce protistrany, tabulku spravuje EF Core interně.

```csharp
public class Student
{
    public required int Id { get; set; }
    public List<Course> Courses { get; set; } = [];
}

public class Course
{
    public required int Id { get; set; }
    public List<Student> Students { get; set; } = [];
}
```

Název tabulky a sloupců se určuje fluent konfigurací v `DbContext.OnModelCreating` (`HasMany().WithMany().UsingEntity(...)`); bez ní si je EF Core odhadne z konvence.

- **Výhody:** idiomatický, čistý kód entit.
- **Nevýhody:** informace se dělí mezi entity a `DbContext` (který dnešní builder negeneruje); spojovací tabulka je v kódu skrytá, takže se při zpětném/cross-ecosystem překladu ztrácí, pokud ji parser nezrekonstruuje; „bohatou" spojovací tabulku (extra sloupce) skip navigation neuveze – potřebovala by druhou cestu.

### 2.2 Varianta B – Explicitní junction entita

Spojovací tabulka je běžná entita se dvěma `ManyToOne` vztahy a kompozitním klíčem; „velké" entity mají kolekci na tuto spojovací entitu.

```csharp
[Table("StudentCourse", Schema = "dbo")]
[PrimaryKey(nameof(StudentId), nameof(CourseId))]
public class StudentCourse
{
    public required int StudentId { get; set; }
    public required int CourseId { get; set; }
    public Student Student { get; set; }
    public Course Course { get; set; }
}

public class Student
{
    public required int Id { get; set; }
    public List<StudentCourse> StudentCourses { get; set; } = [];
}
```

- **Výhody:** přímý obraz IR modelu (§4.5); veškerá informace v entitách, žádný `DbContext` navíc; spojovací entita zůstává v modelu viditelná → symetrický obousměrný i cross-ecosystem překlad; „čistá" i „bohatá" spojovací tabulka stejným kódem (bohatá jen přidá sloupce).
- **Nevýhody:** proti idiomatickému EF Core upovídanější – programátor by čistou M:N spíš psal přes skip navigation.

## 3. Rozhodnutí

**Zvolena varianta B (explicitní junction entita) jako výchozí a jediná povinná cesta.**

Zdůvodnění:

1. **Symetrie s IR modelem.** 001 §4.5 reprezentuje N:M jako junction `EntityMap` + dvě `ManyToOne` relace. Varianta B tuto strukturu promítá jedna k jedné; varianta A ji při generování rozpouští, což je krok stranou od zvoleného modelu.
2. **Obousměrnost a cross-ecosystem (F3, F10).** Spojovací tabulka je nejnižší společný jmenovatel N:M napříč všemi frameworky. Udržení junction entity viditelné v IR umožňuje generovat kterýkoli cílový tvar bez rekonstrukce; skrytí (varianta A) by před každým dalším překladem vyžadovalo entitu znovu odvodit.
3. **Bohatá spojovací tabulka (F3, E1).** Reálné spojovací tabulky mívají extra sloupce a explicitní entitu vyžadují i v EF Core. Varianta B je řeší stejným kódem jako čistou M:N; varianta A by potřebovala dvě cesty a rozhodovací logiku mezi nimi.

## 4. Role `IsJunctionTable`

`IsJunctionTable = true` zůstává v modelu jako **volitelný signál**, že spojovací tabulka je „čistá" (jen FK sloupce). Výchozí generování ho nevyžaduje a vždy vytvoří explicitní entitu. Jako budoucí rozšíření může builder pod tímto signálem nabídnout „zploštění" do skip navigation (varianta A) pro frameworky, které to umí – ale jako opt-in, ne default.

## 5. Dopad na ostatní frameworky

- **NHibernate:** už dnes umí `<many-to-many>` v kolekci (viz `NHibernateEntityBuilder`), i explicitní entitu se dvěma `<many-to-one>`. Explicitní junction entita (varianta B) je konzistentní s tím, jak builder generuje ostatní vztahy po kroku 4.
- **Dapper:** M:N řeší ručními joiny; junction entita jako POCO odpovídá tomu, jak by se to psalo ručně. Klíče/vztahy Dapper negeneruje (rozhodnutí 7.4 v 001) – ponese strukturované varování, až vznikne diagnostika F11.
- **Hibernate / EclipseLink (F7, F9, budoucí):** JPA nabízí `@ManyToMany` + `@JoinTable` i `@Embeddable` junction entitu; explicitní entita z IR se do obou tvarů přeloží, volba tvaru je opět generační rozhodnutí, které tento dokument otevírá pro budoucí rozšíření.

## 6. Otevřené body pro implementaci

1. **Plnění `ColumnPairs`.** Junction generování stojí na naplněných `ColumnPairs` (`StudentId↔Id`, `CourseId↔Id`), které se dnes nikde neplní (viz 001, krok 4). Do vyřešení se skládá junction entita ručně přes builder API v testech; automatické plnění navazuje na F4/F5 (metadata z DB) a parser-side syntézu.
2. **Detekce N:M na vstupu.** Žádný parser dnes N:M nedetekuje do podoby junction entity (EF Core M:N kolekce vůbec, NHibernate `<many-to-many>` jako jedna Inverse relace). Parser-side syntéza je samostatný úkol (cross-entity analýza), řešený mimo tento dokument.
3. **Vícesloupcový FK rendering.** Junction s kompozitním FK potřebuje generovat víc sloupců na vztah – navazuje na `ColumnPairs` z bodu 1.