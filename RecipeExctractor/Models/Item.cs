namespace RecipeExtractor.Models
{
    public class Item
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string LocalizedName { get; set; } = string.Empty;
        public int StackSize { get; set; } = 1;
        public string IconPath { get; set; } = string.Empty;
        public string IconSize { get; set; } = string.Empty;
        public string Subgroup { get; set; } = string.Empty;
        public string Order { get; set; } = string.Empty;
        public Dictionary<string, object> Properties { get; set; } = new();
    }
}