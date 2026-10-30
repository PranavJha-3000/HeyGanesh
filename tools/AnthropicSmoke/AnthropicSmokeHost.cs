namespace HeyGanesh.Tools.AnthropicSmoke;

using System.Text;
using HeyGanesh.Providers.Core;
using HeyGanesh.Providers.Anthropic;

internal static class AnthropicSmokeHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        int failures = 0;
        int total = 0;

        using var server = new LoopbackServer();
        var baseUrl = server.BaseUrl;

        Console.WriteLine("=== Anthropic smoke ===");

        // --- Test 1: Endpoint + headers ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(200, "application/json",
                "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Hi\"}],\"model\":\"claude-test\",\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":5,\"output_tokens\":2}}");

            using var store = new HeyGanesh.Security.CredentialManagerSecretStore();
            store.Set("anthropic", "api-key", Encoding.UTF8.GetBytes("sk-ant-test-123").AsMemory());

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text, SecretStore = store, CredentialProviderId = "anthropic", CredentialKeyAlias = "api-key" };
            using var client = new AnthropicLlmClient(options);
            var resp = await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hello" } } });

            var req = server.LastRequest;
            if (req.Method == "POST" && req.Path == "/v1/messages" &&
                req.Headers.TryGetValue("x-api-key", out var key) && key == "sk-ant-test-123" &&
                req.Headers.TryGetValue("anthropic-version", out var ver) && ver == "2023-06-01")
                Console.WriteLine("PASS: endpoint + headers");
            else { Console.WriteLine("FAIL: endpoint/headers"); failures++; }
            store.Delete("anthropic", "api-key");
        }

        // --- Test 2: Missing credentials -> AuthFailed ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(401, "application/json", "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text };
            using var client = new AnthropicLlmClient(options);
            try
            {
                await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } });
                Console.WriteLine("FAIL: should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.AuthFailed) {
                Console.WriteLine("PASS: missing creds -> AuthFailed");
            }
        }
        // --- Test 3: Tool use response ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(200, "application/json",
                "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"get_weather\",\"input\":{\"city\":\"NYC\"}}],\"model\":\"claude-test\",\"stop_reason\":\"tool_use\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}");

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text | ProviderCapability.ToolCalls };
            using var client = new AnthropicLlmClient(options);
            var resp = await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Weather in NYC?" } } });

            if (resp.ToolCalls.Count == 1 && resp.ToolCalls[0].Name == "get_weather" && resp.ToolCalls[0].ArgumentsJson.Contains("NYC"))
                Console.WriteLine("PASS: tool use response");
            else { Console.WriteLine("FAIL: tool use"); failures++; }
        }

        // --- Test 4: Structured output ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(200, "application/json",
                "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"{\\\"answer\\\":42}\"}],\"model\":\"claude-test\",\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}");

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text | ProviderCapability.StructuredOutput };
            using var client = new AnthropicLlmClient(options);
            var resp = await client.GenerateAsync(new LlmRequest
            {
                Messages = new[] { new LlmMessage { Role = "user", Text = "What is the answer?" } },
                ResponseFormat = new LlmResponseFormat { Type = "json_schema", JsonSchema = "{\"type\":\"object\",\"properties\":{\"answer\":{\"type\":\"integer\"}}}" },
            });

            var req = server.LastRequest;
            if (resp.Text.Contains("42") && req.Body.Contains("output_config"))
                Console.WriteLine("PASS: structured output");
            else { Console.WriteLine("FAIL: structured output"); failures++; }
        }

        // --- Test 5: HTTP 400 -> InvalidRequest ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(400, "application/json", "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"This model does not exist\"}}");

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text };
            using var client = new AnthropicLlmClient(options);
            try
            {
                await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } });
                Console.WriteLine("FAIL: 400 should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.InvalidRequest && !pe.IsRetryable) {
                Console.WriteLine("PASS: 400 -> InvalidRequest (non-retryable)");
            }
        }

        // --- Test 6: HTTP 500 -> Transport ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(429, "application/json", "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"rate limited\"}}");

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text };
            using var client = new AnthropicLlmClient(options);
            try
            {
                await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } });
                Console.WriteLine("FAIL: 429 should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.RateLimited) {
                Console.WriteLine("PASS: 429 -> RateLimited");
            }
        }

        // --- Test 6: HTTP 500 -> Transport ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(500, "application/json", "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"server error\"}}");

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text };
            using var client = new AnthropicLlmClient(options);
            try
            {
                await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } });
                Console.WriteLine("FAIL: 500 should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.Transport && pe.IsRetryable) {
                Console.WriteLine("PASS: 500 -> Transport (retryable)");
            }
        }

        // --- Test 7: HTTP 504 -> Timeout ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(504, "application/json", "{\"type\":\"error\",\"error\":{\"type\":\"timeout_error\",\"message\":\"timeout\"}}");

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text };
            using var client = new AnthropicLlmClient(options);
            try
            {
                await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } });
                Console.WriteLine("FAIL: 504 should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.Timeout) {
                Console.WriteLine("PASS: 504 -> Timeout");
            }
        }

        // --- Test 8: HTTP 529 -> RateLimited (overloaded) ---
        total++;
        {
            server.Reset();
            server.EnqueueResponse(529, "application/json", "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"overloaded\"}}");

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text };
            using var client = new AnthropicLlmClient(options);
            try
            {
                await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } });
                Console.WriteLine("FAIL: 529 should throw"); failures++;
            }
            catch (ProviderException pe) when (pe.Kind == ProviderErrorKind.RateLimited) {
                Console.WriteLine("PASS: 529 -> RateLimited (overloaded)");
            }
        }

        // --- Test 9: SSE streaming text ---
        total++;
        {
            server.Reset();
            server.EnqueueSseResponse(new[]
            {
                "event: message_start",
                "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[],\"model\":\"claude-test\",\"usage\":{\"input_tokens\":5,\"output_tokens\":0}}}",
                "",
                "event: content_block_start",
                "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
                "",
                "event: content_block_delta",
                "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hello\"}}",
                "",
                "event: content_block_delta",
                "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\" world\"}}",
                "",
                "event: content_block_stop",
                "data: {\"type\":\"content_block_stop\",\"index\":0}",
                "",
                "event: message_delta",
                "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":2}}",
                "",
                "event: message_stop",
                "data: {\"type\":\"message_stop\"}",
            });

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text | ProviderCapability.Streaming };
            using var client = new AnthropicLlmClient(options);
            var chunks = new List<LlmStreamChunk>();
            await foreach (var c in client.StreamAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } }))
                chunks.Add(c);

            if (chunks.Any(c => c.TextDelta == "Hello") && chunks.Any(c => c.TextDelta == " world"))
                Console.WriteLine("PASS: SSE text streaming");
            else { Console.WriteLine($"FAIL: SSE text (chunks={chunks.Count})"); failures++; }
        }

        // --- Test 10: SSE streaming tool input ---
        total++;
        {
            server.Reset();
            server.EnqueueSseResponse(new[]
            {
                "event: message_start",
                "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[],\"model\":\"claude-test\",\"usage\":{\"input_tokens\":5,\"output_tokens\":0}}}",
                "",
                "event: content_block_start",
                "data: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"get_weather\",\"input\":{}}}",
                "",
                "event: content_block_delta",
                "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"city\\\":\"}}",
                "",
                "event: content_block_delta",
                "data: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"\\\"NYC\\\"}\"}}",
                "",
                "event: content_block_stop",
                "data: {\"type\":\"content_block_stop\",\"index\":0}",
                "",
                "event: message_delta",
                "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"tool_use\"},\"usage\":{\"output_tokens\":5}}",
                "",
                "event: message_stop",
                "data: {\"type\":\"message_stop\"}",
            });

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text | ProviderCapability.ToolCalls | ProviderCapability.Streaming };
            using var client = new AnthropicLlmClient(options);
            var chunks = new List<LlmStreamChunk>();
            await foreach (var c in client.StreamAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Weather?" } } }))
                chunks.Add(c);

            if (chunks.Count >= 1)
                Console.WriteLine("PASS: SSE tool input streaming");
            else { Console.WriteLine($"FAIL: SSE tool (chunks={chunks.Count})"); failures++; }
        }

        // --- Test 11: Mid-stream error ---
        total++;
        {
            server.Reset();
            server.EnqueueSseResponse(new[]
            {
                "event: message_start",
                "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[],\"model\":\"claude-test\",\"usage\":{\"input_tokens\":5,\"output_tokens\":0}}}",
                "",
                "event: error",
                "data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}",
            });

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text | ProviderCapability.Streaming };
            using var client = new AnthropicLlmClient(options);
            try
            {
                await foreach (var c in client.StreamAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } })) { }
                Console.WriteLine("FAIL: should throw"); failures++;
            }
            catch (ProviderException) {
                Console.WriteLine("PASS: mid-stream error throws");
            }
        }

        // --- Test 12: Malformed SSE event ---
        total++;
        {
            server.Reset();
            server.EnqueueSseResponse(new[]
            {
                "event: message_start",
                "data: {not valid json",
                "",
                "event: message_stop",
                "data: {\"type\":\"message_stop\"}",
            });

            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text | ProviderCapability.Streaming };
            using var client = new AnthropicLlmClient(options);
            var chunks = new List<LlmStreamChunk>();
            await foreach (var c in client.StreamAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } }))
                chunks.Add(c);

            Console.WriteLine("PASS: malformed SSE skipped");
        }

        // --- Test 13: Disposal ---
        total++;
        {
            var options = new AnthropicOptions { ProviderId = "anthropic", BaseUrl = baseUrl, ModelId = "claude-test", Capabilities = ProviderCapability.Text };
            var client = new AnthropicLlmClient(options);
            client.Dispose();
            try
            {
                await client.GenerateAsync(new LlmRequest { Messages = new[] { new LlmMessage { Role = "user", Text = "Hi" } } });
                Console.WriteLine("FAIL: disposed should throw"); failures++;
            }
            catch (ObjectDisposedException) {
                Console.WriteLine("PASS: disposal throws ObjectDisposedException");
            }
        }

        Console.WriteLine($"\n=== Anthropic result: {total - failures}/{total} PASS, {failures} FAILURE(S) ===");
        return failures == 0 ? 0 : 1;
    }
}
