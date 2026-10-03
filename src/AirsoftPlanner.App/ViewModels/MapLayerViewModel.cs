using System.Globalization;
using System.IO;
using AirsoftPlanner.Core.Domain;
using Avalonia.Media.Imaging;

namespace AirsoftPlanner.App.ViewModels;

public class MapLayerViewModel(MapLayer layer) : ViewModelBase
{
    private Bitmap? _bitmap;

    public MapLayer Model => layer;

    public string Name
    {
        get => layer.Name;
        set => SetProperty(layer.Name, value, layer, (l, v) => l.Name = v);
    }

    public string Attribution => layer.Attribution;

    public GeoBounds Bounds => layer.Bounds;

    /// <summary>Image décodée à la première utilisation.</summary>
    public Bitmap Bitmap => _bitmap ??= new Bitmap(new MemoryStream(layer.Image));

    public string Details => string.Format(CultureInfo.GetCultureInfo("fr-FR"), "{0:0.0} Mo · {1:0.00} × {2:0.00} km",
        layer.Image.Length / 1_048_576.0, Bounds.SizeInMeters().Width / 1000, Bounds.SizeInMeters().Height / 1000);
}
