using MoonSharp.Interpreter;
using RecipeExtractor.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
//using RecipeExtractor.Services;

namespace RecipeExtractor.Services
{
    public class LuaDataLoader
    {
        private readonly string _factorioPath;
        private readonly string _modsPath;
        private readonly Script _luaScript;
        private readonly FactorioPathResolver _pathResolver;
        private readonly Dictionary<string, string> _itemIconPaths = new(); // name → game icon path

        // Папки, которые нужно пропускать при загрузке Lua (они используют runtime API Factorio)
        private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "campaigns", "scenarios", "tutorials"
        };

        public LuaDataLoader(string factorioPath, string modsPath)
        {
            _factorioPath = factorioPath;
            _modsPath = modsPath;
            _luaScript = new Script(CoreModules.Preset_HardSandbox);
            _pathResolver = new FactorioPathResolver(factorioPath, modsPath);
            InitializeLuaEnvironment();
        }

        private void InitializeLuaEnvironment()
        {
            var dataTable = new Table(_luaScript);
            var dataRawTable = new Table(_luaScript);
            dataTable.Set("raw", DynValue.NewTable(dataRawTable));
            _luaScript.Globals.Set("data", DynValue.NewTable(dataTable));

            _luaScript.DoString(@"
                function require(module) return {} end
                function data.extend(items)
                    if type(items) ~= 'table' then return end
                    for _, item in ipairs(items) do
                        local itemType = item.type
                        if itemType then
                            if not data.raw[itemType] then data.raw[itemType] = {} end
                            if item.name then data.raw[itemType][item.name] = item end
                        end
                    end
                end
            ");
        }

        public async Task LoadBaseDataAsync()
        {
            var corePath = Path.Combine(_factorioPath, "data", "core");
            var basePath = Path.Combine(_factorioPath, "data", "base");
            if (Directory.Exists(corePath))
                await LoadLuaFilesFromDirectoryAsync(corePath, isBaseData: true);
            if (Directory.Exists(basePath))
                await LoadLuaFilesFromDirectoryAsync(basePath, isBaseData: true);
        }

        public async Task LoadModsAsync()
        {
            if (!Directory.Exists(_modsPath)) return;

            var modListPath = Path.Combine(_modsPath, "mod-list.json");
            var enabledMods = new List<string>();

            if (File.Exists(modListPath))
            {
                var modListJson = await File.ReadAllTextAsync(modListPath);
                var modList = JsonSerializer.Deserialize<ModList>(modListJson);
                enabledMods = modList?.Mods?
                    .Where(m => m.Enabled && m.Name != "base")
                    .Select(m => m.Name).ToList() ?? new List<string>();
            }

            foreach (var modDir in Directory.GetDirectories(_modsPath))
            {
                var modName = Path.GetFileName(modDir);
                if (enabledMods.Contains(modName) || enabledMods.Count == 0)
                    await LoadLuaFilesFromDirectoryAsync(modDir, isBaseData: false);
            }
        }

        private async Task LoadLuaFilesFromDirectoryAsync(string directory, bool isBaseData)
        {
            var luaFiles = Directory.GetFiles(directory, "*.lua", SearchOption.AllDirectories)
                .Where(f => !ShouldSkipFile(f, directory))
                .OrderBy(f => f)
                .ToList();

            foreach (var file in luaFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    _luaScript.DoString(content);
                }
                catch (Exception ex)
                {
                    // Тихо пропускаем ошибки — эти файлы используют runtime API Factorio
                    Console.WriteLine($"[Пропущено] {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Определяет, нужно ли пропустить файл при загрузке.
        /// Пропускаем файлы из campaigns/, scenarios/, tutorials/, lualib/ и т.д.
        /// </summary>
        private bool ShouldSkipFile(string filePath, string rootDirectory)
        {
            var relativePath = Path.GetRelativePath(rootDirectory, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var part in parts)
            {
                if (SkipFolders.Contains(part))
                    return true;
            }

            return false;
        }

        public List<Item> GetItems()
        {
            var items = new List<Item>();
            var dataTable = _luaScript.Globals.Get("data");
            if (dataTable.Type != DataType.Table) return items;
            var dataRaw = dataTable.Table.Get("raw").Table;
            if (dataRaw == null) return items;

            var itemTypes = new[] { "item", "item-with-entity-data", "ammo", "armor",
                "capsule", "gun", "module", "tool", "fluid", "entity" };

            foreach (var type in itemTypes)
            {
                var typeTable = dataRaw.Get(type).Table;
                if (typeTable != null)
                {
                    foreach (var kvp in typeTable.Pairs)
                    {
                        var item = ConvertToItem(kvp.Value, type);
                        if (item != null)
                        {
                            items.Add(item);
                            if (!string.IsNullOrEmpty(item.IconPath) && !string.IsNullOrEmpty(item.Name))
                                _itemIconPaths[item.Name] = item.IconPath;
                        }
                    }
                }
            }
            return items;
        }

        public List<Recipe> GetRecipes()
        {
            var recipes = new List<Recipe>();
            var dataTable = _luaScript.Globals.Get("data");
            if (dataTable.Type != DataType.Table) return recipes;
            var dataRaw = dataTable.Table.Get("raw").Table;
            if (dataRaw == null) return recipes;

            var recipeTable = dataRaw.Get("recipe").Table;
            if (recipeTable != null)
            {
                foreach (var kvp in recipeTable.Pairs)
                {
                    var recipe = ConvertToRecipe(kvp.Value);
                    if (recipe != null) recipes.Add(recipe);
                }
            }
            return recipes;
        }

        private Item? ConvertToItem(DynValue luaItem, string type)
        {
            try
            {
                if (luaItem.Type != DataType.Table) return null;
                var table = luaItem.Table;
                var item = new Item { Type = type };

                var nameVal = table.Get("name");
                if (nameVal.Type == DataType.String) item.Name = nameVal.String?.Trim() ?? string.Empty;

                // Парсим иконку — может быть icon (строка) или icons (таблица)
                var iconVal = table.Get("icon");
                if (iconVal.Type == DataType.String)
                {
                    item.IconPath = iconVal.String?.Trim() ?? string.Empty;
                    item.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(item.IconPath);
                }
                else
                {
                    // Пробуем icons[1].icon
                    var iconsVal = table.Get("icons");
                    if (iconsVal.Type == DataType.Table)
                    {
                        var firstIcon = iconsVal.Table.Get(DynValue.NewNumber(1));
                        if (firstIcon.Type == DataType.Table)
                        {
                            var iconPathVal = firstIcon.Table.Get("icon");
                            if (iconPathVal.Type == DataType.String)
                            {
                                item.IconPath = iconPathVal.String?.Trim() ?? string.Empty;
                                item.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(item.IconPath);
                            }
                        }
                    }
                }

                var iconSizeVal = table.Get("icon_size");
                if (iconSizeVal.Type == DataType.Number) item.IconSize = iconSizeVal.Number.ToString();

                var stackSizeVal = table.Get("stack_size");
                if (stackSizeVal.Type == DataType.Number) item.StackSize = Convert.ToInt32(stackSizeVal.Number);

                var subgroupVal = table.Get("subgroup");
                if (subgroupVal.Type == DataType.String) item.Subgroup = subgroupVal.String?.Trim() ?? string.Empty;

                var orderVal = table.Get("order");
                if (orderVal.Type == DataType.String) item.Order = orderVal.String?.Trim() ?? string.Empty;

                item.Properties = new Dictionary<string, object>();
                foreach (var kvp in table.Pairs)
                {
                    if (kvp.Key.Type == DataType.String)
                    {
                        var key = kvp.Key.String?.Trim() ?? string.Empty;
                        if (!string.IsNullOrEmpty(key))
                            item.Properties[key] = ConvertDynValueToObject(kvp.Value);
                    }
                }

                return string.IsNullOrEmpty(item.Name) ? null : item;
            }
            catch { return null; }
        }

        private Recipe? ConvertToRecipe(DynValue luaRecipe)
        {
            try
            {
                if (luaRecipe.Type != DataType.Table) return null;
                var table = luaRecipe.Table;
                var recipe = new Recipe();

                var nameVal = table.Get("name");
                if (nameVal.Type == DataType.String) recipe.Name = nameVal.String?.Trim() ?? string.Empty;

                var categoryVal = table.Get("category");
                recipe.Category = categoryVal.Type == DataType.String
                    ? (categoryVal.String?.Trim() ?? "crafting") : "crafting";

                var energyVal = table.Get("energy_required");
                recipe.EnergyRequired = energyVal.Type == DataType.Number
                    ? Convert.ToDouble(energyVal.Number) : 0.5;

                // Иконка рецепта
                var iconVal = table.Get("icon");
                if (iconVal.Type == DataType.String)
                {
                    recipe.RecipeIconPath = iconVal.String?.Trim();
                    recipe.RecipeIconPath = recipe.RecipeIconPath ?? string.Empty;
                }

                var iconSizeVal = table.Get("icon_size");
                if (iconSizeVal.Type == DataType.Number) recipe.IconSize = iconSizeVal.Number.ToString();

                var enabledVal = table.Get("enabled");
                recipe.Enabled = enabledVal.Type == DataType.Boolean ? enabledVal.Boolean : true;

                // Формат difficulty (normal/expensive)
                var normalVal = table.Get("normal");
                var expensiveVal = table.Get("expensive");

                if (normalVal.Type == DataType.Table || expensiveVal.Type == DataType.Table)
                {
                    recipe.RecipeFormat = "difficulty";
                    var primaryTable = normalVal.Type == DataType.Table ? normalVal.Table : expensiveVal.Table;
                    if (primaryTable != null)
                    {
                        recipe.Ingredients = ConvertIngredients(primaryTable.Get("ingredients"));
                        ParseResultsFromTable(primaryTable, recipe);
                    }
                }
                else
                {
                    recipe.Ingredients = ConvertIngredients(table.Get("ingredients"));
                    ParseResultsFromTable(table, recipe);
                }

                // Главный продукт
                var mainProductVal = table.Get("main_product");
                if (mainProductVal.Type == DataType.String && !string.IsNullOrWhiteSpace(mainProductVal.String))
                {
                    recipe.MainProduct = mainProductVal.String?.Trim();
                }
                else if (recipe.Results.Count == 1)
                {
                    recipe.MainProduct = recipe.Results[0].Name;
                }
                else if (recipe.Results.Count > 1)
                {
                    recipe.MainProduct = recipe.Results
                        .OrderByDescending(r => r.Probability)
                        .ThenBy(r => r.Amount).First().Name;
                }

                // Определяем иконку
                DetermineRecipeIcon(recipe);

                recipe.Properties = new Dictionary<string, object>();
                foreach (var kvp in table.Pairs)
                {
                    if (kvp.Key.Type == DataType.String)
                    {
                        var key = kvp.Key.String?.Trim() ?? string.Empty;
                        if (!string.IsNullOrEmpty(key))
                            recipe.Properties[key] = ConvertDynValueToObject(kvp.Value);
                    }
                }

                return string.IsNullOrEmpty(recipe.Name) ? null : recipe;
            }
            catch { return null; }
        }

        private void ParseResultsFromTable(Table table, Recipe recipe)
        {
            var resultsVal = table.Get("results");
            var resultVal = table.Get("result");
            var resultCountVal = table.Get("result_count");

            if (resultsVal.Type == DataType.Table && resultsVal.Table.Pairs.Count() > 0)
            {
                recipe.Results = ConvertResults(resultsVal);
                recipe.RecipeFormat = "multiple";
            }
            else if (resultVal.Type == DataType.String)
            {
                int resultCount = 1;
                if (resultCountVal.Type == DataType.Number)
                    resultCount = Convert.ToInt32(resultCountVal.Number);

                recipe.Results = new List<Result>
                {
                    new Result
                    {
                        Name = resultVal.String?.Trim() ?? string.Empty,
                        Amount = resultCount,
                        Probability = 1.0,
                        Type = "item"
                    }
                };
                recipe.RecipeFormat = "single";
            }
            else
            {
                recipe.RecipeFormat = "unknown";
            }
        }

        private void DetermineRecipeIcon(Recipe recipe)
        {
            // 1. Если у рецепта есть своя иконка
            if (!string.IsNullOrEmpty(recipe.RecipeIconPath))
            {
                recipe.ProductIconPath = recipe.RecipeIconPath;
                recipe.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(recipe.RecipeIconPath);
                recipe.IconFileName = $"{recipe.Name}.png";
                return;
            }

            // 2. Если есть main_product, берём его иконку
            if (!string.IsNullOrEmpty(recipe.MainProduct) && _itemIconPaths.TryGetValue(recipe.MainProduct, out var iconPath))
            {
                recipe.ProductIconPath = iconPath;
                recipe.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(iconPath);
                recipe.IconFileName = $"{recipe.MainProduct}.png";
                return;
            }

            // 3. Если один результат, берём его иконку
            if (recipe.Results.Count == 1 && _itemIconPaths.TryGetValue(recipe.Results[0].Name, out var resIcon))
            {
                recipe.ProductIconPath = resIcon;
                recipe.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(resIcon);
                recipe.IconFileName = $"{recipe.Results[0].Name}.png";
                return;
            }

            // 4. Fallback
            recipe.IconFileName = $"{recipe.Name}.png";
        }

        private List<Ingredient> ConvertIngredients(DynValue luaIngredients)
        {
            var ingredients = new List<Ingredient>();
            if (luaIngredients != null && luaIngredients.Type == DataType.Table)
            {
                var table = luaIngredients.Table;
                foreach (var kvp in table.Pairs)
                {
                    var itemVal = kvp.Value;
                    if (itemVal.Type == DataType.Table)
                    {
                        var itemTable = itemVal.Table;
                        var ingredient = new Ingredient();

                        var nameVal = itemTable.Get("name");
                        if (nameVal.Type != DataType.Nil && nameVal.Type != DataType.Void)
                        {
                            ingredient.Name = nameVal.String?.Trim() ?? string.Empty;
                            var amountVal = itemTable.Get("amount");
                            ingredient.Amount = amountVal.Type != DataType.Nil ? Convert.ToInt32(amountVal.Number) : 1;
                            var typeVal = itemTable.Get("type");
                            ingredient.Type = typeVal.Type != DataType.Nil ? (typeVal.String?.Trim() ?? "item") : "item";
                        }
                        else
                        {
                            var firstVal = itemTable.Get(DynValue.NewNumber(1));
                            var secondVal = itemTable.Get(DynValue.NewNumber(2));
                            if (firstVal.Type != DataType.Nil)
                                ingredient.Name = firstVal.String?.Trim() ?? firstVal.ToString()?.Trim() ?? string.Empty;
                            if (secondVal.Type != DataType.Nil)
                                ingredient.Amount = Convert.ToInt32(secondVal.Number);
                        }

                        if (!string.IsNullOrEmpty(ingredient.Name))
                            ingredients.Add(ingredient);
                    }
                }
            }
            return ingredients;
        }

        private List<Result> ConvertResults(DynValue luaResults)
        {
            var results = new List<Result>();
            if (luaResults != null && luaResults.Type == DataType.Table)
            {
                var table = luaResults.Table;
                foreach (var kvp in table.Pairs)
                {
                    var itemVal = kvp.Value;
                    if (itemVal.Type == DataType.Table)
                    {
                        var itemTable = itemVal.Table;
                        var result = new Result();

                        var nameVal = itemTable.Get("name");
                        if (nameVal.Type != DataType.Nil && nameVal.Type != DataType.Void)
                        {
                            result.Name = nameVal.String?.Trim() ?? string.Empty;
                            var amountVal = itemTable.Get("amount");
                            result.Amount = amountVal.Type != DataType.Nil ? Convert.ToInt32(amountVal.Number) : 1;
                            var probVal = itemTable.Get("probability");
                            result.Probability = probVal.Type != DataType.Nil ? Convert.ToDouble(probVal.Number) : 1.0;
                            var typeVal = itemTable.Get("type");
                            result.Type = typeVal.Type != DataType.Nil ? (typeVal.String?.Trim() ?? "item") : "item";
                        }
                        else
                        {
                            var firstVal = itemTable.Get(DynValue.NewNumber(1));
                            var secondVal = itemTable.Get(DynValue.NewNumber(2));
                            if (firstVal.Type != DataType.Nil)
                                result.Name = firstVal.String?.Trim() ?? firstVal.ToString()?.Trim() ?? string.Empty;
                            if (secondVal.Type != DataType.Nil)
                                result.Amount = Convert.ToInt32(secondVal.Number);
                            result.Probability = 1.0;
                            result.Type = "item";
                        }

                        if (!string.IsNullOrEmpty(result.Name))
                            results.Add(result);
                    }
                }
            }
            return results;
        }

        private object? ConvertDynValueToObject(DynValue val) => ConvertDynValueToObject(val, null);

        private object? ConvertDynValueToObject(DynValue val, HashSet<Table>? visitedTables)
        {
            if (val == null || val.Type == DataType.Nil || val.Type == DataType.Void) return null;
            if (val.Type == DataType.String) return val.String?.Trim();
            if (val.Type == DataType.Number) return val.Number;
            if (val.Type == DataType.Boolean) return val.Boolean;
            if (val.Type == DataType.Table)
            {
                var table = val.Table;
                visitedTables ??= new HashSet<Table>();
                if (visitedTables.Contains(table)) return "[circular reference]";
                visitedTables.Add(table);

                bool isArray = true;
                int maxIndex = 0, pairCount = 0;
                foreach (var kvp in table.Pairs)
                {
                    pairCount++;
                    if (kvp.Key.Type == DataType.Number)
                    {
                        int idx = (int)kvp.Key.Number;
                        if (idx < 1) { isArray = false; break; }
                        if (idx > maxIndex) maxIndex = idx;
                    }
                    else { isArray = false; break; }
                }
                if (isArray && maxIndex != pairCount) isArray = false;

                if (isArray && pairCount > 0)
                {
                    var list = new List<object?>();
                    for (int i = 1; i <= maxIndex; i++)
                        list.Add(ConvertDynValueToObject(table.Get(DynValue.NewNumber(i)), visitedTables));
                    visitedTables.Remove(table);
                    return list;
                }
                else
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var kvp in table.Pairs)
                    {
                        string key = kvp.Key.Type == DataType.String
                            ? (kvp.Key.String?.Trim() ?? $"[key_{kvp.Key.GetHashCode()}]")
                            : kvp.Key.Type == DataType.Number
                                ? kvp.Key.Number.ToString()
                                : kvp.Key.ToString()?.Trim() ?? $"[key_{kvp.Key.GetHashCode()}]";
                        dict[key] = ConvertDynValueToObject(kvp.Value, visitedTables);
                    }
                    visitedTables.Remove(table);
                    return dict;
                }
            }
            try { return val.CastToString()?.Trim() ?? val.Type.ToString(); }
            catch { return val.Type.ToString(); }
        }

        private class ModList { [JsonPropertyName("mods")] public List<ModEntry>? Mods { get; set; } }
        private class ModEntry
        {
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        }
    }
}