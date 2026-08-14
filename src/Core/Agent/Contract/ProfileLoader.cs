using System.Text.Json;
using GEmuera.Core.Agent.Contract;

namespace GEmuera.Core.Agent.Contract;

public sealed class ProfileLoadOutcome
{
    public GameProfile? Profile { get; set; }
    public List<string> Errors { get; } = [];
    public bool Ok => Errors.Count == 0 && Profile != null;
}

/// <summary>
/// Profile 加载 + 校验。任何失败都以带字段名的错误列表表达，不抛异常。
/// </summary>
public static class ProfileLoader
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static ProfileLoadOutcome LoadFromJson(string json)
    {
        var outcome = new ProfileLoadOutcome();
        if (string.IsNullOrWhiteSpace(json))
        {
            outcome.Errors.Add("json is empty");
            return outcome;
        }

        GameProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<GameProfile>(json, Options);
        }
        catch (JsonException error)
        {
            outcome.Errors.Add("bad json: " + error.Message);
            return outcome;
        }
        if (profile == null)
        {
            outcome.Errors.Add("json deserialized to null");
            return outcome;
        }

        outcome.Profile = profile;
        Validate(profile, outcome.Errors);
        return outcome;
    }

    public static void Validate(GameProfile p, List<string> errors)
    {
        if (p.ProfileVersion != "0")
            errors.Add("profile_version: expected \"0\", got \"" + p.ProfileVersion + "\"");

        NonEmpty(p.Game.GameId, "game.game_id", errors);
        NonEmpty(p.Game.Title, "game.title", errors);
        NonEmpty(p.Game.GameCode, "game.game_code", errors);
        NonEmpty(p.Game.Version, "game.version", errors);

        if (p.EngineFlow != "train-type" && p.EngineFlow != "map-type")
            errors.Add("engine_flow: expected train-type or map-type, got \"" + p.EngineFlow + "\"");

        NonEmpty(p.CharacterCsv.Dir, "character_csv.dir", errors);
        NonEmpty(p.CharacterCsv.FilenamePattern, "character_csv.filename_pattern", errors);
        if (p.CharacterCsv.SemanticGroups.Talent.Length == 0)
            errors.Add("character_csv.semantic_groups.talent: required");
        if (p.CharacterCsv.SemanticGroups.Ability.Length == 0)
            errors.Add("character_csv.semantic_groups.ability: required");

        if (p.Koujou.Layers.Count == 0)
            errors.Add("koujou.layers: at least one layer required");
        foreach (var layer in p.Koujou.Layers)
        {
            NonEmpty(layer.Id, "koujou.layers[].id", errors);
            NonEmpty(layer.EntryLabel, "koujou.layers[].entry_label", errors);
            NonEmpty(layer.CharaLabelTemplate, "koujou.layers[].chara_label_template", errors);
        }

        if (p.Checkpoints.Primary != "Shop_Begin" && p.Checkpoints.Primary != "custom")
            errors.Add("checkpoints.primary: expected Shop_Begin or custom");
        if (p.EngineFlow == "map-type" && p.Checkpoints.Custom.Count == 0)
            errors.Add("checkpoints.custom: required for map-type games");

        if (p.Behaviors != null)
        {
            foreach (var b in p.Behaviors.Whitelist)
            {
                NonEmpty(b.Id, "behaviors.whitelist[].id", errors);
                if (b.Contexts.Length == 0)
                    errors.Add("behaviors.whitelist[" + b.Id + "].contexts: required");
                bool hasSetCflag = b.Effect.SetCflag != null;
                bool hasCallLabel = !string.IsNullOrEmpty(b.Effect.CallLabel);
                if (hasSetCflag == hasCallLabel)
                    errors.Add("behaviors.whitelist[" + b.Id + "].effect: exactly one of set_cflag/call_label required");
                if (hasSetCflag)
                {
                    NonEmpty(b.Effect.SetCflag!.Name, "behaviors.whitelist[" + b.Id + "].effect.set_cflag.name", errors);
                    if (b.Effect.SetCflag.Value.ValueKind == JsonValueKind.Undefined)
                        errors.Add("behaviors.whitelist[" + b.Id + "].effect.set_cflag.value: required");
                }
            }
        }

        if (p.Budget.TokensPerTurn < 0)
            errors.Add("budget.tokens_per_turn: must be >= 0");
        if (p.Budget.LlmCallsPerCheckpoint is < 0 or > 3)
            errors.Add("budget.llm_calls_per_checkpoint: must be 0..3");

        if (p.Fallback != "vanilla")
            errors.Add("fallback: expected \"vanilla\"");
    }

    static void NonEmpty(string value, string field, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(field + ": required");
    }
}
