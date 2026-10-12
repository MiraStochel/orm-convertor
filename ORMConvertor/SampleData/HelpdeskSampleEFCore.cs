namespace SampleData;

/// <summary>
/// A helpdesk as an ASP.NET Core project keeps it: five C# files in which the mapping and the
/// queries are a small part of the text. The domain file carries entities with business
/// methods beside an enum and a static class of constants; the context overrides
/// SaveChangesAsync with auditing code; the service, the API controller and the background
/// job are mostly validation, logging, mapping to records, in-memory LINQ over what a query
/// already loaded, and string building - each with the queries it hands to EF Core somewhere
/// in the middle. The example shows what the tool takes out of such files (decision 111):
/// every entity class, every place a query is handed over (decision 109), the fluent
/// configuration (decision 132); and what it leaves where it is - methods, records,
/// interfaces, enums, static classes, and LINQ over objects in memory (decision 114).
///
/// The tables live in a schema of their own, Support, as the other larger samples keep theirs.
/// </summary>
public static class HelpdeskSampleEFCore
{
    /// <summary>
    /// Four entities with their mapping in attributes and their behaviour in methods, an enum
    /// the service speaks in, and a static class of state names. Only the classes become
    /// entities; the methods, the read-only properties, the enum and the static class do not.
    /// </summary>
    public const string Domain = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        namespace Helpdesk.Domain;

        /// <summary>How urgent a ticket is; stored as its number.</summary>
        public enum TicketPriority
        {
            Low = 1,
            Normal = 2,
            High = 3,
            Urgent = 4,
        }

        /// <summary>The states a ticket moves through, as they are stored.</summary>
        public static class TicketStates
        {
            public const string New = "New";
            public const string Assigned = "Assigned";
            public const string Waiting = "Waiting";
            public const string Resolved = "Resolved";

            public static bool IsFinal(string state) => state == Resolved;
        }

        /// <summary>Somebody who asks for help.</summary>
        [Table("Requesters", Schema = "Support")]
        [Index(nameof(Email), IsUnique = true)]
        public class Requester
        {
            [Key]
            public int RequesterId { get; set; }

            [MaxLength(200)]
            public string Name { get; set; } = "";

            [MaxLength(254)]
            [Unicode(false)]
            public string Email { get; set; } = "";

            [MaxLength(100)]
            public string? Company { get; set; }

            public bool IsVip { get; set; }

            public List<Ticket> Tickets { get; set; } = [];

            public string DisplayName => Company is null ? Name : $"{Name} ({Company})";
        }

        /// <summary>Somebody who helps, within a limit of open tickets.</summary>
        [Table("Agents", Schema = "Support")]
        public class Agent
        {
            [Key]
            public int AgentId { get; set; }

            [MaxLength(200)]
            public string Name { get; set; } = "";

            [MaxLength(50)]
            public string Team { get; set; } = "";

            public bool IsActive { get; set; }

            public int MaxOpenTickets { get; set; }

            public bool CanTakeMore(int open) => IsActive && open < MaxOpenTickets;
        }

        /// <summary>A support request, from the first message to its resolution.</summary>
        [Table("Tickets", Schema = "Support")]
        [Index(nameof(Reference), IsUnique = true)]
        public class Ticket
        {
            [Key]
            public int TicketId { get; set; }

            [MaxLength(16)]
            [Unicode(false)]
            public string Reference { get; set; } = "";

            [MaxLength(200)]
            public string Subject { get; set; } = "";

            public int RequesterId { get; set; }

            [ForeignKey(nameof(RequesterId))]
            public Requester Requester { get; set; } = null!;

            public int? AssigneeId { get; set; }

            [ForeignKey(nameof(AssigneeId))]
            public Agent? Assignee { get; set; }

            public int Priority { get; set; }

            [MaxLength(20)]
            public string State { get; set; } = "New";

            [Precision(3)]
            public DateTime OpenedAt { get; set; }

            [Precision(3)]
            public DateTime? FirstResponseAt { get; set; }

            [Precision(3)]
            public DateTime? ResolvedAt { get; set; }

            public bool Escalated { get; set; }

            [Timestamp]
            public byte[] RowVersion { get; set; } = [];

            public List<Comment> Comments { get; set; } = [];

            public bool IsOpen => ResolvedAt is null;

            public TicketPriority Urgency => (TicketPriority)Priority;

