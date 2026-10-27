namespace DeskGuard.Core.Serialization;

using System.Text.Json.Serialization;
using DeskGuard.Core.Models;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Default
)]
[JsonSerializable(typeof(EmailRecord))]
[JsonSerializable(typeof(List<EmailRecord>))]
[JsonSerializable(typeof(SecurityPenalty))]
[JsonSerializable(typeof(List<SecurityPenalty>))]
[JsonSerializable(typeof(SecurityVerdict))]
[JsonSerializable(typeof(SecurityEvent))]
[JsonSerializable(typeof(List<SecurityEvent>))]
[JsonSerializable(typeof(DuplicateItem))]
[JsonSerializable(typeof(List<DuplicateItem>))]
[JsonSerializable(typeof(DuplicateGroup))]
[JsonSerializable(typeof(List<DuplicateGroup>))]
[JsonSerializable(typeof(EngineStats))]
[JsonSerializable(typeof(ScanConfiguration))]
[JsonSerializable(typeof(ThresholdSettings))]
[JsonSerializable(typeof(SearchParameters))]
[JsonSerializable(typeof(SearchResult))]
[JsonSerializable(typeof(AttachmentInfo))]
[JsonSerializable(typeof(List<AttachmentInfo>))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(Dictionary<string, bool>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string[]))]
public sealed partial class DeskGuardJsonContext : JsonSerializerContext
{
}
