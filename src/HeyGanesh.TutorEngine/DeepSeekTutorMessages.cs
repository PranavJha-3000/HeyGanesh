using System.Text.Json.Nodes;

namespace HeyGanesh.TutorEngine;

/// <summary>
/// Builds the Chat Completions <c>messages</c> array for one rendered <see cref="TutorPrompt"/>.
/// Lives in the module (not inside the HTTP client), mirroring the Vision leg's <c>VisionPrompt</c>
/// split, so the wire payload shape is reviewable without transport and a proxy-shaped adapter could
/// reuse the same mapping. (DEC-017.)
/// </summary>
/// <remarks>
/// The prompt arrives fully rendered and deterministic (TU-10) — nothing here re-renders, reorders,
/// truncates or annotates it. The system block is plain string content; the user block is a plain
/// string when no visual is attached and the verified two-part content array
/// (<c>text</c> + <c>image_url</c> with a <c>data:image/jpeg;base64,</c> URL) when one is. The JPEG
/// bytes are base64-encoded as-is: the tutor holds no decoder and must not grow one — the visual's
/// caps and JPEG contract are enforced upstream by <see cref="TutorService"/>.
/// </remarks>
internal static class DeepSeekTutorMessages
{
    internal static JsonArray Build(TutorPrompt prompt)
    {
        JsonNode userContent = prompt.Visual is null
            ? JsonValue.Create(prompt.UserText)!
            : new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = prompt.UserText },
                new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject
                    {
                        ["url"] = $"data:image/jpeg;base64,{Convert.ToBase64String(prompt.Visual.JpegBytes)}",
                    },
                },
            };

        return new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = prompt.SystemText },
            new JsonObject { ["role"] = "user", ["content"] = userContent },
        };
    }

    /// <summary>Attaches the caller's image-detail policy to an already-built array's image part.</summary>
    internal static void ApplyImageDetail(JsonArray messages, string detail)
    {
        if (messages[1]?["content"] is not JsonArray parts)
        {
            return; // text-only turn: nothing to tag
        }

        foreach (var part in parts)
        {
            if (part?["type"]?.GetValue<string>() == "image_url" && part["image_url"] is JsonObject imageUrl)
            {
                imageUrl["detail"] = detail;
            }
        }
    }
}