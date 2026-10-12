using RecipeExtractor.Models;

namespace RecipeExtractor.Services
{
    public class DataValidator
    {
        private readonly string _factorioPath;
        private readonly HashSet<string> _extractedIconPaths;

        public DataValidator(string factorioPath, HashSet<string>? extractedIconPaths = null)
        {
            _factorioPath = factorioPath;
            _extractedIconPaths = extractedIconPaths ?? new HashSet<string>();
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
                var IconFileName = TryFindIconForItem(name);

                var stub = new Item
                {
                    Name = name,
                    Type = "item",
                    IconFileName = IconFileName ?? string.Empty,
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
                Console.WriteLine($"  + Создана заглушка: {name} (иконка: {(IconFileName != null ? "найдена" : "не найдена")})");
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

        public ValidationResult Validate(List<Item> items, List<Recipe> recipes,
            HashSet<ValidationSeverity>? severityFilter = null)
        {
            var result = new ValidationResult
            {
                TotalItems = items.Count,
                TotalRecipes = recipes.Count
            };

            Console.WriteLine("\n=== Запуск самопроверки данных ===");

            if (severityFilter != null && severityFilter.Count > 0)
            {
                var modeNames = severityFilter.Select(s => s.ToString()).ToList();
                Console.WriteLine($"Режим фильтрации: [{string.Join(", ", modeNames)}]");
            }
            else
            {
                Console.WriteLine("Режим фильтрации: ВСЕ проверки");
            }

            // === НОВЫЙ КОД: Разрешение отсутствующих предметов ===
            var missingItems = ResolveMissingItems(items, recipes);
            if (missingItems.Count > 0)
            {
                items.AddRange(missingItems);
                result.TotalItems = items.Count;
                Console.WriteLine($"Добавлено {missingItems.Count} заглушек к списку предметов");
            }
            // ====================================================

            var knownItemNames = new HashSet<string>(
                items.Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n))
            );

            ValidateItems(items, knownItemNames, result, severityFilter);
            ValidateRecipes(recipes, knownItemNames, result, severityFilter);
            ValidateCrossReferences(items, recipes, knownItemNames, result, severityFilter);

            PrintSummary(result, severityFilter);

            return result;
        }

        private void ValidateItems(List<Item> items, HashSet<string> knownNames,
            ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            Console.WriteLine("Проверка предметов...");

            var seenNames = new HashSet<string>();
            var itemsWithoutIcon = new List<string>();
            var itemsWithoutSubgroup = new List<string>();

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Item,
                        "[без имени]", item.Type, "name", "Предмет без имени", $"Тип: {item.Type}");
                    continue;
                }

                if (!seenNames.Add(item.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Item,
                        item.Name, item.Type, "name", "Дублирующееся имя предмета", $"Тип: {item.Type}");
                }

