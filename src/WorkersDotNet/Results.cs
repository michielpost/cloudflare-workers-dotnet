using Shared;
using Workers;

namespace WorkersDotNet
{
    /// <summary>
    /// Helpers for returning JSON responses, the main response shape used by
    /// the API endpoints.
    /// </summary>
    /// <remarks>
    /// The Workers compiler only supports concrete parameter types, so these
    /// helpers expose overloads rather than a generic <c>Json&lt;T&gt;</c>.
    /// </remarks>
    public static class Results
    {
        public static Response Ok(ApiResponse value)
        {
            return Response.Json(value, 200);
        }

        public static Response Ok(string message, string path)
        {
            return Response.Json(new ApiResponse(true, message, path, DateTimeOffset.UtcNow), 200);
        }

        public static Response Json(ApiResponse value, int status)
        {
            return Response.Json(value, status);
        }

        public static Response Error(string message, int status)
        {
            return Response.Json(new { error = message, requestId = Guid.NewGuid() }, status);
        }
    }
}
