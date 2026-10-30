using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Shared;
using Workers;

namespace WorkersDotNet
{
    public sealed class PolicyEndpoint
    {
        readonly int MaxBodyBytes = 4096;

        public async Task<Response> HandleAsync(Request request)
        {
            if (request.Method != "POST")
            {
                var notAllowed = Results.Error("Method not allowed", 405);
                notAllowed.Headers.Set("allow", "POST");
                return notAllowed;
            }

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

            return Results.Ok("Reading accepted", new Url(request.Url).Pathname);
        }

        private bool HasJsonBody(Request request)
        {
            var contentType = request.Headers.Get("content-type");
            var contentLength = request.Headers.Get("content-length");

            if (contentType is null || !contentType.StartsWith("application/json") || contentLength is null)
                return false;

            if (!Text.IsDigits(contentLength, 1, 4) || contentLength[0] == '0')
                return false;

            return int.Parse(contentLength) <= MaxBodyBytes
                && request.Body is not null
                && !request.BodyUsed;
        }

        private bool IsValid(ReadingInput? input)
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
