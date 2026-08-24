using FImageStack.Core.Models;
using FImageStack.Infrastructure.IO;

namespace FImageStack.Application.Services;

public interface IProjectService
{
    List<string> DiscoverImageFiles(string directoryPath);
    StackValidationResult ValidateStack(IReadOnlyList<string> filePaths);
}

public sealed class ProjectService : IProjectService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp"
    };

    public List<string> DiscoverImageFiles(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var files = Directory.GetFiles(directoryPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => NaturalSortKey(Path.GetFileName(f)))
            .ToList();

        return files;
    }

    public StackValidationResult ValidateStack(IReadOnlyList<string> filePaths)
    {
        var result = new StackValidationResult
        {
            FrameCount = filePaths.Count
        };

        if (filePaths.Count < 2)
        {
            result.Issues.Add("Focus stacking requires at least 2 image frames.");
            return result;
        }

        return result;
    }

    private static string NaturalSortKey(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, @"\d+", match => match.Value.PadLeft(10, '0'));
    }
}
