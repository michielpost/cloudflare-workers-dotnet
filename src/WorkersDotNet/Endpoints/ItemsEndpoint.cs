using System.Collections.Generic;
using System.Threading.Tasks;
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
    public sealed class ItemsEndpoint
    {
        private readonly ItemsService _items;

        public ItemsEndpoint(ItemsService items)
        {
            _items = items;
        }

        public async Task<Response> HandleAsync(Request request)
        {
            if (request.Method == "POST")
                return await CreateAsync(request);

            if (request.Method != "GET")
                return Results.Error("Only GET and POST are supported on /api/items", 405);

            return Response.Json(await _items.ReadSnapshotAsync(), 200)
                .WithHeader("cache-control", "no-store");
        }

        /// <summary>POST /api/items/update edits one existing item.</summary>
        public async Task<Response> UpdateAsync(Request request)
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

            return ToResponse(await _items.UpdateAsync(input));
        }

        /// <summary>POST /api/items/delete removes one existing item.</summary>
        public async Task<Response> DeleteAsync(Request request)
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

            return ToResponse(await _items.DeleteAsync(input));
        }

        async Task<Response> CreateAsync(Request request)
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

            return ToResponse(await _items.CreateAsync(input));
        }

        static Response ToResponse(ItemsOutcome outcome)
        {
            if (outcome.Error is not null)
                return Results.Error(outcome.Error, outcome.Status);

            return Response.Json(outcome.Result, 200)
                .WithHeader("cache-control", "no-store");
        }
    }
}
