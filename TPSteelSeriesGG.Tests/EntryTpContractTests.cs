using System.Text.Json;
using TPSteelSeriesGG;
using Xunit;

namespace TPSteelSeriesGG.Tests;

/// <summary>
/// Guards the contract between the code (TpIds) and the Touch Portal manifest (entry.tp).
/// Any id referenced by the code must exist in the manifest, and vice versa: a mismatch
/// here is exactly the class of bug that silently breaks user buttons at runtime.
/// Requires the test csproj to copy entry.tp next to the test binaries:
///   &lt;Content Include="..\entry.tp" Link="entry.tp" CopyToOutputDirectory="PreserveNewest" /&gt;
/// </summary>
public class EntryTpContractTests
{
    private static readonly JsonElement Entry = Load();

    private static JsonElement Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "entry.tp");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement Category => Entry.GetProperty("categories")[0];

    private static HashSet<string> IdsOf(string collection) =>
        Category.GetProperty(collection).EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!)
            .ToHashSet();

    // ---------------- Root ----------------

    [Fact]
    public void Manifest_TargetsApi10_WithThePluginId()
    {
        Assert.Equal(10, Entry.GetProperty("api").GetInt32());
        Assert.Equal(TpIds.Plugin, Entry.GetProperty("id").GetString());
        Assert.Equal(1, Entry.GetProperty("categories").GetArrayLength());
    }

    // ---------------- Set equality, both directions ----------------

    [Fact]
    public void States_MatchExactly() =>
        AssertSetEqual(TpIds.AllStateIds, IdsOf("states"), "state");

    [Fact]
    public void Actions_MatchExactly() =>
        AssertSetEqual(TpIds.AllActionIds, IdsOf("actions"), "action");

    [Fact]
    public void Events_MatchExactly() =>
        AssertSetEqual(TpIds.AllEventIds, IdsOf("events"), "event");

    [Fact]
    public void Connectors_MatchExactly() =>
        AssertSetEqual(TpIds.AllConnectorIds, IdsOf("connectors"), "connector");

    [Fact]
    public void Settings_MatchExactly()
    {
        var manifestNames = Entry.GetProperty("settings").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()!)
            .ToHashSet();
        AssertSetEqual(TpIds.AllSettingNames, manifestNames, "setting");
    }

    private static void AssertSetEqual(IReadOnlyList<string> declared, HashSet<string> manifest, string kind)
    {
        var declaredSet = declared.ToHashSet();
        Assert.True(declaredSet.Count == declared.Count, $"Duplicate {kind} ids declared in TpIds");

        var missingInManifest = declaredSet.Except(manifest).ToList();
        var missingInCode = manifest.Except(declaredSet).ToList();

        Assert.True(missingInManifest.Count == 0,
            $"Declared in TpIds but missing from entry.tp ({kind}): {string.Join(", ", missingInManifest)}");
        Assert.True(missingInCode.Count == 0,
            $"Present in entry.tp but not declared in TpIds ({kind}): {string.Join(", ", missingInCode)}");
    }

    // ---------------- Structure details ----------------

    [Fact]
    public void ActionData_MatchTheDeclaredIds()
    {
        foreach (var action in Category.GetProperty("actions").EnumerateArray())
        {
            string actionId = action.GetProperty("id").GetString()!;
            var manifestDataIds = action.GetProperty("data").EnumerateArray()
                .Select(d => d.GetProperty("id").GetString()!)
                .ToHashSet();

            Assert.True(TpIds.ActionDataIds.TryGetValue(actionId, out string[]? expected),
                $"Action {actionId} has no data declaration in TpIds.ActionDataIds");
            Assert.True(manifestDataIds.SetEquals(expected!),
                $"Data ids of {actionId} differ: manifest [{string.Join(", ", manifestDataIds)}] vs TpIds [{string.Join(", ", expected!)}]");
        }
    }

    [Fact]
    public void Events_FollowTheTwoRegimeRule()
    {
        // An event either has a dimension (dropdown: points to a declared trigger state,
        // offers "Any" first) or is parameterless (triggerEvent-fired: empty valueStateId,
        // no choices). Anything else is a manifest bug.
        var triggers = TpIds.AllTriggerStateIds.ToHashSet();
        var referenced = new HashSet<string>();

        foreach (var evt in Category.GetProperty("events").EnumerateArray())
        {
            string id = evt.GetProperty("id").GetString()!;
            string stateId = evt.GetProperty("valueStateId").GetString()!;
            var choices = evt.GetProperty("valueChoices").EnumerateArray()
                .Select(c => c.GetString()!)
                .ToList();

            if (stateId == "")
            {
                Assert.True(choices.Count == 0,
                    $"Parameterless event {id} must not declare choices (found {choices.Count})");
                continue;
            }

            Assert.True(triggers.Contains(stateId),
                $"Event {id} points to '{stateId}', which is not a declared trigger state");
            Assert.True(choices.Count >= 2 && choices[0] == "Any",
                $"Dropdown event {id} must offer 'Any' first plus at least one specific choice");
            Assert.Equal(choices.Count, choices.ToHashSet().Count);
            referenced.Add(stateId);
        }

        Assert.True(referenced.SetEquals(triggers),
            $"Unreferenced trigger states: {string.Join(", ", triggers.Except(referenced))}");
    }

    [Fact]
    public void TriggerStates_AreQuarantinedInTheInternalGroup()
    {
        var triggers = TpIds.AllTriggerStateIds.ToHashSet();
        foreach (var state in Category.GetProperty("states").EnumerateArray())
        {
            if (!triggers.Contains(state.GetProperty("id").GetString()!)) continue;
            Assert.Equal("Internal (event triggers)", state.GetProperty("parentGroup").GetString());
            Assert.Equal("", state.GetProperty("default").GetString());
        }
    }

    [Fact]
    public void AllIds_AreUniqueAcrossTheManifest()
    {
        var all = IdsOf("states").Concat(IdsOf("actions")).Concat(IdsOf("events")).Concat(IdsOf("connectors")).ToList();
        Assert.Equal(all.Count, all.ToHashSet().Count);
    }

    [Fact]
    public void SubCategories_CoverEverySubCategoryReference()
    {
        var declared = Category.GetProperty("subCategories").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()!)
            .ToHashSet();

        foreach (string collection in new[] { "actions", "connectors" })
        {
            foreach (var item in Category.GetProperty(collection).EnumerateArray())
            {
                if (!item.TryGetProperty("subCategoryId", out var sub)) continue;
                Assert.Contains(sub.GetString()!, declared);
            }
        }
    }
}