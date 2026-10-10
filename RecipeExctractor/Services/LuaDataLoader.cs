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
            // Создаем таблицы Lua для эмуляции окружения Factorio
            var dataTable = new Table(_luaScript);
            var dataRawTable = new Table(_luaScript);
            dataTable["raw"] = dataRawTable;
            _luaScript.Globals["data"] = dataTable;

            // Эмулируем функцию require (возвращает пустую таблицу, чтобы избежать ошибок загрузки зависимостей)
            // и функцию data.extend для корректного сбора данных
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
                    // Игнорируем ошибки в отдельных файлах модов, чтобы продолжить загрузку остальных
                    Console.WriteLine($"[Предупреждение] Ошибка загрузки {file}: {ex.Message}");
                }
            }
        }

        public List<Item> GetItems()
        {
            var items = new List<Item>();
            var dataTable = _luaScript.Globals["data"] as Table;
            if (dataTable == null) return items;

            var dataRaw = dataTable["raw"] as Table;
            if (dataRaw == null) return items;

            var itemTypes = new[] { "item", "item-with-entity-data", "ammo", "armor", "capsule", "gun", "module", "tool", "fluid", "entity" };

            foreach (var type in itemTypes)
            {
                var typeTable = dataRaw[type] as Table;
                if (typeTable != null)
                {
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
            var dataTable = _luaScript.Globals["data"] as Table;
            if (dataTable == null) return recipes;

            var dataRaw = dataTable["raw"] as Table;
            if (dataRaw == null) return recipes;

            var recipeTable = dataRaw["recipe"] as Table;
            if (recipeTable != null)
            {
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
                var dict = ConvertLuaTableToDictionary(luaItem);
                if (dict == null) return null;

                var item = new Item { Type = type, Properties = dict };

                if (dict.TryGetValue("name", out var name)) item.Name = name.ToString() ?? string.Empty;
                if (dict.TryGetValue("icon", out var icon)) item.IconPath = icon.ToString() ?? string.Empty;
                if (dict.TryGetValue("icon_size", out var iconSize)) item.IconSize = iconSize.ToString() ?? string.Empty;
                if (dict.TryGetValue("stack_size", out var stackSize)) item.StackSize = Convert.ToInt32(stackSize);
                if (dict.TryGetValue("subgroup", out var subgroup)) item.Subgroup = subgroup.ToString() ?? string.Empty;
                if (dict.TryGetValue("order", out var order)) item.Order = order.ToString() ?? string.Empty;

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
                var dict = ConvertLuaTableToDictionary(luaRecipe);
                if (dict == null) return null;

                var recipe = new Recipe { Properties = dict };

                if (dict.TryGetValue("name", out var name)) recipe.Name = name.ToString() ?? string.Empty;
                if (dict.TryGetValue("category", out var category)) recipe.Category = category.ToString() ?? "crafting";
                if (dict.TryGetValue("energy_required", out var energy)) recipe.EnergyRequired = Convert.ToDouble(energy);
                if (dict.TryGetValue("icon", out var icon)) recipe.IconPath = icon.ToString() ?? string.Empty;
                if (dict.TryGetValue("icon_size", out var iconSize)) recipe.IconSize = iconSize.ToString() ?? string.Empty;
                if (dict.TryGetValue("enabled", out var enabled)) recipe.Enabled = Convert.ToBoolean(enabled);

                if (luaRecipe.Type == DataType.Table)
                {
                    var table = luaRecipe.Table;
                    recipe.Ingredients = ConvertIngredients(table["ingredients"]);
                    recipe.Results = ConvertResults(table["results"]);
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
            if (luaIngredients.Type == DataType.Table)
            {
                var table = luaIngredients.Table;
                foreach (var kvp in table.Pairs)
                {
                    var itemVal = kvp.Value;
                    if (itemVal.Type == DataType.Table)
                    {
                        var itemTable = itemVal.Table;
                        var ingredient = new Ingredient();
                        
                        // Проверяем формат словаря: {name="...", amount=...}
                        var nameVal = itemTable["name"];
                        if (nameVal != null && nameVal.Type != DataType.Nil)
                        {
                            ingredient.Name = nameVal.String ?? string.Empty;
                            var amountVal = itemTable["amount"];
                            ingredient.Amount = amountVal != null && amountVal.Type != DataType.Nil ? Convert.ToInt32(amountVal.Number) : 1;
                            var typeVal = itemTable["type"];
                            ingredient