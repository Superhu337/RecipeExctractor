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

        public ValidationResult Validate(List<Item> items, List<Recipe> recipes)
        {
            var result = new ValidationResult
            {
                TotalItems = items.Count,
                TotalRecipes = recipes.Count
            };

            Console.WriteLine("\n=== Запуск самопроверки данных ===");

            // Сбор всех известных имён предметов для перекрёстных ссылок
            var knownItemNames = new HashSet<string>(
                items.Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n))
            );

            ValidateItems(items, knownItemNames, result);
            ValidateRecipes(recipes, knownItemNames, result);
            ValidateCrossReferences(items, recipes, knownItemNames, result);

            PrintSummary(result);

            return result;
        }

        private void ValidateItems(List<Item> items, HashSet<string> knownNames, ValidationResult result)
        {
            Console.WriteLine("Проверка предметов...");

            var seenNames = new HashSet<string>();
            var itemsWithoutIcon = new List<string>();
            var itemsWithoutSubgroup = new List<string>();

            foreach (var item in items)
            {
                // Проверка имени
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    result.AddIssue(ValidationSeverity.Error, ValidationCategory.Item,
                        "[без имени]", item.Type, "name",
                        "Предмет без имени",
                        $"Тип: {item.Type}");
                    continue;
                }

                // Проверка дубликатов
                if (!seenNames.Add(item.Name))
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Item,
                        item.Name, item.Type, "name",
                        "Дублирующееся имя предмета",
                        $"Тип: {item.Type}");
                }

                // Проверка типа
                if (string.IsNullOrWhiteSpace(item.Type))
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Item,
                        item.Name, item.Type, "type",
                        "Отсутствует тип предмета");
                }

                // Проверка иконки
                if (string.IsNullOrWhiteSpace(item.IconFileName))
                {
                    itemsWithoutIcon.Add(item.Name);
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Item,
                        item.Name, item.Type, "icon",
                        "Отсутствует путь к иконке");
                }
                else if (!File.Exists(item.IconFileName))
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Icon,
                        item.Name, item.Type, "icon",
                        "Файл иконки не найден на диске",
                        item.IconFileName);
                }

                // Проверка размера стака
                if (item.StackSize <= 0)
                {
                    result.AddIssue(ValidationSeverity.Info, ValidationCategory.Item,
                        item.Name, item.Type, "stack_size",
                        "Размер стака не задан или равен 0",
                        $"Значение: {item.StackSize}");
                }
                else if (item.StackSize > 10000)
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Item,
                        item.Name, item.Type, "stack_size",
                        "Подозрительно большой размер стака",
                        $"Значение: {item.StackSize}");
                }

                // Проверка subgroup
                if (string.IsNullOrWhiteSpace(item.Subgroup))
                {
                    itemsWithoutSubgroup.Add(item.Name);
                }

                // Проверка order
                if (string.IsNullOrWhiteSpace(item.Order))
                {
                    result.AddIssue(ValidationSeverity.Info, ValidationCategory.Item,
                        item.Name, item.Type, "order",
                        "Отсутствует поле order (сортировка)");
                }
            }

            // Сводка по предметам без иконок
            if (itemsWithoutIcon.Count > 0)
            {
                Console.WriteLine($"  Предметов без иконки: {itemsWithoutIcon.Count}");
            }
            if (itemsWithoutSubgroup.Count > 0)
            {
                Console.WriteLine($"  Предметов без subgroup: {itemsWithoutSubgroup.Count}");
            }
        }

        private void ValidateRecipes(List<Recipe> recipes, HashSet<string> knownItems, ValidationResult result)
        {
            Console.WriteLine("Проверка рецептов...");

            var seenNames = new HashSet<string>();
            var recipesWithEmptyIngredients = new List<string>();
            var recipesWithEmptyResults = new List<string>();

            foreach (var recipe in recipes)
            {
                // Проверка имени
                if (string.IsNullOrWhiteSpace(recipe.Name))
                {
                    result.AddIssue(ValidationSeverity.Error, ValidationCategory.Recipe,
                        "[без имени]", "recipe", "name",
                        "Рецепт без имени");
                    continue;
                }

                // Проверка дубликатов
                if (!seenNames.Add(recipe.Name))
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "name",
                        "Дублирующееся имя рецепта");
                }

                // Проверка энергии
                if (recipe.EnergyRequired <= 0)
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "energy_required",
                        "Время создания <= 0",
                        $"Значение: {recipe.EnergyRequired}");
                }
                else if (recipe.EnergyRequired > 1000)
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "energy_required",
                        "Подозрительно большое время создания",
                        $"Значение: {recipe.EnergyRequired}");
                }

                // Проверка ингредиентов
                if (recipe.Ingredients == null || recipe.Ingredients.Count == 0)
                {
                    recipesWithEmptyIngredients.Add(recipe.Name);
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "ingredients",
                        "Рецепт не имеет ингредиентов");
                }
                else
                {
                    ValidateIngredients(recipe, knownItems, result);
                }

                // Проверка результатов (теперь корректно обрабатываются оба формата)
                if (recipe.Results == null || recipe.Results.Count == 0)
                {
                    // Проверяем формат рецепта
                    if (recipe.RecipeFormat == "unknown")
                    {
                        recipesWithEmptyResults.Add(recipe.Name);
                        result.AddIssue(ValidationSeverity.Error, ValidationCategory.Recipe,
                            recipe.Name, "recipe", "results",
                            "Рецепт не имеет результатов (неизвестный формат)");
                    }
                    else
                    {
                        result.AddIssue(ValidationSeverity.Info, ValidationCategory.Recipe,
                            recipe.Name, "recipe", "results",
                            $"Рецепт использует формат '{recipe.RecipeFormat}' без результатов");
                    }
                }
                else
                {
                    ValidateResults(recipe, knownItems, result);
                }

                // Проверка главного продукта
                if (string.IsNullOrEmpty(recipe.MainProduct) && recipe.Results.Count > 0)
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "main_product",
                        "Не определен главный продукт рецепта");
                }

                // Проверка несоответствия имени рецепта и результата
                if (recipe.Results.Count == 1 && recipe.RecipeFormat == "single")
                {
                    var singleResult = recipe.Results[0];
                    if (singleResult.Name != recipe.Name && singleResult.Amount == 1)
                    {
                        result.AddIssue(ValidationSeverity.Info, ValidationCategory.Recipe,
                            recipe.Name, "recipe", "name_mismatch",
                            "Имя рецепта не совпадает с результатом (возможно, это нормально)",
                            $"Рецепт: {recipe.Name}, Результат: {singleResult.Name}");
                    }
                }

                // Проверка иконки рецепта
                if (string.IsNullOrWhiteSpace(recipe.IconFileName))
                {
                    result.AddIssue(ValidationSeverity.Info, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "icon",
                        "Рецепт не имеет собственной иконки (используется иконка результата)");
                }
            }

            if (recipesWithEmptyIngredients.Count > 0)
            {
                Console.WriteLine($"  Рецептов без ингредиентов: {recipesWithEmptyIngredients.Count}");
            }
            if (recipesWithEmptyResults.Count > 0)
            {
                Console.WriteLine($"  Рецептов без результатов: {recipesWithEmptyResults.Count}");
            }
        }

        private void ValidateIngredients(Recipe recipe, HashSet<string> knownItems, ValidationResult result)
        {
            var seenIngredients = new HashSet<string>();

            foreach (var ingredient in recipe.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ingredient.Name))
                {
                    result.AddIssue(ValidationSeverity.Error, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "ingredients",
                        "Ингредиент без имени");
                    continue;
                }

                // Проверка дубликатов в одном рецепте
                if (!seenIngredients.Add(ingredient.Name))
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "ingredients",
                        "Дублирующийся ингредиент в рецепте",
                        $"Ингредиент: {ingredient.Name}");
                }

                // Проверка количества
                if (ingredient.Amount <= 0)
                {
                    result.AddIssue(ValidationSeverity.Error, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "ingredients",
                        "Количество ингредиента <= 0",
                        $"{ingredient.Name}: {ingredient.Amount}");
                }

                // Проверка ссылки на известный предмет
                if (!knownItems.Contains(ingredient.Name))
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.CrossReference,
                        recipe.Name, "recipe", "ingredients",
                        "Ингредиент ссылается на неизвестный предмет",
                        $"Неизвестный предмет: {ingredient.Name}");
                }
            }
        }

        private void ValidateResults(Recipe recipe, HashSet<string> knownItems, ValidationResult result)
        {
            var seenResults = new HashSet<string>();

            foreach (var resultItem in recipe.Results)
            {
                if (string.IsNullOrWhiteSpace(resultItem.Name))
                {
                    result.AddIssue(ValidationSeverity.Error, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "results",
                        "Результат без имени");
                    continue;
                }

                if (!seenResults.Add(resultItem.Name))
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "results",
                        "Дублирующийся результат в рецепте",
                        $"Результат: {resultItem.Name}");
                }

                if (resultItem.Amount <= 0)
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "results",
                        "Количество результата <= 0",
                        $"{resultItem.Name}: {resultItem.Amount}");
                }

                if (resultItem.Probability < 0 || resultItem.Probability > 1)
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.Recipe,
                        recipe.Name, "recipe", "results",
                        "Вероятность результата вне диапазона [0, 1]",
                        $"{resultItem.Name}: {resultItem.Probability}");
                }

                if (!knownItems.Contains(resultItem.Name))
                {
                    result.AddIssue(ValidationSeverity.Warning, ValidationCategory.CrossReference,
                        recipe.Name, "recipe", "results",
                        "Результат ссылается на неизвестный предмет",
                        $"Неизвестный предмет: {resultItem.Name}");
                }
            }
        }

        private void ValidateCrossReferences(List<Item> items, List<Recipe> recipes,
            HashSet<string> knownItems, ValidationResult result)
        {
            Console.WriteLine("Проверка перекрёстных ссылок...");

            // Предметы, на которые ссылаются рецепты
            var referencedItems = new HashSet<string>();
            foreach (var recipe in recipes)
            {
                foreach (var ing in recipe.Ingredients)
                    if (!string.IsNullOrEmpty(ing.Name)) referencedItems.Add(ing.Name);
                foreach (var res in recipe.Results)
                    if (!string.IsNullOrEmpty(res.Name)) referencedItems.Add(res.Name);
            }

            // Предметы, на которые никто не ссылается (кроме самих предметов)
            var unreferencedItems = items
                .Where(i => !string.IsNullOrEmpty(i.Name) && !referencedItems.Contains(i.Name))
                .ToList();

            if (unreferencedItems.Count > 0)
            {
                Console.WriteLine($"  Предметов, не используемых в рецептах: {unreferencedItems.Count}");
                // Выводим только первые 20, чтобы не засорять консоль
                foreach (var item in unreferencedItems.Take(20))
                {
                    result.AddIssue(ValidationSeverity.Info, ValidationCategory.CrossReference,
                        item.Name, item.Type, "usage",
                        "Предмет не используется ни в одном рецепте");
                }
                if (unreferencedItems.Count > 20)
                {
                    result.AddIssue(ValidationSeverity.Info, ValidationCategory.CrossReference,
                        "[множество]", "item", "usage",
                        $"И ещё {unreferencedItems.Count - 20} предметов не используются в рецептах");
                }
            }

            // Неизвестные предметы, на которые ссылаются рецепты (уже добавлены в ValidateIngredients/Results)
            var unknownReferenced = referencedItems.Where(r => !knownItems.Contains(r)).ToList();
            if (unknownReferenced.Count > 0)
            {
                Console.WriteLine($"  Неизвестных предметов в ссылках: {unknownReferenced.Count}");
            }
        }

        private void PrintSummary(ValidationResult result)
        {
            Console.WriteLine("\n=== Результаты самопроверки ===");
            Console.WriteLine($"Всего предметов: {result.TotalItems}");
            Console.WriteLine($"Всего рецептов: {result.TotalRecipes}");
            Console.WriteLine($"Найдено проблем: {result.Issues.Count}");
            Console.WriteLine($"  • Ошибок (Error):      {result.ErrorCount}");
            Console.WriteLine($"  • Предупреждений:      {result.WarningCount}");
            Console.WriteLine($"  • Информационных:      {result.InfoCount}");

            if (result.HasErrors)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n✗ ОБНАРУЖЕНЫ КРИТИЧЕСКИЕ ОШИБКИ!");
                Console.ResetColor();
            }
            else if (result.WarningCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n⚠ Есть предупреждения, но данные в целом корректны.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n✓ Все данные прошли проверку успешно!");
                Console.ResetColor();
            }

            // Выводим топ-10 самых серьёзных проблем
            var topIssues = result.Issues
                .OrderByDescending(i => i.Severity)
                .ThenBy(i => i.EntityName)
                .Take(15)
                .ToList();

            if (topIssues.Count > 0)
            {
                Console.WriteLine("\nТоп-15 проблем:");
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