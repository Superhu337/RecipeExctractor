using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace RecipeExtractor.Services
{
    public class IconExtractor
    {
        private readonly string _outputPath;
        private readonly HashSet<string> _copiedFiles = new();

        public IconExtractor(string outputPath)
        {
            _outputPath = outputPath;

            // Гарантированно создаём папку иконок
            if (!Directory.Exists(_outputPath))
            {
                Directory.CreateDirectory(_outputPath);
                Console.WriteLine($"  [IconExtractor] Создана папка: {_outputPath}");
            }
        }

        /// <summary>
        /// Копирует иконку по физическому пути в папку вывода.
        /// Возвращает путь к скопированному файлу или null.
        /// </summary>
        public string? CopyIcon(string? physicalPath, string itemName, string? desiredFileName = null)
        {
            if (string.IsNullOrWhiteSpace(physicalPath))
                return null;

            if (!File.Exists(physicalPath))
                return null;

            var fileName = desiredFileName ?? $"{SanitizeFileName(itemName)}.png";
            var outputPath = Path.Combine(_outputPath, fileName);

            // Не копируем повторно
            if (_copiedFiles.Contains(outputPath))
                return outputPath;

            try
            {
                using (var image = Image.Load(physicalPath))
                {
                    image.SaveAsPng(outputPath);
                }
                _copiedFiles.Add(outputPath);
                return outputPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Ошибка копирования иконки {itemName}: {ex.Message}");
                return null;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        }
    }
}