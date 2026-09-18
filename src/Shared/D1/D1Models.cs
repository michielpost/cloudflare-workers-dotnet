namespace Shared;

/// <summary>Request body of the D1 create endpoint.</summary>
public sealed record ItemCreateRequest(string Title, string Notes, string Status);

/// <summary>Request body of the D1 update endpoint.</summary>
public sealed record ItemUpdateRequest(long Id, string Title, string Notes, string Status);

/// <summary>Request body of the D1 delete endpoint.</summary>
public sealed record ItemDeleteRequest(long Id);

/// <summary>One row of the items table, with the timestamps the UI shows.</summary>
public sealed record ItemRecord(
    long Id,
    string Title,
    string Notes,
    string Status,
    string CreatedAtUtc,
    string UpdatedAtUtc);

/// <summary>
/// Everything GET /api/items returns: the rows plus the names and limits the UI
/// needs, so the frontend never has to hard-code them.
/// </summary>
public sealed record ItemsSnapshot(
    IReadOnlyList<ItemRecord> Items,
    int Count,
    string DatabaseName,
    string Table,
    int MaxTitleLength,
    int MaxNotesLength,
    IReadOnlyList<string> Statuses);

/// <summary>Result of a create, update or delete, together with the new list.</summary>
public sealed record ItemMutationResult(
    bool Ok,
    string Message,
    string Operation,
    long Id,
    ItemsSnapshot Snapshot);
