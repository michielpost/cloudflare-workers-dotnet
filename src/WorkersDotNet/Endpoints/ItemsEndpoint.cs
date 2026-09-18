using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: D1, Cloudflare's SQLite database (<c>env.D1("DB")</c>), in this
    /// deployment the database <c>dotnet</c> and the table <c>items</c>.
    /// </summary>
    /// <remarks>
    /// GET lists the items newest first, POST creates one, POST
    /// /api/items/update edits one and POST /api/items/delete removes one.
    /// Every mutation answers with the full list and with the row it touched, so
    /// the frontend never needs a second request.
    /// Locally wrangler keeps a SQLite file of this database on this machine
    /// (<c>.wrangler/state/v3/d1</c>); apply the schema with
    /// <c>npx wrangler d1 execute dotnet --local --file=migrations/0001_init.sql</c>.
    /// </remarks>
    public static class ItemsEndpoint
    {
        const string Binding = "DB";
        const string DatabaseName = SampleConfig.D1DatabaseName;
        const string TableName = SampleConfig.D1ItemsTable;
        const int MaxTitleLength = SampleConfig.ItemMaxTitleLength;
        const int MaxNotesLength = SampleConfig.ItemMaxNotesLength;
        const int ListLimit = SampleConfig.ItemListLimit;

        /// <summary>GET returns the list, POST creates an item.</summary>
        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            if (request.Method == "POST")
                return await CreateAsync(request, environment);

            if (request.Method != "GET")
                return Results.Error("Only GET and POST are supported on /api/items", 405);

            var snapshot = await ReadSnapshotAsync(environment);
            return Response.Json(snapshot, 200)
                .WithHeader("cache-control", "no-store");
        }

        static async Task<Response> CreateAsync(Request request, Env environment)
        {
            ItemCreateRequest? input;
            try
            {
                input = await request.JsonAsync<ItemCreateRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null)
                return Results.Error("A JSON body with \"title\" is required", 400);

            var title = Trim(input.Title);
            var notes = Trim(input.Notes);
            var status = Trim(input.Status);
            if (status.Length == 0)
                status = FirstStatus();

            var problem = Validate(title, notes, status);
            if (problem is not null)
                return Results.Error(problem, 400);

            var now = DateTimeOffset.UtcNow.ToString("O");
            var db = environment.D1(Binding);
            await db.Prepare("INSERT INTO items (title, notes, status, created_at, updated_at) VALUES (?, ?, ?, ?, ?)")
                .Bind(title, notes, status, now, now)
                .RunAsync();

            // Read back the row that was just inserted, so the answer carries its id.
            var created = await db.Prepare("SELECT id, title, notes, status, created_at AS createdAtUtc, updated_at AS updatedAtUtc FROM items ORDER BY id DESC LIMIT 1")
                .FirstAsync<ItemRecord>();

            var id = created is null ? 0 : created.Id;
            var snapshot = await ReadSnapshotAsync(environment);
            var result = new ItemMutationResult(true, $"Item #{id} created.", "create", id, snapshot);
            return Response.Json(result, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/items/update edits one existing item.</summary>
        public static async Task<Response> UpdateAsync(Request request, Env environment)
        {
            ItemUpdateRequest? input;
            try
            {
                input = await request.JsonAsync<ItemUpdateRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null)
                return Results.Error("A JSON body with \"id\" is required", 400);

            if (input.Id <= 0)
                return Results.Error("A positive \"id\" is required", 400);

            var title = Trim(input.Title);
            var notes = Trim(input.Notes);
            var status = Trim(input.Status);
            var problem = Validate(title, notes, status);
            if (problem is not null)
                return Results.Error(problem, 400);

            var db = environment.D1(Binding);
            var existing = await FirstAsync(db, input.Id);
            if (existing is null)
                return Results.Error($"Item #{input.Id} does not exist.", 404);

            var now = DateTimeOffset.UtcNow.ToString("O");
            await db.Prepare("UPDATE items SET title = ?, notes = ?, status = ?, updated_at = ? WHERE id = ?")
                .Bind(title, notes, status, now, input.Id)
                .RunAsync();

            var snapshot = await ReadSnapshotAsync(environment);
            var result = new ItemMutationResult(true, $"Item #{input.Id} updated.", "update", input.Id, snapshot);
            return Response.Json(result, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/items/delete removes one existing item.</summary>
        public static async Task<Response> DeleteAsync(Request request, Env environment)
        {
            ItemDeleteRequest? input;
            try
            {
                input = await request.JsonAsync<ItemDeleteRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null)
                return Results.Error("A JSON body with \"id\" is required", 400);

            if (input.Id <= 0)
                return Results.Error("A positive \"id\" is required", 400);

            var db = environment.D1(Binding);
            var existing = await FirstAsync(db, input.Id);
            if (existing is null)
                return Results.Error($"Item #{input.Id} does not exist.", 404);

            await db.Prepare("DELETE FROM items WHERE id = ?")
                .Bind(input.Id)
                .RunAsync();

            var snapshot = await ReadSnapshotAsync(environment);
            var result = new ItemMutationResult(true, $"Item #{input.Id} deleted.", "delete", input.Id, snapshot);
            return Response.Json(result, 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>The list plus the counts, limits and status list the UI shows.</summary>
        static async Task<ItemsSnapshot> ReadSnapshotAsync(Env environment)
        {
            var db = environment.D1(Binding);
            var rows = await db.Prepare($"SELECT id, title, notes, status, created_at AS createdAtUtc, updated_at AS updatedAtUtc FROM items ORDER BY updated_at DESC, id DESC LIMIT {ListLimit}")
                .AllAsync<ItemRecord>();

            var items = new List<ItemRecord>();
            if (rows is not null && rows.Results is not null)
            {
                foreach (var row in rows.Results)
                    items.Add(row);
            }

            return new ItemsSnapshot(
                items,
                items.Count,
                DatabaseName,
                TableName,
                MaxTitleLength,
                MaxNotesLength,
                StatusList());
        }

        static async Task<ItemRecord?> FirstAsync(ID1Database db, long id)
        {
            try
            {
                return await db.Prepare("SELECT id, title, notes, status, created_at AS createdAtUtc, updated_at AS updatedAtUtc FROM items WHERE id = ?")
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