            public void AssignTo(Agent agent, DateTime at)
            {
                ArgumentNullException.ThrowIfNull(agent);
                if (!IsOpen)
                {
                    throw new InvalidOperationException($"Ticket {Reference} is resolved and cannot be assigned.");
                }

                AssigneeId = agent.AgentId;
                State = TicketStates.Assigned;
                FirstResponseAt ??= at;
            }

            public void Resolve(DateTime at)
            {
                if (TicketStates.IsFinal(State))
                {
                    return;
                }

                State = TicketStates.Resolved;
                ResolvedAt = at;
            }

            public TimeSpan Age(DateTime now) => (ResolvedAt ?? now) - OpenedAt;
        }

        /// <summary>One message on a ticket; an internal note is never shown to the requester.</summary>
        [Table("Comments", Schema = "Support")]
        public class Comment
        {
            [Key]
            public long CommentId { get; set; }

            public int TicketId { get; set; }

            [ForeignKey(nameof(TicketId))]
            public Ticket Ticket { get; set; } = null!;

            public int? AuthorAgentId { get; set; }

            [MaxLength(4000)]
            public string Body { get; set; } = "";

            public bool IsInternal { get; set; }

            [Precision(3)]
            public DateTime PostedAt { get; set; }

            public string Preview(int length = 80) => Body.Length <= length ? Body : Body[..length] + "...";
        }
        """;

    /// <summary>
    /// The context: its DbSets, a little fluent configuration, and a SaveChangesAsync override
    /// that stamps and audits - code the tool reads past, while the configuration is read.
    /// </summary>
    public const string Context = """
        using Helpdesk.Domain;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.Logging;

        namespace Helpdesk.Data;

        public class HelpdeskContext(DbContextOptions<HelpdeskContext> options, ILogger<HelpdeskContext> logger, TimeProvider clock)
            : DbContext(options)
        {
            public DbSet<Requester> Requesters { get; set; } = null!;
            public DbSet<Agent> Agents { get; set; } = null!;
            public DbSet<Ticket> Tickets { get; set; } = null!;
            public DbSet<Comment> Comments { get; set; } = null!;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Agent>().HasIndex(a => new { a.Team, a.Name }).IsUnique();
                modelBuilder.Entity<Ticket>().Property(t => t.Subject).IsRequired();
                modelBuilder.Entity<Comment>().Property(c => c.Body).IsRequired();
            }

            public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var added = 0;

                foreach (var entry in ChangeTracker.Entries())
                {
                    if (entry.State == EntityState.Added && entry.Entity is Ticket ticket)
                    {
                        ticket.OpenedAt = now;
                        added++;
                    }
                    else if (entry.State == EntityState.Added && entry.Entity is Comment comment)
                    {
                        comment.PostedAt = now;
                    }
                }

