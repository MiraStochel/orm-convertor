namespace SampleData;

/// <summary>
/// A clinic's appointment scheduling as a Spring Boot project with Hibernate keeps it: five
/// Java files in which the mapping and the queries are a small part of the text. The entities
/// carry constants, static factories, business methods, a lifecycle callback and equals and
/// hashCode; the service and the report job are mostly validation, logging, streams over what
/// a query already loaded, records the code speaks in and an interface it calls - each handing
/// JPQL, and once native SQL, to the EntityManager somewhere in the middle. The example shows
/// what the tool takes out of such files (decision 111): every entity class and every place a
/// query is handed over (decision 109), its parameters bound by name and its page set on the
/// query object; and what it leaves where it is - methods, constructors, nested records and
/// interfaces, enums, and the code around the queries.
///
/// The tables live in a schema of their own, Clinic, as the other larger samples keep theirs.
/// </summary>
public static class ClinicSampleHibernate
{
    /// <summary>A patient: the mapping in annotations, a validation annotation the tool does not read, and behaviour in methods.</summary>
    public const string Patient = """
        package clinic.domain;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.OneToMany;
        import jakarta.persistence.Table;
        import jakarta.persistence.Version;
        import jakarta.validation.constraints.Email;
        import java.time.LocalDate;
        import java.time.Period;
        import java.util.ArrayList;
        import java.util.List;
        import java.util.Objects;
        import org.hibernate.annotations.Nationalized;

        @Entity
        @Table(name = "Patients", schema = "Clinic")
        public class Patient {

            public static final int ADULT_AGE = 18;

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "PatientId")
            private Long PatientId;

            @Nationalized
            @Column(name = "FullName", length = 200, nullable = false)
            private String FullName;

            @Email
            @Column(name = "Email", length = 254, nullable = false, unique = true)
            private String Email;

            @Column(name = "Phone", length = 30)
            private String Phone;

            @Column(name = "BirthDate", nullable = false)
            private LocalDate BirthDate;

            @Column(name = "InsuranceNumber", length = 20)
            private String InsuranceNumber;

            @Version
            @Column(name = "Revision")
            private int Revision;

            @OneToMany(mappedBy = "Patient")
            private List<Appointment> Appointments = new ArrayList<>();

            protected Patient() {
            }

            public Patient(String fullName, String email, LocalDate birthDate) {
                this.FullName = Objects.requireNonNull(fullName, "fullName").strip();
                this.Email = Objects.requireNonNull(email, "email").toLowerCase();
                this.BirthDate = Objects.requireNonNull(birthDate, "birthDate");
            }

            public int ageOn(LocalDate day) {
                return Period.between(BirthDate, day).getYears();
            }

            public boolean isMinorOn(LocalDate day) {
                return ageOn(day) < ADULT_AGE;
            }

            public Long getPatientId() { return PatientId; }
            public void setPatientId(Long value) { this.PatientId = value; }
            public String getFullName() { return FullName; }
            public void setFullName(String value) { this.FullName = value; }
            public String getEmail() { return Email; }
            public void setEmail(String value) { this.Email = value; }
            public String getPhone() { return Phone; }
            public void setPhone(String value) { this.Phone = value; }
            public LocalDate getBirthDate() { return BirthDate; }
            public void setBirthDate(LocalDate value) { this.BirthDate = value; }
            public String getInsuranceNumber() { return InsuranceNumber; }
            public void setInsuranceNumber(String value) { this.InsuranceNumber = value; }
            public List<Appointment> getAppointments() { return Appointments; }
            public void setAppointments(List<Appointment> value) { this.Appointments = value; }

            @Override
            public boolean equals(Object other) {
                return other instanceof Patient that && PatientId != null && PatientId.equals(that.PatientId);
            }

            @Override
            public int hashCode() {
                return getClass().hashCode();
            }

            @Override
            public String toString() {
                return "Patient[" + PatientId + ", " + FullName + "]";
            }
        }
        """;

