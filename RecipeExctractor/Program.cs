// Program.cs
namespace RecipeExtractor
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine("║   RecipeExtractor - Factorio Data Tool      ║");
            Console.WriteLine("╚══════════════════════════════════════════════╝");
            Console.WriteLine();

            // Настройка путей
            string factorioPath = GetDefaultFactorioPath();
            string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "FactorioData");
            string modsPath = GetDefaultModsPath();

            // Обработка аргументов командной строки
            if (args.Length >= 1 && !string.IsNullOrEmpty(args[0]))
                factorioPath = args[0];
            if (args.Length >= 2 && !string.IsNullOrEmpty(args[1]))
                outputPath = args[1];
            if (args.Length >= 3 && !string.IsNullOrEmpty(args[2]))
                modsPath = args[2];

            // Проверка путей
            if (!Directory.Exists(factorioPath))
            {
                Console.WriteLine($"✗ Путь к Factorio не найден: {factorioPath}");
                Console.WriteLine("Использование: RecipeExtractor [путь_к_factorio] [путь_вывода] [путь_к_модам]");
                Console.WriteLine("\nПример:");
                Console.WriteLine("  RecipeExtractor \"C:\\Program Files\\Factorio\" \"C:\\Output\"");
                return;
            }

            Console.WriteLine($"Путь к Factorio: {factorioPath}");
            Console.WriteLine($"Путь для сохранения: {outputPath}");
            Console.WriteLine($"Путь к модам: {modsPath}");
            Console.WriteLine();

            var app = new RecipeExtractorApp(factorioPath, outputPath, modsPath);
            await app.RunAsync();

            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }

        private static string GetDefaultFactorioPath()
        {
            // Попытка найти Factorio в стандартных местах
            var possiblePaths = new[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Factorio",
                @"C:\Program Files\Steam\steamapps\common\Factorio",
                @"C:\Games\Factorio",
                @"D:\Games\Factorio",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Factorio")
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                    return path;
            }

            return string.Empty;
        }

        private static string GetDefaultModsPath()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Factorio",
                "mods"
            );

            return Directory.Exists(appDataPath) ? appDataPath : string.Empty;
        }
    }
}