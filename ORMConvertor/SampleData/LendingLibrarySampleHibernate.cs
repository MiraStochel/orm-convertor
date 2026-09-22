namespace SampleData;

/// <summary>
/// The lending library: the larger Java-to-.NET example of the explanatory page (decision
/// 099), five Hibernate entities and five JPQL queries. The domain is chosen for what a
/// single class never shows: a many-to-many over @JoinTable that the target receives as an
/// explicit junction entity (decision 005), a composite key through @IdClass rendered flat
/// with the identity members the target enforces (decision 006), a composite foreign key
/// whose column pairs keep their order (decision 012), a sequence with its parameters
/// (decision 020), a version column and national columns.
///
/// The tables live in a schema of their own, Lending, which the sample database of the
/// container does not have - the same reason the order book has Ordering.
/// </summary>
public static class LendingLibrarySampleHibernate
{
    /// <summary>
    /// A sequence the source names and sizes, a unique constraint, a primitive column, the
    /// owning side of the many-to-many, and a property the database never sees.
    /// </summary>
    public const string Book = """
        package Library;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.JoinTable;
        import jakarta.persistence.ManyToMany;
        import jakarta.persistence.OneToMany;
        import jakarta.persistence.SequenceGenerator;
        import jakarta.persistence.Table;
        import jakarta.persistence.Transient;
        import jakarta.persistence.UniqueConstraint;
        import java.util.ArrayList;
        import java.util.HashSet;
        import java.util.List;
        import java.util.Set;
        import org.hibernate.annotations.Nationalized;

        @Entity
        @Table(name = "Books", schema = "Lending",
               uniqueConstraints = @UniqueConstraint(name = "UQ_Books_Isbn", columnNames = {"Isbn"}))
        public class Book {

            @Id
            @GeneratedValue(strategy = GenerationType.SEQUENCE, generator = "book_numbers")
            @SequenceGenerator(name = "book_numbers", sequenceName = "BookNumbers", schema = "Lending", allocationSize = 20)
            @Column(name = "BookId")
            private Long BookId;

            @Column(name = "Isbn", length = 13, nullable = false)
            private String Isbn;

            @Nationalized
            @Column(name = "Title", length = 300, nullable = false)
            private String Title;

            @Column(name = "PublishedYear")
            private short PublishedYear;

            @Column(name = "PageCount")
            private Integer PageCount;

            @ManyToMany
            @JoinTable(name = "BookAuthors", schema = "Lending",
                       joinColumns = @JoinColumn(name = "BookId"),
                       inverseJoinColumns = @JoinColumn(name = "AuthorId"))
            private Set<Author> Authors = new HashSet<>();

            @OneToMany(mappedBy = "Book")
            private List<BookCopy> Copies = new ArrayList<>();

            @Transient
            private String DisplayTitle;

            public Long getBookId() { return BookId; }
            public void setBookId(Long value) { this.BookId = value; }
            public String getIsbn() { return Isbn; }
            public void setIsbn(String value) { this.Isbn = value; }
            public String getTitle() { return Title; }
            public void setTitle(String value) { this.Title = value; }
            public short getPublishedYear() { return PublishedYear; }
            public void setPublishedYear(short value) { this.PublishedYear = value; }
            public Integer getPageCount() { return PageCount; }
            public void setPageCount(Integer value) { this.PageCount = value; }
            public Set<Author> getAuthors() { return Authors; }
            public void setAuthors(Set<Author> value) { this.Authors = value; }
            public List<BookCopy> getCopies() { return Copies; }
            public void setCopies(List<BookCopy> value) { this.Copies = value; }
            public String getDisplayTitle() { return DisplayTitle; }
            public void setDisplayTitle(String value) { this.DisplayTitle = value; }
        }
        """;

    /// <summary>The inverse side of the many-to-many, and a fixed-length column stated literally.</summary>
    public const string Author = """
        package Library;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.ManyToMany;
        import jakarta.persistence.Table;
        import java.util.HashSet;
        import java.util.Set;
        import org.hibernate.annotations.Nationalized;

        @Entity
        @Table(name = "Authors", schema = "Lending")
        public class Author {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "AuthorId")
            private Integer AuthorId;

            @Nationalized
            @Column(name = "FullName", length = 200, nullable = false)
            private String FullName;

            @Column(name = "Country", length = 2, columnDefinition = "char(2)")
            private String Country;

            @ManyToMany(mappedBy = "Authors")
            private Set<Book> Books = new HashSet<>();

            public Integer getAuthorId() { return AuthorId; }
            public void setAuthorId(Integer value) { this.AuthorId = value; }
            public String getFullName() { return FullName; }
            public void setFullName(String value) { this.FullName = value; }
            public String getCountry() { return Country; }
            public void setCountry(String value) { this.Country = value; }
            public Set<Book> getBooks() { return Books; }
            public void setBooks(Set<Book> value) { this.Books = value; }
        }
        """;

