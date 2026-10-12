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
            _iconExtractor = new IconExtractor(Path.Combine(outputPath, "icons"));
            _exporter = new DataExporter(outputPath);
        }

        public async Task RunAsync()
        {
            Console.WriteLine("Запуск RecipeExtractor...");
            Console.WriteLine($"Путь к Factorio: {_factorioPath}");
            Console.WriteLine($"Путь для сохранения: {_outputPath}");

            try
            {
                // === АВТООЧИСТКА ПАПКИ ВЫВОДА ===
                CleanOutputDirectory();
                // =================================

                Console.WriteLine("\nЗагрузка базовых данных...");
                await _dataLoader.LoadBaseDataAsync();

                Console.WriteLine("\nЗагрузка модов...");
                await _dataLoader.LoadModsAsync();

                Console.WriteLine("\nИзвлечение предметов...");
                var items = _dataLoader.GetItems();
                Console.WriteLine($"Найдено {items.Count} предметов");

                Console.WriteLine("\nИзвлечение рецептов...");
                var recipes = _dataLoader.GetRecipes();
                Console.WriteLine($"Найдено {recipes.Count} рецептов");

                // === КОПИРОВАНИЕ ИКОНОК ===
                Console.WriteLine("\nКопирование иконок предметов...");
                int itemIconCount = 0;
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.PhysicalIconPath))
                    {
                        var copied = _iconExtractor.CopyIcon(item.PhysicalIconPath, item.Name);
                        if (copied != null)
                        {
                            item.CopiedIconPath = copied;
                            itemIconCount++;
                        }
                    }

                    if ((itemIconCount + 1) % 100 == 0)
                        Console.WriteLine($"  Скопировано {itemIconCount} иконок...");
                }
                Console.WriteLine($"✓ Скопировано {itemIconCount} иконок предметов");

                Console.WriteLine("\nКопирование иконок рецептов...");
                int recipeIconCount = 0;
                var processedRecipeIcons = new HashSet<string>();
                foreach (var recipe in recipes)
                {
                    if (!string.IsNullOrEmpty(recipe.PhysicalIconPath) && !string.IsNullOrEmpty(recipe.IconFileName))
                    {
                        if (!processedRecipeIcons.Contains(recipe.IconFileName))
                        {
                            var copied = _iconExtractor.CopyIcon(recipe.PhysicalIconPath, recipe.Name, recipe.IconFileName);
                            if (copied != null)
                            {
                                recipe.CopiedIconPath = copied;
                                recipeIconCount++;
                                processedRecipeIcons.Add(recipe.IconFileName);
                            }
                        }
                        else
                        {
                            recipe.CopiedIconPath = Path.Combine(Path.Combine(_outputPath, "icons"), recipe.IconFileName);
                        }
                    }
                }
                Console.WriteLine($"✓ Скопировано {recipeIconCount} уникальных иконок рецептов");

                // === САМОПРОВЕРКА ===
                Console.WriteLine("\n═══════════════════════════════════════════════════");
                Console.WriteLine("  НАСТРОЙКА САМОПРОВЕРКИ");
                Console.WriteLine("═══════════════════════════════════════════════════");
                var severityFilter = ValidationModeParser.GetModeFromUser();

                var validator = new DataValidator(_factorioPath);
                var validationResult = validator.Validate(items, recipes, severityFilter);

                // === ЭКСПОРТ ===
                Console.WriteLine("\nЭкспорт предметов...");
                await _exporter.ExportItemsAsync(items);

                Console.WriteLine("\nЭкспорт рецептов...");
                await _exporter.ExportRecipesAsync(recipes);

                Console.WriteLine("\nЭкспорт сводки...");
                await _exporter.ExportSummaryAsync(items, recipes);

                Console.WriteLine("\nЭкспорт отчёта о валидации...");
                await _exporter.ExportValidationReportAsync(validationResult);

                Console.WriteLine("\n✓ Готово!");

                if (validationResult.HasErrors)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n Обнаружены критические ошибки в данных.");
                    Console.WriteLine($"  Отчёт: {Path.Combine(_outputPath, "validation_report.json")}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n✗ Ошибка: {ex.Message}");
                Console.WriteLine($"Стек: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Автоматически очищает папку вывода перед началом работы.
        /// Гарантирует существование папки и подпапки icons/.
        /// </summary>
        private void CleanOutputDirectory()
        {
            var iconsPath = Path.Combine(_outputPath, "icons");

            // Если папка не существует — просто создаём её
            if (!Directory.Exists(_outputPath))
            {
                Console.WriteLine($"\nСоздание папки вывода: {_outputPath}");
                Directory.CreateDirectory(_outputPath);
                Directory.CreateDirectory(iconsPath);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"\nПапка '{_outputPath}' существует. Очистить её? (Y/n) [по умолчанию Y]: ");
            Console.ResetColor();

            var input = Console.ReadLine()?.Trim().ToLower();

            if (input == "n" || input == "no")
            {
                Console.WriteLine("  → Пропуск очистки. Существующие файлы будут перезаписаны.");
                // Всё равно убеждаемся, что папка icons существует
                if (!Directory.Exists(iconsPath))
                    Directory.CreateDirectory(iconsPath);
                return;
            }

            Console.WriteLine("\nОчистка папки вывода...");
            try
            {
                var dirInfo = new DirectoryInfo(_outputPath);

                // Удаляем все файлы в корневой папке
                foreach (var file in dirInfo.GetFiles())
                {
                    try { file.Delete(); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ⚠ Не удалось удалить файл {file.Name}: {ex.Message}");
                    }
                }

                // Удаляем все подпапки (icons и т.д.)
                foreach (var subDir in dirInfo.GetDirectories())
                {
                    try { subDir.Delete(true); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   Не удалось удалить папку {subDir.Name}: {ex.Message}");
                    }
                }

                Console.WriteLine($"  ✓ Содержимое папки очищено: {_outputPath}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ Ошибка при очистке: {ex.Message}");
                Console.ResetColor();
            }

            // === ВАЖНО: Гарантированно создаём папку и подпапку icons ===
            try
            {
                if (!Directory.Exists(_outputPath))
                    Directory.CreateDirectory(_outputPath);

                if (!Directory.Exists(iconsPath))
                    Directory.CreateDirectory(iconsPath);

                Console.WriteLine($"  ✓ Папка и подпапка icons/ созданы");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ Ошибка при создании папки: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}