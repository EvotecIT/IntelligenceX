using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Microsoft.AspNetCore.Http.Features;

namespace IntelligenceX.Ledger.Hosting;

internal static partial class CoordinationEndpoints {
    /// <summary>
    /// Upload and download of the images asks and results carry. Any coordination principal (an asking session's client,
    /// a machine's agent service, the operator) may upload one and read one by id; an enrolled requester may not. The
    /// service's 1 MiB request limit is raised for this route only, to the image limit.
    /// </summary>
    private static void MapImages(WebApplication app, CoordinationImageStore images) {
        app.MapPost(Prefix + "/images", async (HttpContext http, CancellationToken ct) => {
            if (!MayUseImages(Caller(http))) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                limit.MaxRequestBodySize = CoordinationImage.MaximumBytes + 1;
            if (http.Request.ContentLength > CoordinationImage.MaximumBytes)
                return Results.Conflict(new { error = "image-too-large" });
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await http.Request.Body.ReadAsync(chunk, ct)) > 0) {
                if (buffer.Length + read > CoordinationImage.MaximumBytes) return Results.Conflict(new { error = "image-too-large" });
                buffer.Write(chunk, 0, read);
            }
            return Results.Ok(images.Put(buffer.ToArray(), DateTimeOffset.UtcNow));
        });
        app.MapGet(Prefix + "/images/{id}", (string id, HttpContext http) =>
            !MayUseImages(Caller(http)) ? Results.StatusCode(StatusCodes.Status403Forbidden)
            : images.Find(id) is { } found ? Results.File(found.Path, found.MediaType, enableRangeProcessing: false)
            : Results.NotFound());
    }

    private static bool MayUseImages(CoordinationPrincipal caller) =>
        caller.Role is "client" or "host" or "operator" && caller.RequesterId is null;
}
