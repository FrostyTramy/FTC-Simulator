using Sim.Editor;

namespace EditorTests;

/// <summary>Workspace projects follow template updates for files the team never edited, and never lose edits.</summary>
public class ProjectTemplateTests
{
    [Fact]
    public void Unedited_files_follow_the_template_and_edited_files_are_kept()
    {
        var root = Path.Combine(Path.GetTempPath(), "ftcsim-template-" + Guid.NewGuid().ToString("N")[..8]);
        var template = Path.Combine(root, "template");
        var project = Path.Combine(root, "project", "TeamCode");
        var manifest = Path.Combine(root, ".ftcsim", "template.json");
        try
        {
            Directory.CreateDirectory(Path.Combine(template, "src"));
            File.WriteAllText(Path.Combine(template, "src", "TeleOp.java"), "v1");
            File.WriteAllText(Path.Combine(template, "src", "Auto.java"), "v1");

            var first = ProjectTemplate.Sync(template, project, manifest);
            Assert.Equal(["src/Auto.java", "src/TeleOp.java"], first.Added);

            File.WriteAllText(Path.Combine(project, "src", "Auto.java"), "my changes");   // the team edits one file
            File.WriteAllText(Path.Combine(template, "src", "TeleOp.java"), "v2");        // the template changes both
            File.WriteAllText(Path.Combine(template, "src", "Auto.java"), "v2");
            File.WriteAllText(Path.Combine(template, "src", "New.java"), "v2");

            var second = ProjectTemplate.Sync(template, project, manifest);
            Assert.Equal(["src/TeleOp.java"], second.Updated);
            Assert.Equal(["src/Auto.java"], second.KeptEdited);
            Assert.Equal(["src/New.java"], second.Added);
            Assert.Equal("v2", File.ReadAllText(Path.Combine(project, "src", "TeleOp.java")));
            Assert.Equal("my changes", File.ReadAllText(Path.Combine(project, "src", "Auto.java")));
            Assert.False(File.Exists(Path.Combine(project, "template.json")));   // the manifest stays out of the repository
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
