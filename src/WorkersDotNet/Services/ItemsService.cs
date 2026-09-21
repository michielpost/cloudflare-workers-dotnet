using System.Collections.Generic;
using System.Threading.Tasks;
using Shared;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The D1 items sample's business logic: validating an item, reading the
    /// list, and creating, updating and deleting rows. The endpoint stays a thin
    /// controller - it parses the HTTP request and maps the result to a response.
    /// </summary>
    /// <summary>The result of an item mutation: the new value, or an error to surface.</summary>
    public sealed record ItemsOutcome(ItemMutationResult? Result, string? Error, int Status);

    public sealed class ItemsService
    {

        readonly string TableName = SampleConfig.D1ItemsTable;
        readonly string DatabaseName = SampleConfig.D1DatabaseName;
        readonly int MaxTitleLength = SampleConfig.ItemMaxTitleLength;
        readonly int MaxNotesLength = SampleConfig.ItemMaxNotesLength;
        readonly int ListLimit = SampleConfig.ItemListLimit;

        readonly string Columns =
            "id, title, notes, status, created_at AS createdAtUtc, updated_at AS updatedAtUtc";

        private readonly D1Database _db;

        public ItemsService(D1Database db)
        {
            _db = db;
        }

        public async Task<ItemsSnapshot> ReadSnapshotAsync()
        {
            var items = await _db.AllAsync<ItemRecord>(
                _db.Prepare($"SELECT {Columns} FROM items ORDER BY updated_at DESC, id DESC LIMIT {ListLimit}"));

            return new ItemsSnapshot(
                items,
                items.Count,
                DatabaseName,
                TableName,
                MaxTitleLength,
                MaxNotesLength,
                StatusList());
        }

        public async Task<ItemsOutcome> CreateAsync(ItemCreateRequest input)
        {
            var title = Trim(input.Title);
            var notes = Trim(input.Notes);
            var status = Trim(input.Status);
            if (status.Length == 0)
                status = FirstStatus();

            var problem = Validate(title, notes, status);
            if (problem is not null)
                return new ItemsOutcome(null, problem, 400);

            var now = DateTimeOffset.UtcNow.ToString("O");
            await _db.ExecuteAsync(
                _db.Prepare(
                    "INSERT INTO items (title, notes, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?)")
                    .Bind(title, notes, status, now, now));

            // Read back the row that was just inserted, so the answer carries its id.
            var createdRows = await _db.AllAsync<ItemRecord>(
                _db.Prepare($"SELECT {Columns} FROM items ORDER BY id DESC LIMIT 1"));
            var created = createdRows.Count == 0 ? null : createdRows[0];
            var id = created is null ? 0 : created.Id;
            var snapshot = await ReadSnapshotAsync();
            return new ItemsOutcome(
                new ItemMutationResult(true, $"Item #{id} created.", "create", id, snapshot),
                null,
                200);
        }

        public async Task<ItemsOutcome> UpdateAsync(ItemUpdateRequest input)
        {
            var title = Trim(input.Title);
            var notes = Trim(input.Notes);
            var status = Trim(input.Status);

            var problem = Validate(title, notes, status);
            if (problem is not null)
                return new ItemsOutcome(null, problem, 400);

            var existing = await _db.FirstAsync<ItemRecord>(
                _db.Prepare($"SELECT {Columns} FROM items WHERE id = ?").Bind(input.Id));
            if (existing is null)
                return new ItemsOutcome(null, $"Item #{input.Id} does not exist.", 404);

            var now = DateTimeOffset.UtcNow.ToString("O");
            await _db.ExecuteAsync(
                _db.Prepare("UPDATE items SET title = ?, notes = ?, status = ?, updated_at = ? WHERE id = ?")
                    .Bind(title, notes, status, now, input.Id));

            var snapshot = await ReadSnapshotAsync();
            return new ItemsOutcome(
                new ItemMutationResult(true, $"Item #{input.Id} updated.", "update", input.Id, snapshot),
                null,
                200);
        }

        public async Task<ItemsOutcome> DeleteAsync(ItemDeleteRequest input)
        {
            var existing = await _db.FirstAsync<ItemRecord>(
                _db.Prepare($"SELECT {Columns} FROM items WHERE id = ?").Bind(input.Id));
            if (existing is null)
                return new ItemsOutcome(null, $"Item #{input.Id} does not exist.", 404);

            await _db.ExecuteAsync(_db.Prepare("DELETE FROM items WHERE id = ?").Bind(input.Id));

            var snapshot = await ReadSnapshotAsync();
            return new ItemsOutcome(
                new ItemMutationResult(true, $"Item #{input.Id} deleted.", "delete", input.Id, snapshot),
                null,
                200);
        }

        /// <summary>
        /// The three statuses the sample accepts. This is also the list the
        /// frontend renders, so worker and UI can never disagree about it.
        /// </summary>
        static List<string> StatusList()
        {
            return new List<string> { "open", "in-progress", "done" };
        }

        static string FirstStatus()
        {
            var statuses = StatusList();
            foreach (var status in statuses)
                return status;

            return "open";
        }

        static string Trim(string? value)
        {
            if (value is null)
                return "";

            return value.Trim();
        }

        /// <summary>Returns the problem with the input, or null when it is valid.</summary>
        string? Validate(string title, string notes, string status)
        {
            if (title.Length == 0)
                return "A title is required";

            if (title.Length > MaxTitleLength)
                return $"The title is {title.Length} characters, the limit is {MaxTitleLength}";

            if (notes.Length > MaxNotesLength)
                return $"The notes are {notes.Length} characters, the limit is {MaxNotesLength}";

            var known = false;
            var statuses = StatusList();
            foreach (var candidate in statuses)
            {
                if (candidate == status)
                    known = true;
            }

            if (!known)
                return $"The status must be one of {StatusText()}";

            return null;
        }

        /// <summary>
        /// The statuses as one comma separated string for the error message. Written
        /// out by hand because the worker has no <c>string.Join</c> (the compiler
        /// only supports a fixed set of string methods).
        /// </summary>
        static string StatusText()
        {
            var text = "";
            var statuses = StatusList();
            foreach (var status in statuses)
            {
                if (text.Length != 0)
                    text += ", ";

                text += status;
            }

            return text;
        }
    }
}
