using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using Microsoft.AspNetCore.Authentication.Cookies;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography.X509Certificates;
using LendMe.Idp.Infrastructure.Persistance.Context;
using LendMe.Idp.Infrastructure.Persistance.Entity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Configure Entity Framework
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // Use in-memory database for demo purposes
    options.UseInMemoryDatabase("IdentityProviderDb");
    
    // Register the OpenIddict entity framework stores
    options.UseOpenIddict();
});

// Configure Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;

        // Configure lockout settings
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Configure OpenIddict 6.x
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<ApplicationDbContext>();
        
        // Enable Quartz.NET integration for scheduled tasks (token pruning, etc.)
        // options.UseQuartz();
    })
    .AddServer(options =>
    {
        // Enable only the essential endpoints for OAuth 2.1
        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token")
               .SetIntrospectionEndpointUris("connect/introspect")
               .SetRevocationEndpointUris("connect/revoke");
               // Удалены в OpenIddict 6.0:
               // .SetLogoutEndpointUris("connect/logout")
               // .SetUserinfoEndpointUris("connect/userinfo") 
               // .SetVerificationEndpointUris("connect/verify")

        // Enable the flows (OAuth 2.1 compliant)
        options.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange() // Обязательно в OAuth 2.1
               .AllowClientCredentialsFlow()
               .AllowRefreshTokenFlow();

        // Register the signing and encryption credentials
        if (builder.Environment.IsDevelopment())
        {
            options.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();
        }
        else
        {
            // In production, use real certificates
            // options.AddSigningCertificate(LoadCertificate("signing.pfx", "password"));
            // options.AddEncryptionCertificate(LoadCertificate("encryption.pfx", "password"));
        }

        // Configure token formats
        // JWT is the default format for access tokens in OpenIddict 6.x
        // Optional: disable encryption for easier debugging in development
        if (builder.Environment.IsDevelopment())
        {
            options.DisableAccessTokenEncryption();
        }

        // Register scopes
        options.RegisterScopes(
            Scopes.OpenId,
            Scopes.Email,
            Scopes.Profile,
            Scopes.Phone,
            Scopes.Address,
            Scopes.Roles,
            Scopes.OfflineAccess,
            "api",
            "api:read",
            "api:write"
        );

        // Register claims
        options.RegisterClaims(
            Claims.Name,
            Claims.Subject,
            Claims.Email,
            Claims.EmailVerified,
            Claims.GivenName,
            Claims.FamilyName,
            Claims.MiddleName,
            Claims.Nickname,
            Claims.PreferredUsername,
            Claims.Profile,
            Claims.Picture,
            Claims.Website,
            Claims.Gender,
            Claims.Birthdate,
            Claims.Zoneinfo,
            Claims.Locale,
            Claims.UpdatedAt,
            Claims.Role
        );

        // Register the ASP.NET Core host and configure options
        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableStatusCodePagesIntegration();
               // Удалены в OpenIddict 6.0:
               // .EnableLogoutEndpointPassthrough()
               // .EnableUserinfoEndpointPassthrough()
               // .EnableVerificationEndpointPassthrough()
               
        // Configure HTTPS requirement
        if (builder.Environment.IsDevelopment())
        {
            // В разработке можно отключить требование HTTPS
            // options.DisableTransportSecurityRequirement();
        }

        // Configure token lifetimes (OAuth 2.1 рекомендации)
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
               .SetIdentityTokenLifetime(TimeSpan.FromMinutes(15))
               .SetRefreshTokenLifetime(TimeSpan.FromDays(7))
               .SetAuthorizationCodeLifetime(TimeSpan.FromMinutes(5));
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
        
        // Configure audiences
        options.AddAudiences("resource-server-1", "resource-server-2");
        
        // Enable authorization entry validation
        options.EnableAuthorizationEntryValidation();
    });

// Configure authentication (OAuth 2.1 compliant)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;
    
    // Настройки cookie для .NET 9
    options.Cookie.Name = "AuthCookie";
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
    options.Cookie.HttpOnly = true; // Защита от XSS
    options.Cookie.IsEssential = true; // Обход GDPR для essential cookies
    
    // Дополнительные настройки безопасности
    if (builder.Environment.IsProduction())
    {
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
    }

})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "your-google-client-id";
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "your-google-client-secret";
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.SaveTokens = true;
    
    options.Events.OnCreatingTicket = async context =>
    {
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        await HandleExternalLogin(context, userManager, "Google");
    };
})
.AddFacebook(options =>
{
    options.AppId = builder.Configuration["Authentication:Facebook:AppId"] ?? "your-facebook-app-id";
    options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"] ?? "your-facebook-app-secret";
    options.Scope.Add("email");
    options.Scope.Add("public_profile");
    options.SaveTokens = true;
    
    options.Fields.Add("name");
    options.Fields.Add("email");
    options.Fields.Add("first_name");
    options.Fields.Add("last_name");
    
    options.Events.OnCreatingTicket = async context =>
    {
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        await HandleExternalLogin(context, userManager, "Facebook");
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "api");
    });
    
    options.AddPolicy("ReadPolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "api:read");
    });
    
    options.AddPolicy("WritePolicy", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "api:write");
    });
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins("https://localhost:5001", "https://localhost:7001")
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials()
               .SetIsOriginAllowedToAllowWildcardSubdomains();
    });
});

// Add hosted services for background tasks
builder.Services.AddHostedService<TestDataSeeder>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowSpecificOrigins");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();

// Helper method to handle external login
static async Task HandleExternalLogin(Microsoft.AspNetCore.Authentication.OAuth.OAuthCreatingTicketContext context, 
    UserManager<ApplicationUser> userManager, string provider)
{
    var email = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
    var externalId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    
    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(externalId))
        return;

    var user = await userManager.FindByEmailAsync(email);
    if (user == null)
    {
        var firstName = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value;
        var lastName = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value;
        
        if (provider == "Facebook")
        {
            firstName = firstName ?? context.Principal?.FindFirst("first_name")?.Value;
            lastName = lastName ?? context.Principal?.FindFirst("last_name")?.Value;
        }
        
        if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
        {
            var fullName = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            if (!string.IsNullOrEmpty(fullName))
            {
                var nameParts = fullName.Split(' ', 2);
                firstName = nameParts[0];
                lastName = nameParts.Length > 1 ? nameParts[1] : "";
            }
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName ?? "User",
            LastName = lastName ?? provider,
            CreatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "User");
            await userManager.AddLoginAsync(user, new UserLoginInfo(provider, externalId, provider));
        }
    }
}

// Helper method to load certificates
static X509Certificate2 LoadCertificate(string path, string password)
{
    return new X509Certificate2(path, password, X509KeyStorageFlags.MachineKeySet);
}