using Shared;
using Workers;
using WorkersDotNet.Services;

namespace WorkersDotNet
{
    /// <summary>
    /// Sample: D1, Cloudflare's SQLite database (<c>env.D1("DB")</c>), in this
    /// deployment the database <c>dotnet</c> and the table <c>items</c>.
    /// </summary>
    /// <remarks>
    /// This is a thin controller: it checks the HTTP method, reads the JSON body
    /// and maps the <see cref="ItemsService"/> result to a response. All the
    /// business logic lives in <see cref="ItemsService"/>.
    /// </remarks>
    public static class ItemsEndpoint
    {
        public static async Task<Response> HandleAsync(Request request, Env environment)
        {
            if (request.Method == "POST")
                return await CreateAsync(request, environment);

            if (request.Method != "GET")
                return Results.Error("Only GET and POST are supported on /api/items", 405);

            return Response.Json(await ItemsService.ReadSnapshotAsync(environment.D1("DB")), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/items/update edits one existing item.</summary>
        public static async Task<Response> UpdateAsync(Request request, Env environment)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/items/update", 405);

            ItemUpdateRequest? input;
            try
            {
                input = await request.JsonAsync<ItemUpdateRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null || input.Id <= 0)
                return Results.Error("A JSON body with a positive \"id\" is required", 400);

            return ToResponse(await ItemsService.UpdateAsync(environment.D1("DB"), input));
        }

        /// <summary>POST /api/items/delete removes one existing item.</summary>
        public static async Task<Response> DeleteAsync(Request request, Env environment)
        {
            if (request.Method != "POST")
                return Results.Error("Only POST is supported on /api/items/delete", 405);

            ItemDeleteRequest? input;
            try
            {
                input = await request.JsonAsync<ItemDeleteRequest>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON body", 400);
            }

            if (input is null || input.Id <= 0)
                return Results.Error("A JSON body with a positive \"id\" is required", 400);

            return ToResponse(await ItemsService.DeleteAsync(environment.D1("DB"), input));
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

            return ToResponse(await ItemsService.CreateAsync(environment.D1("DB"), input));
        }

        static Response ToResponse(ItemsService.Outcome outcome)
        {
            if (outcome.Error is not null)
                return Results.Error(outcome.Error, outcome.Status);

            return Response.Json(outcome.Result, 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
