using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLua;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace RecipeExtractor
{
    // ==========================================
    // КЛАСС ДЛЯ ЗАПИСИ ОТЛАДКИ В ФАЙЛ (СОРТИРОВКА: ОШИБКИ СВЕРХУ)
    // ==========================================
    public static class DebugLogger
    {
        private class LogEntry
        {
            public string Message { get; set; } = string.Empty;
            public bool IsError { get; set; }
        }

        private static readonly List<LogEntry> _logs = new List<LogEntry>();
        private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "debug_log.txt");

        public static void Log(string message, bool isError = false)
        {
            lock (_logs)
            {
                _logs.Add(new LogEntry { Message = message, IsError = isError });
            }
        }

        public static void Save()
        {
            try
            {
                // Сортируем: сначала ошибки (true), потом обычная информация (false)
                var sortedLogs = _logs.OrderByDescending(l => l.IsError).ToList();

                using (var writer = new StreamWriter(LogFilePath, false, Encoding.UTF8))
                {
                    writer.WriteLine($"=== Debug Log Generated: {DateTime.Now} ===");
                    writer.WriteLine(new string('=', 60));

                    bool wroteErrors = false;
                    bool wroteInfo = false;

                    foreach (var log in sortedLogs)
                    {
                        // Добавляем заголовки разделов при переходе от ошибок к информации
                        if (log.IsError && !wroteErrors)
                        {
                            writer.WriteLine("\n🚨 ОШИБКИ И ПРОБЛЕМЫ:");
                            writer.WriteLine(new string('-', 60));
                            wroteErrors = true;
                        }
                        else if (!log.IsError && wroteErrors && !wroteInfo)
                        {
                            writer.WriteLine("\n📝 ОБЫЧНЫЙ ЛОГ ВЫПОЛНЕНИЯ:");
                            writer.WriteLine(new string('-', 60));
                            wroteInfo = true;
                        }

                        writer.WriteLine(log.Message);
                    }

                    if (_logs.Count == 0)
                    {
                        writer.WriteLine("\n(Лог пуст, ошибок не обнаружено)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось сохранить лог-файл: {ex.Message}");
            }
        }
    }
    // ==========================================

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

        [JsonProperty("mod", NullValueHandling = NullValueHandling.Ignore)]
        public string? Mod { get; set; }
    }

    public class FactorioMod
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string PhysicalPath { get; set; } = string.Empty;
        public string DataPath { get; set; } = string.Empty;
        public bool IsZip { get; set; }
        public List<string> Dependencies { get; set; } = new();
        public List<string> OptionalDependencies { get; set; } = new();
        public List<string> Incompatible { get; set; } = new();
    }

    class Program
    {
        static void Main(string[] args)
        {
            DebugLogger.Log("Program started.");

            Console.WriteLine("=== Factorio Recipe & Resource Extractor (with Mods) ===");

            string factorioDataPath = @"C:\Program Files (x86)\Steam\steamapps\common\Factorio\data";
            if (!Directory.Exists(factorioDataPath))
            {
                Console.Write("Введите путь к директории Factorio/data: ");
                factorioDataPath = Console.ReadLine()?.Trim('"') ?? "";
            }

            if (!Directory.Exists(factorioDataPath))
            {
                Console.WriteLine("Ошибка: Указанный путь не существует.");
                DebugLogger.Log($"FATAL: Путь не существует: {factorioDataPath}", true);
                Console.WriteLine("\nНажмите Enter для выхода...");
                Console.ReadLine();
                DebugLogger.Save(); // Сохраняем лог перед выходом
                return;
            }

            string defaultModsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Factorio", "mods");

            string modsPath = Directory.Exists(defaultModsPath) ? defaultModsPath : "";

            Console.WriteLine($"\nПапка модов по умолчанию: {defaultModsPath}");
            Console.Write("Введите путь к папке модов (Enter - использовать по умолчанию, 'skip' - без модов): ");
            string? userInput = Console.ReadLine()?.Trim('"');

            if (!string.IsNullOrEmpty(userInput))
            {
                if (userInput.ToLower() == "skip")
                {
                    modsPath = "";
                }
                else if (Directory.Exists(userInput))
                {
                    modsPath = userInput;
                }
                else
                {
                    Console.WriteLine("Указанная папка не существует. Используем путь по умолчанию.");
                    DebugLogger.Log($"User input path invalid, using default: {defaultModsPath}", true);
                }
            }

            try
            {
                string exeFolder = AppContext.BaseDirectory;
                string iconsFolder = Path.Combine(exeFolder, "icons");
                string tempModsFolder = Path.Combine(exeFolder, "temp_mods");

                Directory.CreateDirectory(iconsFolder);
                if (Directory.Exists(tempModsFolder))
                    Directory.Delete(tempModsFolder, true);
                Directory.CreateDirectory(tempModsFolder);

                Console.WriteLine("\n🔍 Поиск модов...");
                List<FactorioMod> mods = new();
                if (!string.IsNullOrEmpty(modsPath) && Directory.Exists(modsPath))
                {
                    mods = DiscoverMods(modsPath, tempModsFolder);
                    Console.WriteLine($"✅ Найдено модов: {mods.Count}");
                    foreach (var mod in mods)
                    {
                        Console.WriteLine($"   • {mod.Name} v{mod.Version} ({(mod.IsZip ? "zip" : "folder")})");
                    }
                }
                else
                {
                    Console.WriteLine("ℹ️ Работа без модов.");
                }

                Console.WriteLine("\nИнициализация Lua окружения...");
                List<FactorioRecipe> recipes = ExtractRecipes(factorioDataPath, iconsFolder, mods);

                ValidateRecipesAndIcons(recipes, exeFolder);

                string outputPath = Path.Combine(exeFolder, "recipes.json");
                string jsonOutput = JsonConvert.SerializeObject(recipes, Formatting.Indented);
                File.WriteAllText(outputPath, jsonOutput);

                Console.WriteLine($"\n✅ Успешно! Извлечено записей: {recipes.Count}");
                Console.WriteLine($"💾 Файл сохранен в: {outputPath}");
                Console.WriteLine($"📝 Подробный лог (ошибки сверху) сохранен в: {Path.Combine(exeFolder, "debug_log.txt")}");

                try { Directory.Delete(tempModsFolder, true); } catch { }

                Process.Start(new ProcessStartInfo { FileName = outputPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Произошла ошибка: {ex.Message}");
                DebugLogger.Log($"FATAL ERROR: {ex.Message}\n{ex.StackTrace}", true);
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                // Гарантированно сохраняем лог в конце, даже если была ошибка
                DebugLogger.Save();
            }

            Console.WriteLine("\nНажмите Enter для выхода...");
            Console.ReadLine();
        }

        private static List<FactorioMod> DiscoverMods(string modsPath, string tempFolder)
        {
            var mods = new List<FactorioMod>();

            foreach (var entry in Directory.GetFileSystemEntries(modsPath))
            {
                try
                {
                    FactorioMod? mod = null;

                    if (entry.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        mod = ExtractAndReadZipMod(entry, tempFolder);
                    }
                    else if (Directory.Exists(entry))
                    {
                        mod = ReadFolderMod(entry);
                    }

                    if (mod != null && !string.IsNullOrEmpty(mod.Name))
                    {
                        mods.Add(mod);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Не удалось обработать мод {Path.GetFileName(entry)}: {ex.Message}");
                    DebugLogger.Log($"ERROR processing mod {entry}: {ex.Message}", true);
                }
            }

            return SortModsByDependencies(FilterIncompatible(mods));
        }

        private static FactorioMod? ExtractAndReadZipMod(string zipPath, string tempFolder)
        {
            string modName = Path.GetFileNameWithoutExtension(zipPath);
            string extractPath = Path.Combine(tempFolder, modName);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Не удалось распаковать {modName}: {ex.Message}");
                DebugLogger.Log($"ERROR extracting {modName}: {ex.Message}", true);
                return null;
            }

            string? infoPath = FindInfoJson(extractPath);
            if (infoPath == null)
            {
                Console.WriteLine($"⚠️ Мод {modName} не содержит info.json");
                DebugLogger.Log($"ERROR: Mod {modName} missing info.json", true);
                return null;
            }

            return ReadModInfo(infoPath, Path.GetDirectoryName(infoPath)!, isZip: true);
        }

        private static FactorioMod? ReadFolderMod(string folderPath)
        {
            string? infoPath = FindInfoJson(folderPath);
            if (infoPath == null) return null;

            return ReadModInfo(infoPath, folderPath, isZip: false);
        }

        private static string? FindInfoJson(string folderPath)
        {
            string directPath = Path.Combine(folderPath, "info.json");
            if (File.Exists(directPath)) return directPath;

            foreach (var subDir in Directory.GetDirectories(folderPath))
            {
                string subInfoPath = Path.Combine(subDir, "info.json");
                if (File.Exists(subInfoPath)) return subInfoPath;
            }

            return null;
        }

        private static FactorioMod? ReadModInfo(string infoPath, string dataRootPath, bool isZip)
        {
            try
            {
                string jsonContent = File.ReadAllText(infoPath);
                var info = JObject.Parse(jsonContent);

                string modName = info["name"]?.ToString() ?? Path.GetFileNameWithoutExtension(infoPath);
                string physicalPath = Path.GetDirectoryName(infoPath)!;

                string dataPath = Path.Combine(physicalPath, "data");
                if (!Directory.Exists(dataPath))
                {
                    dataPath = physicalPath;
                }

                var mod = new FactorioMod
                {
                    Name = modName,
                    Version = info["version"]?.ToString() ?? "0.0.0",
                    Title = info["title"]?.ToString() ?? "",
                    PhysicalPath = physicalPath,
                    DataPath = dataPath,
                    IsZip = isZip
                };

                if (info["dependencies"] is JArray deps)
                {
                    foreach (var dep in deps)
                    {
                        string depStr = dep.ToString();
                        if (string.IsNullOrEmpty(depStr)) continue;

                        if (depStr.StartsWith("!"))
                        {
                            mod.Incompatible.Add(depStr.Substring(1).Trim());
                        }
                        else if (depStr.StartsWith("?"))
                        {
                            mod.OptionalDependencies.Add(depStr.Substring(1).Trim());
                        }
                        else if (depStr.StartsWith("~") || (depStr.StartsWith("(") && depStr.EndsWith(")")))
                        {
                            // Пропускаем скрытые зависимости
                        }
                        else
                        {
                            string depName = depStr.Split('(', '!', '?', '~', ' ').First().Trim();
                            if (!string.IsNullOrEmpty(depName))
                                mod.Dependencies.Add(depName);
                        }
                    }
                }

                return mod;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка чтения info.json: {ex.Message}");
                DebugLogger.Log($"ERROR reading info.json at {infoPath}: {ex.Message}", true);
                return null;
            }
        }

        private static List<FactorioMod> FilterIncompatible(List<FactorioMod> mods)
        {
            var modNames = new HashSet<string>(mods.Select(m => m.Name));
            var result = new List<FactorioMod>();

            foreach (var mod in mods)
            {
                bool incompatible = false;
                foreach (var inc in mod.Incompatible)
                {
                    if (modNames.Contains(inc))
                    {
                        Console.WriteLine($"⚠️ Мод {mod.Name} несовместим с {inc}, пропускаем");
                        DebugLogger.Log($"INCOMPATIBLE: Mod {mod.Name} skipped due to conflict with {inc}", true);
                        incompatible = true;
                        break;
                    }
                }
                if (!incompatible) result.Add(mod);
            }

            return result;
        }

        private static List<FactorioMod> SortModsByDependencies(List<FactorioMod> mods)
        {
            var modByName = mods.ToDictionary(m => m.Name);
            var sorted = new List<FactorioMod>();
            var visited = new HashSet<string>();
            var inProgress = new HashSet<string>();

            void Visit(FactorioMod mod)
            {
                if (visited.Contains(mod.Name)) return;
                if (inProgress.Contains(mod.Name))
                {
                    Console.WriteLine($"⚠️ Циклическая зависимость у {mod.Name}");
                    DebugLogger.Log($"WARNING: Cyclic dependency detected for {mod.Name}", true);
                    return;
                }

                inProgress.Add(mod.Name);

                foreach (var dep in mod.Dependencies.Concat(mod.OptionalDependencies))
                {
                    if (modByName.TryGetValue(dep, out var depMod))
                    {
                        Visit(depMod);
                    }
                }

                inProgress.Remove(mod.Name);
                visited.Add(mod.Name);
                sorted.Add(mod);
            }

            if (modByName.TryGetValue("base", out var baseMod))
            {
                Visit(baseMod);
            }

            foreach (var mod in mods)
            {
                Visit(mod);
            }

            return sorted;
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

            var recipesByMod = recipes.GroupBy(r => r.Mod ?? "base").ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine("\n📦 Рецепты по источникам:");
            foreach (var kvp in recipesByMod.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"   • {kvp.Key}: {kvp.Value}");
            }

            if (missingIcons.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n⚠️ Рецепты БЕЗ ИКОНОК ({missingIcons.Count}):");
                Console.ResetColor();
                foreach (var name in missingIcons.Take(20))
                    Console.WriteLine($"   • {name}");
                if (missingIcons.Count > 20)
                    Console.WriteLine($"   ... и еще {missingIcons.Count - 20}");
            }

            if (missingFiles.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n❌ БИТЫЕ ССЫЛКИ на иконки ({missingFiles.Count}):");
                Console.ResetColor();
                foreach (var item in missingFiles.Take(10))
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
                foreach (var item in invalidAmounts.Take(15))
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

        private static List<FactorioRecipe> ExtractRecipes(string factorioDataPath, string iconsFolder, List<FactorioMod> mods)
        {
            var recipes = new List<FactorioRecipe>();
            int iconsCopied = 0;

            using (Lua lua = new Lua())
            {
                lua.DoString(@"
                    log = function(msg) end
                    error = function(msg) end
                    print = function(...) end
                    io.write = function(...) end
                    io.write = function(...) return io.stdout end
                    
                    os.execute = function(cmd) return true, 'exit', 0 end
                    io.popen = function(cmd) return nil end
                    os.rename = function() return true end
                    os.remove = function() return true end
                    os.tmpname = function() return '' end

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
                    current_mod = nil
                ");

                string coreLuaPath = Path.Combine(factorioDataPath, "core", "lualib", "?.lua").Replace('\\', '/');
                string baseLuaPath = Path.Combine(factorioDataPath, "base", "?.lua").Replace('\\', '/');
                string baseFolderPath = factorioDataPath.Replace('\\', '/');

                lua.DoString($"package.path = package.path .. ';{coreLuaPath};{baseLuaPath}'");

                foreach (var mod in mods)
                {
                    string modPath = mod.PhysicalPath.Replace('\\', '/');
                    lua.DoString($"package.path = package.path .. ';{modPath}/?.lua'");
                }

                var modPathsList = new List<string> { baseFolderPath };
                foreach (var mod in mods)
                {
                    modPathsList.Add(mod.PhysicalPath.Replace('\\', '/'));
                }

                lua.DoString("mod_search_paths = {}");
                foreach (var path in modPathsList)
                {
                    lua.DoString($"table.insert(mod_search_paths, '{path}')");
                }

                lua.DoString(@"
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
                            
                            for _, base_path in ipairs(mod_search_paths) do
                                local file_path = base_path .. '/' .. mod_name .. '/' .. rest .. '.lua'
                                local f, err = loadfile(file_path)
                                if f then
                                    return f()
                                end
                            end
                            
                            return {}
                        else
                            return old_require(modname)
                        end
                    end
                ");

                try
                {
                    lua.DoFile(Path.Combine(factorioDataPath, "core", "lualib", "util.lua"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Предупреждение при загрузке util.lua: {ex.Message}");
                    DebugLogger.Log($"WARNING loading util.lua: {ex.Message}", true);
                }

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

                Console.WriteLine("Загрузка файлов базовой игры...");
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
                            DebugLogger.Log($"ERROR loading base file {fileName}: {ex.Message}", true);
                        }
                    }
                }
                Console.WriteLine($"✅ Загружено базовых файлов: {loadedCount}");

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

                if (mods.Count > 0)
                {
                    Console.WriteLine($"\nЗагрузка {mods.Count} модов...");
                    int modsLoaded = 0;
                    int modsFailed = 0;

                    foreach (var mod in mods)
                    {
                        DebugLogger.Log($"\n[MOD] Processing: {mod.Name}");
                        DebugLogger.Log($"  PhysicalPath: {mod.PhysicalPath}");
                        DebugLogger.Log($"  DataPath: {mod.DataPath}");

                        bool hasLuaFiles = false;
                        if (Directory.Exists(mod.DataPath))
                        {
                            var files = Directory.GetFiles(mod.DataPath, "*.lua", SearchOption.AllDirectories);
                            DebugLogger.Log($"  Lua files in DataPath: {files.Length}");
                            hasLuaFiles = files.Length > 0;
                        }
                        if (!hasLuaFiles && Directory.Exists(mod.PhysicalPath))
                        {
                            var files = Directory.GetFiles(mod.PhysicalPath, "*.lua", SearchOption.TopDirectoryOnly);
                            DebugLogger.Log($"  Lua files in PhysicalPath: {files.Length}");
                            hasLuaFiles = files.Length > 0;
                        }

                        if (!hasLuaFiles)
                        {
                            Console.WriteLine($"⚠️ Мод {mod.Name}: не найдено lua-файлов");
                            DebugLogger.Log($"  STATUS: FAILED (no lua files found)", true);
                            modsFailed++;
                            continue;
                        }

                        lua["current_mod"] = mod.Name;

                        try
                        {
                            LoadModDataFiles(lua, mod);
                            modsLoaded++;
                            Console.WriteLine($"✅ Мод {mod.Name} загружен");
                            DebugLogger.Log($"  STATUS: SUCCESS");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Ошибка загрузки мода {mod.Name}: {ex.Message}");
                            DebugLogger.Log($"  STATUS: FAILED ({ex.Message})", true);
                            modsFailed++;
                        }
                    }

                    Console.WriteLine($"\n📦 Итого модов: загружено {modsLoaded}, ошибок {modsFailed}");
                }

                LuaTable rawObj = (lua["data"] as LuaTable)?["raw"] as LuaTable;

                if (rawObj != null)
                {
                    if (rawObj["recipe"] is LuaTable recipeTable)
                    {
                        Console.WriteLine("\nПарсинг рецептов...");
                        foreach (var key in recipeTable.Keys)
                        {
                            if (recipeTable[key] is LuaTable recipeData)
                            {
                                var parsed = ParseRecipe(recipeData, lua, factorioDataPath, iconsFolder, mods, ref iconsCopied);
                                if (parsed != null) recipes.Add(parsed);
                            }
                        }
                    }

                    if (rawObj["resource"] is LuaTable resourceTable)
                    {
                        Console.WriteLine("Парсинг ресурсов...");
                        foreach (var key in resourceTable.Keys)
                        {
                            if (resourceTable[key] is LuaTable resData)
                            {
                                var parsed = ParseResourceAsRecipe(resData, lua, factorioDataPath, iconsFolder, mods, ref iconsCopied);
                                if (parsed != null && !recipes.Any(r => r.Name == parsed.Name))
                                    recipes.Add(parsed);
                            }
                        }
                    }

                    Console.WriteLine("Добавление pumping-рецептов...");
                    AddPumpingRecipes(recipes, lua, factorioDataPath, iconsFolder, mods, ref iconsCopied);
                }
                else
                {
                    Console.WriteLine("⚠️ Не удалось получить таблицу data.raw из Lua.");
                    DebugLogger.Log("FATAL: Failed to get data.raw table from Lua", true);
                }
            }

            Console.WriteLine($"\n✅ Скопировано иконок: {iconsCopied}");
            return recipes;
        }

        private static void LoadModDataFiles(Lua lua, FactorioMod mod)
        {
            var searchPaths = new List<string>();

            if (Directory.Exists(mod.DataPath) && mod.DataPath != mod.PhysicalPath)
            {
                searchPaths.Add(mod.DataPath);
            }

            searchPaths.Add(mod.PhysicalPath);

            string? dataLua = null;
            foreach (var path in searchPaths)
            {
                string candidate = Path.Combine(path, "data.lua");
                if (File.Exists(candidate))
                {
                    dataLua = candidate;
                    break;
                }
            }

            if (dataLua != null)
            {
                try { lua.DoFile(dataLua); } catch { }
            }

            var loadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dataLua != null) loadedFiles.Add(dataLua);

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath)) continue;

                foreach (var file in Directory.GetFiles(searchPath, "*.lua", SearchOption.AllDirectories))
                {
                    if (loadedFiles.Contains(file)) continue;
                    loadedFiles.Add(file);

                    string fileName = Path.GetFileName(file).ToLower();
                    if (fileName.Contains("locale") ||
                        fileName.Contains("changelog") ||
                        fileName.Contains("settings") ||
                        fileName.Contains("controls") ||
                        fileName == "info.lua")
                        continue;

                    try { lua.DoFile(file); } catch { }
                }
            }
        }

        private static string? DetectModFromIcon(string? iconPath, List<FactorioMod> mods, string factorioDataPath)
        {
            if (string.IsNullOrEmpty(iconPath)) return null;

            if (iconPath.StartsWith("__"))
            {
                int closeIndex = iconPath.IndexOf("__", 2);
                if (closeIndex > 2)
                {
                    string modName = iconPath.Substring(2, closeIndex - 2);
                    if (modName == "base") return "base";
                    return modName;
                }
            }

            string fullPath = ResolveFactorioPath(iconPath, factorioDataPath, mods);
            foreach (var mod in mods)
            {
                if (fullPath.StartsWith(mod.PhysicalPath, StringComparison.OrdinalIgnoreCase))
                    return mod.Name;
            }

            return null;
        }

        private static void AddPumpingRecipes(List<FactorioRecipe> recipes, Lua lua,
            string factorioDataPath, string iconsFolder, List<FactorioMod> mods, ref int iconsCopied)
        {
            string[] pumpingFluids = { "water", "crude-oil", "steam" };

            LuaTable rawObj = (lua["data"] as LuaTable)?["raw"] as LuaTable;
            if (rawObj == null) return;

            LuaTable? fluidTable = rawObj["fluid"] as LuaTable;
            if (fluidTable == null) return;

            foreach (var fluidName in pumpingFluids)
            {
                string recipeName = $"pumping-{fluidName}";
                if (recipes.Any(r => r.Name == recipeName || r.Name == fluidName))
                    continue;

                if (fluidTable[fluidName] == null) continue;

                LuaTable fluidData = fluidTable[fluidName] as LuaTable;
                if (fluidData == null) continue;

                double defaultAmount = fluidName == "water" ? 1200 : 10;

                var recipe = new FactorioRecipe
                {
                    Name = recipeName,
                    Category = "pumping",
                    EnergyRequired = 1.0,
                    Mod = "base",
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

                CopyAndAssignIcon(recipe, iconPath, factorioDataPath, iconsFolder, mods, ref iconsCopied);
                recipes.Add(recipe);
            }
        }

        private static FactorioRecipe? ParseRecipe(LuaTable raw, Lua lua,
            string factorioDataPath, string iconsFolder, List<FactorioMod> mods, ref int iconsCopied)
        {
            string name = raw["name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(name)) return null;

            var recipe = new FactorioRecipe
            {
                Name = name,
                Category = raw["category"]?.ToString() ?? "crafting",
                EnergyRequired = 0.5
            };

            LuaTable targetTable = raw;
            if (raw["normal"] is LuaTable normalTable) targetTable = normalTable;
            else if (raw["expensive"] is LuaTable expensiveTable) targetTable = expensiveTable;

            if (targetTable["energy_required"] != null)
                recipe.EnergyRequired = Convert.ToDouble(targetTable["energy_required"]);

            if (targetTable["ingredients"] is LuaTable ingTable)
                recipe.Ingredients = ParseItemAmounts(ingTable);

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

            // === ДЕТАЛЬНЫЙ ДЕБАГ РЕЦЕПТОВ (в файл) ===
            DebugLogger.Log($"\n[RECIPE] {name}");
            DebugLogger.Log($"  Raw icon: {raw["icon"]}");

            if (raw["icons"] is LuaTable iconsTable)
            {
                DebugLogger.Log($"  Icons array found:");
                foreach (var val in iconsTable.Values)
                {
                    if (val is LuaTable iconEntry)
                    {
                        DebugLogger.Log($"    - icon: {iconEntry["icon"]}, size: {iconEntry["icon_size"]}");
                    }
                }
            }

            string? iconPath = ExtractIconPath(raw) ?? ExtractIconPath(targetTable);
            DebugLogger.Log($"  Extracted icon: {iconPath ?? "NULL"}");

            if (string.IsNullOrEmpty(iconPath))
            {
                var getIconFunc = lua["get_best_icon"] as LuaFunction;
                if (getIconFunc != null)
                {
                    var res = getIconFunc.Call(name);
                    iconPath = res?.FirstOrDefault()?.ToString();
                    DebugLogger.Log($"  Search by name '{name}': {iconPath ?? "NULL"}");

                    if (string.IsNullOrEmpty(iconPath) && recipe.Products.Count > 0)
                    {
                        res = getIconFunc.Call(recipe.Products[0].Name);
                        iconPath = res?.FirstOrDefault()?.ToString();
                        DebugLogger.Log($"  Search by product '{recipe.Products[0].Name}': {iconPath ?? "NULL"}");
                    }
                }
            }

            DebugLogger.Log($"  Final Icon Path: {iconPath ?? "NULL"}");

            if (!string.IsNullOrEmpty(iconPath))
            {
                string fullPath = ResolveFactorioPath(iconPath, factorioDataPath, mods);
                DebugLogger.Log($"  Resolved Full Path: {fullPath}");

                bool exists = File.Exists(fullPath);
                DebugLogger.Log($"  File Exists: {exists}");

                if (!exists)
                {
                    DebugLogger.Log($"  ERROR: Icon file not found at resolved path!", true);
                }
            }
            else
            {
                DebugLogger.Log($"  ERROR: No icon path could be determined!", true);
            }
            // ===========================================

            recipe.Mod = DetectModFromIcon(iconPath, mods, factorioDataPath);

            CopyAndAssignIcon(recipe, iconPath, factorioDataPath, iconsFolder, mods, ref iconsCopied);
            return recipe;
        }

        private static FactorioRecipe? ParseResourceAsRecipe(LuaTable raw, Lua lua,
            string factorioDataPath, string iconsFolder, List<FactorioMod> mods, ref int iconsCopied)
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

            recipe.Mod = DetectModFromIcon(iconPath, mods, factorioDataPath);
            CopyAndAssignIcon(recipe, iconPath, factorioDataPath, iconsFolder, mods, ref iconsCopied);
            return recipe;
        }

        private static List<FactorioItemAmount> ParseItemAmounts(LuaTable table)
        {
            var list = new List<FactorioItemAmount>();

            foreach (var val in table.Values)
            {
                if (val is not LuaTable t) continue;

                var item = new FactorioItemAmount();

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
                else if (t[1] != null)
                {
                    item.Name = t[1].ToString()!;
                    item.Type = "item";

                    if (t[2] != null)
                        item.Amount = Convert.ToDouble(t[2]);
                    else
                        item.Amount = 1;

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
            string factorioDataPath, string iconsFolder, List<FactorioMod> mods, ref int iconsCopied)
        {
            if (string.IsNullOrEmpty(rawIconPath)) return;

            string fullSourcePath = ResolveFactorioPath(rawIconPath, factorioDataPath, mods);

            if (File.Exists(fullSourcePath))
            {
                string modPrefix = string.IsNullOrEmpty(recipe.Mod) || recipe.Mod == "base" ? "" : $"{recipe.Mod}_";
                string targetFileName = $"{modPrefix}{recipe.Name}{Path.GetExtension(fullSourcePath)}";
                string targetFilePath = Path.Combine(iconsFolder, targetFileName);

                try
                {
                    File.Copy(fullSourcePath, targetFilePath, true);
                    recipe.Icon = Path.Combine("icons", targetFileName).Replace('\\', '/');
                    iconsCopied++;
                }
                catch (IOException ex)
                {
                    DebugLogger.Log($"  ERROR copying icon {targetFileName}: {ex.Message}", true);
                }
            }
        }

        private static string ResolveFactorioPath(string factorioPath, string factorioDataPath, List<FactorioMod> mods)
        {
            if (factorioPath.StartsWith("__"))
            {
                int closeIndex = factorioPath.IndexOf("__", 2);
                if (closeIndex > 2)
                {
                    string modName = factorioPath.Substring(2, closeIndex - 2);
                    string relativePath = factorioPath.Substring(closeIndex + 2).TrimStart('/', '\\');

                    if (modName == "base")
                    {
                        return Path.GetFullPath(Path.Combine(factorioDataPath, "base", relativePath));
                    }

                    var mod = mods.FirstOrDefault(m => m.Name == modName);
                    if (mod != null)
                    {
                        return Path.GetFullPath(Path.Combine(mod.PhysicalPath, relativePath));
                    }

                    return Path.GetFullPath(Path.Combine(factorioDataPath, modName, relativePath));
                }
            }

            return Path.GetFullPath(Path.Combine(factorioDataPath, factorioPath.TrimStart('/', '\\')));
        }
    }
}