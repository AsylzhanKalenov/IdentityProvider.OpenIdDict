using Microsoft.AspNetCore.Mvc;

namespace TestClient.Controllers;

public class CallbackController : Controller

{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}