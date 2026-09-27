using System.Globalization;
using System.Text.RegularExpressions;

namespace Safehouse.Core
{
    /// <summary>
    /// Field checks shared by every type that can come from a data file or a save. Each one either
    /// returns the accepted value or throws, so a caller never has to test the result.
    /// </summary>
    public static class Validate
    {
        // Item ids are derived from names and must stay stable, or a stashed item stops resolving.
        private static readonly Regex IdPattern =
            new Regex("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.Compiled);

        // Instance ids are bare UUID4 hex, the same shape Python's uuid4().hex produces.
        private static readonly Regex InstanceIdPattern =
            new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled);

        public static int Integer(int value, string label, int low = 0, int high = CoreLimits.MaxMoney)
        {
            if (value < low || value > high)
            {
                throw new ValidationException($"{label} must be an integer between {low} and {high}.");
            }

            return value;
        }

        public static string Text(string value, string label, int maxLength = 200, bool allowEmpty = false)
        {
            if (value == null || value.Length > maxLength)
            {
                throw new ValidationException($"{label} must be text, up to {maxLength} characters.");
            }

            if (!allowEmpty && value.Trim().Length == 0)
            {
                throw new ValidationException($"{label} cannot be empty.");
            }

            foreach (var character in value)
            {
                // Control characters survive a round trip through JSON but break every renderer.
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Control)
                {
                    throw new ValidationException($"{label} cannot contain control characters.");
                }
            }

            return value;
        }

        public static string Identifier(string value, string label, bool instance = false)
        {
            var pattern = instance ? InstanceIdPattern : IdPattern;
            if (value == null || !pattern.IsMatch(value))
            {
                throw new ValidationException($"Invalid {label}: '{value}'.");
            }

            return value;
        }

        public static double Weight(double value, string label)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)
                || value < 0 || value > CoreLimits.MaxItemWeight)
            {
                throw new ValidationException($"{label} must be a finite, non-negative number.");
            }

            return value;
        }

        public static double Double(double value, string label, double low = 0.0, double high = CoreLimits.MaxMoney)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)
                || value < low || value > high)
            {
                throw new ValidationException($"{label} must be a number between {low} and {high}.");
            }

            return value;
        }
    }
}
