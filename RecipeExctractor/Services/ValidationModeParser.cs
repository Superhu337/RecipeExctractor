using RecipeExtractor.Models;

namespace RecipeExtractor.Services
{
    public static class ValidationModeParser
    {
        /// <summary>
        /// Парсит ввод пользователя и возвращает набор severity для фильтрации.
        /// Возвращает null, если выбран режим "0" (все проверки).
        /// </summary>
        public static HashSet<ValidationSeverity>? ParseInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null; // режим 0 - все проверки

            input = input.Trim().ToLowerInvariant();

            // Разделители: запятая, пробел, точка с запятой, дефис
            var separators = new[] { ',', ' ', ';', '-', '\t' };
            var parts = input.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            var result = new HashSet<ValidationSeverity>();

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Если введён "0" — возвращаем null (все проверки)
                if (trimmed == "0")
                    return null;

                if (int.TryParse(trimmed, out int mode))
                {
                    switch (mode)
                    {
                        case 1: result.Add(ValidationSeverity.Warning); break;
                        case 2: result.Add(ValidationSeverity.Error); break;
                        case 3: result.Add(ValidationSeverity.Info); break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  ⚠ Неизвестный режим '{mode}'. Допустимые: 0, 1, 2, 3");
                            Console.ResetColor();
                            break;
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"   Некорректный ввод: '{trimmed}'. Ожидается число.");
                    Console.ResetColor();
                }
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Выводит меню выбора режима валидации.
        /// </summary>
        public static void PrintMenu()
        {
            Console.WriteLine("\n════════════════════════════════════════════════════╗");
            Console.WriteLine("║         РЕЖИМЫ САМОПРОВЕРКИ ДАННЫХ               ║");
            Console.WriteLine("╠════════════════════════════════════════════════════╣");
            Console.WriteLine("║  0 - ВСЕ проверки (по умолчанию)                 ║");
            Console.WriteLine("║  1 - Только ПРЕДУПРЕЖДЕНИЯ (Warning)             ║");
            Console.WriteLine("║  2 - Только ОШИБКИ (Error)                       ║");
            Console.WriteLine("║  3 - Только ИНФОРМАЦИОННЫЕ (Info)                ║");
            Console.WriteLine("════════════════════════════════════════════════════╣");
            Console.WriteLine("║  Можно комбинировать: 1,2  или  1 2 3            ║");
            Console.WriteLine("╚════════════════════════════════════════════════════╝");
        }

        /// <summary>
        /// Запрашивает у пользователя выбор режима валидации с повторным запросом
        /// при некорректном вводе.
        /// </summary>
        public static HashSet<ValidationSeverity>? GetModeFromUser()
        {
            PrintMenu();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("\nВведите режим проверки [0]: ");
                Console.ResetColor();

                var input = Console.ReadLine();

                // Пустой ввод = режим 0 (все проверки)
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("  → Выбран режим: ВСЕ проверки");
                    return null;
                }

                var result = ParseInput(input);

                if (result == null)
                {
                    Console.WriteLine("  → Выбран режим: ВСЕ проверки");
                    return null;
                }

                var modeNames = result.Select(s => s.ToString()).ToList();
                Console.WriteLine($"  → Выбраны режимы: [{string.Join(", ", modeNames)}]");
                return result;
            }
        }

        /// <summary>
        /// Находит предметы, на которые ссылаются рецепты, но которых нет в списке.
        /// Создает для них заглушки с автоматическим определением пути к иконке.
        /// </summary>
        private List<Item> ResolveMissingItems(List<Item> items, List<Recipe> recipes)
        {
            var existingNames = new HashSet<string>(items.Select(i => i.Name));
            var referencedNames = new HashSet<string>();

            // Собираем все имена предметов из рецептов
            foreach (var recipe in recipes)
            {
                foreach (var ing in recipe.Ingredients)
                    if (!string.IsNullOrEmpty(ing.Name)) referencedNames.Add(ing.Name);
                foreach (var res in recipe.Results)
                    if (!string.IsNullOrEmpty(res.Name)) referencedNames.Add(res.Name);
            }

            // Находим отсутствующие предметы
            var missingNames = referencedNames.Where(n => !existingNames.Contains(n)).ToList();

            if (missingNames.Count == 0)
                return new List<Item>();

            Console.WriteLine($"\nНайдено {missingNames.Count} отсутствующих предметов, создаю заглушки...");

            var stubItems = new List<Item>();
            foreach (var name in missingNames)
            {
                // Пытаемся найти иконку по стандартному пути
                var iconPath = TryFindIconForItem(name);

                var stub = new Item
                {
                    Name = name,
                    Type = "item",
                    IconPath = iconPath ?? string.Empty,
                    StackSize = 100,
                    Subgroup = "other",
                    Order = $"z[auto-generated]-{name}",
                    Properties = new Dictionary<string, object>
                    {
                        ["type"] = "item",
                        ["name"] = name,
                        ["auto_generated"] = true,
                        ["note"] = "Предмет не был извлечен из data.raw, создана заглушка"
                    }
                };

                stubItems.Add(stub);
                Console.WriteLine($"  + Создана заглушка: {name} (иконка: {(iconPath != null ? "найдена" : "не найдена")})");
            }

            return stubItems;
        }

        /// <summary>
        /// Пытается найти иконку предмета по стандартным путям Factorio.
        /// </summary>
        private string? TryFindIconForItem(string itemName)
        {
            // Стандартные пути к иконкам в Factorio
            var possiblePaths = new[]
            {
        $"base/graphics/icons/{itemName}.png",
        $"base/graphics/icons/fluid/{itemName}.png",
        $"base/graphics/icons/equipment/{itemName}.png",
        $"base/graphics/icons/technology/{itemName}.png"
    };

            foreach (var relativePath in possiblePaths)
            {
                var fullPath = Path.Combine(_factorioPath, "data", relativePath);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }
    }
}