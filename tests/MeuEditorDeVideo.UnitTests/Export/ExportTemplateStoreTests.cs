using System.IO;
using System.Text.Json;
using MeuEditorDeVideo.Core.Export;
using MeuEditorDeVideo.Domain.Model;
using MeuEditorDeVideo.Domain.Time;
using MeuEditorDeVideo.Infrastructure.Export;
using MeuEditorDeVideo.ProjectEngine;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuEditorDeVideo.UnitTests.Export;

public sealed class ExportTemplateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "med-templates-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _userDir;
    private readonly string _builtInDir;

    public ExportTemplateStoreTests()
    {
        _userDir = Path.Combine(_root, "user");
        _builtInDir = FindRepositoryBuiltInDirectory();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private JsonExportTemplateStore Create() =>
        new(_userDir, _builtInDir, NullLogger<JsonExportTemplateStore>.Instance);

    private static string FindRepositoryBuiltInDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "resources", "templates", "export");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("Pasta resources\\templates\\export não encontrada a partir do diretório de saída.");
    }

    private static ExportTemplate Template(string id, string name, ExportSettings? settings = null) => new()
    {
        Id = id,
        Name = name,
        Description = "Descrição de teste",
        Settings = settings ?? new ExportSettings(),
    };

    private static ExportTemplate BuiltIn(IReadOnlyList<ExportTemplate> all, string id) =>
        all.Single(t => t.Id == id);

    // ------------------------------------------------------------ embutidos

    [Fact]
    public void Built_in_templates_are_the_twelve_shipped_ones_in_order()
    {
        var all = Create().GetAll();

        Assert.Equal(12, all.Count);
        Assert.All(all, t => Assert.True(t.IsBuiltIn));
        var expected = Enumerable.Range(1, 12).Select(i => $"EXP{i:000}").ToArray();
        Assert.Equal(expected, all.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void Every_built_in_template_is_valid()
    {
        foreach (var t in Create().GetAll())
            Assert.Empty(ExportFormats.Validate(t.Settings, new ProjectSettings()));
    }

    [Fact]
    public void Exp001_is_youtube_full_hd_keeping_project_frame_rate()
    {
        var s = BuiltIn(Create().GetAll(), "EXP001").Settings;

        Assert.Equal(1920, s.Width);
        Assert.Equal(1080, s.Height);
        Assert.Equal(8000, s.VideoBitrateKbps);
        Assert.Equal(AudioCodec.Aac, s.AudioCodec);
        Assert.Equal(192, s.AudioBitrateKbps);
        Assert.Null(s.FrameRate);
    }

    [Fact]
    public void Exp002_is_vertical_shorts_at_30_fps()
    {
        var s = BuiltIn(Create().GetAll(), "EXP002").Settings;

        Assert.Equal(1080, s.Width);
        Assert.Equal(1920, s.Height);
        Assert.Equal(new FrameRate(30, 1), s.FrameRate);
    }

    [Fact]
    public void Exp006_is_whatsapp_status_split_every_60_seconds()
    {
        var s = BuiltIn(Create().GetAll(), "EXP006").Settings;

        Assert.Equal(720, s.Width);
        Assert.Equal(1280, s.Height);
        Assert.Equal(2000, s.VideoBitrateKbps);
        Assert.Equal(60.0, s.MaxSecondsPerFile);
        Assert.Equal("main", s.Profile);
    }

    [Fact]
    public void Exp007_targets_16_megabytes()
    {
        var s = BuiltIn(Create().GetAll(), "EXP007").Settings;

        Assert.Equal(16.0, s.TargetSizeMegabytes);
    }

    [Fact]
    public void Exp011_uses_constant_quality_and_256_kbps_audio()
    {
        var s = BuiltIn(Create().GetAll(), "EXP011").Settings;

        Assert.Equal(RateControl.ConstantQuality, s.RateControl);
        Assert.Equal(256, s.AudioBitrateKbps);
    }

    [Fact]
    public void Exp012_is_custom_and_inherits_resolution_from_project()
    {
        var s = BuiltIn(Create().GetAll(), "EXP012").Settings;

        Assert.Null(s.Width);
        Assert.Null(s.Height);
    }

    // ------------------------------------------------------------ usuário

    [Fact]
    public async Task Saved_user_template_appears_after_built_ins_and_round_trips_settings()
    {
        var store = Create();
        var settings = new ExportSettings
        {
            Container = ExportContainer.Mkv,
            VideoCodec = VideoCodec.Hevc,
            Encoder = EncoderPreference.Software,
            Width = 1280,
            Height = null,
            FrameRate = new FrameRate(30000, 1001),
            RateControl = RateControl.Cbr,
            Profile = null,
            AudioCodec = AudioCodec.Opus,
            AudioBitrateKbps = 160,
            MaxSecondsPerFile = 90.5,
            TargetSizeMegabytes = null,
        };
        var id = JsonExportTemplateStore.NewUserId();

        await store.SaveAsync(Template(id, "Meu modelo", settings));

        var all = store.GetAll();
        var saved = all[^1];
        Assert.Equal(13, all.Count);
        Assert.Equal(id, saved.Id);
        Assert.False(saved.IsBuiltIn);
        Assert.Equal("Meu modelo", saved.Name);
        Assert.Equal("Descrição de teste", saved.Description);
        Assert.Equal(settings, saved.Settings);
        Assert.Equal(new FrameRate(30000, 1001), saved.Settings.FrameRate);
    }

    [Fact]
    public async Task Saving_with_a_built_in_id_overrides_it_but_keeps_the_built_in_flag()
    {
        var store = Create();
        var custom = new ExportSettings { Width = 1280, Height = 720 };

        await store.SaveAsync(Template("EXP001", "Meu YouTube", custom));

        var all = store.GetAll();
        var entry = BuiltIn(all, "EXP001");
        Assert.Equal(12, all.Count);
        Assert.True(entry.IsBuiltIn);
        Assert.Equal("Meu YouTube", entry.Name);
        Assert.Equal(1280, entry.Settings.Width);

        var original = store.GetBuiltIn("EXP001");
        Assert.NotNull(original);
        Assert.Equal(1920, original.Settings.Width);
        Assert.Equal("YouTube", original.Name);
    }

    [Fact]
    public async Task Deleting_an_overridden_built_in_restores_the_original()
    {
        var store = Create();
        await store.SaveAsync(Template("EXP001", "Meu YouTube", new ExportSettings { Width = 1280, Height = 720 }));

        await store.DeleteAsync("EXP001");

        Assert.Equal(1920, BuiltIn(store.GetAll(), "EXP001").Settings.Width);
    }

    [Fact]
    public async Task Restore_defaults_discards_built_in_overrides_and_keeps_user_templates()
    {
        var store = Create();
        await store.SaveAsync(Template("EXP002", "Shorts editado", new ExportSettings { Width = 500, Height = 1000 }));
        var userId = JsonExportTemplateStore.NewUserId();
        await store.SaveAsync(Template(userId, "Meu modelo"));

        await store.RestoreDefaultsAsync();

        var all = store.GetAll();
        Assert.Equal(1080, BuiltIn(all, "EXP002").Settings.Width);
        Assert.Equal("YouTube Shorts", BuiltIn(all, "EXP002").Name);
        Assert.Contains(all, t => t.Id == userId && !t.IsBuiltIn);
        Assert.Equal(13, all.Count);
    }

    [Fact]
    public async Task Deleting_a_user_template_removes_it()
    {
        var store = Create();
        var id = JsonExportTemplateStore.NewUserId();
        await store.SaveAsync(Template(id, "Descartável"));

        await store.DeleteAsync(id);

        Assert.DoesNotContain(store.GetAll(), t => t.Id == id);
    }

    [Fact]
    public void Corrupt_user_file_is_ignored_without_throwing()
    {
        Directory.CreateDirectory(_userDir);
        File.WriteAllText(Path.Combine(_userDir, "quebrado.json"), "{ isto não é json");
        File.WriteAllText(Path.Combine(_userDir, "sem-nome.json"), "{ \"id\": \"USR-SEM\", \"name\": \" \" }");

        var all = Create().GetAll();

        Assert.Equal(12, all.Count);
        Assert.All(all, t => Assert.True(t.IsBuiltIn));
    }

    [Fact]
    public void Missing_built_in_directory_yields_no_templates()
    {
        var store = new JsonExportTemplateStore(_userDir, null, NullLogger<JsonExportTemplateStore>.Instance);

        Assert.Empty(store.GetAll());
        Assert.Null(store.GetBuiltIn("EXP001"));
    }

    [Fact]
    public void New_user_ids_start_with_usr_and_are_unique()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => JsonExportTemplateStore.NewUserId()).ToList();

        Assert.All(ids, id => Assert.StartsWith("USR-", id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // ------------------------------------------------------------ FrameRate em JSON

    [Fact]
    public void Integer_frame_rate_is_written_as_plain_number_string()
    {
        Assert.Equal("\"30\"", JsonSerializer.Serialize(new FrameRate(30, 1), JsonExportTemplateStore.JsonOptions));
    }

    [Fact]
    public void Fractional_frame_rate_is_written_as_ratio()
    {
        Assert.Equal("\"30000/1001\"", JsonSerializer.Serialize(new FrameRate(30000, 1001), JsonExportTemplateStore.JsonOptions));
    }

    [Theory]
    [InlineData("25", 25, 1)]
    [InlineData("\"25\"", 25, 1)]
    [InlineData("\"24000/1001\"", 24000, 1001)]
    public void Frame_rate_reads_numbers_strings_and_ratios(string json, int numerator, int denominator)
    {
        var fr = JsonSerializer.Deserialize<FrameRate>(json, JsonExportTemplateStore.JsonOptions);

        Assert.Equal(new FrameRate(numerator, denominator), fr);
    }

    [Fact]
    public void Invalid_frame_rate_text_throws_json_exception()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FrameRate>("\"abc\"", JsonExportTemplateStore.JsonOptions));
    }
}
