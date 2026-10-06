using Newtonsoft.Json;
using NLua;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RecipeExtractor
{
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

                Console.WriteLine("Инициализация Lua окружения...");
                List<FactorioRecipe> recipes = ExtractRecipes(factorioDataPath, iconsFolder);

                ValidateRecipesAndIcons(recipes, exeFolder);

                string outputPath = Path.Combine(exeFolder, "recipes.json");
                string jsonOutput = JsonConvert.SerializeObject(recipes, Formatting.Indented);
                File.WriteAllText(outputPath, jsonOutput);

                Console.WriteLine($"\n✅ Успешно! Извлечено записей: {recipes.Count}");
                Console.WriteLine($"💾 Файл сохранен в: {outputPath}");

                Process.Start(new ProcessStartInfo { FileName = outputPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Произошла ошибка: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private static void ValidateRecipesAndIcons(List<FactorioRecipe> recipes, string baseDir)
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("=== ПРОВЕРКА ЦЕЛОСТНОСТИ РЕЦЕПТОВ И ИКОНОК ===");
            Console.WriteLine(new string('=', 60));

            var missingIcons = new List<string>();
            var missingFiles = new List<(string RecipeName, string IconPath)>();
            var noProducts = new List<string>();
            var invalidAmounts = new List<(string RecipeName, string ProductName, double Amount)>();

            foreach (var recipe in recipes)
            {
                if (recipe.Products == null || recipe.Products.Count == 0)
                    noProducts.Add(recipe.Name);

                foreach (var product in recipe.Products ?? new List<FactorioItemAmount>())
                {
                    if (product.Amount <= 0)
                        invalidAmounts.Add((recipe.Name, product.Name, product.Amount));
                }

                if (string.IsNullOrWhiteSpace(recipe.Icon))
                {
                    missingIcons.Add(recipe.Name);
                }
                else
                {
                    string fullIconPath = Path.Combine(baseDir, recipe.Icon.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(fullIconPath))
                        missingFiles.Add((recipe.Name, recipe.Icon));
                }
            }

            Console.WriteLine($"\n📊 Всего проверено записей: {recipes.Count}");

            if (missingIcons.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n⚠️ Рецепты БЕЗ ИКОНОК ({missingIcons.Count}):");
                Console.ResetColor();
                foreach (var name in missingIcons)
                    Console.WriteLine($"   • {name}");
            }

            if (missingFiles.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ БИТЫЕ ССЫЛКИ на иконки ({missingFiles.Count}):");
                Console.ResetColor();
                foreach (var item in missingFiles)
                {
                    Console.WriteLine($"   • {item.RecipeName}");
                    Console.WriteLine($"     Путь: {item.IconPath}");
                }
            }

            if (noProducts.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ Рецепты БЕЗ ПРОДУКТОВ ({noProducts.Count}):");
                Console.ResetColor();
                foreach (var name in noProducts)
                    Console.WriteLine($"   • {name}");
            }

            if (invalidAmounts.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n⚠️ Продукты с НЕКОРРЕКТНЫМ количеством ({invalidAmounts.Count}):");
                Console.ResetColor();
                foreach (var item in invalidAmounts)
                    Console.WriteLine($"   • {item.RecipeName} → {item.ProductName} (кол-во: {item.Amount})");
            }

            Console.WriteLine("\n" + new string('-', 60));
            int totalIssues = missingIcons.Count + missingFiles.Count + noProducts.Count + invalidAmounts.Count;

            if (totalIssues == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ ВСЕ РЕЦЕПТЫ И ИКОНКИ ПОЛНОСТЬЮ КОРРЕКТНЫ!");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"📋 ИТОГО ПРОБЛЕМ: {totalIssues}");
                Console.ResetColor();
                Console.WriteLine($"   • Пропущено иконок: {missingIcons.Count}");
                Console.WriteLine($"   • Битых ссылок: {missingFiles.Count}");
                Console.WriteLine($"   • Без продуктов: {noProducts.Count}");
                Console.WriteLine($"   • Некорректное количество: {invalidAmounts.Count}");
            }

            Console.WriteLine(new string('=', 60) + "\n");
        }

        private static List<FactorioRecipe> ExtractRecipes(string factorioDataPath, string iconsFolder)
        {
            var recipes = new List<FactorioRecipe>();
            int iconsCopied = 0;

            using (Lua lua = new Lua())
            {
                // 1. Настраиваем окружение Factorio
                lua.DoString(@"
                    log = function(msg) end
                    error = function(msg) end

                    defines = {}
                    setmetatable(defines, {
                        __index = function(t, k)
                            local sub_table = {}
                            setmetatable(sub_table, { __index = function(t2, k2) return 0 end })
                            return sub_table
                        end
                    })

                    data = { raw = {} }
                    function data:extend(objs)
                        if type(objs) ~= 'table' then return end
                        local items = (objs[1] ~= nil) and objs or {objs}
                        for _, obj in pairs(items) do
                            if type(obj) == 'table' and obj.type and obj.name then
                                data.raw[obj.type] = data.raw[obj.type] or {}
                                data.raw[obj.type][obj.name] = obj
                            end
                        end
                    end

                    script = {}
                    settings = { startup = {}, runtime = {}, player = {} }
                    game = {}
                ");

                // 2. Настраиваем пути и кастомный require
                string coreLuaPath = Path.Combine(factorioDataPath, "core", "lualib", "?.lua").Replace('\\', '/');
                string baseLuaPath = Path.Combine(factorioDataPath, "base", "?.lua").Replace('\\', '/');
                string baseFolderPath = factorioDataPath.Replace('\\', '/');

                lua.DoString($@"
                    package.path = package.path .. ';{coreLuaPath};{baseLuaPath}'

                    local old_require = require
                    require = function(modname)
                        if modname:match('^__') then
                            local mod_name = modname:match('__(.-)__')
                            local rest = modname:match('__(.-)__(.*)')
                            if rest then
                                rest = rest:sub(2)
                            else
                                rest = ''
                            end
                            local file_path = '{baseFolderPath}/' .. mod_name .. '/' .. rest .. '.lua'
                            local f, err = loadfile(file_path)
                            if f then
                                return f()
                            else
                                return {{}}
                            end
                        else
                            return old_require(modname)
                        end
                    end
                ");

                // 3. Загружаем util.lua
                try
                {
                    lua.DoFile(Path.Combine(factorioDataPath, "core", "lualib", "util.lua"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Предупреждение при загрузке util.lua: {ex.Message}");
                }

                // 4. Функция поиска иконки — ищет по ВСЕМ типам в data.raw
                lua.DoString(@"
                    function get_best_icon(name)
                        for type_name, type_table in pairs(data.raw) do
                            if type_table[name] then
                                local proto = type_table[name]
                                if proto.icon then
                                    return proto.icon
                                end
                                if proto.icons then
                                    for _, icon_data in ipairs(proto.icons) do
                                        if icon_data.icon then
                                            return icon_data.icon
                                        end
                                    end
                                end
                            end
                        end
                        return nil
                    end
                ");

                // 5. Загружаем файлы прототипов
                Console.WriteLine("Загрузка файлов прототипов...");
                string prototypesPath = Path.Combine(factorioDataPath, "base", "prototypes");

                string[] neededFiles = {
                    "item-group.lua",
                    "item.lua",
                    "fluid.lua",
                    "recipe.lua",
                    "technology.lua"
                };

                int loadedCount = 0;
                foreach (var fileName in neededFiles)
                {
                    string filePath = Path.Combine(prototypesPath, fileName);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            lua.DoFile(filePath);
                            loadedCount++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Ошибка при загрузке {fileName}: {ex.Message}");
                        }
                    }
                }
                Console.WriteLine($"✅ Загружено файлов: {loadedCount}");

                // Сканируем остальные файлы, пропуская ненужные
                if (Directory.Exists(prototypesPath))
                {
                    foreach (var file in Directory.GetFiles(prototypesPath, "*.lua", SearchOption.AllDirectories))
                    {
                        if (neededFiles.Any(f => file.EndsWith(f))) continue;

                        string relativePath = Path.GetRelativePath(prototypesPath, file);
                        if (relativePath.Contains("animation") ||
                            relativePath.Contains("sound") ||
                            relativePath.Contains("autoplace") ||
                            relativePath.Contains("noise") ||
                            relativePath.Contains("decorative"))
                            continue;

                        try { lua.DoFile(file); } catch { }
                    }
                }

                // 6. Извлекаем рецепты
                LuaTable rawObj = (lua["data"] as LuaTable)?["raw"] as LuaTable;

                if (rawObj != null)
                {
                    // 6.1. Обычные рецепты
                    if (rawObj["recipe"] is LuaTable recipeTable)
                    {
                        Console.WriteLine("Парсинг рецептов...");
                        foreach (var key in recipeTable.Keys)
                        {
                            if (recipeTable[key] is LuaTable recipeData)
                            {
                                var parsed = ParseRecipe(recipeData, lua, factorioDataPath, iconsFolder, ref iconsCopied);
                                if (parsed != null) recipes.Add(parsed);
                            }
                        }
                    }

                    // 6.2. Ресурсы (добыча)
                    if (rawObj["resource"] is LuaTable resourceTable)
                    {
                        Console.WriteLine("Парсинг ресурсов...");
                        foreach (var key in resourceTable.Keys)
                        {
                            if (resourceTable[key] is LuaTable resData)
                            {
                                var parsed = ParseResourceAsRecipe(resData, lua, factorioDataPath, iconsFolder, ref iconsCopied);
                                if (parsed != null && !recipes.Any(r => r.Name == parsed.Name))
                                    recipes.Add(parsed);
                            }
                        }
                    }

                    // 6.3. Псевдо-рецепты для жидкостей (вода, нефть и т.д.)
                    Console.WriteLine("Добавление pumping-рецептов для жидкостей...");
                    AddPumpingRecipes(recipes, lua, factorioDataPath, iconsFolder, ref iconsCopied);
                }
                else
                {
                    Console.WriteLine("⚠️ Не удалось получить таблицу data.raw из Lua.");
                }
            }

            Console.WriteLine($"✅ Скопировано иконок: {iconsCopied}");
            return recipes;
        }

        /// <summary>
        /// Создаёт псевдо-рецепты для жидкостей, которые добываются насосами
        /// (вода через offshore-pump, нефть через pumpjack и т.д.)
        /// </summary>
        private static void AddPumpingRecipes(List<FactorioRecipe> recipes, Lua lua,
            string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            // Список жидкостей, для которых нужно создать pumping-рецепты
            string[] pumpingFluids = { "water", "crude-oil", "steam" };

            LuaTable rawObj = (lua["data"] as LuaTable)?["raw"] as LuaTable;
            if (rawObj == null) return;

            foreach (var fluidName in pumpingFluids)
            {
                string recipeName = $"pumping-{fluidName}";

                // Пропускаем, если уже есть такой рецепт
                if (recipes.Any(r => r.Name == recipeName || r.Name == fluidName))
                    continue;

                // Проверяем, существует ли жидкость в data.raw.fluid
                LuaTable? fluidTable = rawObj["fluid"] as LuaTable;
                if (fluidTable == null || fluidTable[fluidName] == null)
                    continue;

                LuaTable fluidData = fluidTable[fluidName] as LuaTable;
                if (fluidData == null) continue;

                double defaultAmount = fluidName == "water" ? 1200 : 10;

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
                            Type = "fluid",
                            Amount = defaultAmount
                        }
                    }
                };

                // Ищем иконку жидкости
                string? iconPath = ExtractIconPath(fluidData);
                if (string.IsNullOrEmpty(iconPath))
                {
                    var getIconFunc = lua["get_best_icon"] as LuaFunction;
                    if (getIconFunc != null)
                    {
                        var res = getIconFunc.Call(fluidName);
                        iconPath = res?.FirstOrDefault()?.ToString();
                    }
                }

                CopyAndAssignIcon(recipe, iconPath, factorioDataPath, iconsFolder, ref iconsCopied);
                recipes.Add(recipe);
                Console.WriteLine($"   + Добавлен pumping-рецепт: {recipeName} (иконка: {(string.IsNullOrEmpty(iconPath) ? "НЕТ" : "OK")})");
            }
        }

        private static FactorioRecipe? ParseRecipe(LuaTable raw, Lua lua,
            string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            string name = raw["name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return null;

            var recipe = new FactorioRecipe
            {
                Name = name,
                Category = raw["category"]?.ToString() ?? "crafting",
                EnergyRequired = 0.5
            };

            // Обработка normal/expensive
            LuaTable targetTable = raw;
            if (raw["normal"] is LuaTable normalTable) targetTable = normalTable;
            else if (raw["expensive"] is LuaTable expensiveTable) targetTable = expensiveTable;

            if (targetTable["energy_required"] != null)
                recipe.EnergyRequired = Convert.ToDouble(targetTable["energy_required"]);

            // Парсинг ингредиентов
            if (targetTable["ingredients"] is LuaTable ingTable)
                recipe.Ingredients = ParseItemAmounts(ingTable);

            // Парсинг продуктов
            if (targetTable["results"] is LuaTable resTable)
            {
                recipe.Products = ParseItemAmounts(resTable);
            }
            else if (targetTable["result"] != null)
            {
                string resName = targetTable["result"].ToString()!;
                double count = 1;
                if (targetTable["result_count"] != null)
                    count = Convert.ToDouble(targetTable["result_count"]);

                recipe.Products.Add(new FactorioItemAmount
                {
                    Name = resName,
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

            // Поиск иконки
            string? iconPath = ExtractIconPath(raw) ?? ExtractIconPath(targetTable);
            if (string.IsNullOrEmpty(iconPath))
            {
                var getIconFunc = lua["get_best_icon"] as LuaFunction;
                if (getIconFunc != null)
                {
                    var res = getIconFunc.Call(name);
                    iconPath = res?.FirstOrDefault()?.ToString();

                    if (string.IsNullOrEmpty(iconPath) && recipe.Products.Count > 0)
                    {
                        res = getIconFunc.Call(recipe.Products[0].Name);
                        iconPath = res?.FirstOrDefault()?.ToString();
                    }
                }
            }

            CopyAndAssignIcon(recipe, iconPath, factorioDataPath, iconsFolder, ref iconsCopied);
            return recipe;
        }

        private static FactorioRecipe? ParseResourceAsRecipe(LuaTable raw, Lua lua,
            string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            string name = raw["name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return null;

            double miningTime = 1.0;
            var products = new List<FactorioItemAmount>();

            if (raw["minable"] is LuaTable minable)
            {
                if (minable["mining_time"] != null)
                    miningTime = Convert.ToDouble(minable["mining_time"]);

                if (minable["results"] is LuaTable resTable)
                {
                    products = ParseItemAmounts(resTable);
                }
                else if (minable["result"] != null)
                {
                    products.Add(new FactorioItemAmount
                    {
                        Name = minable["result"].ToString()!,
                        Type = "item",
                        Amount = minable["count"] != null ? Convert.ToDouble(minable["count"]) : 1
                    });
                }
            }

            if (products.Count == 0)
            {
                products.Add(new FactorioItemAmount
                {
                    Name = name,
                    Type = name == "crude-oil" ? "fluid" : "item",
                    Amount = 1
                });
            }

            var recipe = new FactorioRecipe
            {
                Name = $"mining-{name}",
                Category = "mining",
                EnergyRequired = miningTime,
                Ingredients = new List<FactorioItemAmount>(),
                Products = products
            };

            string? iconPath = ExtractIconPath(raw);
            if (string.IsNullOrEmpty(iconPath))
            {
                var getIconFunc = lua["get_best_icon"] as LuaFunction;
                if (getIconFunc != null)
                {
                    var res = getIconFunc.Call(name);
                    iconPath = res?.FirstOrDefault()?.ToString();
                }
            }

            CopyAndAssignIcon(recipe, iconPath, factorioDataPath, iconsFolder, ref iconsCopied);
            return recipe;
        }

        /// <summary>
        /// Универсальный парсер для ингредиентов и продуктов.
        /// Поддерживает все форматы Factorio:
        ///   - { name = "item", amount = 5 }
        ///   - { "item", 5 }
        ///   - { name = "item", amount_min = 1, amount_max = 3, probability = 0.5 }
        /// </summary>
        private static List<FactorioItemAmount> ParseItemAmounts(LuaTable table)
        {
            var list = new List<FactorioItemAmount>();

            foreach (var val in table.Values)
            {
                if (val is not LuaTable t) continue;

                var item = new FactorioItemAmount();

                // ФОРМАТ 1: { name = "...", amount = ..., type = "..." }
                if (t["name"] != null)
                {
                    item.Name = t["name"].ToString()!;
                    item.Type = t["type"]?.ToString() ?? "item";

                    if (t["amount"] != null)
                    {
                        item.Amount = Convert.ToDouble(t["amount"]);
                    }
                    else if (t["amount_min"] != null && t["amount_max"] != null)
                    {
                        double min = Convert.ToDouble(t["amount_min"]);
                        double max = Convert.ToDouble(t["amount_max"]);
                        item.Amount = Math.Round((min + max) / 2.0, 2);
                    }
                    else if (t["amount_min"] != null)
                    {
                        item.Amount = Convert.ToDouble(t["amount_min"]);
                    }
                    else if (t["amount_max"] != null)
                    {
                        item.Amount = Convert.ToDouble(t["amount_max"]);
                    }
                    else
                    {
                        item.Amount = 1;
                    }

                    if (t["probability"] != null)
                        item.Probability = Convert.ToDouble(t["probability"]);

                    list.Add(item);
                }
                // ФОРМАТ 2: { "name", amount } или { "name", amount, probability }
                else if (t[1] != null)
                {
                    item.Name = t[1].ToString()!;
                    item.Type = "item";

                    if (t[2] != null)
                        item.Amount = Convert.ToDouble(t[2]);
                    else
                        item.Amount = 1;

                    // Третий элемент может быть вероятностью
                    if (t[3] != null)
                    {
                        try
                        {
                            double prob = Convert.ToDouble(t[3]);
                            if (prob > 0 && prob <= 1.0)
                                item.Probability = prob;
                        }
                        catch { }
                    }

                    list.Add(item);
                }
            }

            return list;
        }

        private static string? ExtractIconPath(LuaTable raw)
        {
            if (raw["icon"] != null)
                return raw["icon"].ToString();

            if (raw["icons"] is LuaTable iconsTable)
            {
                foreach (var val in iconsTable.Values)
                {
                    if (val is LuaTable iconEntry && iconEntry["icon"] != null)
                        return iconEntry["icon"].ToString();
                }
            }

            return null;
        }

        private static void CopyAndAssignIcon(FactorioRecipe recipe, string? rawIconPath,
            string factorioDataPath, string iconsFolder, ref int iconsCopied)
        {
            if (string.IsNullOrEmpty(rawIconPath)) return;

            string fullSourcePath = ResolveFactorioPath(rawIconPath, factorioDataPath);

            if (File.Exists(fullSourcePath))
            {
                string targetFileName = $"{recipe.Name}{Path.GetExtension(fullSourcePath)}";
                string targetFilePath = Path.Combine(iconsFolder, targetFileName);

                try
                {
                    File.Copy(fullSourcePath, targetFilePath, true);
                    recipe.Icon = Path.Combine("icons", targetFileName).Replace('\\', '/');
                    iconsCopied++;
                }
                catch (IOException) { }
            }
        }

        private static string ResolveFactorioPath(string factorioPath, string factorioDataPath)
        {
            if (factorioPath.StartsWith("__"))
            {
                int closeIndex = factorioPath.IndexOf("__", 2);
                if (closeIndex > 2)
                {
                    string modName = factorioPath.Substring(2, closeIndex - 2);
                    string relativePath = factorioPath.Substring(closeIndex + 2).TrimStart('/', '\\');
                    return Path.GetFullPath(Path.Combine(factorioDataPath, modName, relativePath));
                }
            }

            return Path.GetFullPath(Path.Combine(factorioDataPath, factorioPath.TrimStart('/', '\\')));
        }
    }
}