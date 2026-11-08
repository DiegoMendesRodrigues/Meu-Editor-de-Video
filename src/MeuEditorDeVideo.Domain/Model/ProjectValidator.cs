namespace MeuEditorDeVideo.Domain.Model;

/// <summary>Regras de integridade do projeto, aplicadas ao abrir e antes de salvar (RNF015).</summary>
public static class ProjectValidator
{
    public static IReadOnlyList<string> Validate(Project project)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(project.Name))
            errors.Add("O projeto precisa de um nome.");

        errors.AddRange(project.Settings.Validate());

        if (project.Sequences.Count == 0)
            errors.Add("O projeto não possui nenhuma sequência.");

        var ids = new HashSet<Guid>();
        void Unique(Guid id, string what)
        {
            if (!ids.Add(id)) errors.Add($"Identificador duplicado em {what}: {id}.");
        }

        Unique(project.Id, "projeto");
        foreach (var media in project.Media)
        {
            Unique(media.Id, "mídia");
            if (string.IsNullOrWhiteSpace(media.OriginalPath) && string.IsNullOrWhiteSpace(media.PackagedEntry))
                errors.Add($"A mídia '{media.Name}' não possui caminho de origem.");
        }

        var mediaIds = project.Media.Select(m => m.Id).ToHashSet();

        foreach (var sequence in project.Sequences)
        {
            Unique(sequence.Id, "sequência");
            foreach (var marker in sequence.Markers)
            {
                Unique(marker.Id, "marcador");
                if (marker.Time.Ticks < 0) errors.Add($"O marcador '{marker.Name}' está em tempo negativo.");
            }

            foreach (var track in sequence.Tracks)
            {
                Unique(track.Id, "trilha");
                Clip? previous = null;
                foreach (var clip in track.Clips.OrderBy(c => c.Start))
                {
                    Unique(clip.Id, "clipe");
                    if (clip.Start.Ticks < 0) errors.Add($"Clipe com início negativo na trilha '{track.Name}'.");
                    if (clip.Duration.Ticks <= 0) errors.Add($"Clipe sem duração na trilha '{track.Name}'.");
                    if (clip.SourceIn.Ticks < 0) errors.Add($"Clipe com ponto de entrada negativo na trilha '{track.Name}'.");
                    if (clip.Opacity is < 0 or > 1) errors.Add($"Opacidade inválida em clipe da trilha '{track.Name}'.");
                    if (clip.MediaAssetId is { } mid && !mediaIds.Contains(mid))
                        errors.Add($"Clipe da trilha '{track.Name}' referencia uma mídia inexistente.");
                    if (previous is not null && clip.Start < previous.End)
                        errors.Add($"Clipes sobrepostos na trilha '{track.Name}'.");
                    previous = clip;
                }
            }
        }

        return errors;
    }
}
