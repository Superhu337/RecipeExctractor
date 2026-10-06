using Newtonsoft.Json;
using NLua;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Diagnostics;
namespace RecipeExtractor
{
    // --- Модели данных для JSON ---

    public class FactorioItemAmount
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = "item"; // "item" или "fluid"

        [JsonProperty("amount")]
        public double Amount { get; set; }

        [JsonProperty("probability", NullValueHandling = NullValueHandling.Ignore)]
        public double? Probability { get; set; } // Для вероятностного выхода
    }

    public class FactorioRecipe
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("category")]
        public string Category { get; set; } = "crafting";

        [JsonProperty("energy_required")]
        public double EnergyRequired { get; set; } = 0.5; // Время крафта в секундах (по умолчанию 0.5)

        [JsonProperty("icon")]
        public string Icon { get; set; } = string.Empty;

        [JsonProperty("ingredients")]
        public List<FactorioItemAmount> Ingredients { get; set; } = new();

        [JsonProperty("products")]
        public List<FactorioItemAmount> Products { get; set; } = new();
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Factorio Recipe Extractor ===");

            string factorioDataPath = @"C:\Program Files (x86)\Steam\steamapps\common\Factorio\data";

            if (!Directory.Exists(factorioDataPath))
            {
                Console.Write("Введите путь к директории Factorio/data: ");
                factorioDataPath = Console.ReadLine()?.Trim('"') ?? "";
            }

            if (!Directory.Exists(factorioDataPath))
            {
                Console.WriteLine("Ошибка: Указанный путь не существует.");
                return;
            }

            try
            {
                List<FactorioRecipe> recipes = ExtractRecipes(factorioDataPath);

                string exeFolder = AppContext.BaseDirectory;
                string outputPath = Path.Combine(exeFolder, "recipes.json");
                string jsonOutput = JsonConvert.SerializeObject(recipes, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(outputPath, jsonOutput);

                Console.WriteLine($"\nУспешно! Извлечено рецептов: {recipes.Count}");
                Console.WriteLine($"Файл сохранен в: {outputPath}");

               // //---Автоматическое открытие JSON файла-- -
               //Console.WriteLine("Открываем JSON файл...");
               // Process.Start(new ProcessStartInfo
               // {
               //     FileName = outputPath,
               //     UseShellExecute = true
               // });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nПроизошла ошибка: {ex.Message}");
            }
        
        }

        private static List<FactorioRecipe> ExtractRecipes(string factorioDataPath)
        {
            var recipes = new List<FactorioRecipe>();

            using (Lua lua = new Lua())
            {
                // Инициализируем минимальное окружение Lua для Factorio
                lua.DoString(@"
                    data = { raw = { recipe = {} } }
                    data.extend = function(self, other)
                        for _, obj in ipairs(other) do
                            if obj.type then
                                data.raw[obj.type] = data.raw[obj.type] or {}
                                data.raw[obj.type][obj.name] = obj
                            end
                        end
                    end
                ");

                // Загружаем прототипы рецептов ванильной игры
                string recipeLuaPath = Path.Combine(factorioDataPath, "base", "prototypes", "recipe.lua");
                if (!File.Exists(recipeLuaPath))
                {
                    throw new FileNotFoundException($"Файл рецептов не найден: {recipeLuaPath}");
                }

                lua.DoFile(recipeLuaPath);

                // Получаем таблицу data.raw.recipe
                LuaTable recipeTable = lua.GetTable("data.raw.recipe");

                foreach (var key in recipeTable.Keys)
                {
                    if (recipeTable[key] is LuaTable recipeData)
                    {
                        var parsedRecipe = ParseRecipe(recipeData);
                        if (parsedRecipe != null)
                        {
                            recipes.Add(parsedRecipe);
                        }
                    }
                }
            }

            return recipes;
        }

        private static FactorioRecipe? ParseRecipe(LuaTable raw)
        {
            string name = raw["name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return null;

            var recipe = new FactorioRecipe
            {
                Name = name,
                Category = raw["category"]?.ToString() ?? "crafting",
                Icon = raw["icon"]?.ToString() ?? ""
            };

            // Время выполнения (Energy Required)
            if (raw["energy_required"] != null)
            {
                recipe.EnergyRequired = Convert.ToDouble(raw["energy_required"]);
            }

            // В Factorio рецепты могут быть описаны напрямую или вложены в "normal"/"expensive"
            LuaTable? ingredientsTable = raw["ingredients"] as LuaTable;
            LuaTable? resultsTable = raw["results"] as LuaTable;
            string? singleResult = raw["result"] as string;

            // Если свойства сложены в 'normal' режим
            if (ingredientsTable == null && raw["normal"] is LuaTable normalTable)
            {
                ingredientsTable = normalTable["ingredients"] as LuaTable;
                resultsTable = normalTable["results"] as LuaTable;
                singleResult ??= normalTable["result"] as string;
                if (normalTable["energy_required"] != null)
                {
                    recipe.EnergyRequired = Convert.ToDouble(normalTable["energy_required"]);
                }
            }

            // --- Парсим Ингредиенты (Входы) ---
            if (ingredientsTable != null)
            {
                foreach (var value in ingredientsTable.Values)
                {
                    if (value is LuaTable ing)
                    {
                        recipe.Ingredients.Add(ParseItemAmount(ing));
                    }
                }
            }

            // --- Парсим Выходы (Результаты) ---
            // 1. Несколько выходов (таблица results)
            if (resultsTable != null)
            {
                foreach (var value in resultsTable.Values)
                {
                    if (value is LuaTable prod)
                    {
                        recipe.Products.Add(ParseItemAmount(prod));
                    }
                }
            }
            // 2. Один выход (короткая запись через result и result_count)
            else if (!string.IsNullOrEmpty(singleResult))
            {
                double count = 1;
                if (raw["result_count"] != null)
                {
                    count = Convert.ToDouble(raw["result_count"]);
                }

                recipe.Products.Add(new FactorioItemAmount
                {
                    Name = singleResult,
                    Type = "item",
                    Amount = count
                });
            }

            return recipe;
        }

        private static FactorioItemAmount ParseItemAmount(LuaTable itemTable)
        {
            var item = new FactorioItemAmount();

            // Сложная запись: {type="item", name="iron-plate", amount=2, probability=0.8}
            if (itemTable["name"] != null)
            {
                item.Name = itemTable["name"].ToString()!;
                item.Type = itemTable["type"]?.ToString() ?? "item";
                item.Amount = Convert.ToDouble(itemTable["amount"] ?? 1);

                if (itemTable["probability"] != null)
                {
                    item.Probability = Convert.ToDouble(itemTable["probability"]);
                }
            }
            // Короткая запись: {"iron-plate", 2}
            else if (itemTable[1] != null)
            {
                item.Name = itemTable[1].ToString()!;
                item.Type = "item";
                item.Amount = itemTable[2] != null ? Convert.ToDouble(itemTable[2]) : 1;
            }

            return item;
        }
    }
}