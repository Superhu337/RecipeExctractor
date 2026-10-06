using Newtonsoft.Json;
using NLua;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Formatting = Newtonsoft.Json.Formatting;


namespace RecipeExtractor
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public class FactorioItemAmount
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = "item";

        [JsonProperty("amount")]
        public double Amount { get; set; } = 1;

        [JsonProperty("probability", NullValueHandling = NullValueHandling.Ignore)]
        public double? Probability { get; set; }
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public class FactorioRecipe
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("category")]
        public string Category { get; set; } = "crafting";

        [JsonProperty("energy_required")]
        public double EnergyRequired { get; set; } = 0.5;

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
            Console.WriteLine("=== Factorio Recipe & Resource Extractor ===");

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
                string exeFolder = AppContext.BaseDirectory;
                string iconsFolder = Path.Combine(exeFolder, "icons");
                Directory.CreateDirectory(iconsFolder);

                List<FactorioRecipe> recipes = ExtractRecipesAndResources(factorioDataPath, iconsFolder);

                string outputPath = Path.Combine(exeFolder, "recipes.json");
                string jsonOutput = JsonConvert.SerializeObject(recipes, Formatting.Indented);
                File.WriteAllText(outputPath, jsonOutput);

                Console.WriteLine($"\nУспешно! Извлечено рецептов и ресурсов: {recipes.Count}");
                Console.WriteLine($"Файл сохранен в: {outputPath}");

                Console.WriteLine("Открываем JSON файл...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = outputPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nПроизошла ошибка: {ex.Message}");
            }
        }

        private static List<FactorioRecipe> ExtractRecipesAndResources(string factorioDataPath, string iconsFolder)
        {
            var recipes = new List<FactorioRecipe>();
            int iconsCopied = 0;

            using (Lua lua = new Lua())
            {
                string corePath = Path.Combine(factorioDataPath, "core", "lualib", "?.lua").Replace('\\', '/');
                string basePath = Path.Combine(factorioDataPath, "base", "?.lua").Replace('\\', '/');

                lua.DoString($@"
                    package.path = package.path .. ';{corePath};{basePath}'

                    data = {{ raw = {{}} }}

                    function data:extend(other)
                        if type(other) ~= 'table' then return end
                        for _, obj in ipairs(other) do
                            if type(obj) == 'table' and obj.type and obj.name then
                                data.raw[obj.type] = data.raw[obj.type] or {{}}
                                data.raw[obj.type][obj.name] = obj
                            end
                        end
                    end

                    defines = {{
                        difficulty_settings = {{ recipe_difficulty = {{ normal = 0, expensive = 1 }} }},
                        direction = {{ north = 0, east = 2, south = 4, west = 6 }},
                        render_mode = {{ game = 1 }}
                    }}

                    util = util or {{}}
                    util.table = util.table or {{}}
                    util.table.deepcopy = function(t) return t end

                    local old_require = require
                    require = function(mod)
                        local success, result = pcall(old_require, mod)
                        if success then
                            return result
                        else
                            return setmetatable({{}}, {{
                                __index = function(_, k)
                                    return function() return {{}} end
                                end
                            }})
                        end
                    end
                ");

                // 1. Загружаем ядро
                RunLuaFileIfExists(lua, Path.Combine(factorioDataPath, "core", "lualib", "util.lua"));
                RunLuaFileIfExists(lua, Path.Combine(factorioDataPath, "core", "data.lua"));

                // 2. Сканируем все файлы из prototypes
                string basePrototypes = Path.Combine(factorioDataPath, "base", "prototypes");
                if (Directory.Exists(basePrototypes))
                {
                    foreach (var file in Directory.GetFiles(basePrototypes, "*.lua", SearchOption.AllDirectories))
                    {
                        RunLuaFileIfExists(lua, file);
                    }
                }

                // 3. Загружаем главный файл base
                RunLuaFileIfExists(lua, Path.Combine(factorioDataPath, "base", "data.lua"));

                // 4. Извлекаем обычные рецепты
                if (lua.GetTable("data.raw.recipe") is LuaTable recipeTable)
                {
                    foreach (var key in recipeTable.Keys)
                    {
                        if (recipeTable[key] is LuaTable recipeData)
                        {
                            var parsedRecipe = ParseRecipe(recipeData, lua, factorioDataPath, iconsFolder, ref iconsCopied);
                            if (parsedRecipe != null)
                            {
                                recipes.Add(parsedRecipe);
                            }
                        }
                    }
                }

                // 5. Извлекаем ресурсы из data.raw.resource
                if (lua.GetTable("data.raw.resource") is LuaTable resourceTable)
                {
                    foreach (var key in resourceTable.Keys)
                    {
                        if (resourceTable[key] is LuaTable resourceData)
                        {
                            var parsedResource = ParseResourceAsRecipe(resourceData, lua, factorioDataPath, iconsFolder, ref iconsCopied);
                            if (parsedResource != null && !recipes.Exists(r => r.Name == parsedResource.Name))
                            {
                                recipes.Add(parsedResource);
                            }
                        }
                    }
                }

                // 6. Гарантированная поддержка основных сырьевых ресурсов (stone, coal, ore и т.д.)
                string[] basicResources = { "stone", "coal", "iron-ore", "copper-ore", "uranium-ore" };
                if (lua.GetTable("data.raw.item") is LuaTable itemTable)
                {
                    foreach (var resName in basicResources)
                    {
                        string virtualName = $"mining-{resName}";
                        if (!recipes.Exists(r => r.Name == virtualName || r.Name == resName))
                        {
                            if (itemTable[resName] is LuaTable itemData)
                            {
                                var basicRecipe = CreateRecipeFromItem(itemData, lua, factorioDataPath, iconsFolder, ref iconsCopied);
                                if (basicRecipe != null)
                                {
                                    recipes.Add(basicRecipe);
                                }
                            }
                        }
                    }

                    // Сканирование прочих руд по паттернам
                    foreach (var key in itemTable.Keys)
                    {
                        string itemName = key?.ToString() ?? "";
                        if (itemName.EndsWith("-ore") || itemName.Contains("raw-"))
                        {
                            string virtualRecipeName = $"mining-{itemName}";
                            if (!recipes.Exists(r => r.Name == virtualRecipeName || r.Name == itemName))
                            {
                                if (itemTable[key] is LuaTable itemData)
                                {
                                    var oreRecipe = CreateRecipeFromItem(itemData, lua, factorioDataPath, iconsFolder, ref iconsCopied);
                                    if (oreRecipe != null)
                                    {
                                        recipes.Add(oreRecipe);
                                    }
                                }
                            }
                        }
                    }
                }

                // 7. Добавляем насосные ресурсы (вода, нефть)
                AddFluidResourceIfMissing(recipes, "water", "fluid", lua, factorioDataPath, iconsFolder, ref iconsCopied);
                AddFluidResourceIfMissing(recipes, "crude-oil", "fluid", lua, factorioDataPath, iconsFolder, ref iconsCopied);
            }

            Console.WriteLine($"Успешно скопировано иконок: {iconsCopied}");
            return recipes;
        }

        private static void RunLuaFileIfExists(Lua lua, string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    lua.DoFile(filePath);
                }
                catch
                {
                    // Игнорируем ошибки специфичных скриптов Lua
                }
            }
        }

        private static FactorioRecipe? ParseRecipe(LuaTable raw, Lua lua, string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            string name = raw["name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return null;

            var recipe = new FactorioRecipe
            {
                Name = name,
                Category = raw["category"]?.ToString() ?? "crafting"
            };

            if (raw["energy_required"] != null)
            {
                recipe.EnergyRequired = Convert.ToDouble(raw["energy_required"]);
            }

            LuaTable? ingredientsTable = raw["ingredients"] as LuaTable;
            LuaTable? resultsTable = raw["results"] as LuaTable;
            string? singleResult = raw["result"] as string;

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
            else if (!string.IsNullOrEmpty(singleResult))
            {
                double count = Convert.ToDouble(raw["result_count"] ?? 1);
                recipe.Products.Add(new FactorioItemAmount
                {
                    Name = singleResult,
                    Type = "item",
                    Amount = count
                });
            }
            else
            {
                recipe.Products.Add(new FactorioItemAmount
                {
                    Name = name,
                    Type = "item",
                    Amount = 1
                });
            }

            string? rawIconPath = ExtractIconPath(raw) ?? GetFallbackIconFromPrototypes(recipe.Products, lua, name);
            CopyAndAssignIcon(recipe, rawIconPath, factorioDataPath, iconsFolder, ref iconsCopied);

            return recipe;
        }

        private static FactorioRecipe? ParseResourceAsRecipe(LuaTable raw, Lua lua, string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            string name = raw["name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return null;

            LuaTable? minable = raw["minable"] as LuaTable;

            var recipe = new FactorioRecipe
            {
                Name = $"mining-{name}",
                Category = "mining",
                EnergyRequired = minable != null ? Convert.ToDouble(minable["mining_time"] ?? 1.0) : 1.0,
                Ingredients = new List<FactorioItemAmount>()
            };

            string resultType = "item";

            if (minable != null)
            {
                if (minable["results"] is LuaTable resultsTable)
                {
                    foreach (var value in resultsTable.Values)
                    {
                        if (value is LuaTable prod)
                        {
                            recipe.Products.Add(ParseItemAmount(prod));
                        }
                    }
                }
                else if (minable["result"] != null)
                {
                    string resultName = minable["result"].ToString()!;
                    double count = Convert.ToDouble(minable["count"] ?? 1);
                    recipe.Products.Add(new FactorioItemAmount
                    {
                        Name = resultName,
                        Type = "item",
                        Amount = count
                    });
                }
            }

            if (recipe.Products.Count == 0)
            {
                if (name == "crude-oil") resultType = "fluid";

                recipe.Products.Add(new FactorioItemAmount
                {
                    Name = name,
                    Type = resultType,
                    Amount = 1
                });
            }

            string? rawIconPath = ExtractIconPath(raw) ?? GetFallbackIconFromPrototypes(recipe.Products, lua, name);
            CopyAndAssignIcon(recipe, rawIconPath, factorioDataPath, iconsFolder, ref iconsCopied);

            return recipe;
        }

        private static FactorioRecipe? CreateRecipeFromItem(LuaTable itemTable, Lua lua, string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            string name = itemTable["name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return null;

            var recipe = new FactorioRecipe
            {
                Name = $"mining-{name}",
                Category = "mining",
                EnergyRequired = 1.0,
                Ingredients = new List<FactorioItemAmount>(),
                Products = new List<FactorioItemAmount>
                {
                    new FactorioItemAmount { Name = name, Type = "item", Amount = 1 }
                }
            };

            string? rawIconPath = ExtractIconPath(itemTable) ?? GetFallbackIconFromPrototypes(recipe.Products, lua, name);
            CopyAndAssignIcon(recipe, rawIconPath, factorioDataPath, iconsFolder, ref iconsCopied);

            return recipe;
        }

        private static void AddFluidResourceIfMissing(List<FactorioRecipe> recipes, string fluidName, string fluidType, Lua lua, string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            string recipeName = $"pumping-{fluidName}";
            if (recipes.Exists(r => r.Name == recipeName || r.Name == fluidName)) return;

            var recipe = new FactorioRecipe
            {
                Name = recipeName,
                Category = "pumping",
                EnergyRequired = 1.0,
                Ingredients = new List<FactorioItemAmount>(),
                Products = new List<FactorioItemAmount>
                {
                    new FactorioItemAmount
                    {
                        Name = fluidName,
                        Type = fluidType,
                        Amount = 100
                    }
                }
            };

            string? rawIconPath = GetFallbackIconFromPrototypes(recipe.Products, lua, fluidName);
            CopyAndAssignIcon(recipe, rawIconPath, factorioDataPath, iconsFolder, ref iconsCopied);

            recipes.Add(recipe);
        }

        private static void CopyAndAssignIcon(FactorioRecipe recipe, string? rawIconPath, string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            if (string.IsNullOrEmpty(rawIconPath)) return;

            string fullSourcePath = ResolveFactorioPath(rawIconPath, factorioDataPath);

            if (File.Exists(fullSourcePath))
            {
                string targetFileName = $"{recipe.Name}{Path.GetExtension(fullSourcePath)}";
                string targetFilePath = Path.Combine(iconsFolder, targetFileName);

                File.Copy(fullSourcePath, targetFilePath, true);
                recipe.Icon = Path.Combine("icons", targetFileName).Replace('\\', '/');
                iconsCopied++;
            }
        }

        private static string? ExtractIconPath(LuaTable raw)
        {
            if (raw["icon"] != null)
            {
                return raw["icon"].ToString();
            }

            if (raw["icons"] is LuaTable iconsTable)
            {
                foreach (var val in iconsTable.Values)
                {
                    if (val is LuaTable iconEntry && iconEntry["icon"] != null)
                    {
                        return iconEntry["icon"].ToString();
                    }
                }
            }

            return null;
        }

        private static string? GetFallbackIconFromPrototypes(List<FactorioItemAmount> products, Lua lua, string fallbackName)
        {
            List<string> searchNames = new();

            foreach (var p in products)
            {
                if (!string.IsNullOrEmpty(p.Name)) searchNames.Add(p.Name);
            }
            if (!searchNames.Contains(fallbackName)) searchNames.Add(fallbackName);

            // Если передано mining-stone, также ищем просто stone
            if (fallbackName.StartsWith("mining-"))
            {
                string rawName = fallbackName.Replace("mining-", "");
                if (!searchNames.Contains(rawName)) searchNames.Add(rawName);
            }

            string[] possibleTypes = {
                "item", "fluid", "resource", "tool", "ammo", "capsule",
                "module", "armor", "gun", "item-with-entity-data", "mining-tool",
                "offshore-pump", "pipe", "container", "autoplace-control",
                "entity", "simple-entity"
            };

            foreach (var name in searchNames)
            {
                foreach (var type in possibleTypes)
                {
                    if (lua.GetTable($"data.raw.{type}.{name}") is LuaTable prototypeTable)
                    {
                        string? iconPath = ExtractIconPath(prototypeTable);
                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            return iconPath;
                        }
                    }
                }
            }

            return null;
        }

        private static string ResolveFactorioPath(string factorioPath, string factorioDataPath)
        {
            if (factorioPath.StartsWith("__") && factorioPath.Contains("__"))
            {
                int closeIndex = factorioPath.IndexOf("__", 2);
                if (closeIndex > 2)
                {
                    string modName = factorioPath.Substring(2, closeIndex - 2);
                    string relativePath = factorioPath.Substring(closeIndex + 2).TrimStart('/', '\\');
                    return Path.Combine(factorioDataPath, modName, relativePath);
                }
            }

            return Path.Combine(factorioDataPath, factorioPath.TrimStart('/', '\\'));
        }

        private static FactorioItemAmount ParseItemAmount(LuaTable itemTable)
{
    var item = new FactorioItemAmount();

    // ВАРИАНТ 1: Формат словаря { name = "processing-unit", amount = 250, type = "item" }
    if (itemTable["name"] != null)
    {
        item.Name = itemTable["name"].ToString()!;
        item.Type = itemTable["type"]?.ToString() ?? "item";

        if (itemTable["amount"] != null)
        {
            item.Amount = Convert.ToDouble(itemTable["amount"]);
        }
        else if (itemTable["amount_min"] != null)
        {
            item.Amount = Convert.ToDouble(itemTable["amount_min"]);
        }

        if (itemTable["probability"] != null)
        {
            item.Probability = Convert.ToDouble(itemTable["probability"]);
        }
    }
    // ВАРИАНТ 2: Формат позиционного массива { "processing-unit", 250 }
    else if (itemTable[1] != null)
    {
        item.Name = itemTable[1].ToString()!;
        item.Type = "item";
        
        if (itemTable[2] != null)
        {
            item.Amount = Convert.ToDouble(itemTable[2]);
        }
    }

    return item;
}
    }
}