using System.Text.Json.Nodes;

namespace HeyGanesh.Vision;

/// <summary>
/// Builds the messages VisionService hands to the provider. Lives in the Vision module (not in
/// DeepSeekVisionClient) so a different <see cref="IVisionClient"/> implementation -- e.g. an
/// Anthropic-shapes adapter for a proxy -- can still consume the same prompts, and so the prompt
/// contract is unit-reviewable without touching HTTP.
/// </summary>
internal sealed class VisionPrompt
{
    /// <summary>
    /// The system prompt. It (a) contains the word "json" and a schema example so the provider's JSON
    /// output mode is satisfied, (b) pins the 0..1000 coordinate convention so the model cannot drift
    /// into pixel space or prose, and (c) states the image's exact pixel dimensions so grid values
    /// unambiguously map back to the JPEG.
    /// </summary>
    public string SystemPrompt(int imageWidth, int imageHeight) =>
        $$"""
        You are a precise UI screenshot analyst. I will show you a JPEG of a computer desktop and ask you
        to locate the UI element described by the question. Answer with a single JSON object -- no prose,
        no code fences, just the JSON document.

        Image dimensions: {{imageWidth}}x{{imageHeight}} pixels. (0,0) is the top-left of this image; x grows
        rightward, y downward. Express every coordinate on a 0..1000 integer grid over EACH axis, so the
        image's top-left corner is (0,0) and bottom-right is (1000,1000). A grid value g maps to a pixel
        coordinate round(g/1000 * dimension). Never report pixels directly and never report prose
        coordinates -- only integers in 0..1000.

        JSON schema (exactly these keys):

        {
          "found": true,
          "element": "a short human description of what was located, or null when not found",
          "box": { "x1": 0..1000, "y1": 0..1000, "x2": 0..1000, "y2": 0..1000 },
          "confidence": 0.9,
          "note": "optional brief reason, e.g. why something was not found"
        }

        When the element cannot be located, emit:
        { "found": false, "element": null, "box": null, "confidence": 0.0, "note": "..." }.
        Do not hallucinate a box when nothing matches. Your "confidence" is your own belief that the box
        actually covers the requested element, 0..1.
        """;

        /// <summary>
    /// Assembles the full system + user messages (including the image as a data URL) for a provider
    /// call. Centralising this here keeps prompt text reviewable without touching HTTP, and lets the
    /// same messages be handed to any IVisionClient implementation (direct DeepSeek or a proxy adapter).
    /// </summary>
    internal JsonArray BuildMessages(int imageWidth, int imageHeight, string question, byte[] jpegBytes, string detail)
    {
        var dataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(jpegBytes)}";

        return new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = SystemPrompt(imageWidth, imageHeight),
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = question },
                    new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = dataUrl,
                            ["detail"] = detail,
                        },
                    },
                },
            },
        };
    }
}
