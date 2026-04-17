using System.Text.Json;
using DopeCompanion.Core.Models;

namespace DopeCompanion.Core.Services;

public sealed class DopeControllerBreathingProfileApplyStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _statePath;

    public DopeControllerBreathingProfileApplyStateStore(string studyId, string? stateRoot = null)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            throw new ArgumentException("A study id is required for the Dope controller-breathing apply-state store.", nameof(studyId));
        }

        var root = stateRoot ?? CompanionOperatorDataLayout.SessionRootPath;
        Directory.CreateDirectory(root);
        _statePath = Path.Combine(root, $"dope-controller-breathing-apply-{SanitizeToken(studyId)}.json");
    }

    public DopeControllerBreathingProfileApplyRecord? Load()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return null;
            }

            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<DopeControllerBreathingProfileApplyRecord>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(DopeControllerBreathingProfileApplyRecord? record)
    {
        try
        {
            if (record is null)
            {
                if (File.Exists(_statePath))
                {
                    File.Delete(_statePath);
                }

                return;
            }

            var json = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(_statePath, json);
        }
        catch
        {
            // Best-effort persistence only.
        }
    }

    private static string SanitizeToken(string value)
    {
        var characters = value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        return new string(characters).Trim('-');
    }
}

