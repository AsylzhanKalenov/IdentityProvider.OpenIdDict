 using System.Collections.Immutable;
 using System.Security.Claims;
 using LendMe.Idp.Infrastructure.Persistance.Entity;
 using LendMe.Idp.Models;
 using Microsoft.AspNetCore;
 using Microsoft.AspNetCore.Authentication;
 using Microsoft.AspNetCore.Authentication.Cookies;
 using Microsoft.AspNetCore.Authorization;
 using Microsoft.AspNetCore.Identity;
 using Microsoft.AspNetCore.Mvc;
 using Microsoft.IdentityModel.Tokens;
 using OpenIddict.Abstractions;
 using OpenIddict.Server.AspNetCore;

 public class AuthorizationController : Controller
    {
        private readonly IOpenIddictApplicationManager _applicationManager;
        private readonly IOpenIddictAuthorizationManager _authorizationManager;
        private readonly IOpenIddictScopeManager _scopeManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AuthorizationController(
            IOpenIddictApplicationManager applicationManager,
            IOpenIddictAuthorizationManager authorizationManager,
            IOpenIddictScopeManager scopeManager,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager)
        {
            _applicationManager = applicationManager;
            _authorizationManager = authorizationManager;
            _scopeManager = scopeManager;
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet("~/connect/authorize")]
        [HttpPost("~/connect/authorize")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Authorize()
        {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            // Добавьте отладочную информацию
            var cookies = HttpContext.Request.Cookies;
            Console.WriteLine($"Cookies count: {cookies.Count}");
            foreach (var cookie in cookies)
            {
                Console.WriteLine($"Cookie: {cookie.Key} = {cookie.Value}");
            }

            
            // Retrieve the user principal stored in the authentication cookie
            var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);

            // If the user principal can't be extracted, redirect the user to the login page
            if (!result.Succeeded)
            {
                return Challenge(
                    authenticationSchemes: IdentityConstants.ApplicationScheme,
                    properties: new AuthenticationProperties
                    {
                        RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                            Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
                    });
            }

            var user = await _userManager.GetUserAsync(result.Principal);
            if (user == null || !user.IsActive)
            {
                return Challenge(
                    authenticationSchemes: IdentityConstants.ApplicationScheme,
                    properties: new AuthenticationProperties
                    {
                        RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                            Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
                    });
            }

            // Create the claims-based identity that will be used by OpenIddict to generate tokens
            var identity = new ClaimsIdentity(
                authenticationType: TokenValidationParameters.DefaultAuthenticationType,
                nameType: OpenIddictConstants.Claims.Name,
                roleType: OpenIddictConstants.Claims.Role);

            // Add the claims that will be persisted in the tokens
            identity.SetClaim(OpenIddictConstants.Claims.Subject, await _userManager.GetUserIdAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.Email, await _userManager.GetEmailAsync(user))
                    .SetClaim(OpenIddictConstants.Claims.EmailVerified, user.EmailConfirmed.ToString().ToLower())
                    .SetClaim(OpenIddictConstants.Claims.Name, $"{user.FirstName} {user.LastName}")
                    .SetClaim(OpenIddictConstants.Claims.GivenName, user.FirstName)
                    .SetClaim(OpenIddictConstants.Claims.FamilyName, user.LastName)
                    .SetClaim(OpenIddictConstants.Claims.PreferredUsername, user.UserName)
                    .SetClaim(OpenIddictConstants.Claims.UpdatedAt, new DateTimeOffset(user.UpdatedAt).ToUnixTimeSeconds().ToString());

            // Add optional claims if available
            if (!string.IsNullOrEmpty(user.MiddleName))
                identity.SetClaim(OpenIddictConstants.Claims.MiddleName, user.MiddleName);
            
            if (!string.IsNullOrEmpty(user.ProfilePicture))
                identity.SetClaim(OpenIddictConstants.Claims.Picture, user.ProfilePicture);
            
            if (!string.IsNullOrEmpty(user.Website))
                identity.SetClaim(OpenIddictConstants.Claims.Website, user.Website);
            
            if (!string.IsNullOrEmpty(user.Gender))
                identity.SetClaim(OpenIddictConstants.Claims.Gender, user.Gender);
            
            if (user.DateOfBirth.HasValue)
                identity.SetClaim(OpenIddictConstants.Claims.Birthdate, user.DateOfBirth.Value.ToString("yyyy-MM-dd"));
            
            if (!string.IsNullOrEmpty(user.Locale))
                identity.SetClaim(OpenIddictConstants.Claims.Locale, user.Locale);
            
            if (!string.IsNullOrEmpty(user.TimeZone))
                identity.SetClaim(OpenIddictConstants.Claims.Zoneinfo, user.TimeZone);

            // Add phone claims if phone scope is requested
            if (request.HasScope(OpenIddictConstants.Permissions.Scopes.Phone))
            {
                if (!string.IsNullOrEmpty(user.PhoneNumber))
                {
                    identity.SetClaim(OpenIddictConstants.Claims.PhoneNumber, user.PhoneNumber);
                    identity.SetClaim(OpenIddictConstants.Claims.PhoneNumberVerified, user.PhoneNumberConfirmed.ToString().ToLower());
                }
            }

            // Add role claims
            if (request.HasScope(OpenIddictConstants.Permissions.Scopes.Roles))
            {
                var roles = await _userManager.GetRolesAsync(user);
                identity.SetClaims(OpenIddictConstants.Claims.Role, roles.ToImmutableArray());
            }

            // Add external provider claim if user logged in via external provider
            var logins = await _userManager.GetLoginsAsync(user);
            if (logins.Any())
            {
                var primaryLogin = logins.First();
                identity.SetClaim("idp", primaryLogin.LoginProvider);
                identity.SetClaim("idp_access_token", await _userManager.GetAuthenticationTokenAsync(
                    user, primaryLogin.LoginProvider, "access_token") ?? "");
            }

            // Set the list of scopes granted to the client application
            identity.SetScopes(request.GetScopes());
            identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

            // Check if consent is required
            var application = await _applicationManager.FindByIdAsync(request.ClientId) ??
                throw new InvalidOperationException("The application cannot be found.");

            var consentType = await _applicationManager.GetConsentTypeAsync(application);
            
            if (consentType == OpenIddictConstants.ConsentTypes.Explicit)
            {
                // Check if an authorization already exists
                var authorizations = await _authorizationManager.FindAsync(
                    subject: identity.GetClaim(OpenIddictConstants.Claims.Subject),
                    client: request.ClientId,
                    status: OpenIddictConstants.Statuses.Valid,
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    scopes: request.GetScopes()).ToListAsync();

                if (!authorizations.Any())
                {
                    // If no authorization exists, return the consent view
                    return View("Consent", new AuthorizeViewModel
                    {
                        ApplicationName = await _applicationManager.GetLocalizedDisplayNameAsync(application),
                        Scope = string.Join(" ", request.GetScopes()),
                        ClientId = request.ClientId,
                        RedirectUri = request.RedirectUri,
                        ResponseType = request.ResponseType,
                        State = request.State,
                        CodeChallenge = request.CodeChallenge,
                        CodeChallengeMethod = request.CodeChallengeMethod
                    });
                }
                
                identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorizations.First()));
            }
            else
            {
                // Automatically create a permanent authorization
                var authorization = await _authorizationManager.CreateAsync(
                    identity: identity,
                    subject: identity.GetClaim(OpenIddictConstants.Claims.Subject),
                    client: request.ClientId,
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    scopes: identity.GetScopes());

                identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));
            }

            identity.SetDestinations(GetDestinations);

            // Update last login
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        [HttpPost("~/connect/token")]
        public async Task<IActionResult> Exchange()
        {
            var request = HttpContext.GetOpenIddictServerRequest() ??
                throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            ClaimsPrincipal claimsPrincipal;

            if (request.IsClientCredentialsGrantType())
            {
                // Client credentials flow
                var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);
                identity.AddClaim(OpenIddictConstants.Claims.Subject, request.ClientId ?? throw new InvalidOperationException());
                identity.SetScopes(request.GetScopes());
                identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
                identity.SetDestinations(GetDestinations);

                claimsPrincipal = new ClaimsPrincipal(identity);
            }
            else if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
            {
                // Retrieve the claims principal stored in the authorization code/refresh token
                claimsPrincipal = (await HttpContext.AuthenticateAsync(
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;
            }
            else if (request.IsPasswordGrantType())
            {
                // Password flow
                var user = await _userManager.FindByNameAsync(request.Username);
                if (user == null)
                {
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                        }));
                }

                // Validate the username/password parameters and ensure the account is not locked out
                var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
                if (!result.Succeeded)
                {
                    return Forbid(
                        authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                        properties: new AuthenticationProperties(new Dictionary<string, string>
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                        }));
                }

                var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);
                identity.SetClaim(OpenIddictConstants.Claims.Subject, await _userManager.GetUserIdAsync(user))
                        .SetClaim(OpenIddictConstants.Claims.Email, await _userManager.GetEmailAsync(user))
                        .SetClaim(OpenIddictConstants.Claims.Name, await _userManager.GetUserNameAsync(user))
                        .SetClaim(OpenIddictConstants.Claims.GivenName, user.FirstName)
                        .SetClaim(OpenIddictConstants.Claims.FamilyName, user.LastName);

                identity.SetScopes(request.GetScopes());
                identity.SetResources(await _scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());
                identity.SetDestinations(GetDestinations);

                claimsPrincipal = new ClaimsPrincipal(identity);
            }
            else
            {
                throw new InvalidOperationException("The specified grant type is not supported.");
            }

            return SignIn(claimsPrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
        [HttpGet("~/connect/userinfo")]
        public async Task<IActionResult> Userinfo()
        {
            var claimsPrincipal = (await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

            var user = await _userManager.GetUserAsync(claimsPrincipal);
            if (user == null)
            {
                return Challenge(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidToken,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The specified access token is bound to an account that no longer exists."
                    }));
            }

            var claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [OpenIddictConstants.Claims.Subject] = await _userManager.GetUserIdAsync(user),
                [OpenIddictConstants.Claims.Email] = await _userManager.GetEmailAsync(user),
                [OpenIddictConstants.Claims.EmailVerified] = await _userManager.IsEmailConfirmedAsync(user),
                [OpenIddictConstants.Claims.Name] = user.FirstName + " " + user.LastName,
                [OpenIddictConstants.Claims.GivenName] = user.FirstName,
                [OpenIddictConstants.Claims.FamilyName] = user.LastName
            };

            if (_userManager.SupportsUserRole)
            {
                var roles = await _userManager.GetRolesAsync(user);
                claims["roles"] = roles;
            }

            return Ok(claims);
        }

        [HttpGet("~/connect/logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return SignOut(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties
                {
                    RedirectUri = "/"
                });
        }

        private IEnumerable<string> GetDestinations(Claim claim)
        {
            switch (claim.Type)
            {
                case OpenIddictConstants.Claims.Name:
                    yield return OpenIddictConstants.Destinations.AccessToken;

                    if (claim.Subject.HasScope(OpenIddictConstants.Permissions.Scopes.Profile))
                        yield return OpenIddictConstants.Destinations.IdentityToken;

                    yield break;

                case OpenIddictConstants.Claims.Email:
                    yield return OpenIddictConstants.Destinations.AccessToken;

                    if (claim.Subject.HasScope(OpenIddictConstants.Permissions.Scopes.Email))
                        yield return OpenIddictConstants.Destinations.IdentityToken;

                    yield break;

                case OpenIddictConstants.Claims.Role:
                    yield return OpenIddictConstants.Destinations.AccessToken;

                    if (claim.Subject.HasScope(OpenIddictConstants.Permissions.Scopes.Roles))
                        yield return OpenIddictConstants.Destinations.IdentityToken;

                    yield break;

                case "AspNet.Identity.SecurityStamp": yield break;

                default:
                    yield return OpenIddictConstants.Destinations.AccessToken;
                    yield break;
            }
        }
    }