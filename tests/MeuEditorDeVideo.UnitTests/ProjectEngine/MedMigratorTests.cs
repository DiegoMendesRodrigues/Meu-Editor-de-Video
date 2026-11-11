using System.Text.Json.Nodes;
using MeuEditorDeVideo.ProjectEngine.Format;
using Xunit;

namespace MeuEditorDeVideo.UnitTests.ProjectEngine;

public sealed class MedMigratorTests
{
    [Fact]
    public void Formato_1_2_migra_para_o_atual_com_aviso_e_sem_alterar_o_json()
    {
        var project = new JsonObject { ["name"] = "Antigo" };
        var before = project.ToJsonString();

        var migrated = MedMigrator.Default.Migrate(project, new MedVersion(1, 2), out var warnings);

        Assert.True(migrated);
        Assert.Equal(new MedVersion(1, 3), MedMigrator.Current);
        Assert.Contains(warnings, w => w.Contains("1.2") && w.Contains("1.3"));
        Assert.Equal(before, project.ToJsonString());
    }

    [Fact]
    public void Formato_1_0_percorre_a_cadeia_inteira_ate_o_atual()
    {
        Assert.True(MedMigrator.Default.Migrate(new JsonObject(), new MedVersion(1, 0), out _));
    }
}
