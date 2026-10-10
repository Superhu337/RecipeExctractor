using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RecipeExtractor.Models
{
    public enum ValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum ValidationCategory
    {
        Item,
        Recipe,
        CrossReference,
        Icon
    }

    public class ValidationIssue
    {
        public ValidationSeverity Severity { get; set; }
        public ValidationCategory Category { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;

        public override string ToString()
        {
            var severityIcon = Severity switch
            {
                ValidationSeverity.Error => "✗",
                ValidationSeverity.Warning => "⚠",
                ValidationSeverity.Info => "ℹ",
                _ => "?"
            };
            return $"[{Severity}] {severityIcon} [{Category}] {EntityName}: {Message}" +
                   (string.IsNullOrEmpty(Details) ? "" : $" ({Details})");
        }
    }

    public class ValidationResult
    {
        public List<ValidationIssue> Issues { get; set; } = new();
        public int TotalItems { get; set; }
        public int TotalRecipes { get; set; }
        public DateTime ValidationTime { get; set; } = DateTime.Now;

        public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);
        public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
        public int InfoCount => Issues.Count(i => i.Severity == ValidationSeverity.Info);

        public bool HasErrors => ErrorCount > 0;

        public void AddIssue(ValidationSeverity severity, ValidationCategory category,
            string entityName, string entityType, string field, string message, string details = "")
        {
            Issues.Add(new ValidationIssue
            {
                Severity = severity,
                Category = category,
                EntityName = entityName,
                EntityType = entityType,
                Field = field,
                Message = message,
                Details = details
            });
        }
    }
}
