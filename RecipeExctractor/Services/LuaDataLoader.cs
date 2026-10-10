using MoonSharp.Interpreter;
using RecipeExtractor.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecipeExtractor.Services
{
    public class LuaDataLoader
    {
        private readonly string _factorioPath;
        private readonly string _modsPath;
        private readonly Script _luaScript;

        public LuaDataLoader(string factorioPath, string modsPath)
        {
            _factorioPath = factorioPath;
            _modsPath = modsPath;
            _luaScript = new Script(CoreModules.Preset_HardSandbox);

            InitializeLuaEnvironment();
        }

        private void InitializeLuaEnvironment()
        {
            var dataTable = new Table(_luaScript);
            var dataRawTable = new Table(_luaScript);
            dataTable.Set("raw", DynValue.NewTable(dataRawTable));

            // Используем метод Set для гарантированной типизации
            _luaScript.Globals.Set("data", DynValue.NewTable(dataTable));

            _luaScript.DoString(@"
                function require(module)
                    return {}
                end

                function data.extend(items)
                    if type(items) ~= 'table' then return end
                    for _, item in ipairs(items) do
                        local itemType = item.type
                        if itemType then
                            if not data.raw[itemType] then
                                data.raw[itemType] = {}
                            end
                            if item.name then
                                data.raw[itemType][item.name] = item
                            end
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
                await LoadLuaFilesFromDirectoryAsync(corePath);

            if (Directory.Exists(basePath))
                await LoadLuaFilesFromDirectoryAsync(basePath);
        }

        public async Task LoadModsAsync()
        {
            if (!Directory.Exists(_modsPath))
                return;

            var modListPath = Path.Combine(_modsPath, "mod-list.json");
            var enabledMods = new List<string>();

            if (File.Exists(modListPath))
            {
                var modListJson = await File.ReadAllTextAsync(modListPath);
                var modList = JsonSerializer.Deserialize<ModList>(modListJson);
                enabledMods = modList?.Mods?
                    .Where(m => m.Enabled && m.Name != "base")
                    .Select(m => m.Name)
                    .ToList() ?? new List<string>();
            }

            foreach (var modDir in Directory.GetDirectories(_modsPath))
            {
                var modName = Path.GetFileName(modDir);
                if (enabledMods.Contains(modName) || enabledMods.Count == 0)
                {
                    await LoadLuaFilesFromDirectoryAsync(modDir);
                }
            }
        }

        private async Task LoadLuaFilesFromDirectoryAsync(string directory)
        {
            var luaFiles = Directory.GetFiles(directory, "*.lua", SearchOption.AllDirectories)
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
                    Console.WriteLine($"[Предупреждение] Ошибка загрузки {file}: {ex.Message}");
                }
            }
        }

        public List<Item> GetItems()
        {
            var items = new List<Item>();

            // Используем Get() для гарантированного получения DynValue
            var dataDyn = _luaScript.Globals.Get("data");
            if (dataDyn.Type != DataType.Table) return items;
            var dataTable = dataDyn.Table;

            var dataRawDyn = dataTable.Get("raw");
            if (dataRawDyn.Type != DataType.Table) return items;
            var dataRaw = dataRawDyn.Table;

            var itemTypes = new[] { "item", "item-with-entity-data", "ammo", "armor", "capsule", "gun", "module", "tool", "fluid", "entity" };

            foreach (var type in itemTypes)
            {
                var typeTableDyn = dataRaw.Get(type);
                if (typeTableDyn.Type == DataType.Table)
                {
                    var typeTable = typeTableDyn.Table;
                    foreach (var kvp in typeTable.Pairs)
                    {
                        var item = ConvertToItem(kvp.Value, type);
                        if (item != null)
                        {
                            items.Add(item);
                        }
                    }
                }
            }

            return items;
        }

        public List<Recipe> GetRecipes()
        {
            var recipes = new List<Recipe>();

            var dataDyn = _luaScript.Globals.Get("data");
            if (dataDyn.Type != DataType.Table) return recipes;
            var dataTable = dataDyn.Table;

            var dataRawDyn = dataTable.Get("raw");
            if (dataRawDyn.Type != DataType.Table) return recipes;
            var dataRaw = dataRawDyn.Table;

            var recipeTableDyn = dataRaw.Get("recipe");
            if (recipeTableDyn.Type == DataType.Table)
            {
                var recipeTable = recipeTableDyn.Table;
                foreach (var kvp in recipeTable.Pairs)
                {
                    var recipe = ConvertToRecipe(kvp.Value);
                    if (recipe != null)
                    {
                        recipes.Add(recipe);
                    }
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
                if (nameVal.Type == DataType.String) item.Name = nameVal.String ?? string.Empty;

                var iconVal = table.Get("icon");
                if (iconVal.Type == DataType.String) item.IconPath = iconVal.String ?? string.Empty;

                var iconSizeVal = table.Get("icon_size");
                if (iconSizeVal.Type == DataType.Number) item.IconSize = iconSizeVal.Number.ToString();

                var stackSizeVal = table.Get("stack_size");
                if (stackSizeVal.Type == DataType.Number) item.StackSize = Convert.ToInt32(stackSizeVal.Number);

                var subgroupVal = table.Get("subgroup");
                if (subgroupVal.Type == DataType.String) item.Subgroup = subgroupVal.String ?? string.Empty;

                var orderVal = table.Get("order");
                if (orderVal.Type == DataType.String) item.Order = orderVal.String ?? string.Empty;

                item.Properties = new Dictionary<string, object>();
                foreach (var kvp in table.Pairs)
                {
                    if (kvp.Key.Type == DataType.String)
                    {
                        item.Properties[kvp.Key.String!] = ConvertDynValueToObject(kvp.Value);
                    }
                }

                return string.IsNullOrEmpty(item.Name) ? null : item;
            }
            catch
            {
                return null;
            }
        }

        private Recipe? ConvertToRecipe(DynValue luaRecipe)
        {
            try
            {
                if (luaRecipe.Type != DataType.Table) return null;
                var table = luaRecipe.Table;

                var recipe = new Recipe();

                var nameVal = table.Get("name");
                if (nameVal.Type == DataType.String) recipe.Name = nameVal.String ?? string.Empty;

                var categoryVal = table.Get("category");
                recipe.Category = categoryVal.Type == DataType.String ? (categoryVal.String ?? "crafting") : "crafting";

                var energyVal = table.Get("energy_required");
                recipe.EnergyRequired = energyVal.Type == DataType.Number ? Convert.ToDouble(energyVal.Number) : 0.5;

                var iconVal = table.Get("icon");
                if (iconVal.Type == DataType.String) recipe.IconPath = iconVal.String ?? string.Empty;

                var iconSizeVal = table.Get("icon_size");
                if (iconSizeVal.Type == DataType.Number) recipe.IconSize = iconSizeVal.Number.ToString();

                var enabledVal = table.Get("enabled");
                recipe.Enabled = enabledVal.Type == DataType.Boolean ? enabledVal.Boolean : true;

                recipe.Ingredients = ConvertIngredients(table.Get("ingredients"));
                recipe.Results = ConvertResults(table.Get("results"));

                recipe.Properties = new Dictionary<string, object>();
                foreach (var kvp in table.Pairs)
                {
                    if (kvp.Key.Type == DataType.String)
                    {
                        recipe.Properties[kvp.Key.String!] = ConvertDynValueToObject(kvp.Value);
                    }
                }

                return string.IsNullOrEmpty(recipe.Name) ? null : recipe;
            }
            catch
            {
                return null;
            }
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
                            ingredient.Name = nameVal.String ?? string.Empty;

                            var amountVal = itemTable.Get("amount");
                            ingredient.Amount = amountVal.Type != DataType.Nil ? Convert.ToInt32(amountVal.Number) : 1;

                            var typeVal = itemTable.Get("type");
                            ingredient.Type = typeVal.Type != DataType.Nil ? (typeVal.String ?? "item") : "item";
                        }
                        else
                        {
                            var firstVal = itemTable.Get(DynValue.NewNumber(1));
                            var secondVal = itemTable.Get(DynValue.NewNumber(2));
                            if (firstVal.Type != DataType.Nil)
                            {
                                ingredient.Name = firstVal.String ?? firstVal.ToString() ?? string.Empty;
                            }
                            if (secondVal.Type != DataType.Nil)
                            {
                                ingredient.Amount = Convert.ToInt32(secondVal.Number);
                            }
                        }

                        if (!string.IsNullOrEmpty(ingredient.Name))
                        {
                            ingredients.Add(ingredient);
                        }
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
                            result.Name = nameVal.String ?? string.Empty;

                            var amountVal = itemTable.Get("amount");
                            result.Amount = amountVal.Type != DataType.Nil ? Convert.ToInt32(amountVal.Number) : 1;

                            var probVal = itemTable.Get("probability");
                            result.Probability = probVal.Type != DataType.Nil ? Convert.ToDouble(probVal.Number) : 1.0;

                            var typeVal = itemTable.Get("type");
                            result.Type = typeVal.Type != DataType.Nil ? (typeVal.String ?? "item") : "item";
                        }
                        else
                        {
                            var firstVal = itemTable.Get(DynValue.NewNumber(1));
                            var secondVal = itemTable.Get(DynValue.NewNumber(2));
                            if (firstVal.Type != DataType.Nil)
                            {
                                result.Name = firstVal.String ?? firstVal.ToString() ?? string.Empty;
                            }
                            if (secondVal.Type != DataType.Nil)
                            {
                                result.Amount = Convert.ToInt32(secondVal.Number);
                            }
                            result.Probability = 1.0;
                            result.Type = "item";
                        }

                        if (!string.IsNullOrEmpty(result.Name))
                        {
                            results.Add(result);
                        }
                    }
                }
            }
            return results;
        }

        private object? ConvertDynValueToObject(DynValue val)
        {
            if (val == null || val.Type == DataType.Nil || val.Type == DataType.Void)
                return null;
            if (val.Type == DataType.String)
                return val.String;
            if (val.Type == DataType.Number)
                return val.Number;
            if (val.Type == DataType.Boolean)
                return val.Boolean;
            if (val.Type == DataType.Table)
                return val.Table;
            return val.ToObject();
        }

        private class ModList
        {
            [JsonPropertyName("mods")]
            public List<ModEntry>? Mods { get; set; }
        }

        private class ModEntry
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; } = true;
        }
    }
}