                try
                {
                    var written = await base.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("Saved {Written} rows, {Added} of them new tickets", written, added);
                    return written;
                }
                catch (DbUpdateConcurrencyException conflict)
                {
                    logger.LogWarning(conflict, "A ticket was changed by somebody else in the meantime");
                    throw;
                }
            }
        }
        """;

    /// <summary>
    /// The service: an interface and records it speaks in, then a class with five methods.
    /// Each method validates, logs and maps, and hands EF Core one query in the middle of it -
    /// a page under a sort, a single row by a key, a flag filter, a grouping, a pattern and a
    /// subquery. What happens to the rows once loaded is LINQ over objects and stays here.
    /// </summary>
    public const string Service = """
        using Helpdesk.Data;
        using Helpdesk.Domain;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.Logging;
        using Microsoft.Extensions.Options;

        namespace Helpdesk.Services;

        public sealed record SlaOptions
        {
            public int FirstResponseHours { get; init; } = 4;
            public int PageSize { get; init; } = 25;
        }

        public sealed record TicketView(string Reference, string Subject, TicketPriority Priority, TimeSpan Age);

        public sealed record AgentLoad(int AgentId, string Agent, int OpenTickets, int Escalated);

        public sealed record OpenTicketRequest(string Subject, string RequesterEmail, TicketPriority Priority, string Body);

        public interface ITicketService
        {
            Task<IReadOnlyList<TicketView>> QueueOfAsync(int agentId, int page, CancellationToken ct);
            Task<string> OpenAsync(OpenTicketRequest request, CancellationToken ct);
            Task<int> EscalateOverdueAsync(CancellationToken ct);
            Task<IReadOnlyList<AgentLoad>> WorkloadAsync(CancellationToken ct);
            Task<IReadOnlyList<TicketView>> SearchAsync(string text, CancellationToken ct);
        }

        public sealed class TicketService(
            HelpdeskContext db,
            TimeProvider clock,
            IOptions<SlaOptions> options,
            ILogger<TicketService> logger) : ITicketService
        {
            private readonly SlaOptions sla = options.Value;

            public async Task<IReadOnlyList<TicketView>> QueueOfAsync(int agentId, int page, CancellationToken ct)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(page);
                var skip = page * sla.PageSize;
                var take = sla.PageSize;

                var tickets = await db.Tickets
                    .Where(t => t.AssigneeId == agentId && t.ResolvedAt == null)
                    .OrderByDescending(t => t.Priority)
                    .ThenBy(t => t.OpenedAt)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(ct);

                var now = clock.GetUtcNow().UtcDateTime;
                logger.LogDebug("Agent {AgentId} has {Count} open tickets on page {Page}", agentId, tickets.Count, page);

                return tickets
                    .Select(t => new TicketView(t.Reference, t.Subject, t.Urgency, t.Age(now)))
                    .OrderByDescending(v => v.Priority)
                    .ToList();
            }

            public async Task<string> OpenAsync(OpenTicketRequest request, CancellationToken ct)
            {
                ArgumentNullException.ThrowIfNull(request);
                ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);
                var email = request.RequesterEmail.Trim().ToLowerInvariant();

                var requester = await db.Requesters
                    .Where(r => r.Email == email)
                    .FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException($"No requester is registered under {email}.");

                var priority = requester.IsVip && request.Priority < TicketPriority.High
                    ? TicketPriority.High
                    : request.Priority;

                var ticket = new Ticket
                {
                    Reference = NewReference(clock.GetUtcNow().UtcDateTime),
                    Subject = request.Subject.Trim(),
                    RequesterId = requester.RequesterId,
                    Priority = (int)priority,
                };
                ticket.Comments.Add(new Comment { Body = request.Body });

                db.Add(ticket);
                await db.SaveChangesAsync(ct);

                logger.LogInformation("Opened {Reference} for {Requester} at {Priority}", ticket.Reference, requester.DisplayName, priority);
                return ticket.Reference;
            }

            public async Task<int> EscalateOverdueAsync(CancellationToken ct)
            {
                var deadline = clock.GetUtcNow().UtcDateTime.AddHours(-sla.FirstResponseHours);

                var overdue = await db.Tickets
                    .Where(t => t.FirstResponseAt == null && t.OpenedAt < deadline && !t.Escalated)
                    .ToListAsync(ct);

                foreach (var ticket in overdue.Where(t => t.Urgency >= TicketPriority.Normal))
                {
                    ticket.Escalated = true;
                    logger.LogWarning("{Reference} waits for a first response since {OpenedAt}", ticket.Reference, ticket.OpenedAt);
                }

                await db.SaveChangesAsync(ct);
                return overdue.Count(t => t.Escalated);
            }

            public async Task<IReadOnlyList<AgentLoad>> WorkloadAsync(CancellationToken ct)
            {
                var load = await db.Tickets
                    .Where(t => t.ResolvedAt == null && t.AssigneeId != null)
                    .GroupBy(t => t.AssigneeId)
                    .Select(g => new
                    {
                        AgentId = g.Key,
                        OpenTickets = g.Count(),
                        Escalated = g.Sum(t => t.Escalated ? 1 : 0),
                    })
                    .ToListAsync(ct);

                var agents = await db.Agents
                    .Where(a => a.IsActive)
                    .Select(a => new { a.AgentId, a.Name, a.MaxOpenTickets })
                    .ToListAsync(ct);

                var names = agents.ToDictionary(a => a.AgentId, a => a.Name);
                return load
                    .Where(l => l.AgentId is not null && names.ContainsKey(l.AgentId.Value))
                    .Select(l => new AgentLoad(l.AgentId!.Value, names[l.AgentId.Value], l.OpenTickets, l.Escalated))
                    .OrderByDescending(l => l.OpenTickets)
                    .ToList();
            }

            public async Task<IReadOnlyList<TicketView>> SearchAsync(string text, CancellationToken ct)
            {
                if (string.IsNullOrWhiteSpace(text) || text.Length < 3)
                {
                    return [];
                }

                var pattern = $"%{text.Trim()}%";
                var now = clock.GetUtcNow().UtcDateTime;

                var found = await db.Tickets
                    .Where(t => EF.Functions.Like(t.Subject, pattern)
                        || db.Comments.Where(c => !c.IsInternal && EF.Functions.Like(c.Body, pattern)).Select(c => c.TicketId).Contains(t.TicketId))
                    .OrderByDescending(t => t.OpenedAt)
                    .Take(50)
                    .ToListAsync(ct);

                return found.Select(t => new TicketView(t.Reference, t.Subject, t.Urgency, t.Age(now))).ToList();
            }

            private static string NewReference(DateTime at)
                => $"HD-{at:yyMMdd}-{Random.Shared.Next(1000, 9999)}";
        }
        """;

    /// <summary>
    /// An API controller: routing attributes, model binding, status codes - and one query of
    /// its own in the lookup action, beside the actions that only call the service.
    /// </summary>
    public const string Controller = """
        using Helpdesk.Data;
        using Helpdesk.Services;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.EntityFrameworkCore;

        namespace Helpdesk.Api;

        [ApiController]
        [Route("api/tickets")]
        public sealed class TicketsController(ITicketService tickets, HelpdeskContext db) : ControllerBase
        {
            [HttpGet("{reference}")]
            public async Task<IActionResult> Get(string reference, CancellationToken ct)
            {
                if (reference.Length != 16)
                {
                    return BadRequest(new { error = "A reference has sixteen characters." });
                }

                var found = await db.Tickets
                    .Where(t => t.Reference == reference)
                    .Select(t => new { t.Reference, t.Subject, t.State, t.OpenedAt, t.ResolvedAt })
                    .FirstOrDefaultAsync(ct);

                return found is null ? NotFound() : Ok(found);
            }

            [HttpGet("queue/{agentId:int}")]
            public async Task<IActionResult> Queue(int agentId, [FromQuery] int page = 0, CancellationToken ct = default)
                => Ok(await tickets.QueueOfAsync(agentId, page, ct));

            [HttpPost]
            public async Task<IActionResult> Open(OpenTicketRequest request, CancellationToken ct)
            {
                try
                {
                    var reference = await tickets.OpenAsync(request, ct);
                    return CreatedAtAction(nameof(Get), new { reference }, new { reference });
                }
                catch (InvalidOperationException unknown)
                {
                    return UnprocessableEntity(new { error = unknown.Message });
                }
            }

            [HttpGet("search")]
            public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
                => Ok(await tickets.SearchAsync(q, ct));
        }
        """;

    /// <summary>
    /// A background job: a timer loop, a scope per run, one grouped report query, and the CSV
    /// it writes - with a static class of formatting helpers beside it.
    /// </summary>
    public const string ReportJob = """
        using System.Globalization;
        using System.Text;
        using Helpdesk.Data;
        using Helpdesk.Domain;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;

        namespace Helpdesk.Jobs;

        public static class CsvFormatting
        {
            public static string Cell(string value)
                => value.Contains(';') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

            public static string Hours(TimeSpan span)
                => span.TotalHours.ToString("0.0", CultureInfo.InvariantCulture);
        }

        public sealed class BacklogReportJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<BacklogReportJob> logger)
            : BackgroundService
        {
            private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                using var timer = new PeriodicTimer(Interval);
                do
                {
                    try
                    {
                        await RunOnceAsync(stoppingToken);
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException)
                    {
                        logger.LogError(failure, "The backlog report failed; the next run is in {Interval}", Interval);
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }

            private async Task RunOnceAsync(CancellationToken ct)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HelpdeskContext>();
                var now = clock.GetUtcNow().UtcDateTime;
                var cutoff = now.AddDays(-2);

                var backlog = await db.Tickets
                    .Where(t => t.ResolvedAt == null && t.OpenedAt < cutoff)
                    .GroupBy(t => t.Priority)
                    .Select(g => new
                    {
                        Priority = g.Key,
                        Tickets = g.Count(),
                        Escalated = g.Sum(t => t.Escalated ? 1 : 0),
                        Oldest = g.Min(t => t.OpenedAt),
                    })
                    .OrderByDescending(r => r.Priority)
                    .ToListAsync(ct);

                var csv = new StringBuilder("priority;tickets;escalated;oldest_hours").AppendLine();
                foreach (var row in backlog)
                {
                    csv.Append(CsvFormatting.Cell(((TicketPriority)row.Priority).ToString())).Append(';')
                       .Append(row.Tickets).Append(';')
                       .Append(row.Escalated).Append(';')
                       .AppendLine(CsvFormatting.Hours(now - row.Oldest));
                }

                var total = backlog.Sum(r => r.Tickets);
                logger.LogInformation("Backlog older than two days: {Total} tickets in {Groups} priorities", total, backlog.Count);
                await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), $"backlog-{now:yyyyMMddHH}.csv"), csv.ToString(), ct);
            }
        }
        """;
}
