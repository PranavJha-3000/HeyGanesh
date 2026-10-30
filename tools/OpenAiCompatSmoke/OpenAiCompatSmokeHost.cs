namespace HeyGanesh.Tools.OpenAiCompatSmoke;

using System.Text;
using HeyGanesh.Providers.Core;
using HeyGanesh.Providers.OpenAiCompat;

internal static class OpenAiCompatSmokeHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        int failures = 0;
        int total = 0;

        using var server = new LoopbackServer();
        var baseUrl = server.BaseUrl;

        Console.WriteLine("=== OpenAiCompat smoke ===");

        // --- Test 1: Non-streaming success ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(200, "application/json",
                "{\"id\":\"chatcmpl-1\",\"model\":\"test-model\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"Hello, world!\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5}}");

            var options = new OpenAiCompatOptions { ProviderId = "test", BaseUrl = baseUrl, ModelId = "test-model", Capabilities = ProviderCapability.Text };
            using var client = new OpenAiCompatLlmClient(options);
            var resp = await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } }, SystemPrompt = "You are helpful." });

            if (resp.Text == "Hello, world!" && resp.InputTokens == 10 && resp.OutputTokens == 5)
            {
                var req = server.LastRequest;
                if (req.Method == "POST" && req.Path == "/chat/completions" && req.Body.Contains("\"model\":\"test-model\""))
                    Console.WriteLine("PASS: non-streaming success");
                else { Console.WriteLine($"FAIL: request (method={req.Method}, path={req.Path})"); failures++; }
            }
            else { Console.WriteLine($"FAIL: response (text={resp.Text}, in={resp.InputTokens}, out={resp.OutputTokens})"); failures++; }
        }

        // --- Test 16: Text-only regression (no images) ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(200, "application/json", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");

            var opts16 = new OpenAiCompatOptions { ProviderId = "test", BaseUrl = baseUrl, ModelId = "test-model", Capabilities = ProviderCapability.Text };
            using var client16 = new OpenAiCompatLlmClient(opts16);
            await client16.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hello" } } });

            var req = server.LastRequest;
            if (req.Body.Contains("\"content\":\"Hello\"") && !req.Body.Contains("\"content\":["))
                Console.WriteLine("PASS: text-only regression");
            else { Console.WriteLine($"FAIL: text-only (body={req.Body})"); failures++; }
        }

        // --- Test 17: Text plus image (URL) ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(200, "application/json", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");

            var opts17 = new OpenAiCompatOptions { ProviderId = "test", BaseUrl = baseUrl, ModelId = "test-model", Capabilities = ProviderCapability.Text };
            using var client17 = new OpenAiCompatLlmClient(opts17);
            await client17.GenerateAsync(new LlmRequest
            {
                Messages = new[] { new LlmMessage { Role = "user", Text = "What is this?", Images = new[] { new LlmImagePart { MimeType = "image/jpeg", Data = "https://example.com/img.jpg" } } } },
            });

            var req = server.LastRequest;
            if (req.Body.Contains("\"type\":\"text\"") && req.Body.Contains("\"text\":\"What is this?\"") &&
                req.Body.Contains("\"type\":\"image_url\"") && req.Body.Contains("\"url\":\"https://example.com/img.jpg\""))
                Console.WriteLine("PASS: text plus image (URL)");
            else { Console.WriteLine($"FAIL: text+image (body={req.Body})"); failures++; }
        }
        // --- Test 18: Image-only content (base64) ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(200, "application/json", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");

            var opts18 = new OpenAiCompatOptions { ProviderId = "test", BaseUrl = baseUrl, ModelId = "test-model", Capabilities = ProviderCapability.Text };
            using var client18 = new OpenAiCompatLlmClient(opts18);
            await client18.GenerateAsync(new LlmRequest
            {
                Messages = new[] { new LlmMessage { Role = "user", Images = new[] { new LlmImagePart { MimeType = "image/png", Data = "iVBORw0KGgo=" } } } },
            });

            var req = server.LastRequest;
            if (req.Body.Contains("\"content\":[") && req.Body.Contains("\"type\":\"image_url\"") && req.Body.Contains("data:image/png;base64,iVBORw0KGgo="))
                Console.WriteLine("PASS: image-only (base64 data URL)");
            else { Console.WriteLine($"FAIL: image-only (body={req.Body})"); failures++; }
        }

        // --- Test 19: Image data URL passthrough ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(200, "application/json", "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}");

            var opts19 = new OpenAiCompatOptions { ProviderId = "test", BaseUrl = baseUrl, ModelId = "test-model", Capabilities = ProviderCapability.Text };
            using var client19 = new OpenAiCompatLlmClient(opts19);
            await client19.GenerateAsync(new LlmRequest
            {
                Messages = new[] { new LlmMessage { Role = "user", Text = "Describe", Images = new[] { new LlmImagePart { MimeType = "image/jpeg", Data = "data:image/jpeg;base64,/9j/4AAQ" } } } },
            });

            var req = server.LastRequest;
            if (req.Body.Contains("data:image/jpeg;base64,/9j/4AAQ"))
                Console.WriteLine("PASS: image data URL passthrough");
            else { Console.WriteLine($"FAIL: data URL (body={req.Body})"); failures++; }
        }

        // --- Test 20: Malformed image input (empty data) ---
        total++;
        {
            var opts20 = new OpenAiCompatOptions { ProviderId = "test", BaseUrl = baseUrl, ModelId = "test-model", Capabilities = ProviderCapability.Text };
            using var client20 = new OpenAiCompatLlmClient(opts20);
            try
            {
                await client20.GenerateAsync(new LlmRequest
                {
                    Messages = new[] { new LlmMessage { Role = "user", Images = new[] { new LlmImagePart { MimeType = "image/jpeg", Data = "" } } } },
                });
                Console.WriteLine("FAIL: empty image data should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.InvalidRequest)
            {
                Console.WriteLine("PASS: malformed image (empty data) throws InvalidRequest");
            }
        }

        // --- Test 21: Malformed image input (bad MIME) ---
        total++;
        {
            var opts21 = new OpenAiCompatOptions { ProviderId = "test", BaseUrl = baseUrl, ModelId = "test-model", Capabilities = ProviderCapability.Text };
            using var client21 = new OpenAiCompatLlmClient(opts21);
            try
            {
                await client21.GenerateAsync(new LlmRequest
                {
                    Messages = new[] { new LlmMessage { Role = "user", Images = new[] { new LlmImagePart { MimeType = "not-a-mime", Data = "AAAA" } } } },
                });
                Console.WriteLine("FAIL: bad MIME should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.InvalidRequest)
            {
                Console.WriteLine("PASS: malformed image (bad MIME) throws InvalidRequest");
            }
        }

        // --- Test 22: HTTP 400 -> InvalidRequest ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(400, "application/json", "{\"error\":{\"message\":\"Bad request\",\"type\":\"invalid_request_error\"}}");

            var opts22 = new OpenAiCompatOptions { ProviderId = "test", BaseUrl = baseUrl, ModelId = "test-model", Capabilities = ProviderCapability.Text };
            using var client22 = new OpenAiCompatLlmClient(opts22);
            try
            {
                await client22.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } });
                Console.WriteLine("FAIL: 400 should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.InvalidRequest && !pe.IsRetryable)
            {
                Console.WriteLine("PASS: 400 -> InvalidRequest (non-retryable)");
            }
        }

        Console.WriteLine($"\n=== OpenAiCompat result: {total - failures}/{total} PASS, {failures} FAILURE(S) ===");
        return failures == 0 ? 0 : 1;
    }
}
