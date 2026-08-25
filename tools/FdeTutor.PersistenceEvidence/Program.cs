using System.Text.Json;
using Npgsql;

const int expectedEventCount = 12;
var connectionString = Environment.GetEnvironmentVariable(
    "FDE_TUTOR_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "FDE_TUTOR_POSTGRES_CONNECTION is required.");
}

if (args.Length != 1 || !Guid.TryParse(args[0], out var sessionId))
{
    throw new ArgumentException("Pass exactly one S083 session UUID.");
}

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
PersistenceSnapshot snapshot;
do
{
    snapshot = await ReadSnapshotAsync(connection, sessionId);
    if (snapshot.ProcessedProjectionEvents >= expectedEventCount &&
        snapshot.PublishedOutboxMessages >= expectedEventCount)
    {
        break;
    }

    await Task.Delay(TimeSpan.FromSeconds(2));
} while (DateTimeOffset.UtcNow < deadline);

if (snapshot.LearnerEvents != expectedEventCount ||
    snapshot.OutboxMessages != expectedEventCount ||
    snapshot.PublishedOutboxMessages != expectedEventCount ||
    snapshot.ProcessedProjectionEvents != expectedEventCount ||
    snapshot.ProgressState != "Complete" ||
    snapshot.ProgressVersion != expectedEventCount ||
    snapshot.CompletedRetrievals != 1 ||
    snapshot.ObservedUsers < 1)
{
    throw new InvalidOperationException(
        $"Persistence invariants did not converge: {JsonSerializer.Serialize(snapshot)}");
}

var appendOnlyRejected = await VerifyAppendOnlyAsync(connection, sessionId);
if (!appendOnlyRejected)
{
    throw new InvalidOperationException(
        "The database did not reject mutation of an acknowledged learner event.");
}

Console.WriteLine(JsonSerializer.Serialize(
    new
    {
        sessionId,
        snapshot.LearnerEvents,
        snapshot.OutboxMessages,
        snapshot.PublishedOutboxMessages,
        snapshot.ProcessedProjectionEvents,
        snapshot.ProgressState,
        snapshot.ProgressVersion,
        snapshot.CompletedRetrievals,
        snapshot.ObservedUsers,
        appendOnlyRejected,
    }));

static async Task<PersistenceSnapshot> ReadSnapshotAsync(
    NpgsqlConnection connection,
    Guid sessionId)
{
    const string sql =
        """
        SELECT
            (SELECT count(*) FROM learner_events WHERE session_id = @session_id),
            (
                SELECT count(*)
                FROM outbox_messages o
                JOIN learner_events e ON e.event_id = o.event_id
                WHERE e.session_id = @session_id
            ),
            (
                SELECT count(*)
                FROM outbox_messages o
                JOIN learner_events e ON e.event_id = o.event_id
                WHERE e.session_id = @session_id
                  AND o.published_at IS NOT NULL
            ),
            (
                SELECT count(*)
                FROM processed_projection_events p
                JOIN learner_events e ON e.event_id = p.event_id
                WHERE e.session_id = @session_id
            ),
            (
                SELECT state
                FROM s083_progress
                WHERE session_id = @session_id
            ),
            (
                SELECT projection_version
                FROM s083_progress
                WHERE session_id = @session_id
            ),
            (
                SELECT count(*)
                FROM due_retrievals
                WHERE session_id = @session_id
                  AND completed_event_id IS NOT NULL
            ),
            (SELECT count(*) FROM platform_users);
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("session_id", sessionId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("The persistence snapshot returned no row.");
    }

    return new PersistenceSnapshot(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.GetInt64(2),
        reader.GetInt64(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetInt64(6),
        reader.GetInt64(7));
}

static async Task<bool> VerifyAppendOnlyAsync(
    NpgsqlConnection connection,
    Guid sessionId)
{
    await using var transaction = await connection.BeginTransactionAsync();
    try
    {
        const string sql =
            """
            UPDATE learner_events
            SET payload = payload
            WHERE event_id = (
                SELECT event_id
                FROM learner_events
                WHERE session_id = @session_id
                ORDER BY recorded_sequence
                LIMIT 1
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("session_id", sessionId);
        await command.ExecuteNonQueryAsync();
        return false;
    }
    catch (PostgresException exception)
        when (exception.SqlState == PostgresErrorCodes.RaiseException &&
              exception.MessageText.Contains(
                  "learner_events is append-only",
                  StringComparison.Ordinal))
    {
        return true;
    }
    finally
    {
        await transaction.RollbackAsync();
    }
}

internal sealed record PersistenceSnapshot(
    long LearnerEvents,
    long OutboxMessages,
    long PublishedOutboxMessages,
    long ProcessedProjectionEvents,
    string ProgressState,
    long ProgressVersion,
    long CompletedRetrievals,
    long ObservedUsers);
