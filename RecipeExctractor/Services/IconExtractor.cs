// Services/IconExtractor.cs
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace RecipeExtractor.Services
{
    public class IconExtractor
    {
        private readonly string _factorioPath;
        private readonly string _outputPath;
        private readonly HashSet<string> _searchPaths;

        public IconExtractor(string factorioPath, string outputPath)
        {
            _factorioPath = factorioPath;
            _outputPath = outputPath;
            _searchPaths = new HashSet<string>();

            InitializeSearchPaths();

            if (!Directory.Exists(_outputPath))
            {
                Directory.CreateDirectory(_outputPath);
            }
        }

        private void InitializeSearchPaths()
        {
            _searchPaths.Add(Path.Combine(_factorioPath, "data", "base"));
            _searchPaths.Add(Path.Combine(_factorioPath, "data", "core"));
            
            var modsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Factorio", "mods");
            if (Directory.Exists(modsPath))
            {
                _searchPaths.Add(modsPath);
            }
        }

        public async Task<string?> ExtractIconAsync(string iconPath, string itemName)
        {
            if (string.IsNullOrEmpty(iconPath))
                return null;

            try
            {
                var fullPath = ResolveIconPath(iconPath);
                
                if (fullPath != null && File.Exists(fullPath))
                {
                    var outputPath = Path.Combine(_outputPath, $"{SanitizeFileName(itemName)}.png");
                    
                    using (var image = await Image.LoadAsync(fullPath))
                    {
                        await image.SaveAsPngAsync(outputPath);
                    }
                    
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при извлечении иконки {itemName}: {ex.Message}");
            }

            return null;
        }

        private string? ResolveIconPath(string iconPath)
        {
            // Нормализация пути
            iconPath = iconPath.Replace("\\", Path.DirectorySeparatorChar.ToString());
            iconPath = iconPath.Replace("/", Path.DirectorySeparatorChar.ToString());

            // Прямой путь
            var directPath = Path.Combine(_factorioPath, iconPath);
            if (File.Exists(directPath))
                return directPath;

            // Поиск в папках данных
            foreach (var searchPath in _searchPaths)
            {
                var fullPath = Path.Combine(searchPath, iconPath);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            // Поиск файла по имени
            var fileName = Path.GetFileName(iconPath);
            foreach (var searchPath in _searchPaths)
            {
                if (Directory.Exists(searchPath))
                {
                    var files = Directory.GetFiles(searchPath, fileName, SearchOption.AllDirectories);
                    if (files.Length > 0)
                        return files[0];
                }
            }

            return null;
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return sanitized;
        }
    }
}