    /// <summary>A doctor: a primitive flag, a short code and a method that decides what the clinic offers.</summary>
    public const string Doctor = """
        package clinic.domain;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.Table;
        import java.util.Set;

        @Entity
        @Table(name = "Doctors", schema = "Clinic")
        public class Doctor {

            private static final Set<String> SURGICAL = Set.of("SURGERY", "ORTHOPEDICS");

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "DoctorId")
            private Long DoctorId;

            @Column(name = "FullName", length = 200, nullable = false)
            private String FullName;

            @Column(name = "Specialty", length = 40, nullable = false)
            private String Specialty;

            @Column(name = "Room", length = 10, nullable = false)
            private String Room;

            @Column(name = "Active", nullable = false)
            private boolean Active;

            @Column(name = "SlotMinutes", nullable = false)
            private int SlotMinutes;

            protected Doctor() {
            }

            public boolean isSurgeon() {
                return SURGICAL.contains(Specialty);
            }

            public int slotsPerDay(int openMinutes) {
                return SlotMinutes == 0 ? 0 : openMinutes / SlotMinutes;
            }

            public Long getDoctorId() { return DoctorId; }
            public void setDoctorId(Long value) { this.DoctorId = value; }
            public String getFullName() { return FullName; }
            public void setFullName(String value) { this.FullName = value; }
            public String getSpecialty() { return Specialty; }
            public void setSpecialty(String value) { this.Specialty = value; }
            public String getRoom() { return Room; }
            public void setRoom(String value) { this.Room = value; }
            public boolean isActive() { return Active; }
            public void setActive(boolean value) { this.Active = value; }
            public int getSlotMinutes() { return SlotMinutes; }
            public void setSlotMinutes(int value) { this.SlotMinutes = value; }
        }
        """;

    /// <summary>
    /// An appointment: two foreign keys kept as plain attributes beside their relations, a
    /// nested enum the code uses, state names as constants, a static factory, a lifecycle
    /// callback and the transitions between states.
    /// </summary>
    public const string Appointment = """
        package clinic.domain;

        import jakarta.persistence.Column;
        import jakarta.persistence.Entity;
        import jakarta.persistence.GeneratedValue;
        import jakarta.persistence.GenerationType;
        import jakarta.persistence.Id;
        import jakarta.persistence.JoinColumn;
        import jakarta.persistence.ManyToOne;
        import jakarta.persistence.PrePersist;
        import jakarta.persistence.Table;
        import java.time.LocalDateTime;

        @Entity
        @Table(name = "Appointments", schema = "Clinic")
        public class Appointment {

            public static final String BOOKED = "BOOKED";
            public static final String CANCELLED = "CANCELLED";
            public static final String DONE = "DONE";
            public static final String NO_SHOW = "NO_SHOW";

            public enum Kind { CHECKUP, FOLLOW_UP, PROCEDURE }

            @Id
            @GeneratedValue(strategy = GenerationType.IDENTITY)
            @Column(name = "AppointmentId")
            private Long AppointmentId;

            @Column(name = "PatientId", nullable = false)
            private Long PatientId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "PatientId", insertable = false, updatable = false)
            private Patient Patient;

            @Column(name = "DoctorId", nullable = false)
            private Long DoctorId;

            @ManyToOne(optional = false)
            @JoinColumn(name = "DoctorId", insertable = false, updatable = false)
            private Doctor Doctor;

            @Column(name = "StartsAt", nullable = false)
            private LocalDateTime StartsAt;

            @Column(name = "Minutes", nullable = false)
            private int Minutes;

            @Column(name = "Room", length = 10, nullable = false)
            private String Room;

            @Column(name = "Status", length = 12, nullable = false)
            private String Status;

            @Column(name = "Reason", length = 500)
            private String Reason;

            @Column(name = "CreatedAt", nullable = false)
            private LocalDateTime CreatedAt;

            protected Appointment() {
            }

            public static Appointment book(long patientId, Doctor doctor, LocalDateTime start, String reason) {
                if (!doctor.isActive()) {
                    throw new IllegalStateException("Dr " + doctor.getFullName() + " takes no appointments");
                }
                Appointment appointment = new Appointment();
                appointment.PatientId = patientId;
                appointment.DoctorId = doctor.getDoctorId();
                appointment.StartsAt = start;
                appointment.Minutes = doctor.getSlotMinutes();
                appointment.Room = doctor.getRoom();
                appointment.Status = BOOKED;
                appointment.Reason = reason == null || reason.isBlank() ? null : reason.strip();
                return appointment;
            }

            @PrePersist
            void onCreate() {
                CreatedAt = LocalDateTime.now();
            }

            public void cancel() {
                if (!BOOKED.equals(Status)) {
                    throw new IllegalStateException("Only a booked appointment can be cancelled, this one is " + Status);
                }
                Status = CANCELLED;
            }

            public void markNoShow() {
                Status = NO_SHOW;
            }

            public LocalDateTime endsAt() {
                return StartsAt.plusMinutes(Minutes);
            }

            public boolean overlaps(LocalDateTime start, int minutes) {
                return start.isBefore(endsAt()) && StartsAt.isBefore(start.plusMinutes(minutes));
            }

            public Long getAppointmentId() { return AppointmentId; }
            public void setAppointmentId(Long value) { this.AppointmentId = value; }
            public Long getPatientId() { return PatientId; }
            public void setPatientId(Long value) { this.PatientId = value; }
            public Patient getPatient() { return Patient; }
            public void setPatient(Patient value) { this.Patient = value; }
            public Long getDoctorId() { return DoctorId; }
            public void setDoctorId(Long value) { this.DoctorId = value; }
            public Doctor getDoctor() { return Doctor; }
            public void setDoctor(Doctor value) { this.Doctor = value; }
            public LocalDateTime getStartsAt() { return StartsAt; }
            public void setStartsAt(LocalDateTime value) { this.StartsAt = value; }
            public int getMinutes() { return Minutes; }
            public void setMinutes(int value) { this.Minutes = value; }
            public String getRoom() { return Room; }
            public void setRoom(String value) { this.Room = value; }
            public String getStatus() { return Status; }
            public void setStatus(String value) { this.Status = value; }
            public String getReason() { return Reason; }
            public void setReason(String value) { this.Reason = value; }
        }
        """;