                if (string.IsNullOrWhiteSpace(item.Type))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Item,
                        item.Name, item.Type, "type", "Отсутствует тип предмета");
                }

                if (string.IsNullOrWhiteSpace(item.IconFileName))
                {
                    itemsWithoutIcon.Add(item.Name);
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Item,
                        item.Name, item.Type, "icon", "Отсутствует путь к иконке");
                }
                else if (!File.Exists(item.IconFileName))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Icon,
                        item.Name, item.Type, "icon", "Файл иконки не найден на диске", item.IconFileName);
                }

                if (item.StackSize <= 0)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Info, ValidationCategory.Item,
                        item.Name, item.Type, "stack_size", "Размер стака не задан или равен 0", $"Значение: {item.StackSize}");
                }
                else if (item.StackSize > 10001)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Item,
                        item.Name, item.Type, "stack_size", "Подозрительно большой размер стака", $"Значение: {item.StackSize}");
                }

                if (string.IsNullOrWhiteSpace(item.Subgroup))
                {
                    itemsWithoutSubgroup.Add(item.Name);
                }

                if (string.IsNullOrWhiteSpace(item.Order))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Info, ValidationCategory.Item,
                        item.Name, item.Type, "order", "Отсутствует поле order (сортировка)");
                }
            }

            if (itemsWithoutIcon.Count > 0)
                Console.WriteLine($"  Предметов без иконки: {itemsWithoutIcon.Count}");
            if (itemsWithoutSubgroup.Count > 0)
                Console.WriteLine($"  Предметов без subgroup: {itemsWithoutSubgroup.Count}");
        }

        private void ValidateRecipes(List<Recipe> recipes, HashSet<string> knownItems,
            ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            Console.WriteLine("Проверка рецептов...");

            var seenNames = new HashSet<string>();
            var recipesWithEmptyIngredients = new List<string>();
            var recipesWithEmptyResults = new List<string>();

            foreach (var recipe in recipes)
            {
                if (string.IsNullOrWhiteSpace(recipe.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe,
                        "[без имени]", "recipe", "name", "Рецепт без имени");
                    continue;
                }

                if (!seenNames.Add(recipe.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "name", "Дублирующееся имя рецепта");
                }

                if (recipe.EnergyRequired <= 0)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "energy_required", "Время создания <= 0", $"Значение: {recipe.EnergyRequired}");
                }
                else if (recipe.EnergyRequired > 1000)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "energy_required", "Подозрительно большое время создания", $"Значение: {recipe.EnergyRequired}");
                }

                if (recipe.Ingredients == null || recipe.Ingredients.Count == 0)
                {
                    recipesWithEmptyIngredients.Add(recipe.Name);
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "ingredients", "Рецепт не имеет ингредиентов");
                }
                else
                {
                    ValidateIngredients(recipe, knownItems, result, filter);
                }

                if (recipe.Results == null || recipe.Results.Count == 0)
                {
                    bool hasOldFormatResult = recipe.Properties.ContainsKey("result");

                    if (!hasOldFormatResult)
                    {
                        recipesWithEmptyResults.Add(recipe.Name);
                        TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe,
                            recipe.Name, "recipe", "results", "Рецепт не имеет результатов");
                    }
                    else
                    {
                        TryAddIssue(result, filter, ValidationSeverity.Info, ValidationCategory.Recipe,
                            recipe.Name, "recipe", "results", "Рецепт использует старый формат (поле result вместо results)");
                    }
                }
                else
                {
                    ValidateResults(recipe, knownItems, result, filter);
                }

                if (string.IsNullOrEmpty(recipe.MainProduct) && recipe.Results.Count > 0)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "main_product", "Не определен главный продукт рецепта");
                }

                if (string.IsNullOrWhiteSpace(recipe.RecipeIconPath))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Info, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "icon", "Рецепт не имеет собственной иконки (используется иконка результата)");
                }
            }

            if (recipesWithEmptyIngredients.Count > 0)
                Console.WriteLine($"  Рецептов без ингредиентов: {recipesWithEmptyIngredients.Count}");
            if (recipesWithEmptyResults.Count > 0)
                Console.WriteLine($"  Рецептов без результатов: {recipesWithEmptyResults.Count}");
        }

        private void ValidateIngredients(Recipe recipe, HashSet<string> knownItems,
            ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            var seenIngredients = new HashSet<string>();

            foreach (var ingredient in recipe.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ingredient.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "ingredients", "Ингредиент без имени");
                    continue;
                }

                if (!seenIngredients.Add(ingredient.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "ingredients", "Дублирующийся ингредиент в рецепте", $"Ингредиент: {ingredient.Name}");
                }

                if (ingredient.Amount <= 0)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "ingredients", "Количество ингредиента <= 0", $"{ingredient.Name}: {ingredient.Amount}");
                }

                if (!knownItems.Contains(ingredient.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.CrossReference,
                        recipe.Name, "recipe", "ingredients", "Ингредиент ссылается на неизвестный предмет", $"Неизвестный предмет: {ingredient.Name}");
                }
            }
        }

        private void ValidateResults(Recipe recipe, HashSet<string> knownItems,
            ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            var seenResults = new HashSet<string>();

            foreach (var resultItem in recipe.Results)
            {
                if (string.IsNullOrWhiteSpace(resultItem.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "results", "Результат без имени");
                    continue;
                }

                if (!seenResults.Add(resultItem.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "results", "Дублирующийся результат в рецепте", $"Результат: {resultItem.Name}");
                }

                if (resultItem.Amount <= 0)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "results", "Количество результата <= 0", $"{resultItem.Name}: {resultItem.Amount}");
                }

                if (resultItem.Probability < 0 || resultItem.Probability > 1)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "results", "Вероятность результата вне диапазона [0, 1]", $"{resultItem.Name}: {resultItem.Probability}");
                }

                if (!knownItems.Contains(resultItem.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.CrossReference,
                        recipe.Name, "recipe", "results", "Результат ссылается на неизвестный предмет", $"Неизвестный предмет: {resultItem.Name}");
                }
            }
        }

        private void ValidateCrossReferences(List<Item> items, List<Recipe> recipes,
            HashSet<string> knownItems, ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            Console.WriteLine("Проверка перекрёстных ссылок...");

            var referencedItems = new HashSet<string>();
            foreach (var recipe in recipes)
            {
                foreach (var ing in recipe.Ingredients)
                    if (!string.IsNullOrEmpty(ing.Name)) referencedItems.Add(ing.Name);
                foreach (var res in recipe.Results)
                    if (!string.IsNullOrEmpty(res.Name)) referencedItems.Add(res.Name);
            }

            var unreferencedItems = items
                .Where(i => !string.IsNullOrEmpty(i.Name) && !referencedItems.Contains(i.Name))
                .ToList();

            if (unreferencedItems.Count > 0)
            {
                Console.WriteLine($"  Предметов, не используемых в рецептах: {unreferencedItems.Count}");

                foreach (var item in unreferencedItems.Take(20))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Info, ValidationCategory.CrossReference,
                        item.Name, item.Type, "usage", "Предмет не используется ни в одном рецепте");
                }
                if (unreferencedItems.Count > 20)
                {
                    TryAddIssue(result, filter, ValidationSeverity.Info, ValidationCategory.CrossReference,
                        "[множество]", "item", "usage", $"И ещё {unreferencedItems.Count - 20} предметов не используются в рецептах");
                }
            }

            var unknownReferenced = referencedItems.Where(r => !knownItems.Contains(r)).ToList();
            if (unknownReferenced.Count > 0)
            {
                Console.WriteLine($"  Неизвестных предметов в ссылках: {unknownReferenced.Count}");
            }
        }

        /// <summary>
        /// Добавляет проблему только если её severity разрешён фильтром.
        /// Если фильтр null или пустой — добавляет всегда.
        /// </summary>
        private void TryAddIssue(ValidationResult result, HashSet<ValidationSeverity>? filter,
            ValidationSeverity severity, ValidationCategory category,
            string entityName, string entityType, string field, string message, string details = "")
        {
            if (filter != null && filter.Count > 0 && !filter.Contains(severity))
                return;

            result.AddIssue(severity, category, entityName, entityType, field, message, details);
        }

        private void PrintSummary(ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            Console.WriteLine("\n=== Результаты самопроверки ===");
            Console.WriteLine($"Всего предметов: {result.TotalItems}");
            Console.WriteLine($"Всего рецептов: {result.TotalRecipes}");
            Console.WriteLine($"Найдено проблем (после фильтрации): {result.Issues.Count}");
            Console.WriteLine($"  • Ошибок (Error):      {result.ErrorCount}");
            Console.WriteLine($"  • Предупреждений:      {result.WarningCount}");
            Console.WriteLine($"  • Информационных:      {result.InfoCount}");

            if (result.HasErrors)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n ОБНАРУЖЕНЫ КРИТИЧЕСКИЕ ОШИБКИ!");
                Console.ResetColor();
            }
            else if (result.WarningCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n⚠ Есть предупреждения, но данные в целом корректны.");
                Console.ResetColor();
            }
            else if (result.Issues.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\nℹ Найдены только информационные сообщения.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n✓ Все данные прошли проверку успешно!");
                Console.ResetColor();
            }

            // Выводим топ-15 самых серьёзных проблем
            var topIssues = result.Issues
                .OrderByDescending(i => i.Severity)
                .ThenBy(i => i.EntityName)
                .Take(15)
                .ToList();

            if (topIssues.Count > 0)
            {
                Console.WriteLine($"\nТоп-{Math.Min(15, topIssues.Count)} проблем:");
                foreach (var issue in topIssues)
                {
                    Console.ForegroundColor = issue.Severity switch
                    {
                        ValidationSeverity.Error => ConsoleColor.Red,
                        ValidationSeverity.Warning => ConsoleColor.Yellow,
                        _ => ConsoleColor.Gray
                    };
                    Console.WriteLine($"  {issue}");
                    Console.ResetColor();
                }
            }
        }
    }
}