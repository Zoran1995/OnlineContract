
using System.Text.RegularExpressions;

namespace OnlineContract.Helpers
{
    public static class PhoneHelper
    {
        // Returns (ok, normalized, errorMessage)
        public static (bool ok, string? normalized, string? error) NormalizeSerbianPhone(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (false, null, "Phone number is required. Please provide a phone number.");

            var raw = input.Trim();

            // Reject extensions (look for 'ext' or similar patterns)
            if (Regex.IsMatch(raw, @"\b(ext|extension|x)\b", RegexOptions.IgnoreCase))
                return (false, null, "Please omit extension text; store it separately.");

            // Allowed characters: digits, +, spaces, dashes, dots, slashes, parentheses
            if (!Regex.IsMatch(raw, @"^[0-9+()\-./\s]*$"))
                return (false, null, "Phone number must contain digits only, plus sign, or separators.");

            // Remove separators (spaces, dashes, dots, slashes, parentheses)
            var compact = Regex.Replace(raw, @"[\s\-./()]+", "");

            string normalized;

            if (compact.StartsWith("+381"))
            {
                var rest = compact.Substring(4);
                if (!Regex.IsMatch(rest, @"^[0-9]+$"))
                    return (false, null, "Phone number must contain digits only, plus sign, or separators.");
                normalized = "+381" + rest;
            }
            else if (compact.StartsWith("00381"))
            {
                var rest = compact.Substring(5);
                if (!Regex.IsMatch(rest, @"^[0-9]+$"))
                    return (false, null, "Phone number must contain digits only, plus sign, or separators.");
                normalized = "+381" + rest;
            }
            else if (compact.StartsWith("381"))
            {
                var rest = compact.Substring(3);
                if (!Regex.IsMatch(rest, @"^[0-9]+$"))
                    return (false, null, "Phone number must contain digits only, plus sign, or separators.");
                normalized = "+381" + rest;
            }
            else if (compact.StartsWith("0"))
            {
                var rest = compact.Substring(1);
                if (!Regex.IsMatch(rest, @"^[0-9]+$"))
                    return (false, null, "Phone number must contain digits only, plus sign, or separators.");
                normalized = "+381" + rest;
            }
            else if (Regex.IsMatch(compact, @"^[0-9]+$") && (compact.StartsWith("6") || compact.StartsWith("1")))
            {
                // Conservative: assume missing trunk 0 -> +381 + compact
                normalized = "+381" + compact;
            }
            else
            {
                return (false, null, "Phone number is not a valid Serbian format.");
            }

            // Final length check: +381 followed by 7-11 digits
            if (!Regex.IsMatch(normalized, @"^\+381[0-9]{7,11}$"))
                return (false, null, "Phone number length is not valid for Serbia.");

            return (true, normalized, null);
        }
    }
}
