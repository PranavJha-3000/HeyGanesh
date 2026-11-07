using HeyGanesh.Tools.ClaudeCodeSmoke;

// The offline rows start this same exe as the `claude` CLI; the variable decides which role it plays.
return Environment.GetEnvironmentVariable(FakeClaude.ModeVariable) is { Length: > 0 } mode
    ? FakeClaude.Run(mode, args)
    : await ClaudeCodeSmokeHost.RunAsync(args);