    /// <summary>
    /// The composite key: the book and the number of its copy, declared through @IdClass. The
    /// first part is at the same time the foreign key to the book, so the relation is the
    /// one kept from writing the column.
    /// </summary>
    public const string BookCopy = """
        package Library;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.Id;
        import jakarta.persistence.IdClass;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.Table;
        import java.io.Serializable;
        import java.time.LocalDate;
        import java.util.Objects;

        @Entity
        @IdClass(BookCopy.BookCopyId.class)
        @Table(name = "BookCopies", schema = "Lending")
        public class BookCopy {

            @Id
            @Column(name = "BookId")
            private Long BookId;

            @Id
            @Column(name = "CopyNumber")
            private Short CopyNumber;

            @ManyToOne(optional = false)
            @JoinColumn(name = "BookId", referencedColumnName = "BookId", insertable = false, updatable = false)
            private Book Book;

            @Column(name = "AcquiredOn", nullable = false)
            private LocalDate AcquiredOn;

            @Column(name = "ShelfMark", length = 20)
            private String ShelfMark;

            public Long getBookId() { return BookId; }
            public void setBookId(Long value) { this.BookId = value; }
            public Short getCopyNumber() { return CopyNumber; }
            public void setCopyNumber(Short value) { this.CopyNumber = value; }
            public Book getBook() { return Book; }
            public void setBook(Book value) { this.Book = value; }
            public LocalDate getAcquiredOn() { return AcquiredOn; }
            public void setAcquiredOn(LocalDate value) { this.AcquiredOn = value; }
            public String getShelfMark() { return ShelfMark; }
            public void setShelfMark(String value) { this.ShelfMark = value; }

            public static class BookCopyId implements Serializable {
                private Long BookId;
                private Short CopyNumber;

                @Override
                public boolean equals(Object other) {
                    return other instanceof BookCopyId that
                        && Objects.equals(BookId, that.BookId)
                        && Objects.equals(CopyNumber, that.CopyNumber);
                }

                @Override
                public int hashCode() {
                    return Objects.hash(BookId, CopyNumber);
                }
            }
        }
        """;

    /// <summary>
    /// A composite foreign key over @JoinColumns, the foreign-key columns kept as plain
    /// attributes beside the relations - the shape a JPQL query needs to name them -, a
    /// version column, and fractional seconds.
    /// </summary>
    public const string Loan = """
        package Library;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.JoinColumns;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.Table;
        import jakarta.persistence.Version;
        import java.time.LocalDate;
        import java.time.LocalDateTime;

        @Entity
        @Table(name = "Loans", schema = "Lending")
        public class Loan {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "LoanId")
            private Long LoanId;

            @Column(name = "BookId", nullable = false)
            private Long BookId;

            @Column(name = "CopyNumber", nullable = false)
            private Short CopyNumber;

            @ManyToOne(optional = false)
            @JoinColumns({
                @JoinColumn(name = "BookId", referencedColumnName = "BookId", insertable = false, updatable = false),
                @JoinColumn(name = "CopyNumber", referencedColumnName = "CopyNumber", insertable = false, updatable = false)
            })
            private BookCopy Copy;

            @Column(name = "MemberId", nullable = false)
            private Integer MemberId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "MemberId", referencedColumnName = "MemberId", insertable = false, updatable = false)
            private Member Member;

            @Column(name = "LoanedAt", secondPrecision = 3, nullable = false)
            private LocalDateTime LoanedAt;

            @Column(name = "DueOn", nullable = false)
            private LocalDate DueOn;

            @Column(name = "ReturnedAt", secondPrecision = 3)
            private LocalDateTime ReturnedAt;

            @Version
            @Column(name = "Revision")
            private int Revision;

            public Long getLoanId() { return LoanId; }
            public void setLoanId(Long value) { this.LoanId = value; }
            public Long getBookId() { return BookId; }
            public void setBookId(Long value) { this.BookId = value; }
            public Short getCopyNumber() { return CopyNumber; }
            public void setCopyNumber(Short value) { this.CopyNumber = value; }
            public BookCopy getCopy() { return Copy; }
            public void setCopy(BookCopy value) { this.Copy = value; }
            public Integer getMemberId() { return MemberId; }
            public void setMemberId(Integer value) { this.MemberId = value; }
            public Member getMember() { return Member; }
            public void setMember(Member value) { this.Member = value; }
            public LocalDateTime getLoanedAt() { return LoanedAt; }
            public void setLoanedAt(LocalDateTime value) { this.LoanedAt = value; }
            public LocalDate getDueOn() { return DueOn; }
            public void setDueOn(LocalDate value) { this.DueOn = value; }
            public LocalDateTime getReturnedAt() { return ReturnedAt; }
            public void setReturnedAt(LocalDateTime value) { this.ReturnedAt = value; }
            public int getRevision() { return Revision; }
            public void setRevision(int value) { this.Revision = value; }
        }
        """;

