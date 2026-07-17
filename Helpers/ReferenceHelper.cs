using System.Globalization;

namespace BudgetAPI.Helpers
{
    public static class ReferenceHelper
    {
        public static bool TryParse(string? reference, out int year, out int month)
        {
            year = 0;
            month = 0;

            if (reference is null || reference.Length != 6 ||
                !reference.All(character => character is >= '0' and <= '9') ||
                !int.TryParse(reference.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
                !int.TryParse(reference.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month) ||
                year < DateTime.MinValue.Year || year > DateTime.MaxValue.Year ||
                month < 1 || month > 12)
            {
                year = 0;
                month = 0;
                return false;
            }

            return true;
        }

        public static DateTime GetReferenceMonth(string reference)
        {
            if (!TryParse(reference, out int year, out int month))
                throw new ArgumentException("Referência inválida. Informe a referência no formato yyyyMM.", nameof(reference));

            return new DateTime(year, month, 1);
        }

        public static string FormatReference(string reference)
        {
            DateTime referenceMonth = GetReferenceMonth(reference);
            return referenceMonth.ToString("MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
