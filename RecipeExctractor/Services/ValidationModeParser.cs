using RecipeExtractor.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RecipeExtractor.Services
{
    public static class ValidationModeParser
    {
        public static HashSet<ValidationSeverity>? ParseInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            input = input.Trim().ToLowerInvariant();
            var separators = new[] { ',', ' ', ';', '-', '\t' };
            var parts = input.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            var result = new HashSet<ValidationSeverity>();

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed == "0") return null;

                if (int.TryParse(trimmed, out int mode))
                {
                    switch (mode)
                    {
                        case 1: result.Add(ValidationSeverity.Warning); break;
                        case 2: result.Add(ValidationSeverity.Error); break;
                        case 3: result.Add(ValidationSeverity.Info); break;
                        default:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"  ⚠ Неизвестный режим '{mode}'. Допустимые: 0, 1, 2, 3");
                            Console.ResetColor();
                            break;
                    }
                }
            }
            return result.Count > 0 ? result : null;
        }

        public static void PrintMenu()
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════╗");
            Console.WriteLine("║         РЕЖИМЫ САМОПРОВЕРКИ ДАННЫХ                 ║");
            Console.WriteLine("╠════════════════════════════════════════════════════╣");
            Console.WriteLine("║  0 - ВСЕ проверки (по умолчанию)                   ║");
            Console.WriteLine("║  1 - Только ПРЕДУПРЕЖДЕНИЯ (Warning)               ║");
            Console.WriteLine("║  2 - Только ОШИБКИ (Error)                         ║");
            Console.WriteLine("║  3 - Только ИНФОРМАЦИОННЫЕ (Info)                  ║");
            Console.WriteLine("╠════════════════════════════════════════════════════╣");
            Console.WriteLine("║  Можно комбинировать: 1,2  или  1 2 3              ║");
            Console.WriteLine("╚════════════════════════════════════════════════════╝");
        }

        public static HashSet<ValidationSeverity>? GetModeFromUser()
        {
            PrintMenu();
            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("\nВведите режим проверки [0]: ");
                Console.ResetColor();

                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("  → Выбран режим: ВСЕ проверки");
                    return null;
                }

                var result = ParseInput(input);
                if (result == null)
                {
                    Console.WriteLine("  → Выбран режим: ВСЕ проверки");
                    return null;
                }

                var modeNames = result.Select(s => s.ToString()).ToList();
                Console.WriteLine($"  → Выбраны режимы: [{string.Join(", ", modeNames)}]");
                return result;
            }
        }
    }
}