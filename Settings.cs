namespace BudgetAPI
{
    public static class Settings
    {
        public static string Secret => Environment.GetEnvironmentVariable("BUDGETAPI_SECRET") ?? string.Empty;
    }
}
