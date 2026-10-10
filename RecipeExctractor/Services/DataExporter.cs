// Services/DataExporter.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using RecipeExtractor.Models;

namespace RecipeExtractor.Services
{
    public class DataExporter
    {
        private readonly string _outputPath;
        private readonly JsonSerializerOptions _jsonOptions;

        public DataExporter(string outputPath)
        {
            _outputPath = outputPath;
            
            if (!Directory.Exists(_outputPath))
            {
                Directory.CreateDirectory(_outputPath);
            }

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        public async Task ExportItemsAsync(List<Item> items, string filename = "items.json")
        {
            var filePath = Path.Combine(_outputPath, filename);
            var json = JsonSerializer.Serialize(items, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            Console.WriteLine($"Экспортировано {items.Count} предметов в {filePath}");
        }

        public async Task ExportRecipesAsync(List<Recipe> recipes, string filename = "recipes.json")
        {
            var filePath = Path.Combine(_outputPath, filename);
            var json = JsonSerializer.Serialize(recipes, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            Console.WriteLine($"Экспортировано {recipes.Count} рецептов в {filePath}");
        }

        public async Task ExportSummaryAsync(List<Item> items, List<Recipe> recipes, string filename = "summary.json")
        {
            var summary = new
            {
                TotalItems = items.Count,
                TotalRecipes = recipes.Count,
                ExportDate = DateTime.Now,
                ItemTypes = items.GroupBy(i => i.Type).ToDictionary(g => g.Key, g => g.Count()),
                RecipeCategories = recipes.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count())
            };

            var filePath = Path.Combine(_outputPath, filename);
            var json = JsonSerializer.Serialize(summary, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            Console.WriteLine($"Сводка экспортирована в {filePath}");
        }
    }
}