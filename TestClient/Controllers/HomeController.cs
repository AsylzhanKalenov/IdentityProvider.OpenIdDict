
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace TestClient.Controllers;

public class HomeController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IHttpClientFactory httpClientFactory, ILogger<HomeController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code, string state, string error = null, string error_description = null)
    {
        // Проверяем на ошибки OAuth2
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogError("OAuth2 error: {Error} - {Description}", error, error_description);
            ViewBag.Error = $"Authorization failed: {error}";
            ViewBag.ErrorDescription = error_description;
            return View("Error");
        }

        if (string.IsNullOrEmpty(code))
        {
            ViewBag.Error = "Authorization code is missing";
            return View("Error");
        }

        if (string.IsNullOrEmpty(state) || state != "test123")
        {
            ViewBag.Error = "Invalid state parameter";
            return View("Error");
        }

        try
        {
            // Обмениваем authorization code на токены
            var tokenResponse = await ExchangeCodeForTokens(code);
            
            if (tokenResponse.IsSuccessful)
            {
                // Сохраняем токены в сессии (в продакшене лучше использовать более безопасное хранение)
                HttpContext.Session.SetString("access_token", tokenResponse.AccessToken);
                HttpContext.Session.SetString("refresh_token", tokenResponse.RefreshToken ?? "");
                HttpContext.Session.SetString("id_token", tokenResponse.IdToken ?? "");

                // Перенаправляем на страницу успеха с токенами
                return View("CallbackSuccess", tokenResponse);
            }
            else
            {
                ViewBag.Error = "Failed to exchange code for tokens";
                ViewBag.ErrorDescription = tokenResponse.Error;
                return View("Error");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token exchange");
            ViewBag.Error = "An error occurred during token exchange";
            ViewBag.ErrorDescription = ex.Message;
            return View("Error");
        }
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var accessToken = HttpContext.Session.GetString("access_token");
        if (string.IsNullOrEmpty(accessToken))
        {
            return RedirectToAction("Index");
        }

        try
        {
            // Используем access token для получения информации о пользователе
            var userInfo = await GetUserInfo(accessToken);
            return View("Profile", userInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching user profile");
            ViewBag.Error = "Failed to fetch user profile";
            return View("Error");
        }
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // Очищаем сессию
        HttpContext.Session.Clear();
        return RedirectToAction("Index");
    }

    private async Task<TokenResponse> ExchangeCodeForTokens(string code)
    {
        var httpClient = _httpClientFactory.CreateClient();
        
        // Параметры для обмена code на токены
        var tokenRequest = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("client_id", "spa-client"),
            new("code", code),
            new("redirect_uri", "https://localhost:7255/callback"),
            new("code_verifier", GetCodeVerifierFromSession()) // Получаем сохраненный code_verifier
        };

        var requestContent = new FormUrlEncodedContent(tokenRequest);
        
        var response = await httpClient.PostAsync("https://localhost:7066/connect/token", requestContent);
        var responseContent = await response.Content.ReadAsStringAsync();

        _logger.LogInformation("Token response: {Response}", responseContent);

        if (response.IsSuccessStatusCode)
        {
            var tokenData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            return new TokenResponse
            {
                IsSuccessful = true,
                AccessToken = tokenData.TryGetProperty("access_token", out var accessToken) ? accessToken.GetString() : null,
                RefreshToken = tokenData.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() : null,
                IdToken = tokenData.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null,
                TokenType = tokenData.TryGetProperty("token_type", out var tokenType) ? tokenType.GetString() : "Bearer",
                ExpiresIn = tokenData.TryGetProperty("expires_in", out var expiresIn) ? expiresIn.GetInt32() : 3600,
                Scope = tokenData.TryGetProperty("scope", out var scope) ? scope.GetString() : null
            };
        }
        else
        {
            var errorData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return new TokenResponse
            {
                IsSuccessful = false,
                Error = errorData.TryGetProperty("error", out var error) ? error.GetString() : "unknown_error",
                ErrorDescription = errorData.TryGetProperty("error_description", out var errorDesc) ? errorDesc.GetString() : "Unknown error occurred"
            };
        }
    }
    
    [HttpPost("SaveCodeVerifier")]
    public IActionResult SaveCodeVerifier([FromBody] CodeVerifierRequest request)
    {
        HttpContext.Session.SetString("code_verifier", request.CodeVerifier);
        return Ok();
    }

    public class CodeVerifierRequest
    {
        public string CodeVerifier { get; set; }
    }


    private async Task<UserInfo> GetUserInfo(string accessToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Пытаемся получить информацию о пользователе через API
        // Поскольку у нас нет userinfo endpoint, декодируем JWT токен
        var userInfo = DecodeJwtToken(accessToken);
        return userInfo;
    }

    private UserInfo DecodeJwtToken(string token)
    {
        try
        {
            // Простое декодирование JWT payload (в продакшене используйте библиотеку для JWT)
            var parts = token.Split('.');
            if (parts.Length != 3)
                throw new ArgumentException("Invalid JWT token format");

            var payload = parts[1];
            // Добавляем padding если необходимо
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var jsonBytes = Convert.FromBase64String(payload);
            var json = Encoding.UTF8.GetString(jsonBytes);
            var claims = JsonSerializer.Deserialize<JsonElement>(json);

            return new UserInfo
            {
                Subject = claims.TryGetProperty("sub", out var sub) ? sub.GetString() : null,
                Email = claims.TryGetProperty("email", out var email) ? email.GetString() : null,
                Name = claims.TryGetProperty("name", out var name) ? name.GetString() : null,
                GivenName = claims.TryGetProperty("given_name", out var givenName) ? givenName.GetString() : null,
                FamilyName = claims.TryGetProperty("family_name", out var familyName) ? familyName.GetString() : null,
                Roles = claims.TryGetProperty("role", out var role) ? 
                    (role.ValueKind == JsonValueKind.Array ? 
                        role.EnumerateArray().Select(r => r.GetString()).ToList() : 
                        new List<string> { role.GetString() }) : 
                    new List<string>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decoding JWT token");
            throw new InvalidOperationException("Failed to decode user information from token", ex);
        }
    }

    private string GetCodeVerifierFromSession()
    {
        // В реальном приложении code_verifier должен храниться более безопасно
        // Для демо будем искать его в заголовках или создавать заново
        return HttpContext.Session.GetString("code_verifier") ?? GenerateCodeVerifier();
    }

    private string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("=", "").Replace("+", "-").Replace("/", "_");
    }
}

// Модели для работы с токенами
public class TokenResponse
{
    public bool IsSuccessful { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public string IdToken { get; set; }
    public string TokenType { get; set; }
    public int ExpiresIn { get; set; }
    public string Scope { get; set; }
    public string Error { get; set; }
    public string ErrorDescription { get; set; }
}

public class UserInfo
{
    public string Subject { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public string GivenName { get; set; }
    public string FamilyName { get; set; }
    public List<string> Roles { get; set; } = new();
}