    /// <summary>
    /// The service: an interface and records it speaks in, nested; constructor injection, a
    /// logger and a clock; and five operations, each handing one query over among the code
    /// that validates, computes and maps - a projection of single values, a page of one row, a
    /// count, an entity join, and a NOT EXISTS over a parameter.
    /// </summary>
    public const string SchedulingService = """"
        package clinic.scheduling;

        import clinic.domain.Appointment;
        import clinic.domain.Doctor;
        import clinic.domain.Patient;
        import jakarta.persistence.EntityManager;
        import jakarta.persistence.PersistenceContext;
        import jakarta.persistence.Tuple;
        import java.time.Clock;
        import java.time.LocalDate;
        import java.time.LocalDateTime;
        import java.time.LocalTime;
        import java.util.ArrayList;
        import java.util.LinkedHashMap;
        import java.util.List;
        import java.util.Map;
        import java.util.NoSuchElementException;
        import java.util.Objects;
        import java.util.Optional;
        import org.slf4j.Logger;
        import org.slf4j.LoggerFactory;
        import org.springframework.stereotype.Service;
        import org.springframework.transaction.annotation.Transactional;

        @Service
        @Transactional(readOnly = true)
        public class SchedulingService {

            private static final Logger log = LoggerFactory.getLogger(SchedulingService.class);
            private static final LocalTime OPENS = LocalTime.of(8, 0);
            private static final LocalTime CLOSES = LocalTime.of(16, 0);
            private static final String AUDIT_LINE = "select %s booked %s for patient %d";

            public interface Reminders {
                void send(String email, String text);
            }

            public record Slot(LocalDateTime start, int minutes, String room) {
                public boolean isMorning() {
                    return start.toLocalTime().isBefore(LocalTime.NOON);
                }
            }

            public record Visit(String patient, LocalDateTime start, String room) {
            }

            @PersistenceContext
            private EntityManager em;

            private final Clock clock;
            private final Reminders reminders;

            public SchedulingService(Clock clock, Reminders reminders) {
                this.clock = Objects.requireNonNull(clock);
                this.reminders = Objects.requireNonNull(reminders);
            }

            public List<Slot> freeSlots(long doctorId, LocalDate day) {
                if (day.isBefore(LocalDate.now(clock))) {
                    throw new IllegalArgumentException("No slots in the past: " + day);
                }
                Doctor doctor = Optional.ofNullable(em.find(Doctor.class, doctorId))
                    .orElseThrow(() -> new NoSuchElementException("No doctor " + doctorId));

                List<LocalDateTime> taken = em.createQuery("""
                        select a.StartsAt from Appointment a
                        where a.DoctorId = :doctorId
                          and a.StartsAt >= :dayStart and a.StartsAt < :dayEnd
                          and a.Status <> 'CANCELLED'
                        order by a.StartsAt
                        """, LocalDateTime.class)
                    .setParameter("doctorId", doctorId)
                    .setParameter("dayStart", day.atTime(OPENS))
                    .setParameter("dayEnd", day.atTime(CLOSES))
                    .getResultList();

                List<Slot> free = new ArrayList<>();
                for (LocalDateTime start = day.atTime(OPENS); start.toLocalTime().isBefore(CLOSES); start = start.plusMinutes(doctor.getSlotMinutes())) {
                    if (!taken.contains(start)) {
                        free.add(new Slot(start, doctor.getSlotMinutes(), doctor.getRoom()));
                    }
                }
                log.debug("Dr {} has {} free slots on {}", doctor.getFullName(), free.size(), day);
                return free;
            }

            public Optional<Appointment> nextAppointmentOf(long patientId) {
                return em.createQuery("""
                        select a from Appointment a
                        where a.PatientId = :patientId and a.StartsAt > :now and a.Status = 'BOOKED'
                        order by a.StartsAt
                        """, Appointment.class)
                    .setParameter("patientId", patientId)
                    .setParameter("now", LocalDateTime.now(clock))
                    .setMaxResults(1)
                    .getResultStream()
                    .findFirst();
            }

            @Transactional
            public Appointment book(long patientId, long doctorId, LocalDateTime start, String reason) {
                Objects.requireNonNull(start, "start");
                Patient patient = em.find(Patient.class, patientId);
                Doctor doctor = em.find(Doctor.class, doctorId);
                if (patient == null || doctor == null) {
                    throw new NoSuchElementException("Unknown patient or doctor");
                }

                Long clashes = em.createQuery("""
                        select count(a) as clashes from Appointment a
                        where a.DoctorId = :doctorId and a.StartsAt = :start and a.Status = 'BOOKED'
                        """, Long.class)
                    .setParameter("doctorId", doctorId)
                    .setParameter("start", start)
                    .getSingleResult();
                if (clashes > 0) {
                    throw new IllegalStateException("The slot at " + start + " is taken");
                }

                Appointment appointment = Appointment.book(patientId, doctor, start, reason);
                em.persist(appointment);
                reminders.send(patient.getEmail(), "Your appointment with Dr " + doctor.getFullName() + " is at " + start);
                log.info(String.format(AUDIT_LINE, doctor.getFullName(), start, patientId));
                return appointment;
            }

            public Map<String, List<Visit>> agendaOf(long doctorId, LocalDate day) {
                List<Tuple> rows = em.createQuery("""
                        select p.FullName as patient, a.StartsAt as startsAt, a.Room as room
                        from Appointment a
                        join Patient p on p.PatientId = a.PatientId
                        where a.DoctorId = :doctorId
                          and a.StartsAt >= :dayStart and a.StartsAt < :dayEnd
                          and a.Status = 'BOOKED'
                        order by a.StartsAt
                        """, Tuple.class)
                    .setParameter("doctorId", doctorId)
                    .setParameter("dayStart", day.atStartOfDay())
                    .setParameter("dayEnd", day.plusDays(1).atStartOfDay())
                    .getResultList();

                Map<String, List<Visit>> byPeriod = new LinkedHashMap<>();
                for (Tuple row : rows) {
                    Visit visit = new Visit(row.get("patient", String.class), row.get("startsAt", LocalDateTime.class), row.get("room", String.class));
                    String period = visit.start().toLocalTime().isBefore(LocalTime.NOON) ? "morning" : "afternoon";
                    byPeriod.computeIfAbsent(period, key -> new ArrayList<>()).add(visit);
                }
                return byPeriod;
            }

            public List<Patient> patientsNotSeenSince(LocalDate since, int limit) {
                if (limit <= 0 || limit > 500) {
                    throw new IllegalArgumentException("limit must be between 1 and 500");
                }
                return em.createQuery("""
                        select p from Patient p
                        where not exists (
                            select a.AppointmentId from Appointment a
                            where a.PatientId = p.PatientId and a.Status = 'DONE' and a.StartsAt >= :since)
                        order by p.FullName
                        """, Patient.class)
                    .setParameter("since", since.atStartOfDay())
                    .setMaxResults(limit)
                    .getResultList();
            }
        }
        """";

