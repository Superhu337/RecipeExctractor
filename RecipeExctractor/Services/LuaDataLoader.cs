using MoonSharp.Interpreter;
using RecipeExtractor.Models;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace RecipeExtractor.Services
{
    public class LuaDataLoader
    {
        private readonly string _factorioPath;
        private readonly string _modsPath;
        private readonly Script _luaScript;
        private readonly FactorioPathResolver _pathResolver;
        private readonly Dictionary<string, string> _itemIconPaths = new();
        private readonly List<string> _requireRoots = new();
        private string _tempModsDir = "";

        private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "campaigns", "scenarios", "tutorials", "locale", "migrations"
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

            _requireRoots.Add(Path.Combine(_factorioPath, "data", "core", "lualib"));
            _requireRoots.Add(Path.Combine(_factorioPath, "data", "base", "lualib"));

            // Эмуляция util и других runtime-функций Factorio
            _luaScript.DoString(@"
                if util == nil then util = {} end
                
                if util.table == nil then util.table = {} end
                function util.table.deepcopy(t)
                    if type(t) ~= 'table' then return t end
                    local copy = {}
                    for k,v in pairs(t) do 
                        copy[util.table.deepcopy(k)] = util.table.deepcopy(v) 
                    end
                    return copy
                end
                
                function util.table.merge(a, b)
                    if not b then return a end
                    for k,v in pairs(b) do a[k] = v end
                    return a
                end
                
                function util.next_power_of_2(n)
                    local p = 1
                    while p < n do p = p * 2 end
                    return p
                end
                
                if util.color == nil then util.color = {} end
                function util.color.fromstring(s) return {r=1,g=1,b=1,a=1} end
                function util.color.tohex(c) return 'ffffffff' end
                
                function helpers() return {} end
                
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
            {
                Console.WriteLine("  Папка модов не найдена: " + _modsPath);
                return;
            }

            var enabled = await ReadEnabledModsAsync();
            Console.WriteLine($"  Включённых модов: {enabled.Count}");

            _tempModsDir = Path.Combine(Path.GetTempPath(), "RecipeExtractor", "mods");
            if (Directory.Exists(_tempModsDir))
            {
                try { Directory.Delete(_tempModsDir, true); } catch { }
            }
            Directory.CreateDirectory(_tempModsDir);

            int loaded = 0;
            foreach (var zipPath in Directory.GetFiles(_modsPath, "*.zip"))
            {
                var archName = Path.GetFileNameWithoutExtension(zipPath);
                var modName = GetModNameFromArchive(archName);

                if (enabled.Count > 0 && !enabled.Contains(modName)) continue;

                var dest = Path.Combine(_tempModsDir, modName);
                try
                {
                    ZipFile.ExtractToDirectory(zipPath, dest, overwriteFiles: true);
                    loaded++;
                    Console.WriteLine($"  ✓ Распакован: {modName}");

                    var libDir = Path.Combine(dest, "lualib");
                    if (Directory.Exists(libDir))
                        _requireRoots.Add(libDir);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Ошибка распаковки {modName}: {ex.Message}");
                }
            }

            foreach (var d in Directory.GetDirectories(_modsPath))
            {
                var dirName = Path.GetFileName(d);
                var modName = GetModNameFromArchive(dirName);
                if (enabled.Count > 0 && !enabled.Contains(modName)) continue;

                var dest = Path.Combine(_tempModsDir, modName);
                if (!Directory.Exists(dest))
                {
                    CopyDirectory(d, dest);
                    loaded++;
                    Console.WriteLine($"  ✓ Скопирована папка мода: {modName}");

                    var libDir = Path.Combine(dest, "lualib");
                    if (Directory.Exists(libDir))
                        _requireRoots.Add(libDir);
                }
            }

            Console.WriteLine($"  Итого загружено модов: {loaded}");

            // Сначала lualib (хелперы), потом prototypes/data
            await LoadInOrderAsync(_tempModsDir, phase: "lualib");
            await LoadInOrderAsync(_tempModsDir, phase: "data");
        }

        private async Task<HashSet<string>> ReadEnabledModsAsync()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var p = Path.Combine(_modsPath, "mod-list.json");
            if (!File.Exists(p)) return set;

            try
            {
                var json = await File.ReadAllTextAsync(p);
                var ml = JsonSerializer.Deserialize<ModList>(json);
                foreach (var m in ml?.Mods ?? new())
                {
                    if (m.Enabled &&
                        !string.Equals(m.Name, "base", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(m.Name, "core", StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(m.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Не удалось прочитать mod-list.json: {ex.Message}");
            }
            return set;
        }

        private async Task LoadInOrderAsync(string root, string phase)
        {
            if (!Directory.Exists(root)) return;

            var allFiles = Directory.GetFiles(root, "*.lua", SearchOption.AllDirectories);

            IEnumerable<string> filtered;
            if (phase == "lualib")
            {
                filtered = allFiles.Where(f =>
                    f.Contains($"{Path.DirectorySeparatorChar}lualib{Path.DirectorySeparatorChar}",
                               StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                filtered = allFiles.Where(f =>
                    !f.Contains($"{Path.DirectorySeparatorChar}lualib{Path.DirectorySeparatorChar}",
                                 StringComparison.OrdinalIgnoreCase) &&
                    !ShouldSkipFile(f, root));
            }

            var sorted = filtered
                .OrderBy(f => GetLoadPriority(Path.GetFileName(f)))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Console.WriteLine($"  [{phase}] Файлов к загрузке: {sorted.Count}");

            foreach (var file in sorted)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    _luaScript.DoString(content);
                }
                catch
                {
                    // Тихо пропускаем ошибки
                }
            }
        }

        private async Task LoadLuaFilesFromDirectoryAsync(string directory)
        {
            var luaFiles = Directory.GetFiles(directory, "*.lua", SearchOption.AllDirectories)
                .Where(f => !ShouldSkipFile(f, directory))
                .OrderBy(f => GetLoadPriority(Path.GetFileName(f)))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in luaFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(file);
                    _luaScript.DoString(content);
                }
                catch
                {
                    // Тихо пропускаем
                }
            }
        }

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

        private static int GetLoadPriority(string fileName)
        {
            return fileName.ToLowerInvariant() switch
            {
                "data.lua" => 0,
                "data-updates.lua" => 1,
                "data-final-fixes.lua" => 2,
                "control.lua" => 99,
                _ => 50
            };
        }

        private static string GetModNameFromArchive(string archiveOrFolderName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                archiveOrFolderName, @"^(.+?)_\d+\.\d+");
            return match.Success ? match.Groups[1].Value : archiveOrFolderName;
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // ================================================================
        // ИЗВЛЕЧЕНИЕ ПРЕДМЕТОВ (включая entity для машин)
        // ================================================================

        public List<Item> GetItems()
        {
            var items = new List<Item>();
            var dataTable = _luaScript.Globals.Get("data");
            if (dataTable.Type != DataType.Table) return items;
            var dataRaw = dataTable.Table.Get("raw").Table;
            if (dataRaw == null) return items;

            // Обычные предметы
            var itemTypes = new[] { "item", "ammo", "armor", "capsule", "gun", "module", "tool", "fluid" };
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

            // Entity (машины, роботы, печи и т.д.)
            var entityTable = dataRaw.Get("entity").Table;
            if (entityTable != null)
            {
                foreach (var kvp in entityTable.Pairs)
                {
                    var entItem = ConvertEntityToItem(kvp.Value);
                    if (entItem != null)
                    {
                        items.Add(entItem);
                        if (!string.IsNullOrEmpty(entItem.IconPath) && !string.IsNullOrEmpty(entItem.Name))
                            _itemIconPaths[entItem.Name] = entItem.IconPath;
                    }
                }
            }

            return items;
        }

        private Item? ConvertEntityToItem(DynValue luaEntity)
        {
            try
            {
                if (luaEntity.Type != DataType.Table) return null;
                var table = luaEntity.Table;

                var nameVal = table.Get("name");
                if (nameVal.Type != DataType.String) return null;
                var name = nameVal.String?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) return null;

                // Ищем иконку напрямую у сущности
                string? iconPath = null;
                var iconVal = table.Get("icon");
                if (iconVal.Type == DataType.String)
                {
                    iconPath = NormalizeGamePath(iconVal.String);
                }
                else
                {
                    var iconsVal = table.Get("icons");
                    if (iconsVal.Type == DataType.Table)
                    {
                        var firstIcon = iconsVal.Table.Get(DynValue.NewNumber(1));
                        if (firstIcon.Type == DataType.Table)
                        {
                            var ip = firstIcon.Table.Get("icon");
                            if (ip.Type == DataType.String)
                                iconPath = NormalizeGamePath(ip.String);
                        }
                    }
                }

                // Если нет иконки у entity, пробуем взять из place_result
                if (string.IsNullOrEmpty(iconPath))
                {
                    var placeResultVal = table.Get("place_result");
                    if (placeResultVal.Type == DataType.String)
                    {
                        var itemName = placeResultVal.String?.Trim();
                        if (!string.IsNullOrEmpty(itemName) && _itemIconPaths.TryGetValue(itemName, out var storedIcon))
                        {
                            iconPath = storedIcon;
                        }
                    }
                }

                // Создаём Item только если нашли иконку
                if (string.IsNullOrEmpty(iconPath)) return null;

                var item = new Item
                {
                    Name = name,
                    Type = "entity",
                    IconPath = iconPath,
                    PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(iconPath),
                    StackSize = 1,
                    Subgroup = "other",
                    Order = $"z[entity]-{name}",
                    Properties = new Dictionary<string, object>()
                };

                // Fallback для иконки по имени
                if (string.IsNullOrEmpty(item.PhysicalIconPath) || !File.Exists(item.PhysicalIconPath))
                {
                    var byFile = _pathResolver.FindIconByFileName(item.IconPath);
                    if (byFile != null) item.PhysicalIconPath = byFile;
                }
                if (string.IsNullOrEmpty(item.PhysicalIconPath) || !File.Exists(item.PhysicalIconPath))
                {
                    var byName = _pathResolver.FindIconByName(item.Name);
                    if (byName != null)
                    {
                        item.PhysicalIconPath = byName;
                        if (string.IsNullOrEmpty(item.IconPath))
                            item.IconPath = "__base__/graphics/entity/" + item.Name + "/" + item.Name + ".png";
                    }
                }

                return item;
            }
            catch { return null; }
        }

        // ================================================================
        // ИЗВЛЕЧЕНИЕ РЕЦЕПТОВ (с поддержкой normal/expensive)
        // ================================================================

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

        private Recipe? ConvertToRecipe(DynValue luaRecipe)
        {
            try
            {
                if (luaRecipe.Type != DataType.Table) return null;
                var table = luaRecipe.Table;
                var recipe = new Recipe();

                var nameVal = table.Get("name");
                if (nameVal.Type == DataType.String)
                    recipe.Name = nameVal.String?.Trim() ?? string.Empty;

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
					recipe.RecipeIconPath = iconVal.String?.Trim() ?? string.Empty;
                }

                var iconSizeVal = table.Get("icon_size");
                if (iconSizeVal.Type == DataType.Number)
                    recipe.IconSize = iconSizeVal.Number.ToString();

                var enabledVal = table.Get("enabled");
                recipe.Enabled = enabledVal.Type == DataType.Boolean ? enabledVal.Boolean : true;

                // === КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: поддержка normal/expensive ===
                var normalVal = table.Get("normal");
                var expensiveVal = table.Get("expensive");

                if (normalVal.Type == DataType.Table || expensiveVal.Type == DataType.Table)
                {
                    recipe.RecipeFormat = "difficulty";
                    
                    // Приоритет: normal > expensive > общий
                    Table? primaryTable = null;
                    if (normalVal.Type == DataType.Table)
                        primaryTable = normalVal.Table;
                    else if (expensiveVal.Type == DataType.Table)
                        primaryTable = expensiveVal.Table;

                    if (primaryTable != null)
                    {
                        // Ингредиенты из primary
                        recipe.Ingredients = ConvertIngredients(primaryTable.Get("ingredients"));
                        
                        // Результаты из primary
                        ParseResultsFromTable(primaryTable, recipe);

                        // Если в primary нет results/result, пробуем из корня таблицы
                        if (recipe.Results.Count == 0 && recipe.Ingredients.Count == 0)
                        {
                            recipe.Ingredients = ConvertIngredients(table.Get("ingredients"));
                            ParseResultsFromTable(table, recipe);
                        }
                    }
                }
                else
                {
                    // Стандартный формат (без difficulty)
                    recipe.Ingredients = ConvertIngredients(table.Get("ingredients"));
                    ParseResultsFromTable(table, recipe);
                }
                // =================================================================

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
            // 1. Своя иконка рецепта
            if (!string.IsNullOrEmpty(recipe.RecipeIconPath))
            {
                recipe.ProductIconPath = recipe.RecipeIconPath;
                recipe.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(recipe.RecipeIconPath);
                recipe.IconFileName = $"{recipe.Name}.png";

                if (File.Exists(recipe.PhysicalIconPath)) return;

                var byFile = _pathResolver.FindIconByFileName(recipe.RecipeIconPath);
                if (byFile != null) { recipe.PhysicalIconPath = byFile; return; }
            }

            // 2. Иконка главного продукта
            if (!string.IsNullOrEmpty(recipe.MainProduct))
            {
                if (_itemIconPaths.TryGetValue(recipe.MainProduct, out var gp))
                {
                    recipe.ProductIconPath = gp;
                    recipe.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(gp);
                }
                else
                {
                    recipe.PhysicalIconPath = _pathResolver.FindIconByName(recipe.MainProduct);
                }
                recipe.IconFileName = $"{recipe.MainProduct}.png";
                if (File.Exists(recipe.PhysicalIconPath)) return;

                var byFile = _pathResolver.FindIconByFileName(recipe.ProductIconPath);
                if (byFile != null) { recipe.PhysicalIconPath = byFile; return; }
            }

            // 3. Единственный результат
            if (recipe.Results.Count == 1)
            {
                var rn = recipe.Results[0].Name;
                if (_itemIconPaths.TryGetValue(rn, out var rp))
                {
                    recipe.ProductIconPath = rp;
                    recipe.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(rp);
                }
                else
                {
                    recipe.PhysicalIconPath = _pathResolver.FindIconByName(rn);
                }
                recipe.IconFileName = $"{rn}.png";
                if (File.Exists(recipe.PhysicalIconPath)) return;

                var byFile = _pathResolver.FindIconByFileName(recipe.ProductIconPath);
                if (byFile != null) { recipe.PhysicalIconPath = byFile; return; }
            }

            // 4. Последний fallback
            recipe.PhysicalIconPath = _pathResolver.FindIconByName(recipe.Name);
            recipe.IconFileName = $"{recipe.Name}.png";
        }

        // ================================================================
        // ИНГРЕДИЕНТЫ И РЕЗУЛЬТАТЫ
        // ================================================================

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
                            ingredient.Amount = amountVal.Type != DataType.Nil
                                ? Convert.ToInt32(amountVal.Number) : 1;
                            var typeVal = itemTable.Get("type");
                            ingredient.Type = typeVal.Type != DataType.Nil
                                ? (typeVal.String?.Trim() ?? "item") : "item";
                        }
                        else
                        {
                            var firstVal = itemTable.Get(DynValue.NewNumber(1));
                            var secondVal = itemTable.Get(DynValue.NewNumber(2));
                            if (firstVal.Type != DataType.Nil)
                                ingredient.Name = firstVal.String?.Trim()
                                    ?? firstVal.ToString()?.Trim() ?? string.Empty;
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
                            result.Amount = amountVal.Type != DataType.Nil
                                ? Convert.ToInt32(amountVal.Number) : 1;
                            var probVal = itemTable.Get("probability");
                            result.Probability = probVal.Type != DataType.Nil
                                ? Convert.ToDouble(probVal.Number) : 1.0;
                            var typeVal = itemTable.Get("type");
                            result.Type = typeVal.Type != DataType.Nil
                                ? (typeVal.String?.Trim() ?? "item") : "item";
                        }
                        else
                        {
                            var firstVal = itemTable.Get(DynValue.NewNumber(1));
                            var secondVal = itemTable.Get(DynValue.NewNumber(2));
                            if (firstVal.Type != DataType.Nil)
                                result.Name = firstVal.String?.Trim()
                                    ?? firstVal.ToString()?.Trim() ?? string.Empty;
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

        // ================================================================
        // КОНВЕРТАЦИЯ ПРЕДМЕТОВ (item types)
        // ================================================================

        private Item? ConvertToItem(DynValue luaItem, string type)
        {
            try
            {
                if (luaItem.Type != DataType.Table) return null;
                var table = luaItem.Table;
                var item = new Item { Type = type };

                var nameVal = table.Get("name");
                if (nameVal.Type == DataType.String)
                    item.Name = nameVal.String?.Trim() ?? string.Empty;

                var iconVal = table.Get("icon");
                if (iconVal.Type == DataType.String)
                {
                    item.IconPath = NormalizeGamePath(iconVal.String);
                    item.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(item.IconPath);
                }
                else
                {
                    var iconsVal = table.Get("icons");
                    if (iconsVal.Type == DataType.Table)
                    {
                        var firstIcon = iconsVal.Table.Get(DynValue.NewNumber(1));
                        if (firstIcon.Type == DataType.Table)
                        {
                            var ip = firstIcon.Table.Get("icon");
                            if (ip.Type == DataType.String)
                            {
                                item.IconPath = NormalizeGamePath(ip.String);
                                item.PhysicalIconPath = _pathResolver.ResolveToPhysicalPath(item.IconPath);
                            }
                        }
                    }
                }

                // Fallback цепочка для иконок
                if (string.IsNullOrEmpty(item.PhysicalIconPath) || !File.Exists(item.PhysicalIconPath))
                {
                    var byFile = _pathResolver.FindIconByFileName(item.IconPath);
                    if (byFile != null) item.PhysicalIconPath = byFile;
                }
                if (string.IsNullOrEmpty(item.PhysicalIconPath) || !File.Exists(item.PhysicalIconPath))
                {
                    var byName = _pathResolver.FindIconByName(item.Name);
                    if (byName != null)
                    {
                        item.PhysicalIconPath = byName;
                        if (string.IsNullOrEmpty(item.IconPath))
                            item.IconPath = "__base__/graphics/icons/" + item.Name + ".png";
                    }
                }

                var iconSizeVal = table.Get("icon_size");
                if (iconSizeVal.Type == DataType.Number)
                    item.IconSize = iconSizeVal.Number.ToString();

                var stackSizeVal = table.Get("stack_size");
                if (stackSizeVal.Type == DataType.Number)
                    item.StackSize = Convert.ToInt32(stackSizeVal.Number);

                var subgroupVal = table.Get("subgroup");
                if (subgroupVal.Type == DataType.String)
                    item.Subgroup = subgroupVal.String?.Trim() ?? string.Empty;

                var orderVal = table.Get("order");
                if (orderVal.Type == DataType.String)
                    item.Order = orderVal.String?.Trim() ?? string.Empty;

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

        // ================================================================
        // НОРМАЛИЗАЦИЯ ПУТЕЙ (исправленная версия)
        // ================================================================

		private static string NormalizeGamePath(string? gamePath)
		{
			if (string.IsNullOrWhiteSpace(gamePath)) return string.Empty;
			
			var p = gamePath.Trim();

			// Если путь УЖЕ содержит корректный префикс "__name__/", оставляем как есть
			if (p.StartsWith("__") && p.IndexOf("__", 2) > 0)
			{
				return p;
			}

			// Лечим специфичные искажения MoonSharp
			if (!p.Contains("__"))
			{
				p = Regex.Replace(p, @"^\s*_+\s*base\s*_+\s*/", "__base__/", RegexOptions.IgnoreCase);
				p = Regex.Replace(p, @"^\s*_+\s*core\s*_+\s*/", "__core__/", RegexOptions.IgnoreCase);
				
				// Универсальный случай для модов: " modname /..." -> "__modname__/"
				p = Regex.Replace(
					p, 
					@"^\s*([a-zA-Z][\w\-]*)\s*/", 
					m => $"__{m.Groups[1].Value}__/"
				);
			}

			// Удаляем лишние пробелы вокруг __...__ внутри строки
			p = Regex.Replace(p, @"\s+(__[^/\s]+__)\s+", "$1");
			
			// Нормализуем двойные слеши
			p = p.Replace("//", "/").Trim();

			return p;
		}

        // ================================================================
        // DYNVALUE → OBJECT
        // ================================================================

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
