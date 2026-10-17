using appErasmus.Data;
using appErasmus.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Shared;
using System.Security.Principal;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<Utilizador>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Serviço de autenticação da Microsoft (Configurado para Single-Tenant)
builder.Services.AddAuthentication()
    .AddMicrosoftAccount(microsoftOptions =>
    {
        // Vai buscar o ID do teu Tenant aos Secrets
        var tenantId = builder.Configuration["Authentication:Microsoft:TenantId"];

        microsoftOptions.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
        microsoftOptions.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;

        // Forçamos o código a usar o teu Tenant específico em vez da porta "/common"
        microsoftOptions.AuthorizationEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize";
        microsoftOptions.TokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

        // Forçar a Microsoft a perguntar qual é a conta
        microsoftOptions.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            // Adiciona o parâmetro prompt=select_account ao URL da Microsoft
            context.Response.Redirect(context.RedirectUri + "&prompt=select_account");
            return Task.CompletedTask;
        };

    });
// Registar o serviço de emails
builder.Services.AddScoped<appErasmus.Services.IServicoEmail, appErasmus.Services.ServicoEmail>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

//// --- INÍCIO DA INJEÇÃO DE DADOS AUTOMÁTICA ---
//using (var scope = app.Services.CreateScope())
//{
//    var services = scope.ServiceProvider;
//    var context = services.GetRequiredService<appErasmus.Data.ApplicationDbContext>();

//    // Sem try-catch temporariamente! Queremos ver o erro escarrapachado no ecrã.
//    appErasmus.Data.DbInitializer.Initialize(context);

//    // Vai semear as Roles e dar os "super poderes"
//    await appErasmus.Data.DbInitializer.SeedRolesAndSuperUserAsync(services);
//}
//// --- FIM DA INJEÇÃO DE DADOS ---

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.Use(async (context, next) =>
{
    // Use PathString.StartsWithSegments for segment-safe, case-insensitive matching
    var blockedPathStrings = new[]
    {
        "/Identity/Account/Register",
        "/Identity/Account/LoginWith2fa",
        "/Identity/Account/LoginWithRecoveryCode",
        "/Identity/Account/RegisterConfirmation",
        "/Identity/Account/ForgotPassword",
        "/Identity/Account/ForgotPasswordConfirmation",
        "/Identity/Account/ResetPassword",
        "/Identity/Account/ResetPasswordConfirmation",
        "/Identity/Account/ConfirmEmail",
        "/Identity/Account/ConfirmEmailChange",
        "/Identity/Account/ResendEmailConfirmation",
        "/Identity/Account/Lockout",
        "/Identity/Account/AccessDenied",

        
        "/Identity/Account/Manage/Index",
        "/Identity/Account/Manage/Email",
        "/Identity/Account/Manage/ChangePassword",
        "/Identity/Account/Manage/SetPassword",
        "/Identity/Account/Manage/PersonalData",
        "/Identity/Account/Manage/DownloadPersonalData",
        "/Identity/Account/Manage/DeletePersonalData",
        "/Identity/Account/Manage/ExternalLogins",
        "/Identity/Account/Manage/TwoFactorAuthentication",
        "/Identity/Account/Manage/EnableAuthenticator",
        "/Identity/Account/Manage/Disable2fa",
        "/Identity/Account/Manage/GenerateRecoveryCodes",
        "/Identity/Account/Manage/ResetAuthenticator",
        "/Identity/Account/Manage/ShowRecoveryCodes"
    };

    var blockedPaths = blockedPathStrings.Select(p => new PathString(p)).ToArray();

    if (context.Request.Path.HasValue && blockedPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
    {
        context.Response.Redirect("/Home/AcessoRestrito"); 
        return;
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages()
   .WithStaticAssets();

app.Run();
