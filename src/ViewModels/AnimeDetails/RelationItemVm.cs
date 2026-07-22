using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class RelationItemVm : ObservableObject
{
    public Models.Entities.AnimeRelation Relation { get; }

    [ObservableProperty]
    private string? _imageUrl;

    [ObservableProperty]
    private string? _displayTargetType;

    public RelationItemVm(Models.Entities.AnimeRelation relation)
    {
        Relation = relation;
        DisplayTargetType = relation.TargetType;
    }
}