    /// <summary>A unique column stated on the column itself, and the inverse side of the loans.</summary>
    public const string Member = """
        package Library;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.OneToMany;
        import jakarta.persistence.Table;
        import java.time.LocalDate;
        import java.util.ArrayList;
        import java.util.List;
        import org.hibernate.annotations.Nationalized;

        @Entity
        @Table(name = "Members", schema = "Lending")
        public class Member {

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "MemberId")
            private Integer MemberId;

            @Nationalized
            @Column(name = "GivenName", length = 100, nullable = false)
            private String GivenName;

            @Nationalized
            @Column(name = "Surname", length = 100, nullable = false)
            private String Surname;

            @Column(name = "Email", length = 254, nullable = false, unique = true)
            private String Email;

            @Column(name = "JoinedOn", nullable = false)
            private LocalDate JoinedOn;

            @OneToMany(mappedBy = "Member")
            private List<Loan> Loans = new ArrayList<>();

            public Integer getMemberId() { return MemberId; }
            public void setMemberId(Integer value) { this.MemberId = value; }
            public String getGivenName() { return GivenName; }
            public void setGivenName(String value) { this.GivenName = value; }
            public String getSurname() { return Surname; }
            public void setSurname(String value) { this.Surname = value; }
            public String getEmail() { return Email; }
            public void setEmail(String value) { this.Email = value; }
            public LocalDate getJoinedOn() { return JoinedOn; }
            public void setJoinedOn(LocalDate value) { this.JoinedOn = value; }
            public List<Loan> getLoans() { return Loans; }
            public void setLoans(List<Loan> value) { this.Loans = value; }
        }
        """;

    /// <summary>An entity join with an ON condition, a null test and a parameter.</summary>
    public const string OverdueLoansQuery = """
        select m.Surname as Surname, m.Email as Email, l.DueOn as DueOn
        from Loan l
        join Member m on m.MemberId = l.MemberId
        where l.ReturnedAt is null and l.DueOn < :today
        order by l.DueOn asc, m.Surname asc
        """;

    /// <summary>A grouping with a filter over the groups, both sides of it parameters.</summary>
    public const string ActiveMembersQuery = """
        select l.MemberId as MemberId, count(l) as Loans
        from Loan l
        where l.LoanedAt >= :since
        group by l.MemberId
        having count(l) >= :minimumLoans
        order by l.MemberId asc
        """;

    /// <summary>A correlated NOT EXISTS beside a scalar subquery over the same table.</summary>
    public const string NeverBorrowedQuery = """
        select b
        from Book b
        where not exists (select l from Loan l where l.BookId = b.BookId)
          and b.PageCount > (select avg(x.PageCount) from Book x)
        order by b.Title asc
        """;

    /// <summary>
    /// A collection parameter, a between the target writes as two comparisons (rule Q14), and
    /// a page whose bounds the caller supplies (decision 085).
    /// </summary>
    public const string CopiesAcquiredQuery = """
        select c
        from BookCopy c
        where c.BookId in :bookIds and c.AcquiredOn between :fromDate and :toDate
        order by c.BookId asc, c.CopyNumber asc
        limit :take offset :skip
        """;

    /// <summary>A distinct projection filtered by a list of values.</summary>
    public const string AuthorsFromRegionQuery = """
        select distinct a.FullName as FullName, a.Country as Country
        from Author a
        where a.Country in ('CZ', 'SK', 'PL')
        order by a.FullName asc
        """;
}
