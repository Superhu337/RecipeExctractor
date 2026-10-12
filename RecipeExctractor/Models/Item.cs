namespace RecipeExtractor.Models
{
    public class Item
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;
        public int StackSize { get; set; } = 1;

        // Игровой путь (как в Factorio Lua, например "__base__/graphics/icons/iron-plate.png")
        public string IconPath { get; set; } = string.Empty;

        // Физический путь на диске (для копирования)
        public string? PhysicalIconPath { get; set; }

        // Путь к скопированной иконке в папке вывода
        public string? CopiedIconPath { get; set; }

        public string IconSize { get; set; } = string.Empty;
        public string Subgroup { get; set; } = string.Empty;
        public string Order { get; set; } = string.Empty;
        public Dictionary<string, object> Properties { get; set; } = new();
    }
}