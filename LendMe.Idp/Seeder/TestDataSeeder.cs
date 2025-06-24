// Background service for seeding test data

using LendMe.Idp.Infrastructure.Persistance.Context;
using LendMe.Idp.Infrastructure.Persistance.Entity;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

// Background service for seeding test data
public class TestDataSeeder : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public TestDataSeeder(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        await CreateApplicationsAsync(scope.ServiceProvider, cancellationToken);
        await CreateScopesAsync(scope.ServiceProvider, cancellationToken);
        await CreateUsersAsync(scope.ServiceProvider, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreateApplicationsAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var manager = provider.GetRequiredService<IOpenIddictApplicationManager>();

        // Create a test SPA client (with PKCE)
        if (await manager.FindByClientIdAsync("spa-client", cancellationToken) == null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "spa-client",
                ClientType = OpenIddictConstants.ClientTypes.Public,
                DisplayName = "SPA Client Application",
                RedirectUris = { 
                    new Uri("https://localhost:7255/callback"),  // Обновленный порт
                    new Uri("https://localhost:5001/callback")
                },
                PostLogoutRedirectUris = { 
                    new Uri("https://localhost:7255/"),
                    new Uri("https://localhost:5001/") 
                },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    //OpenIddictConstants.Permissions.Endpoints.Logout,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Revocation,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    //OpenIddictConstants.Permissions.Scopes.OpenId,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    //OpenIddictConstants.Permissions.Scopes.OfflineAccess,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "api"
                },
                Requirements =
                {
                    OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
                }
            }, cancellationToken);
        }

        // Create a test confidential client
        if (await manager.FindByClientIdAsync("mvc-client", cancellationToken) == null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "mvc-client",
                ClientSecret = "mvc-client-secret",
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                DisplayName = "MVC Client Application",
                RedirectUris = { new Uri("https://localhost:5002/signin-oidc") },
                PostLogoutRedirectUris = { new Uri("https://localhost:5002/signout-callback-oidc") },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    //OpenIddictConstants.Permissions.Endpoints.Logout,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Introspection,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    //OpenIddictConstants.Permissions.Scopes.OpenId,
                    OpenIddictConstants.Permissions.Scopes.Email,
                    OpenIddictConstants.Permissions.Scopes.Profile,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    //OpenIddictConstants.Permissions.Scopes.OfflineAccess,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "api"
                }
            }, cancellationToken);
        }

        // Create a machine-to-machine client
        if (await manager.FindByClientIdAsync("m2m-client", cancellationToken) == null)
        {
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "m2m-client",
                ClientSecret = "m2m-client-secret",
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
                DisplayName = "Machine-to-Machine Client",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Introspection,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "api",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "api:read",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "api:write"
                }
            }, cancellationToken);
        }
    }

    private async Task CreateScopesAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var manager = provider.GetRequiredService<IOpenIddictScopeManager>();

        // Create API scopes
        if (await manager.FindByNameAsync("api", cancellationToken) == null)
        {
            await manager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "api",
                DisplayName = "API Access",
                Description = "Allows access to the API",
                Resources = { "resource-server-1" }
            }, cancellationToken);
        }

        if (await manager.FindByNameAsync("api:read", cancellationToken) == null)
        {
            await manager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "api:read",
                DisplayName = "API Read Access",
                Description = "Allows read access to the API",
                Resources = { "resource-server-1", "resource-server-2" }
            }, cancellationToken);
        }

        if (await manager.FindByNameAsync("api:write", cancellationToken) == null)
        {
            await manager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = "api:write",
                DisplayName = "API Write Access",
                Description = "Allows write access to the API",
                Resources = { "resource-server-1" }
            }, cancellationToken);
        }
    }

    private async Task CreateUsersAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();

        // Create roles
        string[] roles = { "Admin", "User", "Manager" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Create test users
        var testUsers = new[]
        {
            new { Email = "admin@example.com", Password = "Admin123!", FirstName = "Admin", LastName = "User", Roles = new[] { "Admin", "User" } },
            new { Email = "user@example.com", Password = "User123!", FirstName = "Test", LastName = "User", Roles = new[] { "User" } },
            new { Email = "manager@example.com", Password = "Manager123!", FirstName = "Manager", LastName = "User", Roles = new[] { "Manager", "User" } }
        };

        foreach (var testUser in testUsers)
        {
            if (await userManager.FindByEmailAsync(testUser.Email) == null)
            {
                var user = new ApplicationUser
                {
                    UserName = testUser.Email,
                    Email = testUser.Email,
                    EmailConfirmed = true,
                    FirstName = testUser.FirstName,
                    LastName = testUser.LastName,
                    PhoneNumber = "+1234567890",
                    PhoneNumberConfirmed = true
                };

                var result = await userManager.CreateAsync(user, testUser.Password);
                if (result.Succeeded)
                {
                    await userManager.AddToRolesAsync(user, testUser.Roles);
                }
            }
        }
    }
}