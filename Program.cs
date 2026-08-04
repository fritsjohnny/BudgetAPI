using BudgetAPI.Authorization;
using BudgetAPI.Data;
using BudgetAPI.Helpers;
using BudgetAPI.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

string connectionName = builder.Environment.IsDevelopment() ? "LocalConnection" : "AzureConnection";

string connectionString = builder.Configuration.GetConnectionString(connectionName) ??
    throw new InvalidOperationException($"A connection string '{connectionName}' não foi configurada.");

builder.Services.AddDbContext<BudgetContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(config =>
{
    config.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "BudgetAPI",
        Version = "v1"
    });
});
builder.Services.AddCors();
// Configure strongly typed settings object
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));
// Configure DI for application services
builder.Services.AddScoped<IJwtUtils, JwtUtils>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IAccountPostingService, AccountPostingService>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ICardService, CardService>();
builder.Services.AddScoped<ICardPostingService, CardPostingService>();
builder.Services.AddScoped<ICardReceiptService, CardReceiptService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IIncomeService, IncomeService>();
builder.Services.AddScoped<IPeopleService, PeopleService>();
builder.Services.AddScoped<IAccountApplicationService, AccountApplicationService>();
builder.Services.AddScoped<IAccountYieldRangeService, AccountYieldRangeService>();
builder.Services.AddScoped<IInvestmentStrategyService, InvestmentStrategyService>();
builder.Services.AddScoped<IAnnualSavingsService, AnnualSavingsService>();
builder.Services.AddScoped<IFinancialHealthService, FinancialHealthService>();
builder.Services.AddScoped<ICardsInvoiceClosingService, CardsInvoiceClosingService>();
builder.Services.AddHttpContextAccessor();

if (!builder.Environment.IsDevelopment())
{
    // Serviço para manter a API acordada
    builder.Services.AddHostedService<KeepAliveService>();
    // Configuração do Firebase para notificações push
    builder.Services.AddSingleton<FirebaseNotificationService>();
    // Configuração do serviço de notificações diárias
    builder.Services.AddScoped<INotificationJobService, NotificationJobService>();
    builder.Services.AddHostedService<DailyNotificationHostedService>();
}

var app = builder.Build();

app.UseCors(options => options//.WithOrigins("http://localhost:4200")
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowAnyOrigin()
                       );

// Swagger middleware (liga só em dev)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// global error handler
app.UseMiddleware<ErrorHandlerMiddleware>();

// custom jwt auth middleware
app.UseMiddleware<JwtMiddleware>();

app.MapControllers();

app.Run();
