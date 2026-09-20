using Shared;
using Workers;

namespace WorkersDotNet.Services
{
    /// <summary>
    /// The D1 items sample's business logic: validating an item, reading the
    /// list, and creating, updating and deleting rows. The endpoint stays a thin
    /// controller - it parses the HTTP request and maps the result to a response.
    /// </summary>
    /// <remarks>
    /// This is a static class because the worker transpiler does not support
    /// user-defined instance methods or generic types. Every method takes the D1
    /// binding it needs, and mutations return an <see cref="Outcome"/> carrying
    /// either the new value or an error to surface.
    /// </remarks>
    public static class ItemsService
    {
        /// <summary>The result of a mutation: the new value, or an error to surface.</summary>
        public sealed record Outcome(ItemMutationResult? Result, string? Error, int Status);

        const string TableName = SampleConfig.D1ItemsTable;
        const string DatabaseName = SampleConfig.D1DatabaseName;
        const int MaxTitleLength = SampleConfig.ItemMaxTitleLength;
        const int MaxNotesLength = SampleConfig.ItemMaxNotesLength;
        const int ListLimit = SampleConfig.ItemListLimit;

        const string Columns =
            "id, title, notes, status, created_at AS createdAtUtc, updated_at AS updatedAtUtc";

        public static async Task<ItemsSnapshot> ReadSnapshotAsync(ID1Database db)
        {
            var items = await AllAsync(
                db,
                $"SELECT {Columns} FROM items ORDER BY updated_at DESC, id DESC LIMIT {ListLimit}");

            return new ItemsSnapshot(
                items,
                items.Count,
                DatabaseName,
                TableName,
                MaxTitleLength,
                MaxNotesLength,
                StatusList());
        }

        public static async Task<Outcome> CreateAsync(ID1Database db, ItemCreateRequest input)
        {
            var title = Trim(input.Title);
            var notes = Trim(input.Notes);
            var status = Trim(input.Status);
            if (status.Length == 0)
                status = FirstStatus();

            var problem = Validate(title, notes, status);
            if (problem is not null)
                return new Outcome(null, problem, 400);

            var now = DateTimeOffset.UtcNow.ToString("O");
            await db.Prepare(
                "INSERT INTO items (title, notes, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?)")
                .Bind(title, notes, status, now, now)
                .RunAsync();

            // Read back the row that was just inserted, so the answer carries its id.
            var createdRows = await AllAsync(
                db,
                $"SELECT {Columns} FROM items ORDER BY id DESC LIMIT 1");
            var created = createdRows.Count == 0 ? null : createdRows[0];
            var id = created is null ? 0 : created.Id;
            var snapshot = await ReadSnapshotAsync(db);
            return new Outcome(
                new ItemMutationResult(true, $"Item #{id} created.", "create", id, snapshot),
                null,
                200);
        }

        public static async Task<Outcome> UpdateAsync(ID1Database db, ItemUpdateRequest input)
        {
            var title = Trim(input.Title);
            var notes = Trim(input.Notes);
            var status = Trim(input.Status);

            var problem = Validate(title, notes, status);
            if (problem is not null)
                return new Outcome(null, problem, 400);

            var existing = await FirstAsync(db, input.Id);
            if (existing is null)
                return new Outcome(null, $"Item #{input.Id} does not exist.", 404);

            var now = DateTimeOffset.UtcNow.ToString("O");
            await db.Prepare("UPDATE items SET title = ?, notes = ?, status = ?, updated_at = ? WHERE id = ?")
                .Bind(title, notes, status, now, input.Id)
                .RunAsync();

            var snapshot = await ReadSnapshotAsync(db);
            return new Outcome(
                new ItemMutationResult(true, $"Item #{input.Id} updated.", "update", input.Id, snapshot),
                null,
                200);
        }

        public static async Task<Outcome> DeleteAsync(ID1Database db, ItemDeleteRequest input)
        {
            var existing = await FirstAsync(db, input.Id);
            if (existing is null)
                return new Outcome(null, $"Item #{input.Id} does not exist.", 404);

            await db.Prepare("DELETE FROM items WHERE id = ?").Bind(input.Id).RunAsync();

            var snapshot = await ReadSnapshotAsync(db);
            return new Outcome(
                new ItemMutationResult(true, $"Item #{input.Id} deleted.", "delete", input.Id, snapshot),
                null,
                200);
        }

        /// <summary>Runs a select and returns the rows as a plain list (empty when none).</summary>
        static async Task<List<ItemRecord>> AllAsync(ID1Database db, string sql)
        {
            var result = await db.Prepare(sql).AllAsync<ItemRecord>();
            var list = new List<ItemRecord>();
            if (result is not null && result.Results is not null)
            {
                foreach (var row in result.Results)
                    list.Add(row);
            }
            return list;
        }

        static async Task<ItemRecord?> FirstAsync(ID1Database db, long id)
        {
            try
            {
                return await db.Prepare($"SELECT {Columns} FROM items WHERE id = ?")
                    .Bind(id)
                    .FirstAsync<ItemRecord>();
            }
            catch (Exception)
            {
                return null;
            }
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
        static string? Validate(string title, string notes, string status)
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