    /// <summary>
    /// A scheduled report: a grouped JPQL query with a conditional aggregate, a native SQL
    /// query the tool reads as T-SQL, and the CSV and arithmetic around them.
    /// </summary>
    public const string Reports = """"
        package clinic.reporting;

        import jakarta.persistence.EntityManager;
        import jakarta.persistence.Tuple;
        import java.io.IOException;
        import java.io.UncheckedIOException;
        import java.nio.file.Files;
        import java.nio.file.Path;
        import java.time.Clock;
        import java.time.LocalDate;
        import java.util.List;
        import java.util.stream.Collectors;
        import org.slf4j.Logger;
        import org.slf4j.LoggerFactory;
        import org.springframework.scheduling.annotation.Scheduled;
        import org.springframework.stereotype.Component;

        @Component
        public class ClinicReports {

            private static final Logger log = LoggerFactory.getLogger(ClinicReports.class);

            public enum Format { CSV, TSV }

            private final EntityManager em;
            private final Clock clock;
            private final Path outbox;

            public ClinicReports(EntityManager em, Clock clock, Path outbox) {
                this.em = em;
                this.clock = clock;
                this.outbox = outbox;
            }

            @Scheduled(cron = "0 0 6 * * MON-FRI")
            public void yesterdaysLoad() {
                LocalDate day = LocalDate.now(clock).minusDays(1);
                List<Tuple> load = em.createQuery("""
                        select d.FullName as doctor, d.Specialty as specialty, count(a) as visits,
                               sum(case when a.Status = 'NO_SHOW' then 1 else 0 end) as noShows
                        from Appointment a
                        join Doctor d on d.DoctorId = a.DoctorId
                        where a.StartsAt >= :dayStart and a.StartsAt < :dayEnd
                        group by d.FullName, d.Specialty
                        order by count(a) desc
                        """, Tuple.class)
                    .setParameter("dayStart", day.atStartOfDay())
                    .setParameter("dayEnd", day.plusDays(1).atStartOfDay())
                    .getResultList();

                String csv = load.stream()
                    .map(row -> String.join(";",
                        row.get("doctor", String.class),
                        row.get("specialty", String.class),
                        String.valueOf(row.get("visits", Long.class)),
                        String.valueOf(row.get("noShows", Long.class))))
                    .collect(Collectors.joining("\n", "doctor;specialty;visits;no_shows\n", "\n"));

                write("load-" + day + ".csv", csv);
                log.info("Wrote the load of {} doctors for {}", load.size(), day);
            }

            public double noShowRate(LocalDate from, LocalDate to) {
                if (to.isBefore(from)) {
                    throw new IllegalArgumentException("The period ends before it starts");
                }
                Object[] totals = (Object[]) em.createNativeQuery("""
                        SELECT COUNT(*) AS Total, SUM(CASE WHEN a.Status = 'NO_SHOW' THEN 1 ELSE 0 END) AS Missed
                        FROM Clinic.Appointments AS a
                        WHERE a.StartsAt >= :fromDay AND a.StartsAt < :toDay AND a.Status <> 'CANCELLED'
                        """)
                    .setParameter("fromDay", from.atStartOfDay())
                    .setParameter("toDay", to.plusDays(1).atStartOfDay())
                    .getSingleResult();

                long total = ((Number) totals[0]).longValue();
                long missed = totals[1] == null ? 0 : ((Number) totals[1]).longValue();
                return total == 0 ? 0.0 : (double) missed / total;
            }

            private void write(String name, String content) {
                try {
                    Files.writeString(outbox.resolve(name), content);
                } catch (IOException failure) {
                    throw new UncheckedIOException("Could not write " + name, failure);
                }
            }
        }
        """";
}
