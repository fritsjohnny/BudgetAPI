using System.Globalization;

namespace BudgetAPI.Helpers
{
    public static class ReferenceDateHelper
    {
        public static DateTime GetProportionalDate(
            DateTime sourceDate,
            string sourceReference,
            string targetReference,
            int? fixedDay = null)
        {
            DateTime sourceReferenceDate = ParseReference(sourceReference);
            DateTime targetReferenceDate = ParseReference(targetReference);

            int monthOffset =
                ((sourceDate.Year - sourceReferenceDate.Year) * 12) +
                sourceDate.Month -
                sourceReferenceDate.Month;

            DateTime targetMonth = targetReferenceDate.AddMonths(monthOffset);

            int requestedDay = fixedDay ?? sourceDate.Day;
            int lastDayOfMonth = DateTime.DaysInMonth(
                targetMonth.Year,
                targetMonth.Month);

            int day = Math.Min(
                Math.Max(requestedDay, 1),
                lastDayOfMonth);

            return new DateTime(
                targetMonth.Year,
                targetMonth.Month,
                day);
        }

        public static DateTime? GetProportionalDate(
            DateTime? sourceDate,
            string sourceReference,
            string targetReference,
            int? fixedDay = null)
        {
            if (!sourceDate.HasValue)
            {
                return null;
            }

            return GetProportionalDate(
                sourceDate.Value,
                sourceReference,
                targetReference,
                fixedDay);
        }

        private static DateTime ParseReference(string reference)
        {
            if (!DateTime.TryParseExact(
                    reference,
                    "yyyyMM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime referenceDate))
            {
                throw new ArgumentException(
                    $"Referência inválida: '{reference}'. O formato esperado é yyyyMM.");
            }

            return referenceDate;
        }
    }
}