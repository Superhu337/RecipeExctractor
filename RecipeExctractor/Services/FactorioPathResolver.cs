namespace RecipeExtractor.Services
{
    public class FactorioPathResolver
    {
        private readonly string _factorioPath;
        private readonly string _modsPath;

        public FactorioPathResolver(string factorioPath, string modsPath)
        {
            _factorioPath = factorioPath;
            _modsPath = modsPath;
        }

        /// <summary>
        /// Преобразует игровой путь (__base__/..., __core__/..., __mod__/...) в физический путь на диске.
        /// </summary>
        public string? ResolveToPhysicalPath(string? gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
                return null;

            gamePath = gamePath.Trim();

            // __base__/graphics/icons/... → data/base/graphics/icons/...
            if (gamePath.StartsWith("__base__/"))
            {
                var relative = gamePath.Substring("__base__/".Length)
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                return Path.Combine(_factorioPath, "data", "base", relative);
            }

            // __core__/graphics/icons/... → data/core/graphics/icons/...
            if (gamePath.StartsWith("__core__/"))
            {
                var relative = gamePath.Substring("__core__/".Length)
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                return Path.Combine(_factorioPath, "data", "core", relative);
            }

            // __modname__/... → mods/modname/...
            // Ищем второе вхождение "__" начиная с позиции 2
            if (gamePath.StartsWith("__"))
            {
                var endPrefix = gamePath.IndexOf("__", 2, StringComparison.Ordinal);
                if (endPrefix > 0)
                {
                    var modName = gamePath.Substring(2, endPrefix - 2);
                    var rest = gamePath.Substring(endPrefix + 2)
                        .Replace('/', Path.DirectorySeparatorChar)
                        .Replace('\\', Path.DirectorySeparatorChar);
                    return Path.Combine(_modsPath, modName, rest);
                }
            }

            // Если путь уже начинается с data/ или абсолютный
            if (gamePath.StartsWith("data/") || gamePath.StartsWith("data\\"))
            {
                var normalized = gamePath
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);
                return Path.Combine(_factorioPath, normalized);
            }

            if (Path.IsPathRooted(gamePath))
            {
                return gamePath;
            }

            return null;
        }

        public bool FileExists(string? physicalPath)
        {
            return !string.IsNullOrEmpty(physicalPath) && File.Exists(physicalPath);
        }
    }
}