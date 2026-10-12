using RecipeExtractor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        public ValidationResult Validate(List<Item> items, List<Recipe> recipes, HashSet<ValidationSeverity>? severityFilter = null)
        {
            var result = new ValidationResult { TotalItems = items.Count, TotalRecipes = recipes.Count };

            Console.WriteLine("\n=== Запуск самопроверки данных ===");
            if (severityFilter != null && severityFilter.Count > 0)
                Console.WriteLine($"Режим фильтрации: [{string.Join(", ", severityFilter.Select(s => s.ToString()))}]");
            else
                Console.WriteLine("Режим фильтрации: ВСЕ проверки");

            // === АВТОМАТИЧЕСКОЕ СОЗДАНИЕ ЗАГЛУШЕК ДЛЯ ОТСУТСТВУЮЩИХ ПРЕДМЕТОВ ===
            var missingItems = ResolveMissingItems(items, recipes);
            if (missingItems.Count > 0)
            {
                items.AddRange(missingItems);
                result.TotalItems = items.Count;
                Console.WriteLine($"✓ Добавлено {missingItems.Count} заглушек для отсутствующих предметов");
            }
            // =====================================================================

            var knownItemNames = new HashSet<string>(items.Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n)));

            ValidateItems(items, knownItemNames, result, severityFilter);
            ValidateRecipes(recipes, knownItemNames, result, severityFilter);
            ValidateCrossReferences(items, recipes, knownItemNames, result, severityFilter);

            PrintSummary(result, severityFilter);
            return result;
        }

        private List<Item> ResolveMissingItems(List<Item> items, List<Recipe> recipes)
        {
            var existingNames = new HashSet<string>(items.Select(i => i.Name));
            var referencedNames = new HashSet<string>();

            foreach (var recipe in recipes)
            {
                foreach (var ing in recipe.Ingredients) if (!string.IsNullOrEmpty(ing.Name)) referencedNames.Add(ing.Name);
                foreach (var res in recipe.Results) if (!string.IsNullOrEmpty(res.Name)) referencedNames.Add(res.Name);
            }

            var missingNames = referencedNames.Where(n => !existingNames.Contains(n)).ToList();
            if (missingNames.Count == 0) return new List<Item>();

            Console.WriteLine($"\n⚠ Найдено {missingNames.Count} отсутствующих предметов, создаю заглушки...");
            var stubItems = new List<Item>();

            foreach (var name in missingNames)
            {
                var iconPath = TryFindIconForItem(name);
                stubItems.Add(new Item
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
                });
                Console.WriteLine($"  + Создана заглушка: {name} (иконка: {(iconPath != null ? "найдена" : "не найдена")})");
            }
            return stubItems;
        }

        private string? TryFindIconForItem(string itemName)
        {
            var possiblePaths = new[]
            {
                $"base/graphics/icons/{itemName}.png",
                $"base/graphics/icons/fluid/{itemName}.png",
                $"base/graphics/icons/equipment/{itemName}.png"
            };

            foreach (var relativePath in possiblePaths)
            {
                var fullPath = Path.Combine(_factorioPath, "data", relativePath);
                if (File.Exists(fullPath)) return fullPath;
            }
            return null;
        }

        private void ValidateItems(List<Item> items, HashSet<string> knownNames, ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            Console.WriteLine("Проверка предметов...");
            var seenNames = new HashSet<string>();
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Item, "[без имени]", item.Type, "name", "Предмет без имени");
                    continue;
                }
                if (!seenNames.Add(item.Name))
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Item, item.Name, item.Type, "name", "Дублирующееся имя предмета");

                if (string.IsNullOrWhiteSpace(item.IconPath))
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Item, item.Name, item.Type, "icon", "Отсутствует путь к иконке");
                else if (!File.Exists(item.IconPath))
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Icon, item.Name, item.Type, "icon", "Файл иконки не найден", item.IconPath);

                if (item.StackSize > 10000)
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Item, item.Name, item.Type, "stack_size", "Подозрительно большой размер стака", $"Значение: {item.StackSize}");
            }
        }

        private void ValidateRecipes(List<Recipe> recipes, HashSet<string> knownItems, ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            Console.WriteLine("Проверка рецептов...");
            var seenNames = new HashSet<string>();
            foreach (var recipe in recipes)
            {
                if (string.IsNullOrWhiteSpace(recipe.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe, "[без имени]", "recipe", "name", "Рецепт без имени");
                    continue;
                }
                if (!seenNames.Add(recipe.Name))
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe, recipe.Name, "recipe", "name", "Дублирующееся имя рецепта");

                if (recipe.Ingredients == null || recipe.Ingredients.Count == 0)
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe, recipe.Name, "recipe", "ingredients", "Рецепт не имеет ингредиентов");
                else
                    ValidateIngredients(recipe, knownItems, result, filter);

                if (recipe.Results == null || recipe.Results.Count == 0)
                {
                    // Проверяем, есть ли результат в properties (старый формат)
                    bool hasOldFormat = recipe.Properties != null && recipe.Properties.ContainsKey("result");
                    if (!hasOldFormat)
                        TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe, recipe.Name, "recipe", "results", "Рецепт не имеет результатов");
                    else
                        TryAddIssue(result, filter, ValidationSeverity.Info, ValidationCategory.Recipe, recipe.Name, "recipe", "results", "Рецепт использует старый формат (поле result)");
                }
                else
                {
                    ValidateResults(recipe, knownItems, result, filter);
                }
            }
        }

        private void ValidateIngredients(Recipe recipe, HashSet<string> knownItems, ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            var seen = new HashSet<string>();
            foreach (var ing in recipe.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ing.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe, recipe.Name, "recipe", "ingredients", "Ингредиент без имени");
                    continue;
                }
                if (!seen.Add(ing.Name))
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe, recipe.Name, "recipe", "ingredients", "Дублирующийся ингредиент", ing.Name);

                if (ing.Amount <= 0)
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe, recipe.Name, "recipe", "ingredients", "Количество <= 0", $"{ing.Name}: {ing.Amount}");

                if (!knownItems.Contains(ing.Name))
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.CrossReference, recipe.Name, "recipe", "ingredients", "Ссылка на неизвестный предмет", ing.Name);
            }
        }

        private void ValidateResults(Recipe recipe, HashSet<string> knownItems, ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            var seen = new HashSet<string>();
            foreach (var res in recipe.Results)
            {
                if (string.IsNullOrWhiteSpace(res.Name))
                {
                    TryAddIssue(result, filter, ValidationSeverity.Error, ValidationCategory.Recipe, recipe.Name, "recipe", "results", "Результат без имени");
                    continue;
                }
                if (!seen.Add(res.Name))
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.Recipe, recipe.Name, "recipe", "results", "Дублирующийся результат", res.Name);

                if (!knownItems.Contains(res.Name))
                    TryAddIssue(result, filter, ValidationSeverity.Warning, ValidationCategory.CrossReference, recipe.Name, "recipe", "results", "Ссылка на неизвестный предмет", res.Name);
            }
        }

        private void ValidateCrossReferences(List<Item> items, List<Recipe> recipes, HashSet<string> knownItems, ValidationResult result, HashSet<ValidationSeverity>? filter)
        {
            Console.WriteLine("Проверка перекрёстных ссылок...");
            var referenced = new HashSet<string>();
            foreach (var recipe in recipes)
            {
                foreach (var ing in recipe.Ingredients) if (!string.IsNullOrEmpty(ing.Name)) referenced.Add(ing.Name);
                foreach (var res in recipe.Results) if (!string.IsNullOrEmpty(res.Name)) referenced.Add(res.Name);
            }

            var unreferenced = items.Where(i => !string.IsNullOrEmpty(i.Name) && !referenced.Contains(i.Name)).ToList();
            if (unreferenced.Count > 20)
            {
                TryAddIssue(result, filter, ValidationSeverity.Info, ValidationCategory.CrossReference, "[множество]", "item", "usage", $"И ещё {unreferenced.Count - 20} предметов не используются в рецептах");
            }
        }

        private void TryAddIssue(ValidationResult result, HashSet<ValidationSeverity>? filter, ValidationSeverity severity, ValidationCategory category, string entityName, string entityType, string field, string message, string details = "")
        {
            if (filter != null && filter.Count > 0 && !filter.Contains(severity)) return;
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
        }
    }
}