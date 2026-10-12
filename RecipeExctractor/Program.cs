namespace RecipeExtractor
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════╗");
            Console.WriteLine("║   RecipeExtractor - Factorio Data Tool       ║");
            Console.WriteLine("╚══════════════════════════════════════════════╝");
            Console.WriteLine();

            try
            {
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

                // Проверка существования пути к Factorio
                if (!Directory.Exists(factorioPath))
                {
                    Console.WriteLine($"✗ Путь к Factorio не найден: {factorioPath}");
                    Console.WriteLine("Использование: RecipeExtractor [путь_к_factorio] [путь_вывода] [путь_к_модам]");
                    Console.WriteLine("\nПример:");
                    Console.WriteLine("  RecipeExtractor \"C:\\Program Files\\Factorio\" \"C:\\Output\"");
                    return; // finally всё равно выполнится и попросит Enter
                }

                Console.WriteLine($"Путь к Factorio: {factorioPath}");
                Console.WriteLine($"Путь для сохранения: {outputPath}");
                Console.WriteLine($"Путь к модам: {modsPath}");
                Console.WriteLine();

                var app = new RecipeExtractorApp(factorioPath, outputPath, modsPath);
                await app.RunAsync();
            }
            catch (Exception ex)
            {
                // Необработанное исключение — выводим и даём пользователю прочитать
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n✗ Критическая ошибка: {ex.Message}");
                Console.WriteLine($"Стек: {ex.StackTrace}");
                Console.ResetColor();
            }
            finally
            {
                // === ГАРАНТИРОВАННАЯ ПАУЗА В КОНЦЕ ===
                Console.WriteLine();
                Console.WriteLine("──────────────────────────────────────────────────");
                Console.WriteLine("Нажмите ENTER для выхода...");
                Console.WriteLine("──────────────────────────────────────────────────");
                Console.ResetColor();
                Console.ReadLine();
            }
        }

        private static string GetDefaultFactorioPath()
        {
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