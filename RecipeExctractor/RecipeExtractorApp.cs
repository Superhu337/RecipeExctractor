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

                // Извлечение иконок для заглушек (если они были добавлены)
                Console.WriteLine("\nИзвлечение иконок для заглушек...");
                int stubIconCount = 0;
                var extractedIconPaths = new HashSet<string>();
                var processedRecipeIcons = new HashSet<string>();
                foreach (var item in items.Where(i => i.Properties.ContainsKey("auto_generated")))
                {
                    if (!string.IsNullOrEmpty(item.IconFileName) && !File.Exists(item.IconFileName))
                    {
                        // Пытаемся найти иконку по имени предмета
                        var possibleIconPath = Path.Combine(_factorioPath, "data", $"base/graphics/icons/{item.Name}.png");
                        if (File.Exists(possibleIconPath))
                        {
                            var IconFileName = await _iconExtractor.ExtractIconAsync(possibleIconPath, item.Name);
                            if (IconFileName != null)
                            {
                                item.IconFileName = IconFileName;
                                extractedIconPaths.Add(IconFileName);
                                stubIconCount++;
                            }
                        }
                    }
                }
                if (stubIconCount > 0)
                    Console.WriteLine($"Извлечено {stubIconCount} иконок для заглушек");

                // === ИНТЕРАКТИВНЫЙ ВЫБОР РЕЖИМА ВАЛИДАЦИИ ===
                Console.WriteLine("\n═══════════════════════════════════════════════════");
                Console.WriteLine("  НАСТРОЙКА САМОПРОВЕРКИ");
                Console.WriteLine("═══════════════════════════════════════════════════");

                var severityFilter = ValidationModeParser.GetModeFromUser();

                // === САМОПРОВЕРКА ДАННЫХ ===
                var validator = new DataValidator(_factorioPath, extractedIconPaths);
                var validationResult = validator.Validate(items, recipes, severityFilter);

                // Экспорт данных (items теперь содержит заглушки)
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