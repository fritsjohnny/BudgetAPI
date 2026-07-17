namespace BudgetAPI.Helpers
{
    public static class BrazilDateTimeHelper
    {
        public static DateTime GetCurrentDate()
        {
            TimeZoneInfo timeZone;
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            }
            catch (TimeZoneNotFoundException)
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
            }

            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date;
        }
    }
}
