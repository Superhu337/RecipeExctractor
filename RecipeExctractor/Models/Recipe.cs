namespace RecipeExtractor.Models
{
    public class Recipe
    {
        public string Name { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;
        public string Category { get; set; } = "crafting";
        public double EnergyRequired { get; set; } = 0.5;
        public List<Ingredient> Ingredients { get; set; } = new();
        public List<Result> Results { get; set; } = new();

        public string? MainProduct { get; set; }
        public string RecipeFormat { get; set; } = "unknown";

        // Игровой путь иконки рецепта (если есть)
        public string? RecipeIconPath { get; set; }

        // Игровой путь иконки главного продукта
        public string? ProductIconPath { get; set; }

        // Физический путь на диске
        public string? PhysicalIconPath { get; set; }

        // Путь к скопированной иконке
        public string? CopiedIconPath { get; set; }

        public string IconFileName { get; set; } = string.Empty;
        public string IconSize { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    public class Ingredient
    {
        public string Name { get; set; } = string.Empty;
        public int Amount { get; set; } = 1;
        public string Type { get; set; } = "item";
    }

    public class Result
    {
        public string Name { get; set; } = string.Empty;
        public int Amount { get; set; } = 1;
        public double Probability { get; set; } = 1.0;
        public string Type { get; set; } = "item";
    }
}