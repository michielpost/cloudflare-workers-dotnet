using System.Text.RegularExpressions;
using Shared;
using Workers;

namespace WorkersDotNet
{
    public static class PolicyEndpoint
    {
        private const int MaxBodyBytes = 4096;

        public static async Task<Response> HandleAsync(Request request)
        {
            if (request.Method != "POST")
                return Results.Error("Method not allowed", 405).WithHeader("allow", "POST");

            if (!HasJsonBody(request))
                return Results.Error("A bounded JSON body is required", 400);

            ReadingInput? input;
            try
            {
                input = await request.JsonAsync<ReadingInput>();
            }
            catch (Exception)
            {
                return Results.Error("Malformed JSON", 400);
            }

            if (!IsValid(input))
                return Results.Error("Invalid reading", 400);

            return Results.Ok("Reading accepted", request.Path);
        }

        private static bool HasJsonBody(Request request)
        {
            var contentType = request.Headers.Get("content-type");
            var contentLength = request.Headers.Get("content-length");

            if (contentType is null || !contentType.StartsWith("application/json") || contentLength is null)
                return false;

            if (!Regex.IsMatch(contentLength, "^[1-9][0-9]{0,3}$"))
                return false;

            return int.Parse(contentLength) <= MaxBodyBytes
                && request.BodyStream() is not null
                && !request.BodyUsed;
        }

        private static bool IsValid(ReadingInput? input)
        {
            if (input is null || input.DeviceId.Length == 0)
                return false;
            if (input.Value < -100 || input.Value > 1000 || input.Tags.Count > 8)
                return false;

            var tags = new HashSet<string>();
            foreach (var tag in input.Tags)
                if (tag.Length == 0 || tag.Length > 24 || !tags.Add(tag))
                    return false;
            return true;
        }
    }
}
