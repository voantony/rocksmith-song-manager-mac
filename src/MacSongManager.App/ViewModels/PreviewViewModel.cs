using MacSongManager.Core.Actions;

namespace MacSongManager.ViewModels;

public sealed record PreviewRow(string Action, string From, string To, string Note);

public sealed class PreviewViewModel
{
    public PreviewViewModel(ChangePlan plan, string note)
    {
        Heading = plan.Kind switch
        {
            ChangeKind.Enable => "Enable songs",
            ChangeKind.Disable => "Disable songs",
            ChangeKind.Organize => "Organize into artist folders",
            ChangeKind.Trash => "Move to trash",
            _ => "Undo"
        };
        Summary = $"{plan.Changes.Count} to apply, {plan.Skipped.Count} skipped. Files are renamed or moved. Nothing is deleted.";
        Note = note;
        CanApply = plan.HasWork;
        Rows = plan.Changes
            .Select(change => new PreviewRow(change.Description, change.SourcePath, change.DestinationPath, ""))
            .Concat(plan.Skipped.Select(skip => new PreviewRow("Skip", skip.Path, "", skip.Reason)))
            .ToList();
    }

    public string Heading { get; }
    public string Summary { get; }
    public string Note { get; }
    public bool CanApply { get; }
    public IReadOnlyList<PreviewRow> Rows { get; }
}
