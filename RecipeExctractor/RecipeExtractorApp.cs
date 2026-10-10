// RecipeExtractorApp.cs
using RecipeExtractor.Models;
using RecipeExtractor.Services;

namespace RecipeExtractor
{
    public class RecipeExtractorApp
    {
        private readonly string _factorioPath;
        private readonly string _outputPath;
        private readonly string _modsPath;
        private readonly LuaDataLoader _dataLoader;
        private readonly IconExtractor _iconExtractor;
        private readonly DataExporter _exporter;

        public RecipeExtractorApp(string factorioPath, string outputPath, string modsPath)
        {
            _factorioPath = factorioPath;
            _outputPath = outputPath;
            _modsPath = modsPath;

            _dataLoader = new LuaDataLoader(factorioPath, modsPath);
            _iconExtractor = new IconExtractor(factorioPath, Path.Combine(outputPath, "icons"));
            _exporter = new DataExporter(outputPath);
        }

        public async Task RunAsync()
        {
            Console.WriteLine("Запуск RecipeExtractor...");
            Console.WriteLine($"Путь к Factorio: {_factorioPath}");
            Console.WriteLine($"Путь для сохранения: {_outputPath}");

            try
            {
                // Загрузка данных из Lua
                Console.WriteLine("\nЗагрузка базовых данных...");
                await _dataLoader.LoadBaseDataAsync();

                Console.WriteLine("\nЗагрузка модов...");
                await _dataLoader.LoadModsAsync();

                // Получение предметов
                Console.WriteLine("\nИзвлечение предметов...");
                var items = _dataLoader.GetItems();
                Console.WriteLine($"Найдено {items.Count} предметов");

                // Получение рецептов
                Console.WriteLine("\nИзвлечение рецептов...");
                var recipes = _dataLoader.GetRecipes();
                Console.WriteLine($"Найдено {recipes.Count} рецептов");

                // Извлечение иконок
                Console.WriteLine("\nИзвлечение иконок...");
                int iconCount = 0;
                int totalItems = items.Count;
                var extractedIconPaths = new HashSet<string>();

                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.IconPath))
                    {
                        var iconPath = await _iconExtractor.ExtractIconAsync(item.IconPath, item.Name);
                        if (iconPath != null)
                        {
                            item.IconPath = iconPath;
                            extractedIconPaths.Add(iconPath);
                            iconCount++;
                        }
                    }

                    if ((iconCount + 1) % 100 == 0)
                    {
                        Console.WriteLine($"Обработано {iconCount + 1}/{totalItems} предметов, извлечено {iconCount} иконок...");
                    }
                }
                Console.WriteLine($"Всего извлечено {iconCount} иконок");

                // === САМОПРОВЕРКА ДАННЫХ ===
                var validator = new DataValidator(_factorioPath, extractedIconPaths);
                var validationResult = validator.Validate(items, recipes);

                // Экспорт данных
                Console.WriteLine("\nЭкспорт предметов...");
                await _exporter.ExportItemsAsync(items);

                Console.WriteLine("\nЭкспорт рецептов...");
                await _exporter.ExportRecipesAsync(recipes);

                Console.WriteLine("\nЭкспорт сводки...");
                await _exporter.ExportSummaryAsync(items, recipes);

                // Экспорт отчёта о валидации
                Console.WriteLine("\nЭкспорт отчёта о валидации...");
                await _exporter.ExportValidationReportAsync(validationResult);

                Console.WriteLine("\n✓ Готово!");

                if (validationResult.HasErrors)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n⚠ Внимание: обнаружены критические ошибки в данных.");
                    Console.WriteLine($"  Подробный отчёт: {Path.Combine(_outputPath, "validation_report.json")}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ Ошибка: {ex.Message}");
                Console.WriteLine($"Стек: {ex.StackTrace}");
            }
        }
    }
}
