using System.Collections.Generic;
using System.Text;

namespace GameFramework.UI.Editor
{
    internal static class CSharpIdentifierUtility
    {
        private static readonly HashSet<string> Keywords = new()
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
            "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
            "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };

        public static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Keywords.Contains(value)) return false;
            if (value[0] != '_' && !char.IsLetter(value[0])) return false;
            for (int i = 1; i < value.Length; i++)
            {
                if (value[i] != '_' && !char.IsLetterOrDigit(value[i])) return false;
            }

            return true;
        }

        public static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "uiElement";

            StringBuilder builder = new(value.Length + 1);
            bool capitalizeNext = false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character != '_' && !char.IsLetterOrDigit(character))
                {
                    capitalizeNext = builder.Length > 0;
                    continue;
                }

                if (builder.Length == 0 && char.IsDigit(character)) builder.Append('_');
                if (capitalizeNext && char.IsLetter(character)) character = char.ToUpperInvariant(character);
                builder.Append(character);
                capitalizeNext = false;
            }

            if (builder.Length == 0) return "uiElement";
            int firstLetterIndex = builder[0] == '_' && builder.Length > 1 ? 1 : 0;
            if (char.IsLetter(builder[firstLetterIndex])) builder[firstLetterIndex] = char.ToLowerInvariant(builder[firstLetterIndex]);
            string result = builder.ToString();
            return Keywords.Contains(result) ? $"_{result}" : result;
        }

        public static string ToPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            int firstLetterIndex = value[0] == '_' && value.Length > 1 ? 1 : 0;
            char[] characters = value.ToCharArray();
            characters[firstLetterIndex] = char.ToUpperInvariant(characters[firstLetterIndex]);
            return new string(characters);
        }
    }
}
