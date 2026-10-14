using System.Text.RegularExpressions;

namespace RecipeExtractor.Services
{
    public class FactorioPathResolver
    {
        private readonly string _factorioPath;
        private readonly string _modsPath;
        private readonly Dictionary<string, string?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
        private bool _indexBuilt;
        private readonly Dictionary<string, List<string>> _iconIndex = new(StringComparer.OrdinalIgnoreCase);

        public FactorioPathResolver(string factorioPath, string modsPath)
        {
            _factorioPath = factorioPath;
            _modsPath = modsPath;
        }

        /// <summary>
        /// Строгая нормализация: НЕ добавляет подчёркивания, если они уже есть.
        /// </summary>
        public static string NormalizeGamePath(string? gamePath)
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

        public string? ResolveToPhysicalPath(string? gamePath)
        {
            gamePath = NormalizeGamePath(gamePath);
            if (string.IsNullOrEmpty(gamePath)) return null;

            if (gamePath.StartsWith("__base__/", StringComparison.Ordinal))
            {
                var rel = gamePath.Substring("__base__/".Length)
                    .Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(_factorioPath, "data", "base", rel);
            }

            if (gamePath.StartsWith("__core__/", StringComparison.Ordinal))
            {
                var rel = gamePath.Substring("__core__/".Length)
                    .Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(_factorioPath, "data", "core", rel);
            }

            // Модовые пути: __modname__/...
            if (gamePath.StartsWith("__") && gamePath.IndexOf("__", 2, StringComparison.Ordinal) > 0)
            {
                var end = gamePath.IndexOf("__", 2, StringComparison.Ordinal);
                var modName = gamePath.Substring(2, end - 2);
                var rest = gamePath.Substring(end + 2).Replace('/', Path.DirectorySeparatorChar);

                // Сначала ищем распакованный мод во временной папке
                var tempDir = Path.Combine(Path.GetTempPath(), "RecipeExtractor", "mods", modName);
                var tempPath = Path.Combine(tempDir, rest);
                if (File.Exists(tempPath)) return tempPath;

                // Потом в исходной папке модов (если распакован вручную)
                var srcPath = Path.Combine(_modsPath, modName, rest);
                if (File.Exists(srcPath)) return srcPath;

                // Возвращаем даже несуществующий путь — caller сам проверит File.Exists
                return srcPath;
            }

            if (gamePath.StartsWith("data/", StringComparison.OrdinalIgnoreCase) ||
                gamePath.StartsWith("data\\", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(_factorioPath, gamePath.Replace('/', Path.DirectorySeparatorChar));
            }

            if (Path.IsPathRooted(gamePath)) return gamePath;

            return null;
        }

        /// <summary>Fallback №1: поиск PNG по basename из игрового пути.</summary>
        public string? FindIconByFileName(string? gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath)) return null;

            var normalized = NormalizeGamePath(gamePath);
            var fileName = Path.GetFileName(normalized.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return null;

            BuildIconIndex();

            if (_iconIndex.TryGetValue(fileName, out var candidates))
            {
                var hit = candidates.FirstOrDefault(File.Exists);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Fallback №2: поиск PNG по имени предмета.</summary>
        public string? FindIconByName(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;

            if (_iconCache.TryGetValue(itemName, out var cached))
                return cached;

            BuildIconIndex();

            string? result = null;

            // Точное совпадение
            if (_iconIndex.TryGetValue(itemName + ".png", out var exact))
            {
                result = exact.FirstOrDefault(File.Exists);
            }

            // Нестрогое: имя файла содержит имя предмета
            if (result == null)
            {
                foreach (var kv in _iconIndex)
                {
                    if (kv.Key.Contains(itemName, StringComparison.OrdinalIgnoreCase))
                    {
                        var h = kv.Value.FirstOrDefault(File.Exists);
                        if (h != null) { result = h; break; }
                    }
                }
            }

            _iconCache[itemName] = result;
            return result;
        }

        /// <summary>Строит индекс всех PNG по basename один раз.</summary>
        public void BuildIconIndex()
        {
            if (_indexBuilt) return;
            _indexBuilt = true;
            _iconIndex.Clear();

            var roots = new List<string>
            {
                Path.Combine(_factorioPath, "data", "base", "graphics"),
                Path.Combine(_factorioPath, "data", "core", "graphics")
            };

            // Распакованные моды во временной папке
            var tempMods = Path.Combine(Path.GetTempPath(), "RecipeExtractor", "mods");
            if (Directory.Exists(tempMods))
            {
                foreach (var d in Directory.GetDirectories(tempMods))
                {
                    var gfx = Path.Combine(d, "graphics");
                    if (Directory.Exists(gfx)) roots.Add(gfx);
                }
            }

            // Исходная папка модов (распакованные вручную)
            if (Directory.Exists(_modsPath))
            {
                foreach (var d in Directory.GetDirectories(_modsPath))
                {
                    var gfx = Path.Combine(d, "graphics");
                    if (Directory.Exists(gfx)) roots.Add(gfx);
                }
            }

            int count = 0;
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var png in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
                    {
                        var bn = Path.GetFileName(png);
                        if (!_iconIndex.TryGetValue(bn, out var lst))
                            _iconIndex[bn] = lst = new List<string>();
                        lst.Add(png);
                        count++;
                    }
                }
                catch { /* пропускаем недоступные папки */ }
            }

            Console.WriteLine($"  [IconIndex] Проиндексировано PNG: {count} из {_iconIndex.Count} уникальных имён");
        }

        public bool FileExists(string? p) => !string.IsNullOrEmpty(p) && File.Exists(p);
    }
}